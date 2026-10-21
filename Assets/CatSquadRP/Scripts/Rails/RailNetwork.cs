using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Red de rieles del modo automático (primera persona).
    /// Es una grilla construida desde el MISMO mapa ASCII del nivel (una celda = 2 m, plano X-Y).
    /// Se recalcula en vivo porque el mundo cambia: una celda solo es transitable si está bajo el
    /// agua (las bombas llenan zonas), dentro del límite de presión (mejora del casco) y sin
    /// compuerta cerrada. Los objetos que ocupan su celda (cajas, bidones, jaulas, bombas,
    /// cascabeles secretos) se rodean.
    ///
    /// Dijkstra da la distancia por el riel desde el submarino a cada celda; con ella se elige el
    /// objetivo más cercano DE VERDAD (no en línea recta a través de las paredes). La ruta luego se
    /// suaviza "tirando de la cuerda" y la recorre un PathFollower: Seek en los puntos intermedios
    /// y Arrive en el último.
    /// </summary>
    public class RailNetwork : MonoBehaviour
    {
        public static RailNetwork Instance { get; private set; }

        [Tooltip("Copia del mapa ASCII (la escribe el constructor de escena)")]
        public string[] rows = new string[0];
        public float cellSize = 2f;
        [Tooltip("Compuerta que bloquea las celdas 'G' mientras está cerrada")]
        public FloodGate gate;
        [Tooltip("Ciudad: columnas con compuertas de esclusa ('H') y la compuerta de cada una")]
        public int[] gateColumns = new int[0];
        public FloodGate[] gateDoors = new FloodGate[0];

        [Header("Holguras")]
        [Tooltip("Margen bajo la superficie del agua para que el casco no asome")]
        public float surfaceMargin = 0.35f;
        [Tooltip("Margen sobre el límite de presión")]
        public float pressureMargin = 0.35f;
        [Tooltip("Costo extra por pared vecina: el riel prefiere ir por el centro de las salas")]
        public float wallPenalty = 0.6f;
        [Tooltip("Holgura lateral al comprobar que un tramo recto está libre (radio del casco 0.62 + margen)")]
        public float hullRadius = 1.0f;
        [Tooltip("Cuánto se puede correr cada punto hacia el centro de un pasillo angosto")]
        public float maxCentering = 1.0f;

        private int w, h;
        private float[] dist;
        private int[] prev;
        private bool[] passCache;
        private int startIndex = -1;
        private readonly MinHeap heap = new MinHeap();

        private static readonly int[] DC = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] DR = { 0, 0, 1, -1, 1, -1, 1, -1 };

        public int Width { get { return w; } }
        public int Height { get { return h; } }

        private void Awake()
        {
            Instance = this;
            h = rows != null ? rows.Length : 0;
            w = 0;
            if (rows != null) foreach (string r in rows) w = Mathf.Max(w, r.Length);
            dist = new float[w * h];
            prev = new int[w * h];
            passCache = new bool[w * h];
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ celdas

        public char At(int c, int r)
        {
            if (r < 0 || r >= h || c < 0) return '#';
            string row = rows[r];
            return c < row.Length ? row[c] : '#';
        }

        public Vector2Int CellOf(Vector3 p)
        {
            return new Vector2Int(Mathf.RoundToInt(p.x / cellSize), Mathf.RoundToInt(-p.y / cellSize));
        }

        public Vector3 CellCenter(int c, int r, float z)
        {
            return new Vector3(c * cellSize, -r * cellSize, z);
        }

        /// <summary>Objetos que ocupan la celda: el riel pasa alrededor, nunca por encima.</summary>
        public static bool IsOccupiedChar(char ch)
        {
            return ch == 'K' || ch == 'P' || ch == 'Q' || ch == '$' || ch == 'M' || ch == 'F' || ch == 'D' || ch == 'R';
        }

        /// <summary>Sólidos del mapa: suelo, diques, edificios de la avenida, puentes y rejas.</summary>
        public static bool IsSolidChar(char ch)
        {
            return ch == '#' || ch == 'X' || ch == 'B' || ch == '=' || ch == '|' || ch == '-';
        }

        private bool GateColumnOpen(int c)
        {
            for (int i = 0; i < gateColumns.Length && i < gateDoors.Length; i++)
            {
                if (gateColumns[i] == c) return gateDoors[i] != null && gateDoors[i].IsOpen;
            }
            return false;
        }

        /// <summary>
        /// ¿Puede el riel pasar por esta celda? Usa la foto del mundo tomada en el último ComputeFrom
        /// (el agua y las compuertas cambian despacio; se recalcula en cada replanificación).
        /// </summary>
        public bool Passable(int c, int r)
        {
            if (c < 0 || r < 0 || c >= w || r >= h) return false;
            return passCache[r * w + c];
        }

        private void RefreshPassable()
        {
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                    passCache[r * w + c] = PassableNow(c, r);
        }

        /// <summary>¿Puede el riel pasar por esta celda AHORA (agua, presión, compuerta)?</summary>
        public bool PassableNow(int c, int r)
        {
            char ch = At(c, r);
            if (IsSolidChar(ch)) return false;
            if (ch == 'G' && (gate == null || !gate.IsOpen)) return false;
            if (ch == 'H' && !GateColumnOpen(c)) return false;
            if (IsOccupiedChar(ch)) return false;

            Vector3 p = CellCenter(c, r, 0f);
            if (p.y + surfaceMargin > WaterZone.SurfaceAt(p)) return false;
            // límite de presión (en la ciudad se mide desde la superficie del agua local)
            if (GameManager.Instance != null && p.y - pressureMargin < GameManager.Instance.DepthLimitAt(p)) return false;
            return true;
        }

        private bool PointPassable(float x, float y)
        {
            return Passable(Mathf.RoundToInt(x / cellSize), Mathf.RoundToInt(-y / cellSize));
        }

        private float WallCost(int c, int r)
        {
            int n = 0;
            if (!Passable(c + 1, r)) n++;
            if (!Passable(c - 1, r)) n++;
            if (!Passable(c, r + 1)) n++;
            if (!Passable(c, r - 1)) n++;
            return n * wallPenalty;
        }

        // ------------------------------------------------------------------ distancias

        /// <summary>Celda transitable más cercana a un punto (para empezar la búsqueda).</summary>
        public bool NearestPassable(Vector3 p, out Vector2Int cell)
        {
            Vector2Int c0 = CellOf(p);
            cell = c0;
            float best = float.MaxValue;
            bool found = false;
            for (int dr = -2; dr <= 2; dr++)
            {
                for (int dc = -2; dc <= 2; dc++)
                {
                    int c = c0.x + dc, r = c0.y + dr;
                    if (!Passable(c, r)) continue;
                    float d = Vector2.Distance(new Vector2(c * cellSize, -r * cellSize), new Vector2(p.x, p.y));
                    if (d < best) { best = d; cell = new Vector2Int(c, r); found = true; }
                }
            }
            return found;
        }

        /// <summary>Dijkstra desde la posición: distancia por el riel a todas las celdas.</summary>
        public bool ComputeFrom(Vector3 position)
        {
            startIndex = -1;
            RefreshPassable();
            for (int i = 0; i < dist.Length; i++) { dist[i] = float.PositiveInfinity; prev[i] = -1; }
            Vector2Int start;
            if (!NearestPassable(position, out start)) return false;

            startIndex = start.y * w + start.x;
            dist[startIndex] = 0f;
            heap.Clear();
            heap.Push(startIndex, 0f);
            while (heap.Count > 0)
            {
                float d;
                int idx = heap.Pop(out d);
                if (d > dist[idx]) continue;
                int c = idx % w, r = idx / w;
                for (int k = 0; k < 8; k++)
                {
                    int nc = c + DC[k], nr = r + DR[k];
                    if (nc < 0 || nr < 0 || nc >= w || nr >= h) continue;
                    if (!Passable(nc, nr)) continue;
                    bool diagonal = DC[k] != 0 && DR[k] != 0;
                    if (diagonal && (!Passable(c + DC[k], r) || !Passable(c, r + DR[k]))) continue; // sin cortar esquinas
                    float nd = d + (diagonal ? 1.41421f : 1f) * cellSize + WallCost(nc, nr);
                    int nIdx = nr * w + nc;
                    if (nd < dist[nIdx])
                    {
                        dist[nIdx] = nd;
                        prev[nIdx] = idx;
                        heap.Push(nIdx, nd);
                    }
                }
            }
            return true;
        }

        public float DistanceTo(Vector2Int cell)
        {
            if (cell.x < 0 || cell.y < 0 || cell.x >= w || cell.y >= h) return float.PositiveInfinity;
            return dist[cell.y * w + cell.x];
        }

        // ------------------------------------------------------------------ aproximación a un objetivo

        /// <summary>
        /// Celda del riel desde donde trabajar sobre un objetivo: alcanzable, a 1.5–5 m (en el plano del
        /// mapa) y sin paredes entre medio (las rejillas no importan: el brazo pasa entre los barrotes).
        /// Se prefiere la más cercana por el riel y a unos 3 m del objetivo.
        /// </summary>
        public bool FindApproach(Vector3 target, float minDistance, float maxDistance, float ideal, out Vector2Int best, out float score)
        {
            Vector2Int c0 = CellOf(target);
            best = c0;
            score = float.PositiveInfinity;
            for (int dr = -3; dr <= 3; dr++)
            {
                for (int dc = -3; dc <= 3; dc++)
                {
                    int c = c0.x + dc, r = c0.y + dr;
                    float dRail = DistanceTo(new Vector2Int(c, r));
                    if (float.IsInfinity(dRail)) continue;
                    Vector2 p = new Vector2(c * cellSize, -r * cellSize);
                    float d = Vector2.Distance(p, new Vector2(target.x, target.y));
                    if (d < minDistance || d > maxDistance) continue;
                    if (!ArmClear(p, new Vector2(target.x, target.y))) continue;
                    float s = dRail + 2f * Mathf.Abs(d - ideal);
                    if (s < score) { score = s; best = new Vector2Int(c, r); }
                }
            }
            return !float.IsInfinity(score);
        }

        /// <summary>Punto de llegada directo (sin distancia mínima), p. ej. el punto de calibración o el muelle.</summary>
        public bool FindArrival(Vector3 point, out Vector2Int best, out float score)
        {
            best = CellOf(point);
            score = float.PositiveInfinity;
            for (int dr = -2; dr <= 2; dr++)
            {
                for (int dc = -2; dc <= 2; dc++)
                {
                    int c = best.x + dc, r = best.y + dr;
                    float dRail = DistanceTo(new Vector2Int(c, r));
                    if (float.IsInfinity(dRail)) continue;
                    float d = Vector2.Distance(new Vector2(c * cellSize, -r * cellSize), new Vector2(point.x, point.y));
                    float s = dRail + 3f * d;
                    if (s < score) { score = s; best = new Vector2Int(c, r); }
                }
            }
            return !float.IsInfinity(score);
        }

        private bool ArmClear(Vector2 from, Vector2 to)
        {
            Vector2Int targetCell = CellOf(new Vector3(to.x, to.y, 0f));
            float length = Vector2.Distance(from, to);
            int n = Mathf.Max(1, Mathf.CeilToInt(length / 0.2f));
            for (int i = 1; i < n; i++)
            {
                Vector2 p = Vector2.Lerp(from, to, i / (float)n);
                Vector2Int cell = CellOf(new Vector3(p.x, p.y, 0f));
                if (cell == targetCell) continue; // el objetivo puede estar en su propio nicho
                char ch = At(cell.x, cell.y);
                if (ch == '#' || ch == 'G' || ch == 'X' || ch == 'B' || ch == '=') return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ ruta

        /// <summary>
        /// Ruta desde la posición actual hasta la celda meta (debe llamarse después de ComputeFrom).
        /// Los puntos van a la profundidad (Z) del carril indicado.
        /// </summary>
        public bool BuildPath(Vector3 from, Vector2Int goal, float laneZ, List<Vector3> result)
        {
            result.Clear();
            if (startIndex < 0) return false;
            int gIdx = goal.y * w + goal.x;
            if (goal.x < 0 || goal.y < 0 || goal.x >= w || goal.y >= h || float.IsInfinity(dist[gIdx])) return false;

            List<Vector2> cells = new List<Vector2>();
            int guard = w * h;
            for (int idx = gIdx; idx >= 0 && guard-- > 0; idx = prev[idx])
            {
                cells.Add(new Vector2((idx % w) * cellSize, -(idx / w) * cellSize));
                if (idx == startIndex) break;
            }
            cells.Reverse();

            // El primer punto es el submarino mismo; la celda de inicio solo sirve si el tramo no está libre.
            // Cada punto se corre hacia el centro del pasillo para que el casco no roce las paredes.
            List<Vector2> pts = new List<Vector2> { new Vector2(from.x, from.y) };
            for (int i = 0; i < cells.Count; i++)
            {
                if (i == 0 && cells.Count > 1 && LineClear(pts[0], cells[1])) continue;
                pts.Add(Centered(cells[i]));
            }

            // Tirar de la cuerda: desde cada punto, saltar al más lejano visible con el ancho del casco
            int a = 0;
            while (a < pts.Count - 1)
            {
                int b = pts.Count - 1;
                while (b > a + 1 && !LineClear(pts[a], pts[b])) b--;
                result.Add(new Vector3(pts[b].x, pts[b].y, laneZ));
                a = b;
            }
            if (result.Count == 0) result.Add(new Vector3(pts[pts.Count - 1].x, pts[pts.Count - 1].y, laneZ));
            return true;
        }

        /// <summary>Distancia libre desde un punto en una dirección (hasta maxDistance).</summary>
        private float FreeExtent(Vector2 p, Vector2 dir, float maxDistance)
        {
            float d = 0f;
            while (d < maxDistance)
            {
                d += 0.25f;
                Vector2 q = p + dir * d;
                if (!PointPassable(q.x, q.y)) return d - 0.25f;
            }
            return maxDistance;
        }

        /// <summary>En pasillos y pozos angostos, lleva el punto al centro (a lo más maxCentering).</summary>
        private Vector2 Centered(Vector2 p)
        {
            Vector2 q = p;
            float left = FreeExtent(q, Vector2.left, 4f), right = FreeExtent(q, Vector2.right, 4f);
            if (left + right < 6f) q.x += Mathf.Clamp((right - left) * 0.5f, -maxCentering, maxCentering);
            float down = FreeExtent(q, Vector2.down, 4f), up = FreeExtent(q, Vector2.up, 4f);
            if (down + up < 6f) q.y += Mathf.Clamp((up - down) * 0.5f, -maxCentering, maxCentering);
            return PointPassable(q.x, q.y) ? q : p;
        }

        /// <summary>Ruta completa por el riel hasta un punto (p. ej. la balsa del barrio al terminar la sesión).</summary>
        public bool PathTo(Vector3 from, Vector3 point, List<Vector3> result)
        {
            result.Clear();
            if (!ComputeFrom(from)) return false;
            Vector2Int cell;
            float score;
            if (!FindArrival(point, out cell, out score)) return false;
            if (!BuildPath(from, cell, 0f, result)) return false;
            result.Add(point);
            return true;
        }

        /// <summary>¿El casco cabe a lo largo del segmento? (centro y dos bordes laterales)</summary>
        public bool LineClear(Vector2 a, Vector2 b)
        {
            float length = Vector2.Distance(a, b);
            if (length < 0.0001f) return PointPassable(a.x, a.y);
            Vector2 u = (b - a) / length;
            Vector2 side = new Vector2(-u.y, u.x) * hullRadius;
            int n = Mathf.CeilToInt(length / 0.25f);
            for (int i = 0; i <= n; i++)
            {
                Vector2 p = a + u * Mathf.Min(length, i * 0.25f);
                if (!PointPassable(p.x, p.y)) return false;
                Vector2 q = p + side;
                if (!PointPassable(q.x, q.y)) return false;
                q = p - side;
                if (!PointPassable(q.x, q.y)) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ cola de prioridad

        private class MinHeap
        {
            private readonly List<int> items = new List<int>();
            private readonly List<float> keys = new List<float>();

            public int Count { get { return items.Count; } }

            public void Clear() { items.Clear(); keys.Clear(); }

            public void Push(int item, float key)
            {
                items.Add(item);
                keys.Add(key);
                int i = items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (keys[parent] <= keys[i]) break;
                    Swap(i, parent);
                    i = parent;
                }
            }

            public int Pop(out float key)
            {
                int top = items[0];
                key = keys[0];
                int last = items.Count - 1;
                items[0] = items[last];
                keys[0] = keys[last];
                items.RemoveAt(last);
                keys.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < items.Count && keys[l] < keys[m]) m = l;
                    if (r < items.Count && keys[r] < keys[m]) m = r;
                    if (m == i) break;
                    Swap(i, m);
                    i = m;
                }
                return top;
            }

            private void Swap(int a, int b)
            {
                int ti = items[a]; items[a] = items[b]; items[b] = ti;
                float tk = keys[a]; keys[a] = keys[b]; keys[b] = tk;
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || dist == null) return;
            Gizmos.color = new Color(0.3f, 1f, 0.9f, 0.25f);
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    if (!float.IsInfinity(dist[r * w + c])) Gizmos.DrawWireCube(CellCenter(c, r, 0f), Vector3.one * 0.3f);
                }
            }
        }
    }
}
