using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatSquad.RescateProfundo.EditorTools
{
    /// <summary>
    /// Primera persona: GATÓPOLIS, la ciudad inundada.
    /// El mapa ASCII (CityLayout) describe el corte de la avenida principal. Todo lo que no es avenida
    /// se genera: edificios a ambos lados (con ventanas que se encienden al liberar el barrio), faroles,
    /// autos, diques de concreto que cruzan todo el ancho y el mar abierto con su lecho de arena.
    /// </summary>
    public static partial class RescateProfundoSceneBuilder
    {
        /// <summary>Mitad del ancho del mundo en Z (más allá, niebla y un límite invisible).</summary>
        private const float CityHalfZ = 42f;
        /// <summary>Mitad del ancho de la avenida (el "corte" jugable del mapa).</summary>
        private const float AvenueHalf = 5f;

        private class CityInfo
        {
            public Dictionary<string, WaterZone> zones = new Dictionary<string, WaterZone>();
            public Dictionary<string, CityDistrict> districts = new Dictionary<string, CityDistrict>();
            public Dictionary<string, List<Renderer>> windows = new Dictionary<string, List<Renderer>>();
            public Dictionary<string, List<Light>> lamps = new Dictionary<string, List<Light>>();
            public List<LockStation> locks = new List<LockStation>();
            public List<int> gateColumns = new List<int>();
            public List<FloodGate> gateDoors = new List<FloodGate>();
            public List<PumpStation> pumps = new List<PumpStation>();
            public List<Dock> rafts = new List<Dock>();
            public List<Bounds> buildings = new List<Bounds>();
        }

        private static CityInfo city;

        // ================================================================== consultas del mapa

        private static int ZoneIndexOfColumn(int c)
        {
            for (int i = 0; i < CityLayout.Zones.Length; i++)
            {
                if (c >= CityLayout.Zones[i].col0 && c <= CityLayout.Zones[i].col1) return i;
            }
            return 0;
        }

        private static string ZoneIdOfColumn(int c) { return CityLayout.Zones[ZoneIndexOfColumn(c)].id; }

        /// <summary>
        /// Hueco en el suelo (bolsillo con bidón bajo escombros, alcantarilla con caja): en la avenida es agua,
        /// pero fuera de ella el suelo sigue macizo (no es una zanja que cruce toda la ciudad).
        /// </summary>
        private static bool IsPocket(int c, int r)
        {
            char ch = At(c, r);
            if ("RFM-".IndexOf(ch) < 0) return false;
            bool sideSolid = At(c - 1, r) == '#' || At(c + 1, r) == '#' || IsPocketChar(At(c - 1, r)) || IsPocketChar(At(c + 1, r));
            char below = At(c, r + 1);
            return sideSolid && (below == '#' || IsPocketChar(below)) && (At(c - 1, r) == '#' || At(c + 1, r) == '#');
        }

        private static bool IsPocketChar(char ch) { return "RFM-".IndexOf(ch) >= 0; }

        /// <summary>Primera fila de suelo ('#', 'X' o bolsillo) desde arriba, sin contar la corona del dique.</summary>
        private static int GroundRow(int c)
        {
            for (int r = 3; r < H; r++)
            {
                char ch = At(c, r);
                if (ch == '#' || (ch == 'X' && r > 3) || IsPocket(c, r)) return r;
            }
            return H;
        }

        private static float StreetTopY(int c) { return -GroundRow(c) * C + C * 0.5f; }

        // ================================================================== escena completa

        private static void BuildCity(GameObject world, GameObject level, GameObject waterRoot, GameObject itemsRoot,
                                      GameObject decoRoot, GameObject pointsRoot, GameObject actorsRoot, GameObject systemsRoot)
        {
            city = new CityInfo();
            BuildEnvironment(systemsRoot);

            EditorUtility.DisplayProgressBar("Cat Squad", "Calles y diques de Gatópolis...", 0.3f);
            BuildCityGround(level);
            BuildGrates(level);
            BuildCityBuildings(level);

            Dictionary<char, Transform> points = BuildPoints(pointsRoot);
            Vector3 subSpawn = CellCenter(FindChar('S'));
            subSpawn.y = -5.25f;

            EditorUtility.DisplayProgressBar("Cat Squad", "Agua, esclusas y barrios...", 0.45f);
            BuildCityWater(waterRoot);
            BuildLocks(itemsRoot);

            EditorUtility.DisplayProgressBar("Cat Squad", "Objetos de la ciudad...", 0.55f);
            LevelItems items = BuildCityItems(itemsRoot, decoRoot);
            BuildDistricts(world);

            EditorUtility.DisplayProgressBar("Cat Squad", "Submarino, brazo y gatos...", 0.7f);
            SubmarineController sub = BuildSubmarine(actorsRoot, subSpawn);
            ManipulatorArm arm = BuildArm(actorsRoot, sub);
            GuideCat guide = BuildGuide(actorsRoot, subSpawn + new Vector3(16f, 0.3f, 0f));
            Dock dock = BuildDock(world, subSpawn);
            dock.displayName = "Base de rescate";
            BuildRafts(world);

            EditorUtility.DisplayProgressBar("Cat Squad", "Cabina y mapa lateral...", 0.85f);
            Camera cam = BuildCockpitCamera(sub);
            BuildSideMapCamera(systemsRoot, sub);

            // Lab 05: lanzador (latas, lastres, boyas) y sus blancos
            EditorUtility.DisplayProgressBar("Cat Squad", "Lanzador y blancos (Lab 05)...", 0.8f);
            BuildLauncher(actorsRoot, sub, arm, cam);
            BuildLaunchTargets(itemsRoot);
            BuildSystems(systemsRoot, sub, arm, guide, dock, cam, city.zones, items, points, subSpawn);
        }

        // ================================================================== suelo, diques y estructuras de la avenida

        private static bool IsGroundCell(int c, int r) { char ch = At(c, r); return ch == '#'; }
        private static bool IsDikeCell(int c, int r) { char ch = At(c, r); return ch == 'X'; }

        private static void BuildCityGround(GameObject level)
        {
            // Suelo y diques: bloques fusionados que cruzan todo el ancho del mundo (Z)
            bool[,] used = new bool[W, H];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int r = 0; r < H; r++)
                {
                    for (int c = 0; c < W; c++)
                    {
                        if (used[c, r]) continue;
                        bool ok = pass == 0 ? IsGroundCell(c, r) : IsDikeCell(c, r);
                        if (!ok) continue;
                        int w = 1;
                        while (c + w < W && !used[c + w, r] && (pass == 0 ? IsGroundCell(c + w, r) : IsDikeCell(c + w, r))
                               && ZoneKind(c + w) == ZoneKind(c)) w++;
                        int h = 1;
                        bool grow = true;
                        while (grow && r + h < H)
                        {
                            for (int k = 0; k < w; k++)
                            {
                                bool cell = pass == 0 ? IsGroundCell(c + k, r + h) : IsDikeCell(c + k, r + h);
                                if (used[c + k, r + h] || !cell) { grow = false; break; }
                            }
                            if (grow) h++;
                        }
                        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) used[c + x, r + y] = true;
                        string mat = pass == 1 ? "Concreto" : (ZoneKind(c) == 0 ? "Arena" : "Asfalto");
                        CityBlock(level.transform, pass == 1 ? "Dique" : "Suelo", c, r, w, h, -CityHalfZ, CityHalfZ, mat, pass == 1 ? 4f : 6f);
                    }
                }
            }

            // Edificios dentro de la avenida ('B') y puentes ('='): solo ocupan el ancho de la avenida
            bool[,] usedB = new bool[W, H];
            foreach (char kind in new[] { 'B', '=' })
            {
                for (int r = 0; r < H; r++)
                {
                    for (int c = 0; c < W; c++)
                    {
                        if (usedB[c, r] || At(c, r) != kind) continue;
                        int w = 1;
                        while (c + w < W && !usedB[c + w, r] && At(c + w, r) == kind) w++;
                        int h = 1;
                        bool grow = true;
                        while (grow && r + h < H)
                        {
                            for (int k = 0; k < w; k++) if (usedB[c + k, r + h] || At(c + k, r + h) != kind) { grow = false; break; }
                            if (grow) h++;
                        }
                        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) usedB[c + x, r + y] = true;
                        if (kind == 'B')
                        {
                            bool sea = ZoneKind(c) == 0;
                            GameObject b = CityBlock(level.transform, sea ? "Ruina" : "EdificioAvenida", c, r, w, h, -AvenueHalf, AvenueHalf,
                                                     sea ? "ParedRoca" : FacadeMatName(c + r), sea ? 3f : 6f);
                            if (!sea) AddWindows(ZoneIdOfColumn(c), b.GetComponent<Renderer>());
                            if (!sea) RoofProps(level.transform, c * C - C * 0.5f, (c + w) * C - C * 0.5f, -r * C + C * 0.5f, -AvenueHalf, AvenueHalf, c * 7 + r);
                        }
                        else
                        {
                            CityBlock(level.transform, "Puente", c, r, w, h, -AvenueHalf, AvenueHalf, "Concreto", 4f, 0.6f);
                            BridgeRails(level.transform, c, r, w);
                        }
                    }
                }
            }

            // Bolsillos del suelo: macizos fuera de la avenida. Nichos dentro de ruinas: con tapas al frente y al fondo
            for (int r = 0; r < H; r++)
            {
                for (int c = 0; c < W; c++)
                {
                    if (IsPocket(c, r))
                    {
                        CityBlock(level.transform, "SueloBolsillo", c, r, 1, 1, AvenueHalf, CityHalfZ, ZoneKind(c) == 0 ? "Arena" : "Asfalto", 6f);
                        CityBlock(level.transform, "SueloBolsillo", c, r, 1, 1, -CityHalfZ, -AvenueHalf, ZoneKind(c) == 0 ? "Arena" : "Asfalto", 6f);
                    }
                    else if (At(c, r) == 'M' && NicheWalls(c, r) >= 3)
                    {
                        CityBlock(level.transform, "TapaNicho", c, r, 1, 1, AvenueHalf - 0.8f, AvenueHalf, "ParedRoca", 3f);
                        CityBlock(level.transform, "TapaNicho", c, r, 1, 1, -AvenueHalf, -AvenueHalf + 0.8f, "ParedRoca", 3f);
                    }
                }
            }

            // Muros de las esclusas fuera de la avenida: el dique es macizo salvo la cámara
            foreach (Vector2Int lk in LockColumns())
            {
                int floor = GroundRow(lk.x + 1);
                float x0 = lk.x * C - C * 0.5f, x1 = lk.y * C + C * 0.5f;
                float yTop = -1 * C + C * 0.5f, yBottom = -floor * C + C * 0.5f;
                foreach (float s in new[] { -1f, 1f })
                {
                    float z0 = s > 0 ? AvenueHalf : -CityHalfZ, z1 = s > 0 ? CityHalfZ : -AvenueHalf;
                    GameObject wall = Primitive(PrimitiveType.Cube, "DiqueLateral", level.transform,
                        new Vector3((x0 + x1) * 0.5f, (yTop + yBottom) * 0.5f, (z0 + z1) * 0.5f),
                        new Vector3(x1 - x0, yTop - yBottom, z1 - z0), mats["Concreto"], true);
                    wall.AddComponent<WorldUVBox>().tileSize = 4f;
                    wall.isStatic = true;
                }
            }

            // Límites invisibles del mundo
            float midY = -H * C * 0.5f;
            BoundsWall(level.transform, "LimiteIzq", new Vector3(-1.5f, midY, 0f), new Vector3(1f, H * C + 20f, CityHalfZ * 2f + 4f));
            BoundsWall(level.transform, "LimiteDer", new Vector3(W * C - 0.5f, midY, 0f), new Vector3(1f, H * C + 20f, CityHalfZ * 2f + 4f));
            BoundsWall(level.transform, "LimiteFondo", new Vector3(W * C * 0.5f, midY, CityHalfZ + 0.5f), new Vector3(W * C + 4f, H * C + 20f, 1f));
            BoundsWall(level.transform, "LimiteFrente", new Vector3(W * C * 0.5f, midY, -CityHalfZ - 0.5f), new Vector3(W * C + 4f, H * C + 20f, 1f));
        }

        private static int NicheWalls(int c, int r)
        {
            int n = 0;
            foreach (Vector2Int d in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
            {
                char ch = At(c + d.x, r + d.y);
                if (ch == 'B' || ch == '|' || ch == '-') n++;
            }
            return n;
        }

        /// <summary>0 = mar, 1 = esclusa/dique, 2 = barrio (para no fusionar arena con asfalto).</summary>
        private static int ZoneKind(int c)
        {
            CityLayout.Zone z = CityLayout.Zones[ZoneIndexOfColumn(c)];
            if (z.id == "mar") return 0;
            return z.isLock ? 1 : 2;
        }

        private static GameObject CityBlock(Transform parent, string name, int c, int r, int w, int h, float z0, float z1,
                                            string mat, float tile, float heightScale = 1f)
        {
            float x0 = c * C - C * 0.5f, x1 = (c + w) * C - C * 0.5f;
            float yTop = -r * C + C * 0.5f, yBottom = -(r + h) * C + C * 0.5f;
            if (heightScale < 1f) yBottom = yTop - (yTop - yBottom) * heightScale;
            GameObject go = Primitive(PrimitiveType.Cube, name, parent,
                new Vector3((x0 + x1) * 0.5f, (yTop + yBottom) * 0.5f, (z0 + z1) * 0.5f),
                new Vector3(x1 - x0, yTop - yBottom, z1 - z0), mats[mat], true);
            go.AddComponent<WorldUVBox>().tileSize = tile;
            go.isStatic = true;
            return go;
        }

        private static IEnumerable<Vector2Int> LockColumns()
        {
            foreach (CityLayout.Zone z in CityLayout.Zones)
            {
                if (z.isLock) yield return new Vector2Int(z.col0, z.col1);
            }
        }

        private static void BridgeRails(Transform parent, int c, int r, int w)
        {
            float x0 = c * C - C * 0.5f, x1 = (c + w) * C - C * 0.5f;
            float top = -r * C + C * 0.5f;
            foreach (float z in new[] { -AvenueHalf + 0.3f, AvenueHalf - 0.3f })
            {
                GameObject rail = Primitive(PrimitiveType.Cube, "Baranda", parent, new Vector3((x0 + x1) * 0.5f, top + 0.9f, z),
                    new Vector3(x1 - x0, 0.08f, 0.08f), mats["Rejilla"]);
                rail.isStatic = true;
                for (float x = x0 + 0.5f; x < x1; x += 1.2f)
                {
                    GameObject post = Primitive(PrimitiveType.Cube, "Poste", parent, new Vector3(x, top + 0.45f, z),
                        new Vector3(0.06f, 0.9f, 0.06f), mats["Rejilla"]);
                    post.isStatic = true;
                }
            }
        }

        // ================================================================== edificios de las veredas

        private static string FacadeMatName(int seed)
        {
            string[] names = { "FachadaCrema", "FachadaSalmon", "FachadaCeleste", "FachadaMenta", "FachadaLadrillo" };
            return names[Mathf.Abs(seed) % names.Length];
        }

        private static void AddWindows(string zoneId, Renderer r)
        {
            if (r == null) return;
            List<Renderer> list;
            if (!city.windows.TryGetValue(zoneId, out list)) { list = new List<Renderer>(); city.windows[zoneId] = list; }
            list.Add(r);
        }

        /// <summary>Techo máximo permitido en X (para que ningún edificio tape un cascabel secreto de las azoteas).</summary>
        private static float MaxRoofAt(float x0, float x1, float zSide)
        {
            float max = 8f;
            foreach (Vector2Int cell in FindAll('$'))
            {
                float bz = SecretBellZ(cell);
                if (Mathf.Abs(bz) < AvenueHalf || Mathf.Sign(bz) != Mathf.Sign(zSide)) continue;
                float bx = cell.x * C;
                if (bx > x0 - 6f && bx < x1 + 6f) max = Mathf.Min(max, -cell.y * C - 3f);
            }
            return max;
        }

        private static void BuildCityBuildings(GameObject level)
        {
            GameObject root = Child(level, "Edificios");
            System.Random rnd = new System.Random(5);
            foreach (CityLayout.Zone z in CityLayout.Zones)
            {
                if (z.isLock || z.id == "mar") continue;
                // la vereda empieza después del muro de la esclusa y termina antes del siguiente
                float xStart = z.col0 * C - C * 0.5f;
                float xEnd = z.col1 * C + C * 0.5f;
                if (z.col1 >= W - 1) xEnd = (W - 1) * C - C * 0.5f;
                foreach (float side in new[] { -1f, 1f })
                {
                    for (int row = 0; row < 2; row++)
                    {
                        // Ciudad ABIERTA: edificios separados por callejones y plazas, casi todos bajo el
                        // nivel del mar (sobre ellos hay agua libre) y solo algunas torres que asoman.
                        float x = xStart;
                        while (x < xEnd - 2f)
                        {
                            float width = Mathf.Min(xEnd - x, 6f + (float)rnd.NextDouble() * 7f);
                            if (xEnd - (x + width) < 3f) width = xEnd - x;
                            bool gap = rnd.NextDouble() < (row == 0 ? 0.35 : 0.5);
                            if (gap && width < xEnd - x) { x += 4f + (float)rnd.NextDouble() * 5f; continue; }
                            int midCol = Mathf.RoundToInt((x + width * 0.5f) / C);
                            float street = StreetTopY(midCol);
                            float setback = row == 0 ? (float)rnd.NextDouble() * 3f : 0f;
                            float zNear = row == 0 ? AvenueHalf + setback : AvenueHalf + 18f + (float)rnd.NextDouble() * 4f;
                            float zFar = Mathf.Min(zNear + 8f + (float)rnd.NextDouble() * 8f, CityHalfZ);
                            float height = 6f + (float)rnd.NextDouble() * 12f;
                            bool tower = rnd.NextDouble() < 0.15;
                            if (tower) height = 20f + (float)rnd.NextDouble() * 22f;
                            float roof = Mathf.Min(street + height, MaxRoofAt(x, x + width, side));
                            roof = Mathf.Max(roof, street + 5f);
                            float z0 = side > 0 ? zNear : -zFar, z1 = side > 0 ? zFar : -zNear;
                            string mat = FacadeMatName(midCol * 3 + row + (side > 0 ? 1 : 0));
                            GameObject b = Primitive(PrimitiveType.Cube, tower ? "Torre" : "Edificio", root.transform,
                                new Vector3(x + width * 0.5f, (street + roof) * 0.5f, (z0 + z1) * 0.5f),
                                new Vector3(width - 0.3f, roof - street, z1 - z0), mats[mat], true);
                            b.AddComponent<WorldUVBox>().tileSize = 6f;
                            city.buildings.Add(new Bounds(new Vector3(x + width * 0.5f, (street + roof) * 0.5f, (z0 + z1) * 0.5f),
                                                          new Vector3(width - 0.3f, roof - street, z1 - z0)));
                            b.isStatic = true;
                            AddWindows(z.id, b.GetComponent<Renderer>());
                            RoofProps(root.transform, x, x + width, roof, z0, z1, midCol * 13 + row);
                            x += width;
                        }
                    }
                }
                StreetFurniture(root.transform, z, rnd);
            }
        }

        private static void RoofProps(Transform parent, float x0, float x1, float roof, float z0, float z1, int seed)
        {
            // cornisa + tanque de agua o antena
            GameObject cornice = Primitive(PrimitiveType.Cube, "Cornisa", parent, new Vector3((x0 + x1) * 0.5f, roof + 0.15f, (z0 + z1) * 0.5f),
                new Vector3(x1 - x0 - 0.1f, 0.3f, Mathf.Abs(z1 - z0) + 0.2f), mats["Concreto"]);
            cornice.isStatic = true;
            float cx = x0 + (x1 - x0) * (0.3f + 0.4f * ((seed * 37) % 10) / 10f);
            float cz = (z0 + z1) * 0.5f;
            if (seed % 3 == 0)
            {
                GameObject tank = Primitive(PrimitiveType.Cylinder, "Tanque", parent, new Vector3(cx, roof + 1.3f, cz), new Vector3(1.4f, 1f, 1.4f), mats["Madera"]);
                tank.isStatic = true;
            }
            else if (seed % 3 == 1)
            {
                GameObject ant = Primitive(PrimitiveType.Cylinder, "Antena", parent, new Vector3(cx, roof + 1.8f, cz), new Vector3(0.08f, 1.8f, 0.08f), mats["Rejilla"]);
                ant.isStatic = true;
            }
        }

        /// <summary>Faroles (se encienden al liberar el barrio) y autos estacionados junto a las fachadas.</summary>
        private static void StreetFurniture(Transform parent, CityLayout.Zone z, System.Random rnd)
        {
            List<Light> lamps;
            if (!city.lamps.TryGetValue(z.id, out lamps)) { lamps = new List<Light>(); city.lamps[z.id] = lamps; }
            int i = 0;
            for (int c = z.col0 + 2; c <= z.col1 - 1; c += 6, i++)
            {
                if (At(c, GroundRow(c) - 1) != '.' && At(c, GroundRow(c) - 1) != 'w') continue;
                float street = StreetTopY(c);
                float zs = (i % 2 == 0) ? -AvenueHalf + 0.5f : AvenueHalf - 0.5f;
                Vector3 b = new Vector3(c * C, street, zs);
                GameObject pole = Primitive(PrimitiveType.Cylinder, "Farol", parent, b + Vector3.up * 2.2f, new Vector3(0.12f, 2.2f, 0.12f), mats["SubOscuro"]);
                pole.isStatic = true;
                GameObject head = Primitive(PrimitiveType.Sphere, "Lampara", parent, b + Vector3.up * 4.5f, Vector3.one * 0.45f, mats["FaroLuz"]);
                head.isStatic = true;
                Light l = new GameObject("LuzFarol").AddComponent<Light>();
                l.transform.SetParent(head.transform, false);
                l.type = LightType.Point;
                l.range = 9f;
                l.intensity = 2f;
                l.color = new Color(1f, 0.82f, 0.5f);
                l.enabled = false;
                lamps.Add(l);
            }
            for (int c = z.col0 + 4; c <= z.col1 - 2; c += 5)
            {
                if (rnd.NextDouble() < 0.45) continue;
                if (At(c, GroundRow(c) - 1) != '.') continue;
                float street = StreetTopY(c);
                float zs = rnd.NextDouble() < 0.5 ? -AvenueHalf + 1.1f : AvenueHalf - 1.1f;
                string[] carMats = { "SubAcento", "BombaCuerpo", "GorraAzul", "FaroBlanco" };
                Material m = mats[carMats[rnd.Next(carMats.Length)]];
                GameObject car = new GameObject("Auto");
                car.transform.SetParent(parent, false);
                car.transform.position = new Vector3(c * C, street, zs);
                car.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 20f - 10f, (float)rnd.NextDouble() * 6f - 3f);
                GameObject body = Primitive(PrimitiveType.Cube, "Carroceria", car.transform, car.transform.position + Vector3.up * 0.45f, new Vector3(3.2f, 0.7f, 1.5f), m, true);
                body.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                GameObject cab = Primitive(PrimitiveType.Cube, "Cabina", car.transform, car.transform.position + Vector3.up * 1.05f, new Vector3(1.7f, 0.55f, 1.35f), mats["SubVidrio"]);
                cab.transform.localPosition = new Vector3(-0.2f, 1.05f, 0f);
                for (int k = 0; k < 4; k++)
                {
                    GameObject wheel = Primitive(PrimitiveType.Cylinder, "Rueda", car.transform, car.transform.position, new Vector3(0.6f, 0.12f, 0.6f), mats["SubOscuro"]);
                    wheel.transform.localPosition = new Vector3(k < 2 ? -1f : 1f, 0.28f, k % 2 == 0 ? -0.72f : 0.72f);
                    wheel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                }
            }
        }

        // ================================================================== agua y barrios

        private static void BuildCityWater(GameObject waterRoot)
        {
            foreach (CityLayout.Zone z in CityLayout.Zones)
            {
                WaterZone wz = Zone(waterRoot, z.id, z.name, z.col0, z.col1, 0, H - 1, CityLayout.SeaLevel, 1000f);
                wz.order = z.order;
                wz.isLockChamber = z.isLock;
                wz.riseSpeed = z.isLock ? 20f : 1.6f;
                float half = z.isLock ? AvenueHalf - 0.05f : CityHalfZ;
                wz.visualZFront = -half;
                wz.visualZBack = half;
                if (wz.surface != null)
                {
                    wz.surface.localScale = new Vector3(wz.max.x - wz.min.x, 0.12f, half * 2f);
                    Vector3 p = wz.surface.position; p.z = 0f; wz.surface.position = p;
                }
                city.zones[z.id] = wz;
            }
        }

        private static void BuildLocks(GameObject itemsRoot)
        {
            int n = 0;
            for (int i = 0; i < CityLayout.Zones.Length; i++)
            {
                CityLayout.Zone z = CityLayout.Zones[i];
                if (!z.isLock) continue;
                n++;
                GameObject root = new GameObject("Esclusa_" + n);
                root.transform.SetParent(itemsRoot.transform, false);
                LockStation lk = root.AddComponent<LockStation>();
                lk.displayName = z.name;
                lk.chamber = city.zones[z.id];
                lk.west = city.zones[CityLayout.Zones[i - 1].id];
                lk.east = city.zones[CityLayout.Zones[i + 1].id];
                lk.westGate = CreateLockGate(root.transform, z.col0, "CompuertaOeste");
                lk.eastGate = CreateLockGate(root.transform, z.col1, "CompuertaEste");
                city.gateColumns.Add(z.col0); city.gateDoors.Add(lk.westGate);
                city.gateColumns.Add(z.col1); city.gateDoors.Add(lk.eastGate);

                // Boya de mando: flota en la cámara, pegada al fondo, con una palanca grande y una luz
                float cx = (z.col0 + z.col1) * 0.5f * C;
                GameObject buoy = new GameObject("BoyaMando");
                buoy.transform.SetParent(root.transform, false);
                buoy.transform.position = new Vector3(cx, CityLayout.SeaLevel, lk.buoyZ);
                GameObject floatBody = Primitive(PrimitiveType.Cylinder, "Flotador", buoy.transform, buoy.transform.position, new Vector3(1.6f, 0.35f, 1.6f), mats["BidonRojo"]);
                floatBody.transform.localPosition = Vector3.zero;
                GameObject stripe = Primitive(PrimitiveType.Cylinder, "Franja", buoy.transform, buoy.transform.position, new Vector3(1.65f, 0.08f, 1.65f), mats["FaroBlanco"]);
                stripe.transform.localPosition = new Vector3(0f, 0.1f, 0f);
                GameObject box = Primitive(PrimitiveType.Cube, "Consola", buoy.transform, buoy.transform.position, new Vector3(0.9f, 0.9f, 0.6f), mats["BombaCuerpo"]);
                box.transform.localPosition = new Vector3(0f, 0.75f, 0f);
                GameObject leverPivot = new GameObject("PivotePalanca");
                leverPivot.transform.SetParent(buoy.transform, false);
                leverPivot.transform.localPosition = new Vector3(0f, 1.2f, -0.2f);
                GameObject stick = Primitive(PrimitiveType.Cylinder, "Palanca", leverPivot.transform, Vector3.zero, new Vector3(0.1f, 0.45f, 0.1f), mats["SubOscuro"]);
                stick.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                GameObject knob = Primitive(PrimitiveType.Sphere, "Pomo", leverPivot.transform, Vector3.zero, Vector3.one * 0.28f, mats["BombaPalanca"]);
                knob.transform.localPosition = new Vector3(0f, 0.95f, 0f);
                GameObject lightBall = Primitive(PrimitiveType.Sphere, "Luz", buoy.transform, buoy.transform.position, Vector3.one * 0.3f, mats["BombaLuz"]);
                lightBall.transform.localPosition = new Vector3(0.3f, 1.3f, 0.1f);
                Light bl = new GameObject("LuzBoya").AddComponent<Light>();
                bl.transform.SetParent(buoy.transform, false);
                bl.transform.localPosition = new Vector3(0f, 2f, -0.5f);
                bl.type = LightType.Point;
                bl.range = 7f;
                bl.intensity = 1.5f;
                bl.color = new Color(0.7f, 1f, 0.9f);
                lk.buoy = buoy.transform;
                lk.lever = leverPivot.transform;
                lk.statusLight = lightBall.GetComponent<Renderer>();
                city.locks.Add(lk);
            }
        }

        private static FloodGate CreateLockGate(Transform parent, int col, string name)
        {
            int top = -1, bottom = -1;
            for (int r = 0; r < H; r++)
            {
                if (At(col, r) != 'H') continue;
                if (top < 0) top = r;
                bottom = r;
            }
            float yTop = -top * C + C * 0.5f, yBottom = -bottom * C - C * 0.5f;
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(col * C, (yTop + yBottom) * 0.5f, 0f);
            GameObject door = Primitive(PrimitiveType.Cube, "Hoja", root.transform, root.transform.position,
                new Vector3(C * 0.7f, yTop - yBottom, AvenueHalf * 2f), mats["CompuertaFranjas"], true);
            door.AddComponent<WorldUVBox>().tileSize = 2f;
            FloodGate g = root.AddComponent<FloodGate>();
            g.displayName = name;
            g.door = door.transform;
            g.blocker = door.GetComponent<Collider>();
            g.openHeight = (yTop - yBottom) + 3f;
            g.openDuration = 2f;
            return g;
        }

        // ================================================================== objetos

        private static LevelItems BuildCityItems(GameObject itemsRoot, GameObject decoRoot)
        {
            LevelItems items = new LevelItems();
            Transform t = itemsRoot.transform;
            foreach (Vector2Int cell in FindAll('M'))
            {
                // la caja del tutorial: en el mar, dentro de la ruina, detrás de una reja
                bool niche = ZoneIdOfColumn(cell.x) == "mar" && (At(cell.x + 1, cell.y) == '|' || At(cell.x - 1, cell.y) == '|');
                MaterialCrate crate = CreateCrate(t, cell, niche ? 0f : RandZ());
                if (niche) { crate.isTutorialCrate = true; items.tutorialCrate = crate; crate.name += "_Tutorial"; }
            }
            foreach (Vector2Int cell in FindAll('F'))
            {
                bool pocket = At(cell.x, cell.y - 1) == 'R';
                FuelCanister f = CreateFuel(t, cell, pocket ? 0f : RandZ());
                if (ZoneIdOfColumn(cell.x) == "mar") items.tutorialFuel.Add(f);
            }
            foreach (Vector2Int cell in FindAll('R'))
            {
                Grabbable d = CreateDebris(t, cell, 0f);
                if (ZoneIdOfColumn(cell.x) == "mar") items.tutorialDebris.Add(d);
            }

            // Bombas de drenaje: la i-ésima (de izquierda a derecha) drena su barrio
            List<Vector2Int> pumpCells = FindAll('P');
            pumpCells.Sort((a, b) => a.x.CompareTo(b.x));
            for (int i = 0; i < pumpCells.Count && i < CityLayout.PumpDrains.Length; i++)
            {
                string zoneId = CityLayout.PumpDrains[i];
                CityLayout.Zone dz = FindZone(zoneId);
                PumpStation p = CreatePump(t, pumpCells[i], "drenaje_" + zoneId, "Bomba de drenaje (" + dz.name + ")");
                p.waterTargets = new[] { new PumpWaterTarget { zone = city.zones[zoneId], levelFrom = CityLayout.SeaLevel, levelTo = dz.drainedLevel } };
                p.fuelToComplete = 2f;
                city.pumps.Add(p);
                if (i == 0) items.pump1 = p;
                if (i == 1) items.pump2 = p;
            }

            string[] names = { "Mishi", "Pelusín", "Canela", "Nube", "Tofu", "Bigotes", "Pompón" };
            string[] furs = { "PelajeGris", "PelajeBlanco", "PelajeCanela", "PelajeNegro", "PelajeCrema", "GatoPelaje", "PelajeGris" };
            int k = 0;
            foreach (Vector2Int cell in FindAll('K'))
            {
                CreateKittenCage(t, cell, "gatito_" + (k + 1), names[k % names.Length], furs[k % furs.Length]);
                k++;
            }

            int bellIndex = 0;
            foreach (Vector2Int cell in FindAll('$')) CreateSecretBell(t, cell, bellIndex++);

            System.Random rnd = new System.Random(11);
            foreach (Vector2Int cell in FindAll('x')) CreateFish(decoRoot.transform, cell, rnd);
            foreach (Vector2Int cell in FindAll('w'))
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector3 p = new Vector3(cell.x * C + (float)(rnd.NextDouble() - 0.5) * 1.6f, FloorBelow(cell), DecoZ(cell, rnd, 0f));
                    GameObject a = Model("Alga.fbx", decoRoot.transform, p, 1.6f + (float)rnd.NextDouble() * 1.8f, null, true);
                    a.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                }
            }
            foreach (Vector2Int cell in FindAll('o'))
            {
                Vector3 p = new Vector3(cell.x * C, FloorBelow(cell) + 0.2f, DecoZ(cell, rnd, 1.4f));
                GameObject rock = Model("Roca.fbx", decoRoot.transform, p, 1.6f + (float)rnd.NextDouble());
                rock.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
            }
            // Mar abierto: el lecho está lleno de rocas, corales y algas hacia los costados
            for (int i = 0; i < 60; i++)
            {
                float x = (float)rnd.NextDouble() * 34f;
                float z = ((float)rnd.NextDouble() * 2f - 1f) * (CityHalfZ - 2f);
                if (Mathf.Abs(z) < 6f) z += Mathf.Sign(z == 0f ? 1f : z) * 6f;
                int c = Mathf.RoundToInt(x / C);
                float floor = StreetTopY(c);
                if (rnd.NextDouble() < 0.4)
                {
                    GameObject rock = Model("Roca.fbx", decoRoot.transform, new Vector3(x, floor + 0.3f, z), 1.5f + (float)rnd.NextDouble() * 3f);
                    rock.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                }
                else if (rnd.NextDouble() < 0.5)
                {
                    Model("Coral.fbx", decoRoot.transform, new Vector3(x, floor, z), 1f + (float)rnd.NextDouble(), null, true);
                }
                else
                {
                    GameObject a = Model("Alga.fbx", decoRoot.transform, new Vector3(x, floor, z), 2f + (float)rnd.NextDouble() * 3f, null, true);
                    a.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                }
            }
            return items;
        }

        private static CityLayout.Zone FindZone(string id)
        {
            foreach (CityLayout.Zone z in CityLayout.Zones) if (z.id == id) return z;
            return CityLayout.Zones[0];
        }

        /// <summary>Z de cada cascabel: el del mar entre las rocas; los de las azoteas, fuera de la avenida.</summary>
        private static float SecretBellZ(Vector2Int cell)
        {
            if (FP && rows == CityLayout.Rows)
            {
                string zone = ZoneIdOfColumn(cell.x);
                if (zone == "mar") return -16f;
                if (GroundRow(cell.x) - cell.y <= 2) return -3.4f;         // rincón de la calle, junto a la fachada
                return (cell.x % 2 == 0) ? 13f : -13f;                       // sobre las azoteas
            }
            return 0f;
        }

        private static void BuildDistricts(GameObject world)
        {
            GameObject root = Child(world, "Barrios");
            foreach (CityLayout.Zone z in CityLayout.Zones)
            {
                if (z.isLock) continue;
                GameObject go = Child(root, "Barrio_" + z.name);
                CityDistrict d = go.AddComponent<CityDistrict>();
                d.levelNumber = Mathf.RoundToInt(z.order);
                d.displayName = z.name;
                d.zone = city.zones[z.id];
                int pi = System.Array.IndexOf(CityLayout.PumpDrains, z.id);
                d.pump = pi >= 0 && pi < city.pumps.Count ? city.pumps[pi] : null;
                List<Renderer> w;
                d.windows = city.windows.TryGetValue(z.id, out w) ? w.ToArray() : new Renderer[0];
                List<Light> l;
                d.lamps = city.lamps.TryGetValue(z.id, out l) ? l.ToArray() : new Light[0];
                city.districts[z.id] = d;
            }
        }

        /// <summary>Balsas de rescate: una por barrio, flotan en su nivel de agua.</summary>
        private static void BuildRafts(GameObject world)
        {
            foreach (CityLayout.Zone z in CityLayout.Zones)
            {
                if (z.raftCol < 0) continue;
                GameObject root = new GameObject("Balsa_" + z.name);
                root.transform.SetParent(world.transform, false);
                Vector3 pos = new Vector3(z.raftCol * C, CityLayout.SeaLevel + 0.05f, 2.8f);
                root.transform.position = pos;
                GameObject deck = Primitive(PrimitiveType.Cube, "Cubierta", root.transform, pos, new Vector3(4.2f, 0.25f, 2.4f), mats["Madera"]);
                deck.transform.localPosition = new Vector3(0f, 0.15f, 0f);
                foreach (float sx in new[] { -1.6f, 1.6f })
                {
                    GameObject tube = Primitive(PrimitiveType.Cylinder, "Flotador", root.transform, pos, new Vector3(0.7f, 2.2f, 0.7f), mats["BidonRojo"]);
                    tube.transform.localPosition = new Vector3(sx, 0f, 0f);
                    tube.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                }
                GameObject mast = Primitive(PrimitiveType.Cylinder, "Mastil", root.transform, pos, new Vector3(0.1f, 1.4f, 0.1f), mats["MaderaOscura"]);
                mast.transform.localPosition = new Vector3(1.5f, 1.6f, 0.8f);
                GameObject flag = Primitive(PrimitiveType.Cube, "Bandera", root.transform, pos, new Vector3(0.9f, 0.55f, 0.03f), mats["FaroRojo"]);
                flag.transform.localPosition = new Vector3(1.05f, 2.65f, 0.8f);
                Light l = new GameObject("Farolillo").AddComponent<Light>();
                l.transform.SetParent(root.transform, false);
                l.transform.localPosition = new Vector3(1.5f, 3.1f, 0.8f);
                l.type = LightType.Point;
                l.range = 8f;
                l.intensity = 1.6f;
                l.color = new Color(1f, 0.85f, 0.55f);

                Dock dock = root.AddComponent<Dock>();
                dock.isBase = false;
                dock.displayName = "Balsa de " + z.name;
                dock.floatZone = city.zones[z.id];
                dock.rescueRadius = 5.5f;
                List<Transform> seats = new List<Transform>();
                for (int i = 0; i < 6; i++)
                {
                    GameObject s = new GameObject("Asiento" + i);
                    s.transform.SetParent(root.transform, false);
                    s.transform.localPosition = new Vector3(-1.5f + (i % 3) * 1f, 0.35f, i < 3 ? -0.6f : 0.4f);
                    seats.Add(s.transform);
                }
                dock.seats = seats.ToArray();
                GameObject subPoint = new GameObject("PuntoSubmarino");
                subPoint.transform.SetParent(root.transform, false);
                subPoint.transform.localPosition = new Vector3(0f, 0f, -3.4f);
                dock.subPoint = subPoint.transform;
                GameObject entry = new GameObject("EntradaAgua");
                entry.transform.SetParent(root.transform, false);
                entry.transform.localPosition = new Vector3(0f, -0.4f, -1.6f);
                dock.waterEntry = entry.transform;
                city.rafts.Add(dock);
            }
        }

        // ================================================================== mapa lateral (cámara del prototipo)

        /// <summary>Capa de la cabina (el mapa lateral no la dibuja) y capa del gato piloto (solo el mapa la dibuja).</summary>
        private const int LayerCabina = 29;
        private const int LayerSoloMapa = 30;

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
        }

        private static void NameLayer(int index, string name)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;
            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null || index >= layers.arraySize) return;
            SerializedProperty sp = layers.GetArrayElementAtIndex(index);
            if (string.IsNullOrEmpty(sp.stringValue))
            {
                sp.stringValue = name;
                tagManager.ApplyModifiedProperties();
            }
        }

        private static RenderTexture MapRenderTexture()
        {
            string path = MatDir + "/MapaLateral.renderTexture";
            RenderTexture rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if (rt == null)
            {
                rt = new RenderTexture(800, 520, 16);
                rt.name = "MapaLateral";
                rt.antiAliasing = 2;
                AssetDatabase.CreateAsset(rt, path);
            }
            return rt;
        }

        private static void BuildSideMapCamera(GameObject systems, SubmarineController sub)
        {
            GameObject go = Child(systems, "CamaraMapaLateral");
            Camera cam = go.AddComponent<Camera>();
            cam.fieldOfView = 38f;
            cam.farClipPlane = 90f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.42f, 0.55f);
            cam.cullingMask = ~(1 << LayerCabina);
            cam.depth = -2f;
            SideMapCamera side = go.AddComponent<SideMapCamera>();
            side.target = sub.transform;
            side.texture = MapRenderTexture();
            side.minBounds = new Vector2(8f, -(H - 5) * C);
            side.maxBounds = new Vector2(W * C - 10f, 2f);
            go.transform.position = sub.transform.position + side.offset;
        }
    }
}
