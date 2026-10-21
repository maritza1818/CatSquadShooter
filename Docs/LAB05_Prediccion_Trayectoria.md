# Lab 05 · Predicción de trayectoria en Cat Squad

## 1. La fórmula y dónde vive

```
p(t) = p0 + u·sm·t + ½·g·t²
```

- `p0` es la posición de disparo, `u` la dirección normalizada, `sm` la rapidez de salida y `g` la gravedad.
- Está en `Scripts/Trajectory/TrajectoryMath.cs` (`PositionAt`). La usan el juego 3D y el minijuego 2D.

| Función | Qué resuelve |
|---|---|
| `PositionAt` / `VelocityAt` | Posición y velocidad en el instante t (la fórmula del lab) |
| `TimeToHeight` / `LandingPoint` | **Cuándo y dónde cae**: resuelve ½·g·t² + vy·t + (y0 − h) = 0 |
| `FiringSolutions` | **Ángulo para dar en un blanco** con rapidez fija: tan θ = (v² ± √(v⁴ − g(gx² + 2yv²))) / (gx). Da dos soluciones: tiro bajo y tiro alto |
| `MinimumSpeed` | Rapidez mínima para llegar: v² = g(y + √(x² + y²)) |
| `EffectiveGravity` | Bajo el agua: g_ef = g·(1 − ρ_agua/ρ_objeto) (peso − empuje de Arquímedes) |
| `PositionAtDiscrete` / `DiscreteLaunchVelocity` | La versión "por pasos" que integra la física de Unity (ver §4) |
| `Simulate` / `FiringSolutionNumeric` | Predicción paso a paso, con arrastre y cambio de medio, y búsqueda numérica del ángulo |

## 2. Ejercicio 1 — los métodos del minijuego (misma estructura que el lab)

**Ball** (`Scripts/Minijuego2D/Ball.cs`)

- `Push(force)`: aplica `AddForce(force, ForceMode2D.Impulse)`. Con masa 1, la velocidad inicial es igual a la fuerza.
- `ActivateRb()`: pone el cuerpo en *Dynamic*. Desde ahí lo mueve la física: gravedad, choques y rebote.
- `DesactivateRb()`: pone la velocidad en 0 y el cuerpo en *Kinematic*, para que el ovillo no se caiga mientras se apunta.
- `OnTriggerEnter2D`: si entra al trigger de un blanco, avisa al GameManager para que cuente el acierto.

**Trajectory** (`Scripts/Minijuego2D/Trajectory.cs`)

- `PrepareDots()`: instancia `dotsNumber` puntos dentro de `dotsParent`. Van achicándose de `dotMaxScale` a `dotMinScale`.
- `UpdateDots(ballPos, force)`: coloca el punto i en t = i·dotSpacing con la fórmula. Usa v0 = fuerza/masa y g = gravedad 2D × gravityScale.
  - Los puntos bajo el agua se ocultan.
  - En modo **rebotes** (tecla T) los puntos salen de una simulación de física paralela.
- `Show()` / `Hide()`: activan o desactivan el padre de los puntos.

**MuelleGameManager**: es el GameManager del lab (`Scripts/Minijuego2D/MuelleGameManager.cs`).

- `OnDragStart()`: deja el ovillo quieto, guarda el punto inicial del mouse y muestra los puntos.
- `OnDrag()`: calcula distancia = |inicio − actual| (con tope) y dirección = (inicio − actual) normalizada.
  La fuerza es dirección·distancia·pushForce, y con ella se actualizan los puntos.
- `OnDragEnd()`: llama a `ActivateRb()` y a `Push(fuerza)`, y oculta los puntos.
- `OnBallInTarget()`: suma los puntos del blanco (doble si hay racha de 3 o más) y lanza los sonidos y el texto flotante.
- `Solve()`: calcula la solución de disparo hacia el blanco elegido.
  - Elige el arco (bajo o alto) que no choca con el faro.
  - Para la balsa, predice dónde estará cuando llegue el ovillo: tiempo de vuelo → nueva posición, repetido 3 veces.
- `DrawGhost()` y `AutoShot()` usan `Solve()`: el primero dibuja el arco guía y el segundo hace el tiro automático.

**MuelleTarget**: es la taza del lab, con Rigidbody2D, EdgeCollider2D en forma de U y un trigger interior.

- La cesta flota con `MovePosition`.
- `PredictAimPoint(t)` usa la misma fórmula que la mueve, así que se sabe dónde estará.

**TrajectorySimulator2D**

