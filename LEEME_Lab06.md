# Lab 06 · Waypoints — "CatRacing" (los tanques son gatos en karts, las palmeras son los waypoints)

## Instalar
1. Copia la carpeta `Assets/CatSquadRP/Lab06` dentro de `Assets/CatSquadRP/` de tu proyecto.
   Usa el gato de `Assets/CatSquadRP/Models/Gato.fbx`, que ya tienes.
2. Espera a que Unity importe y compile.

## Las actividades del lab, paso a paso

| Paso del lab | En CatRacing |
|---|---|
| 1–3. Proyecto 3D e importar los recursos | Ya están: palmera, kart y arena vienen en `Lab06` |
| 4–5. Abrir la escena TankRacing | Menú **CatSquad → Lab 06: Escena para hacer las actividades (sin script)**. Crea `Lab06/Scenes/CatRacing` |
| 6. Duplicar las palmeras (Ctrl+D) en circuito | Ya hay 10 palmeras en lazo: `Palmera`, `Palmera (1)` … `Palmera (9)` |
| 7. Revisar el script FollowWP | `Lab06/Scripts/FollowWP.cs`, comentado en español |
| 8. Arrastrar el script al GreenTank | Arrástralo a **GatoVerde** |
| 9. Cerrar el candado del Inspector | El candadito arriba a la derecha del Inspector |
| 10. Seleccionar todas las palmeras y arrastrarlas al script | Clic en `Palmera` y Shift+clic en `Palmera (9)` en la Hierarchy, luego suéltalas sobre **Waypoints** |
| 11. Abrir el candado | — |
| 12. Ejecutar | Play |
| 13. Cambiar la velocidad | `Speed` y `Rot Speed` en el Inspector del gato |
| 14. Repetir los pasos 8–11 | Con **GatoRojo**, **GatoAzul** y **GatoAmarillo** |
| 15. Ejecutar y verificar | Play: los cuatro dan vueltas al circuito |

**¿Sin tiempo?** Usa **CatSquad → Lab 06: Escena terminada (FollowWP ya asignado)**. Crea `CatRacing_Terminada` con todo puesto. Los valores de cada gato son:

| Gato | Speed | Rot Speed |
|---|---|---|
| GatoVerde | 10 | 10 (los del tutorial) |
| GatoRojo | 9 | 6 |
| GatoAzul | 11 | 4 |
| GatoAmarillo | 8 | 12 |

## Capturas para el informe
- Escena recién creada, con el circuito de palmeras (paso 5–6).
- Script FollowWP abierto (paso 7).
- Inspector de GatoVerde con el candado cerrado y las 10 palmeras en Waypoints (pasos 9–10).
- Play con los gatos corriendo (paso 12). Si seleccionas un gato, la vista Scene muestra en verde el circuito, en amarillo la palmera a la que va y el radio de llegada.
- El mismo gato con otra Speed o Rot Speed (paso 13).

## Notas
- `FollowWP` está en el namespace `CatSquad.Lab06`. Así no choca con el FollowWP del paquete del tutorial si también lo importaste.
- Los karts pasan muy cerca de las palmeras y entre ellos, igual que los tanques del tutorial. Es la limitación de ir directo a cada punto. El ejercicio 1 ("Following a circuit") la arregla con un *tracker* que va adelante.
