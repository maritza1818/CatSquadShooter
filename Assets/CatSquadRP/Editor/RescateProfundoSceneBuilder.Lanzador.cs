using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatSquad.RescateProfundo.EditorTools
{
    /// <summary>
    /// LAB 05 (predicción de trayectoria) dentro de Gatópolis:
    ///  · el lanzador neumático en la proa del submarino (latas, lastres, boyas) con sus puntos de trayectoria;
    ///  · cestas con gatitos con hambre sobre azoteas junto a cada balsa (y dos junto a la base, para probar al empezar);
    ///  · en el mar abierto: placas de presión en el fondo (lastre) y ganchos bajo pasarelas flotantes (boya).
    /// </summary>
    public static partial class RescateProfundoSceneBuilder
    {
        private static void CreateLauncherMaterials()
        {
            Mat("Lata", new Color(0.82f, 0.84f, 0.88f), 0.85f, 0.7f);
            Mat("LataEtiqueta", new Color(0.2f, 0.55f, 0.95f), 0.1f, 0.5f);
            Mat("Lastre", new Color(0.2f, 0.21f, 0.24f), 0.7f, 0.35f);
            Mat("Boya", new Color(1f, 0.45f, 0.12f), 0f, 0.5f, new Color(0.35f, 0.12f, 0f));
            Mat("Cesta", new Color(0.72f, 0.52f, 0.28f), 0f, 0.15f);
            Mat("PlacaPresion", new Color(1f, 0.82f, 0.15f), 0.3f, 0.4f, new Color(0.35f, 0.25f, 0f));
            Mat("LanzadorTubo", new Color(0.95f, 0.72f, 0.2f), 0.6f, 0.5f);
            UnlitMat("PuntoTrayectoria", Color.white, false);
            UnlitMat("AnilloImpacto", new Color(1f, 0.85f, 0.3f), false);
            UnlitMat("ArcoGuia", new Color(0.55f, 0.95f, 1f, 0.45f), true);
            UnlitMat("BlancoListo", new Color(1f, 0.85f, 0.3f, 0.55f), true);
        }

        // ================================================================== lanzador

        private static ProjectileLauncher BuildLauncher(GameObject parent, SubmarineController sub, ManipulatorArm arm, Camera cam)
        {
            CreateLauncherMaterials();
            GameObject root = Child(parent, "Lanzador");
            ProjectileLauncher launcher = root.AddComponent<ProjectileLauncher>();
            launcher.submarine = sub;
            launcher.arm = arm;

            // Torreta en la proa, a la derecha de la vista: se ve desde la cabina y en el mapa lateral
            Vector3 camLocal = cam != null ? sub.transform.InverseTransformPoint(cam.transform.position) : new Vector3(0f, 0.6f, 0.6f);
            GameObject turret = new GameObject("TorretaLanzador");
            turret.transform.SetParent(sub.transform, false);
            turret.transform.localPosition = new Vector3(0.55f, camLocal.y - 0.05f, camLocal.z + 1.45f);
            GameObject baseRing = Primitive(PrimitiveType.Cylinder, "Base", turret.transform, turret.transform.position, new Vector3(0.36f, 0.06f, 0.36f), mats["SubOscuro"]);
            baseRing.transform.localPosition = Vector3.zero;
            GameObject barrel = new GameObject("Canon");
            barrel.transform.SetParent(turret.transform, false);
            barrel.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            GameObject tube = Primitive(PrimitiveType.Cylinder, "Tubo", barrel.transform, barrel.transform.position, new Vector3(0.16f, 0.32f, 0.16f), mats["LanzadorTubo"]);
            tube.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            tube.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            GameObject mouth = Primitive(PrimitiveType.Cylinder, "Boca", barrel.transform, barrel.transform.position, new Vector3(0.2f, 0.03f, 0.2f), mats["SubOscuro"]);
            mouth.transform.localPosition = new Vector3(0f, 0f, 0.62f);
            mouth.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // el tiro sale del pivote del cañón (fijo respecto del submarino: la predicción no cambia al girar el cañón)
            GameObject muzzle = new GameObject("BocaLanzador");
            muzzle.transform.SetParent(turret.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            launcher.muzzle = muzzle.transform;
            launcher.barrel = barrel.transform;

            // puntos de la trayectoria + anillo de impacto + arco guía
            GameObject dotsGo = Child(root, "PuntosTrayectoria");
            TrajectoryDots dots = dotsGo.AddComponent<TrajectoryDots>();
            dots.dotMaterial = mats["PuntoTrayectoria"];
            GameObject ring = Primitive(PrimitiveType.Cylinder, "AnilloImpacto", dotsGo.transform, Vector3.zero, new Vector3(1.1f, 0.01f, 1.1f), mats["AnilloImpacto"]);
            ring.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            dots.landingMarker = ring.transform;
            GameObject ghost = Child(root, "ArcoGuia");
            LineRenderer lr = ghost.AddComponent<LineRenderer>();
            lr.sharedMaterial = mats["ArcoGuia"];
            lr.widthMultiplier = 0.07f;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.useWorldSpace = true;
            lr.enabled = false;
            dots.ghostLine = lr;
            launcher.dots = dots;

            // plantillas de proyectiles (desactivadas: se clonan al disparar)
            GameObject templates = Child(root, "Plantillas");
            launcher.lataTemplate = MakeProjectile(templates.transform, ProjectileKind.Lata);
            launcher.lastreTemplate = MakeProjectile(templates.transform, ProjectileKind.Lastre);
            launcher.boyaTemplate = MakeProjectile(templates.transform, ProjectileKind.Boya);
            return launcher;
        }

        private static Projectile MakeProjectile(Transform parent, ProjectileKind kind)
        {
            GameObject go;
            Projectile p;
            switch (kind)
            {
                case ProjectileKind.Lastre:
                    go = Primitive(PrimitiveType.Sphere, "Plantilla_Lastre", parent, Vector3.zero, Vector3.one * 0.34f, mats["Lastre"], true);
                    GameObject handle = Primitive(PrimitiveType.Cylinder, "Argolla", go.transform, Vector3.zero, new Vector3(0.35f, 0.15f, 0.35f), mats["Rejilla"]);
                    handle.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                    p = go.AddComponent<Projectile>();
                    p.density = 7800f;     // hierro: g_ef ≈ 0.87 g hacia abajo
                    p.waterDrag = 0.5f;
                    break;
                case ProjectileKind.Boya:
                    go = Primitive(PrimitiveType.Sphere, "Plantilla_Boya", parent, Vector3.zero, Vector3.one * 0.4f, mats["Boya"], true);
                    GameObject band = Primitive(PrimitiveType.Cylinder, "Franja", go.transform, Vector3.zero, new Vector3(1.04f, 0.12f, 1.04f), mats["FaroBlanco"]);
                    band.transform.localPosition = Vector3.zero;
                    p = go.AddComponent<Projectile>();
                    p.density = 500f;      // corcho y aire: g_ef = −g (sube)
                    p.waterDrag = 1.2f;
                    break;
                default:
                    go = Primitive(PrimitiveType.Cylinder, "Plantilla_Lata", parent, Vector3.zero, new Vector3(0.26f, 0.08f, 0.26f), mats["Lata"], true);
                    GameObject label = Primitive(PrimitiveType.Cylinder, "Etiqueta", go.transform, Vector3.zero, new Vector3(1.03f, 0.6f, 1.03f), mats["LataEtiqueta"]);
                    label.transform.localPosition = Vector3.zero;
                    p = go.AddComponent<Projectile>();
                    p.density = 1300f;     // atún en lata: se hunde despacio (g_ef ≈ 0.23 g)
                    p.waterDrag = 1.5f;
                    break;
            }
            p.kind = kind;
            p.airGravityScale = 1.5f;
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.mass = kind == ProjectileKind.Lastre ? 2f : 0.4f;
            rb.useGravity = false;
            rb.isKinematic = true;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            go.SetActive(false);
            return p;
        }

        // ================================================================== blancos

        private static void BuildLaunchTargets(GameObject itemsRoot)
        {
            GameObject root = Child(itemsRoot, "BlancosLanzador");
            string[] kittenNames = { "Bigotes", "Pelusín", "Mishi", "Canela", "Tofu", "Luna", "Pimienta", "Michi", "Nube", "Coco", "Galleta" };
            string[] furs = { "PelajeCrema", "PelajeGris", "PelajeBlanco", "PelajeCanela", "PelajeNegro", "GatoPelaje" };
            int n = 0;

            // 1) junto a la base (mar abierto): para probar el lanzador apenas empieza la partida
            Vector3 spawn = CellCenter(FindChar('S'));
            BuildBasket(root.transform, new Vector3(spawn.x + 14f, CityLayout.SeaLevel + 3.5f, 13f), spawn, CityLayout.SeaLevel - 18f,
                        kittenNames[n % kittenNames.Length], furs[n % furs.Length]); n++;
            BuildBasket(root.transform, new Vector3(spawn.x + 18f, CityLayout.SeaLevel + 6.5f, -17f), spawn, CityLayout.SeaLevel - 18f,
                        kittenNames[n % kittenNames.Length], furs[n % furs.Length]); n++;

            // 2) tres cestas alrededor de cada balsa, sobre las azoteas (sin chocar con las torres)
            foreach (CityLayout.Zone z in CityLayout.Zones)
            {
                if (z.raftCol < 0) continue;
                Vector3 raft = new Vector3(z.raftCol * C, CityLayout.SeaLevel, 2.8f);
                float xMin = z.col0 * C + 3f, xMax = z.col1 * C - 3f;
                Vector2[] candidates =
                {
                    new Vector2(-10f, 14f), new Vector2(9f, 19f), new Vector2(3f, -16f), new Vector2(-6f, -22f),
                    new Vector2(15f, 12f), new Vector2(-14f, 24f), new Vector2(12f, -13f), new Vector2(0f, 27f), new Vector2(-12f, -12f)
                };
                float[] heights = { 3f, 6f, 9f };
                int placed = 0;
                foreach (Vector2 c in candidates)
                {
                    if (placed >= 3) break;
                    float x = Mathf.Clamp(raft.x + c.x, xMin, xMax);
                    float y = CityLayout.SeaLevel + heights[placed];
                    Vector3 pos = new Vector3(x, y, c.y);
                    Bounds deck = new Bounds(pos + Vector3.up * 0.8f, new Vector3(4f, 3f, 4f));
                    bool blocked = false;
                    foreach (Bounds b in city.buildings) if (b.Intersects(deck)) { blocked = true; break; }
                    if (blocked) continue;
                    int col = Mathf.Clamp(Mathf.RoundToInt(x / C), 0, W - 1);
                    float floor = StreetTopY(col);
                    foreach (Bounds b in city.buildings)
                    {
                        if (x > b.min.x && x < b.max.x && c.y > b.min.z && c.y < b.max.z) floor = Mathf.Max(floor, b.max.y);
                    }
                    BuildBasket(root.transform, pos, raft, floor, kittenNames[n % kittenNames.Length], furs[n % furs.Length]);
                    n++;
                    placed++;
                }
            }

            // 3) bajo el agua, en el mar abierto (fuera de la avenida del tutorial): placas y ganchos
            float seabed = StreetTopY(10);
            BuildPressurePlate(root.transform, new Vector3(spawn.x + 12f, seabed, 9f));
            BuildPressurePlate(root.transform, new Vector3(spawn.x + 16f, seabed, -8f));
            BuildHookWalkway(root.transform, new Vector3(spawn.x + 8f, CityLayout.SeaLevel, 14f));
            BuildHookWalkway(root.transform, new Vector3(spawn.x + 18f, CityLayout.SeaLevel, -11f));
        }

        /// <summary>Plataforma sobre pilotes con una cesta abierta y un gatito con hambre que mira a la balsa.</summary>
        private static void BuildBasket(Transform parent, Vector3 pos, Vector3 lookAt, float floorY, string kittenName, string fur)
        {
            GameObject go = new GameObject("Cesta_" + kittenName);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            Vector3 look = lookAt - pos;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f) go.transform.rotation = Quaternion.LookRotation(look.normalized);

            // pilotes hasta la azotea o la calle
            float pileLen = Mathf.Max(0.5f, pos.y - floorY);
            foreach (Vector2 o in new[] { new Vector2(-1.2f, -1.2f), new Vector2(1.2f, -1.2f), new Vector2(-1.2f, 1.2f), new Vector2(1.2f, 1.2f) })
            {
                GameObject pile = Primitive(PrimitiveType.Cylinder, "Pilote", go.transform, pos, new Vector3(0.22f, pileLen * 0.5f, 0.22f), mats["MaderaOscura"]);
                pile.transform.localPosition = new Vector3(o.x, -pileLen * 0.5f, o.y);
            }
            GameObject deck = Primitive(PrimitiveType.Cube, "Plataforma", go.transform, pos, new Vector3(3f, 0.25f, 3f), mats["Madera"], true);
            deck.transform.localPosition = new Vector3(0f, -0.12f, 0f);

            // cesta: fondo + 4 paredes (sólidas, la lata rebota adentro) + volumen trigger para detectar
            GameObject basket = new GameObject("Cesta");
            basket.transform.SetParent(go.transform, false);
            basket.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            float w = 1.25f, h = 0.6f, t = 0.08f;
            GameObject bottom = Primitive(PrimitiveType.Cube, "Fondo", basket.transform, pos, new Vector3(w, t, w), mats["Cesta"], true);
            bottom.transform.localPosition = new Vector3(0f, t * 0.5f, 0f);
            Vector3[] wallPos = { new Vector3(0f, h * 0.5f, w * 0.5f), new Vector3(0f, h * 0.5f, -w * 0.5f), new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(-w * 0.5f, h * 0.5f, 0f) };
            Vector3[] wallSize = { new Vector3(w, h, t), new Vector3(w, h, t), new Vector3(t, h, w), new Vector3(t, h, w) };
            for (int i = 0; i < 4; i++)
            {
                GameObject wall = Primitive(PrimitiveType.Cube, "Pared", basket.transform, pos, wallSize[i], mats["Cesta"], true);
                wall.transform.localPosition = wallPos[i];
            }
            GameObject inside = new GameObject("Interior");
            inside.transform.SetParent(basket.transform, false);
            inside.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            BoxCollider trig = inside.AddComponent<BoxCollider>();
            trig.isTrigger = true;
            trig.size = new Vector3(w - 0.1f, h, w - 0.1f);
            GameObject aim = new GameObject("CentroBlanco");
            aim.transform.SetParent(basket.transform, false);
            aim.transform.localPosition = new Vector3(0f, h * 0.6f, 0f);

            // anillo que indica que está lista
            GameObject ring = Primitive(PrimitiveType.Cylinder, "AnilloListo", basket.transform, pos, new Vector3(1.7f, 0.01f, 1.7f), mats["BlancoListo"]);
            ring.transform.localPosition = new Vector3(0f, h + 0.05f, 0f);
            ring.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            // gatito con hambre (mira hacia la balsa)
            GameObject kitten = Model("Gato.fbx", go.transform, pos + go.transform.right * -0.9f + go.transform.forward * 0.6f, 0.55f,
                                      new Dictionary<string, string> { { "GatoPelaje", fur } }, true);
            kitten.name = "Gatito_" + kittenName;
            kitten.transform.localPosition = new Vector3(-0.95f, 0f, 0.7f);
            kitten.transform.localRotation = Quaternion.identity;

            // banderín para verlo de lejos
            GameObject mast = Primitive(PrimitiveType.Cylinder, "Mastil", go.transform, pos, new Vector3(0.06f, 1.1f, 0.06f), mats["MaderaOscura"]);
            mast.transform.localPosition = new Vector3(1.25f, 1.1f, -1.25f);
            GameObject flag = Primitive(PrimitiveType.Cube, "Banderin", go.transform, pos, new Vector3(0.7f, 0.42f, 0.03f), mats["FaroRojo"]);
            flag.transform.localPosition = new Vector3(0.9f, 1.95f, -1.25f);

            LaunchTarget target = go.AddComponent<LaunchTarget>();
            target.kind = LaunchTargetKind.Cesta;
            target.displayName = "la cesta de " + kittenName;
            target.aimPoint = aim.transform;
            target.kitten = kitten.transform;
            target.indicator = ring.GetComponent<Renderer>();
            target.reward = 2;
            target.resetSeconds = 30f;
        }

        /// <summary>Placa de presión en el fondo del mar: se activa con un LASTRE.</summary>
        private static void BuildPressurePlate(Transform parent, Vector3 floorPos)
        {
            GameObject go = new GameObject("PlacaPresion");
            go.transform.SetParent(parent, false);
            go.transform.position = floorPos;
            GameObject frame = Primitive(PrimitiveType.Cylinder, "Marco", go.transform, floorPos, new Vector3(2.4f, 0.12f, 2.4f), mats["CajaMetal"], true);
            frame.transform.localPosition = new Vector3(0f, 0.12f, 0f);
            GameObject plate = Primitive(PrimitiveType.Cylinder, "Placa", go.transform, floorPos, new Vector3(1.7f, 0.1f, 1.7f), mats["PlacaPresion"], true);
            plate.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            GameObject zone = new GameObject("Zona");
            zone.transform.SetParent(go.transform, false);
            zone.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            BoxCollider trig = zone.AddComponent<BoxCollider>();
            trig.isTrigger = true;
            trig.size = new Vector3(1.9f, 0.8f, 1.9f);
            GameObject ring = Primitive(PrimitiveType.Cylinder, "AnilloListo", go.transform, floorPos, new Vector3(2.8f, 0.01f, 2.8f), mats["BlancoListo"]);
            ring.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            Light l = new GameObject("Luz").AddComponent<Light>();
            l.transform.SetParent(go.transform, false);
            l.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            l.type = LightType.Point;
            l.range = 6f;
            l.intensity = 1.5f;
            l.color = new Color(1f, 0.8f, 0.3f);

            LaunchTarget target = go.AddComponent<LaunchTarget>();
            target.kind = LaunchTargetKind.Placa;
            target.displayName = "la placa del fondo";
            target.aimPoint = zone.transform;
            target.movingPart = plate.transform;
            target.indicator = ring.GetComponent<Renderer>();
            target.reward = 3;
            target.resetSeconds = 15f;
        }

        /// <summary>Pasarela flotante con un gancho debajo: una BOYA que sube se engancha.</summary>
        private static void BuildHookWalkway(Transform parent, Vector3 surfacePos)
        {
            GameObject go = new GameObject("PasarelaGancho");
            go.transform.SetParent(parent, false);
            go.transform.position = surfacePos;
            GameObject deck = Primitive(PrimitiveType.Cube, "Pasarela", go.transform, surfacePos, new Vector3(6f, 0.3f, 2f), mats["Madera"], true);
            deck.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            foreach (float sx in new[] { -2.6f, 2.6f })
            {
                GameObject fl = Primitive(PrimitiveType.Cylinder, "Flotador", go.transform, surfacePos, new Vector3(0.6f, 1f, 0.6f), mats["BidonRojo"], true);
                fl.transform.localPosition = new Vector3(sx, -0.15f, 0f);
                fl.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            GameObject hook = new GameObject("Gancho");
            hook.transform.SetParent(go.transform, false);
            hook.transform.localPosition = new Vector3(0f, -0.9f, 0f);
            GameObject rod = Primitive(PrimitiveType.Cylinder, "Varilla", hook.transform, surfacePos, new Vector3(0.08f, 0.35f, 0.08f), mats["Rejilla"]);
            rod.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            GameObject cup = Primitive(PrimitiveType.Sphere, "Iman", hook.transform, surfacePos, new Vector3(0.5f, 0.25f, 0.5f), mats["PlacaPresion"]);
            cup.transform.localPosition = Vector3.zero;
            GameObject zone = new GameObject("Zona");
            zone.transform.SetParent(hook.transform, false);
            zone.transform.localPosition = new Vector3(0f, -0.2f, 0f);
            SphereCollider trig = zone.AddComponent<SphereCollider>();
            trig.isTrigger = true;
            trig.radius = 0.85f;
            GameObject ring = Primitive(PrimitiveType.Sphere, "AnilloListo", hook.transform, surfacePos, Vector3.one * 1.3f, mats["BlancoListo"]);
            ring.transform.localPosition = new Vector3(0f, -0.2f, 0f);

            LaunchTarget target = go.AddComponent<LaunchTarget>();
            target.kind = LaunchTargetKind.Gancho;
            target.displayName = "el gancho de la pasarela";
            target.aimPoint = zone.transform;
            target.indicator = ring.GetComponent<Renderer>();
            target.reward = 3;
            target.resetSeconds = 15f;
        }
    }
}