- Crea una escena con física 2D propia (`LocalPhysicsMode.Physics2D`).
- Pone copias invisibles de los colliders y un ovillo fantasma.
- Avanza la física a mano con `PhysicsScene2D.Simulate(dt)`.

## 3. Ejercicios 2 a 4

| Ejercicio | Qué se hizo |
|---|---|
| 2. Otro objetivo | El **balde** en la azotea. Vale 3 puntos y es más difícil: hay que pasar sobre el faro y subir |
| 3. Puntuación | Puntos, aciertos de tiros, racha y mejor racha (arriba a la izquierda) |
| 4. *Calculating Trajectories* | Lo cubre `FiringSolutions` (fórmula cerrada del ángulo, dos soluciones), el arco guía (A), el tiro automático (Espacio) y la predicción del blanco que se mueve |

En el ejercicio 4, "cubre" significa lo que hace nuestro código. Conviene contrastarlo con el texto del tutorial al redactar el informe.

## 4. Detalle fino: fórmula continua vs. física por pasos

Unity no evalúa la fórmula: integra por pasos de `Time.fixedDeltaTime` (Euler semi-implícito):

```
v ← v + g·dt ;  p ← p + v·dt
```

Tras n pasos queda:

```
p_n = p0 + (v0 + g·dt/2)·t + ½·g·t²
```

- La trayectoria real va un poco por encima de la fórmula, unos **½·g·dt·t**: 20 cm a los 2 s con g = 9.81 y dt = 0.02.
- Por eso el tiro automático y la asistencia lanzan con **v0 = v' − g·dt/2**, y caen **exactamente** donde dice la fórmula.
- Se comprobó en Python: la versión discreta coincide paso a paso con el integrador.
- Esto explica por qué los puntos del lab "casi" coinciden con el vuelo.

## 5. En el juego 3D (Gatópolis)

- **Lanzador neumático** (`ProjectileLauncher`). Usa la resortera del lab en 3D:
  - El tirón (ancla − mano) da dirección y fuerza.
  - Los puntos (`TrajectoryDots`) y el anillo de impacto salen de `TrajectoryMath.Simulate`, la misma integración que el Rigidbody, así que coinciden.
- **Gravedad por objeto** (el enunciado dice que en los juegos se usan valores distintos):

| Objeto | Gravedad que se aplica |
|---|---|
| Lata en el aire | 1.5·g |
| Lastre bajo el agua | +0.87·g |
| Boya bajo el agua | −g (sube) |
| Lata en el agua | +0.23·g |

- Sin arrastre la trayectoria sigue siendo una parábola, porque la aceleración es constante.
- Con arrastre no hay fórmula cerrada: el ángulo se busca numéricamente (bisección sobre el error vertical).
  En la prueba en Python el error al blanco fue de 1 a 4 cm.
- **Predicción para la IA** (`PredictedImpacts`). Es la aplicación "más simple" que menciona el lab: saber si algo te va a alcanzar.
  - Los peces hacen ARRIVE al punto donde la lata entrará al agua.
  - La gata guía hace FLEE del punto donde caerá un lastre.

## 6. Cuestionario (borrador)

**1. ¿Cómo funciona la predicción de la trayectoria de un proyectil?**

- Con solo gravedad, la aceleración es constante, así que la posición es la fórmula del lab: una parábola.
- Para predecir, se evalúa p(t) en varios instantes (los puntos) o se despeja t para una altura (dónde cae).
- Para apuntar se despeja el ángulo: hay 0, 1 o 2 soluciones según la rapidez.
- Si hay arrastre, rebotes o cambio de medio, la parábola ya no basta. Entonces se simula paso a paso con el mismo integrador del motor.

**2. ¿Qué elementos da Unity para implementarla?**

- **Componentes de física:**
  - `Rigidbody` / `Rigidbody2D` (masa, gravityScale, `AddForce` con `ForceMode.Impulse` o `VelocityChange`).
  - Colliders 2D y 3D (Box, Circle, Edge, Sphere…).
  - `PhysicsMaterial` / `PhysicsMaterial2D` (rebote y fricción).
- **Consultas y simulación:**
  - `Physics.gravity` / `Physics2D.gravity`.
  - `Physics.Raycast` / `Linecast` y `Physics2D.LinecastAll`, para cortar la predicción donde choca.
  - Las **escenas de física locales** con `PhysicsScene.Simulate` / `PhysicsScene2D.Simulate`, para predecir con choques reales.
- **Para dibujar:** `LineRenderer` y prefabs para los puntos.
