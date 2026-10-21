using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>Medio por el que viaja un proyectil: gravedad efectiva y arrastre lineal.</summary>
    public struct TrajectoryMedium
    {
        public Vector3 gravity;
        /// <summary>Arrastre lineal k (1/s): a = g - k·v. Con k = 0 la trayectoria es una parábola exacta.</summary>
        public float drag;

        public TrajectoryMedium(Vector3 gravity, float drag) { this.gravity = gravity; this.drag = drag; }
    }

    /// <summary>
    /// LAB 05 · PREDICCIÓN DE TRAYECTORIA (Millington, cap. 3.5). Núcleo compartido por el juego 3D
    /// (lanzador, lastres, boyas, peces y gata guía) y el minijuego 2D "El Muelle".
    ///
    ///   p(t) = p0 + u·sm·t + ½·g·t²        (u = dirección normalizada, sm = rapidez de salida)
    ///
    /// Además de la fórmula del lab incluye:
    ///   · tiempo en que el proyectil llega a una altura (ecuación cuadrática) → punto de caída;
    ///   · solución de disparo (ángulo bajo y alto) para dar en un blanco con una rapidez dada;
    ///   · gravedad efectiva bajo el agua por flotabilidad: g_ef = g·(1 − ρ_agua / ρ_objeto);
    ///   · predicción numérica paso a paso (la usa el agua, donde el arrastre rompe la parábola).
    /// </summary>
    public static class TrajectoryMath
    {
        public const float WaterDensity = 1000f;

        // ------------------------------------------------------------------ fórmula del lab

        /// <summary>p(t) = p0 + v0·t + ½·g·t²  (v0 = u·sm).</summary>
        public static Vector3 PositionAt(Vector3 p0, Vector3 v0, Vector3 g, float t)
        {
            return p0 + v0 * t + 0.5f * g * t * t;
        }

        /// <summary>Misma fórmula escrita como en el enunciado: dirección normalizada + rapidez de salida.</summary>
        public static Vector3 PositionAt(Vector3 p0, Vector3 direction, float muzzleSpeed, Vector3 g, float t)
        {
            return PositionAt(p0, direction.normalized * muzzleSpeed, g, t);
        }

        public static Vector2 PositionAt(Vector2 p0, Vector2 v0, Vector2 g, float t)
        {
            return p0 + v0 * t + 0.5f * g * t * t;
        }

        /// <summary>v(t) = v0 + g·t</summary>
        public static Vector3 VelocityAt(Vector3 v0, Vector3 g, float t) { return v0 + g * t; }

        /// <summary>
        /// Versión DISCRETA de la misma parábola: la posición tras n pasos de Euler semi-implícito
        /// (el integrador de PhysX). Coincide exactamente con lo que hará el Rigidbody:
        ///   p_n = p0 + v0·n·dt + g·dt²·n(n+1)/2
        /// La diferencia con la fórmula continua es ½·g·dt·t (unos cm): se usa para comparar en el informe.
        /// </summary>
        public static Vector3 PositionAtDiscrete(Vector3 p0, Vector3 v0, Vector3 g, float dt, int n)
        {
            return p0 + v0 * (n * dt) + g * (dt * dt * n * (n + 1) * 0.5f);
        }

        /// <summary>
        /// Velocidad que hay que darle al Rigidbody para que, integrado por pasos de dt, recorra EXACTAMENTE la
        /// parábola continua de velocidad v': como p_n = p0 + (v0 + g·dt/2)·t + ½·g·t², basta v0 = v' − g·dt/2.
        /// </summary>
        public static Vector3 DiscreteLaunchVelocity(Vector3 continuousVelocity, Vector3 g, float dt)
        {
            return continuousVelocity - g * (dt * 0.5f);
        }

        public static Vector2 DiscreteLaunchVelocity(Vector2 continuousVelocity, Vector2 g, float dt)
        {
            return continuousVelocity - g * (dt * 0.5f);
        }

        /// <summary>Puntos equiespaciados en el tiempo (como los "dots" del lab).</summary>
        public static int SamplePoints(Vector3 p0, Vector3 v0, Vector3 g, float timeStep, List<Vector3> output, int maxPoints)
        {
            output.Clear();
            for (int i = 0; i < maxPoints; i++) output.Add(PositionAt(p0, v0, g, i * timeStep));
            return output.Count;
        }

        // ------------------------------------------------------------------ cuándo y dónde cae

        /// <summary>
        /// Tiempo en que la componente vertical vale <paramref name="height"/>:
        ///   y0 + vy·t + ½·gy·t² = h   →   ½·gy·t² + vy·t + (y0 − h) = 0
        /// descending = true devuelve el cruce "cayendo encima" (la raíz mayor si la gravedad apunta abajo);
        /// false devuelve el primer cruce (sirve para una boya que sube y choca con un techo).
        /// </summary>
        public static bool TimeToHeight(float y0, float vy, float gy, float height, bool descending, out float t)
        {
            t = 0f;
            float a = 0.5f * gy, b = vy, c = y0 - height;
            if (Mathf.Abs(a) < 1e-6f)
            {
                if (Mathf.Abs(b) < 1e-6f) return false;
                t = -c / b;
                return t > 0f;
            }
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return false;
            float sq = Mathf.Sqrt(disc);
            float t1 = (-b - sq) / (2f * a), t2 = (-b + sq) / (2f * a);
            float lo = Mathf.Min(t1, t2), hi = Mathf.Max(t1, t2);
            if (descending)
            {
                if (hi > 1e-4f) { t = hi; return true; }
                return false;
            }
            if (lo > 1e-4f) { t = lo; return true; }
            if (hi > 1e-4f) { t = hi; return true; }
            return false;
        }

        /// <summary>Punto donde la trayectoria cruza el plano horizontal y = height (o false si nunca llega).</summary>
        public static bool LandingPoint(Vector3 p0, Vector3 v0, Vector3 g, float height, bool descending, out Vector3 point, out float time)
        {
            point = p0;
            if (!TimeToHeight(p0.y, v0.y, g.y, height, descending, out time)) return false;
            point = PositionAt(p0, v0, g, time);
            return true;
        }

        /// <summary>Altura máxima (si la gravedad apunta hacia abajo y el tiro sube): y0 + vy²/(2|g|).</summary>
        public static float ApexHeight(Vector3 p0, Vector3 v0, Vector3 g)
        {
            if (g.y >= 0f || v0.y <= 0f) return p0.y;
            return p0.y + v0.y * v0.y / (-2f * g.y);
        }

        // ------------------------------------------------------------------ solución de disparo

        /// <summary>
        /// Velocidades de salida para ir de <paramref name="from"/> a <paramref name="to"/> con rapidez fija
        /// (gravedad vertical de módulo gMag). Con x = distancia horizontal e y = desnivel:
        ///   tan θ = (v² ± √(v⁴ − g·(g·x² + 2·y·v²))) / (g·x)
        /// Devuelve 0 (fuera de alcance), 1 o 2 soluciones: tiro bajo (rápido) y tiro alto (bombeado).
        /// </summary>
        public static int FiringSolutions(Vector3 from, Vector3 to, float speed, float gMag, out Vector3 low, out Vector3 high)
        {
            low = high = Vector3.zero;
            Vector3 delta = to - from;
            Vector3 flat = new Vector3(delta.x, 0f, delta.z);
            float x = flat.magnitude;
            float y = delta.y;
            float v2 = speed * speed;
            if (gMag <= 1e-5f) { low = high = delta.normalized * speed; return 1; }
            if (x < 1e-4f)
            {
                // tiro vertical
                if (y > 0f && v2 < 2f * gMag * y) return 0;
                low = high = (y >= 0f ? Vector3.up : Vector3.down) * speed;
                return 1;
            }
            float disc = v2 * v2 - gMag * (gMag * x * x + 2f * y * v2);
            if (disc < 0f) return 0;
            float sq = Mathf.Sqrt(disc);
            Vector3 dir = flat / x;
            float aLow = Mathf.Atan((v2 - sq) / (gMag * x));
            float aHigh = Mathf.Atan((v2 + sq) / (gMag * x));
            low = (dir * Mathf.Cos(aLow) + Vector3.up * Mathf.Sin(aLow)) * speed;
            high = (dir * Mathf.Cos(aHigh) + Vector3.up * Mathf.Sin(aHigh)) * speed;
            return disc < 1e-6f ? 1 : 2;
        }

        /// <summary>Rapidez mínima para alcanzar un punto (el ángulo óptimo): v² = g·(y + √(x² + y²)).</summary>
        public static float MinimumSpeed(Vector3 from, Vector3 to, float gMag)
        {
            Vector3 d = to - from;
            float x = new Vector2(d.x, d.z).magnitude;
            return Mathf.Sqrt(gMag * (d.y + Mathf.Sqrt(x * x + d.y * d.y)));
        }

        /// <summary>Tiempo de vuelo de un tiro (componente horizontal constante): t = x / vx.</summary>
        public static float FlightTime(Vector3 from, Vector3 to, Vector3 v0)
        {
            float x = new Vector2(to.x - from.x, to.z - from.z).magnitude;
            float vx = new Vector2(v0.x, v0.z).magnitude;
            return vx > 1e-4f ? x / vx : 0f;
        }

        /// <summary>Versión 2D (plano XY, gravedad en -Y) para el minijuego.</summary>
        public static int FiringSolutions2D(Vector2 from, Vector2 to, float speed, float gMag, out Vector2 low, out Vector2 high)
        {
            Vector3 l, h;
            int n = FiringSolutions(new Vector3(from.x, from.y, 0f), new Vector3(to.x, to.y, 0f), speed, gMag, out l, out h);
            low = new Vector2(l.x, l.y);
            high = new Vector2(h.x, h.y);
            return n;
        }

        // ------------------------------------------------------------------ bajo el agua

        /// <summary>
        /// Gravedad efectiva de un objeto sumergido (peso − empuje de Arquímedes):
        ///   g_ef = g · (1 − ρ_fluido / ρ_objeto)
        /// Hierro (7800) ≈ 0.87·g hacia abajo; boya (250) = −3·g (sube); ρ = 1000 → flota quieto.
        /// Como sigue siendo una aceleración CONSTANTE, la trayectoria sin arrastre sigue siendo una parábola.
        /// </summary>
        public static Vector3 EffectiveGravity(Vector3 g, float objectDensity, float fluidDensity = WaterDensity)
        {
            return g * (1f - fluidDensity / Mathf.Max(1f, objectDensity));
        }

        // ------------------------------------------------------------------ predicción numérica

        /// <summary>Paso de Euler semi-implícito con arrastre lineal (el mismo que aplica Projectile en FixedUpdate).</summary>
        public static void Step(ref Vector3 p, ref Vector3 v, TrajectoryMedium m, float dt)
        {
            v += (m.gravity - m.drag * v) * dt;
            p += v * dt;
        }

        public delegate TrajectoryMedium MediumAt(Vector3 position);

        private static readonly RaycastHit[] castBuffer = new RaycastHit[16];

        /// <summary>Primer choque (no trigger) entre dos puntos, ignorando un objeto y sus hijos (el que dispara).</summary>
        public static bool SegmentCast(Vector3 a, Vector3 b, int layerMask, Transform ignoreRoot, out RaycastHit best)
        {
            best = new RaycastHit();
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-5f) return false;
            int n = Physics.RaycastNonAlloc(a, d / len, castBuffer, len, layerMask, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                if (ignoreRoot != null && castBuffer[i].collider.transform.IsChildOf(ignoreRoot)) continue;
                if (castBuffer[i].distance < bestDist) { bestDist = castBuffer[i].distance; best = castBuffer[i]; found = true; }
            }
            return found;
        }

        /// <summary>Resultado de una predicción por simulación.</summary>
        public struct Prediction
        {
            public bool hit;
            public Vector3 point;
            public Vector3 normal;
            public float time;
            public Collider collider;
            /// <summary>Primer punto donde cruza la superficie del agua (si la cruza).</summary>
            public bool entersWater;
            public Vector3 waterEntry;
            public float waterEntryTime;
        }

        /// <summary>
        /// Simula el vuelo paso a paso (como PhysX) cambiando de medio al cruzar el agua y comprueba
        /// el choque con Linecast entre puntos. Guarda un punto cada <paramref name="recordEvery"/> pasos.
        /// </summary>
        public static Prediction Simulate(Vector3 p0, Vector3 v0, MediumAt medium, float dt, float maxTime,
                                          List<Vector3> points, int recordEvery, int layerMask, Transform ignoreRoot = null,
                                          System.Func<Vector3, float> surfaceAt = null)
        {
            Prediction pred = new Prediction();
            if (points != null) { points.Clear(); points.Add(p0); }
            Vector3 p = p0, v = v0;
            bool wasUnder = surfaceAt != null && p0.y < surfaceAt(p0);
            int steps = Mathf.CeilToInt(maxTime / Mathf.Max(0.001f, dt));
            for (int i = 1; i <= steps; i++)
            {
                Vector3 prev = p;
                Step(ref p, ref v, medium(p), dt);
                if (surfaceAt != null && !pred.entersWater)
                {
                    float s = surfaceAt(p);
                    bool under = p.y < s;
                    if (under && !wasUnder && !float.IsInfinity(s))
                    {
                        float k = Mathf.Abs(prev.y - p.y) > 1e-5f ? (prev.y - s) / (prev.y - p.y) : 0f;
                        pred.entersWater = true;
                        pred.waterEntry = Vector3.Lerp(prev, p, Mathf.Clamp01(k));
                        pred.waterEntryTime = (i - 1 + Mathf.Clamp01(k)) * dt;
                    }
                    wasUnder = under;
                }
                RaycastHit hit;
                if (SegmentCast(prev, p, layerMask, ignoreRoot, out hit))
                {
                    pred.hit = true;
                    pred.point = hit.point;
                    pred.normal = hit.normal;
                    pred.collider = hit.collider;
                    pred.time = (i - 1 + hit.distance / Mathf.Max(1e-4f, (p - prev).magnitude)) * dt;
                    if (points != null) points.Add(hit.point);
                    return pred;
                }
                if (points != null && i % Mathf.Max(1, recordEvery) == 0) points.Add(p);
            }
            pred.point = p;
            pred.time = maxTime;
            return pred;
        }

        // ------------------------------------------------------------------ solución numérica (con arrastre)

        /// <summary>
        /// Con arrastre no hay fórmula cerrada: se busca el ángulo numéricamente. Para cada elevación se simula
        /// el vuelo (sin choques) hasta la distancia horizontal del blanco y se mide el error vertical; donde el
        /// error cambia de signo se refina por bisección. Devuelve 0, 1 o 2 soluciones (la más baja y la más alta).
        /// </summary>
        public static int FiringSolutionNumeric(Vector3 from, Vector3 to, float speed, MediumAt medium, float dt,
                                                out Vector3 low, out Vector3 high, float maxTime = 6f)
        {
            low = high = Vector3.zero;
            Vector3 flat = new Vector3(to.x - from.x, 0f, to.z - from.z);
            float x = flat.magnitude;
            Vector3 dir = x > 1e-4f ? flat / x : Vector3.forward;
            const float a0 = -85f, a1 = 85f, step = 2.5f;
            int found = 0;
            float prevA = a0;
            float prevE = VerticalError(from, to, dir, x, a0, speed, medium, dt, maxTime);
            float firstA = float.NaN, lastA = float.NaN;
            for (float a = a0 + step; a <= a1 + 1e-3f; a += step)
            {
                float e = VerticalError(from, to, dir, x, a, speed, medium, dt, maxTime);
                if (!float.IsNaN(e) && !float.IsNaN(prevE) && Mathf.Sign(e) != Mathf.Sign(prevE))
                {
                    float lo = prevA, hi = a, eLo = prevE;
                    for (int k = 0; k < 14; k++)
                    {
                        float mid = 0.5f * (lo + hi);
                        float em = VerticalError(from, to, dir, x, mid, speed, medium, dt, maxTime);
                        if (float.IsNaN(em)) break;
                        if (Mathf.Sign(em) == Mathf.Sign(eLo)) { lo = mid; eLo = em; } else hi = mid;
                    }
                    float sol = 0.5f * (lo + hi);
                    if (float.IsNaN(firstA)) firstA = sol;
                    lastA = sol;
                    found++;
                }
                prevA = a;
                prevE = e;
            }
            if (found == 0) return 0;
            low = Launch(dir, firstA, speed);
            high = Launch(dir, lastA, speed);
            return found == 1 ? 1 : 2;
        }

        private static Vector3 Launch(Vector3 flatDir, float elevationDeg, float speed)
        {
            float r = elevationDeg * Mathf.Deg2Rad;
            return (flatDir * Mathf.Cos(r) + Vector3.up * Mathf.Sin(r)) * speed;
        }

        /// <summary>Altura del vuelo al llegar a la distancia horizontal x, menos la del blanco (NaN si no llega).</summary>
        private static float VerticalError(Vector3 from, Vector3 to, Vector3 flatDir, float x, float elevationDeg,
                                           float speed, MediumAt medium, float dt, float maxTime)
        {
            Vector3 p = from, v = Launch(flatDir, elevationDeg, speed);
            if (x < 1e-3f) return float.NaN;
            int steps = Mathf.CeilToInt(maxTime / dt);
            for (int i = 0; i < steps; i++)
            {
                Vector3 prev = p;
                Step(ref p, ref v, medium(p), dt);
                float dPrev = Vector3.Dot(prev - from, flatDir), dNow = Vector3.Dot(p - from, flatDir);
                if (dNow >= x)
                {
                    float k = Mathf.Abs(dNow - dPrev) > 1e-6f ? (x - dPrev) / (dNow - dPrev) : 0f;
                    return Mathf.Lerp(prev.y, p.y, k) - to.y;
                }
            }
            return float.NaN;
        }

        /// <summary>Rapidez mínima (buscada entre min y max) con la que existe solución numérica; −1 si ninguna.</summary>
        public static float MinimumSpeedNumeric(Vector3 from, Vector3 to, float minSpeed, float maxSpeed, MediumAt medium, float dt)
        {
            Vector3 l, h;
            for (float s = minSpeed; s <= maxSpeed + 1e-3f; s += 0.5f)
            {
                if (FiringSolutionNumeric(from, to, s, medium, dt, out l, out h) > 0) return s;
            }
            return -1f;
        }
    }
}
