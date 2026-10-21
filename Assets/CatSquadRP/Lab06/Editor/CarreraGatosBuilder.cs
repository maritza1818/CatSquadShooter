using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CatSquad.Lab06.EditorTools
{
    /// <summary>
    /// LAB 06 · WAYPOINTS — escena "CatRacing" (equivalente a TankRacing del tutorial):
    /// un desierto en la playa de Gatópolis, palmeras formando un circuito y cuatro gatos en karts
    /// (en lugar de los tanques: GatoVerde = GreenTank, GatoRojo, GatoAzul, GatoAmarillo).
    ///
    /// Dos opciones en el menú CatSquad:
    ///  · "Escena para hacer las actividades": palmeras y gatos colocados, SIN el script. Tú haces los pasos
    ///    8–14 del lab (arrastrar FollowWP, candado del Inspector, arrastrar las palmeras) y tomas las capturas.
    ///  · "Escena terminada": lo mismo pero con FollowWP ya puesto y las palmeras asignadas (para probar o exponer).
    /// </summary>
    public static class CarreraGatosBuilder
    {
        private static string root;
        private static string Root
        {
            get
            {
                if (root != null) return root;
                foreach (string guid in AssetDatabase.FindAssets("CarreraGatosBuilder t:MonoScript"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                    const string tail = "/Editor/CarreraGatosBuilder.cs";
                    if (path.EndsWith(tail)) { root = path.Substring(0, path.Length - tail.Length); return root; }
                }
                root = "Assets/CatSquadRP/Lab06";
                return root;
            }
        }
        private static string MatDir { get { return Root + "/Materials"; } }

        // Circuito: las palmeras en orden (x, z). Lazo irregular como en la figura del lab.
        private static readonly Vector2[] Circuit =
        {
            new Vector2(0f, -30f), new Vector2(22f, -28f), new Vector2(36f, -14f), new Vector2(38f, 6f),
            new Vector2(26f, 22f), new Vector2(6f, 26f), new Vector2(-14f, 30f), new Vector2(-32f, 18f),
            new Vector2(-36f, -2f), new Vector2(-22f, -22f)
        };

        private struct Racer
        {
            public string name, fur; public Color paint, furColor; public float speed, rotSpeed;
            public Racer(string n, Color p, string f, Color fc, float s, float r) { name = n; paint = p; fur = f; furColor = fc; speed = s; rotSpeed = r; }
        }

        // GatoVerde usa los valores del tutorial (10 / 10); los demás cambian la velocidad (paso 13 del lab)
        private static readonly Racer[] Racers =
        {
            new Racer("GatoVerde",    new Color(0.25f, 0.75f, 0.3f), "PelajeNaranja", new Color(0.97f, 0.56f, 0.18f), 10f, 10f),
            new Racer("GatoRojo",     new Color(0.85f, 0.2f, 0.18f), "PelajeGris",    new Color(0.55f, 0.58f, 0.64f),  9f,  6f),
            new Racer("GatoAzul",     new Color(0.2f, 0.42f, 0.9f),  "PelajeBlanco",  new Color(0.95f, 0.95f, 0.97f), 11f,  4f),
            new Racer("GatoAmarillo", new Color(0.98f, 0.8f, 0.15f), "PelajeNegro",   new Color(0.16f, 0.16f, 0.2f),   8f, 12f),
        };

        [MenuItem("CatSquad/Lab 06: Escena para hacer las actividades (sin script)", priority = 4)]
        public static void BuildForActivities() { Build(false); }

        [MenuItem("CatSquad/Lab 06: Escena terminada (FollowWP ya asignado)", priority = 5)]
        public static void BuildFinished() { Build(true); }

        private static void Build(bool withScript)
        {
            root = null;
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(Root, "Materials");
            if (!AssetDatabase.IsValidFolder(Root + "/Scenes")) AssetDatabase.CreateFolder(Root, "Scenes");
            ConfigureTexture(Root + "/Textures/ArenaDesierto.png");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------- luz y ambiente
            GameObject sun = new GameObject("Sol");
            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.color = new Color(1f, 0.95f, 0.85f);
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.78f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.85f, 0.8f, 0.7f);
            RenderSettings.ambientGroundColor = new Color(0.55f, 0.48f, 0.38f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.78f, 0.86f, 0.93f);
            RenderSettings.fogStartDistance = 120f;
            RenderSettings.fogEndDistance = 320f;

            // ---------------- cámara (vista del circuito completo, como en la figura del lab)
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            cam.farClipPlane = 600f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.8f, 0.95f);
            camGo.transform.position = new Vector3(0f, 62f, -78f);
            camGo.transform.LookAt(new Vector3(0f, 0f, -4f));
            camGo.AddComponent<AudioListener>();

            // ---------------- escenario: desierto de playa
            Material sand = Mat("ArenaDesierto", new Color(1f, 1f, 1f), 0f, 0.1f, Root + "/Textures/ArenaDesierto.png", 24f);
            Material sandDark = Mat("ArenaDuna", new Color(0.86f, 0.74f, 0.52f), 0f, 0.1f);
            Material sandLight = Mat("ArenaDunaClara", new Color(0.9f, 0.8f, 0.6f), 0f, 0.1f);
            Material water = Mat("MarPlaya", new Color(0.22f, 0.62f, 0.78f), 0f, 0.85f);
            Material rock = Mat("RocaDesierto", new Color(0.55f, 0.47f, 0.4f), 0f, 0.2f);

            GameObject env = new GameObject("Escenario");
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Arena";
            ground.transform.SetParent(env.transform, false);
            ground.transform.localScale = new Vector3(16f, 1f, 16f);   // 160 x 160 m
            ground.GetComponent<Renderer>().sharedMaterial = sand;
            ground.isStatic = true;
            GameObject sea = GameObject.CreatePrimitive(PrimitiveType.Plane);
            sea.name = "Mar";
            sea.transform.SetParent(env.transform, false);
            sea.transform.position = new Vector3(0f, -0.25f, 0f);
            sea.transform.localScale = new Vector3(70f, 1f, 70f);
            Object.DestroyImmediate(sea.GetComponent<Collider>());
            sea.GetComponent<Renderer>().sharedMaterial = water;

            System.Random rnd = new System.Random(6);
            for (int i = 0; i < 26; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float r = 50f + (float)rnd.NextDouble() * 22f;
                Vector3 p = new Vector3(Mathf.Cos(a) * r, -0.6f, Mathf.Sin(a) * r);
                GameObject dune = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dune.name = "Duna";
                dune.transform.SetParent(env.transform, false);
                dune.transform.position = p;
                dune.transform.localScale = new Vector3(10f + (float)rnd.NextDouble() * 12f, 2.5f + (float)rnd.NextDouble() * 3f, 8f + (float)rnd.NextDouble() * 10f);
                dune.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 180f, 0f);
                dune.GetComponent<Renderer>().sharedMaterial = i % 3 == 0 ? sandDark : sandLight;
                Object.DestroyImmediate(dune.GetComponent<Collider>());
                dune.isStatic = true;
            }
            for (int i = 0; i < 14; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float r = 44f + (float)rnd.NextDouble() * 20f;
                GameObject rk = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rk.name = "Roca";
                rk.transform.SetParent(env.transform, false);
                rk.transform.position = new Vector3(Mathf.Cos(a) * r, 0.3f, Mathf.Sin(a) * r);
                rk.transform.localScale = new Vector3(1.2f + (float)rnd.NextDouble() * 2f, 0.8f + (float)rnd.NextDouble(), 1.2f + (float)rnd.NextDouble() * 2f);
                rk.transform.rotation = Quaternion.Euler((float)rnd.NextDouble() * 30f, (float)rnd.NextDouble() * 180f, 0f);
                rk.GetComponent<Renderer>().sharedMaterial = rock;
                rk.isStatic = true;
            }
            // un par de palmeras decorativas lejos del circuito (no son waypoints)
            GameObject palmAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/Palmera.fbx");
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f + 0.3f;
                GameObject deco = MakePalm(palmAsset, env.transform, "PalmeraDecorativa", new Vector3(Mathf.Cos(a) * 58f, 0f, Mathf.Sin(a) * 58f), rnd);
                deco.isStatic = true;
            }

            // ---------------- paso 6: las palmeras del circuito (como si se duplicaran con Ctrl+D)
            List<GameObject> palms = new List<GameObject>();
            for (int i = 0; i < Circuit.Length; i++)
            {
                string n = i == 0 ? "Palmera" : "Palmera (" + i + ")";
                palms.Add(MakePalm(palmAsset, null, n, new Vector3(Circuit[i].x, 0f, Circuit[i].y), rnd));
            }

            // ---------------- los gatos en karts (los "tanques")
            GameObject kartAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/Carrito.fbx");
            GameObject catAsset = FindCatModel();
            Vector3 p0 = palms[0].transform.position;
            Vector3 dir = (p0 - palms[palms.Count - 1].transform.position).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            for (int i = 0; i < Racers.Length; i++)
            {
                Racer rc = Racers[i];
                int row = i / 2;
                float lat = (i % 2 == 0 ? -1f : 1f) * 2.6f;
                Vector3 pos = p0 - dir * (10f + row * 5f) + side * lat;
                GameObject racer = MakeRacer(kartAsset, catAsset, rc, pos, Quaternion.LookRotation(dir));
                if (withScript)
                {
                    FollowWP f = racer.AddComponent<FollowWP>();
                    f.waypoints = palms.ToArray();
                    f.speed = rc.speed;
                    f.rotSpeed = rc.rotSpeed;
                }
            }

            string scenePath = Root + "/Scenes/" + (withScript ? "CatRacing_Terminada.unity" : "CatRacing.unity");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Lab 06 · Waypoints",
                "Escena creada en " + scenePath + ".\n\n" + (withScript
                    ? "Los cuatro gatos ya tienen FollowWP con las palmeras asignadas: pulsa Play."
                    : "Ahora haz los pasos del lab:\n 8. Arrastra el script FollowWP (Lab06/Scripts) a GatoVerde.\n 9. Cierra el candado del Inspector.\n10. Selecciona todas las palmeras (Palmera … Palmera (9)) y arrástralas a Waypoints.\n11. Abre el candado.\n12. Play.\n13. Cambia Speed y Rot Speed.\n14. Repite con GatoRojo, GatoAzul y GatoAmarillo."),
                "¡Miau!");
        }

        // ------------------------------------------------------------------ piezas

        private static GameObject MakePalm(GameObject asset, Transform parent, string name, Vector3 pos, System.Random rnd)
        {
            GameObject go;
            if (asset != null) go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.transform.localScale = new Vector3(0.5f, 3.3f, 0.5f);
            }
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
            float s = 0.9f + (float)rnd.NextDouble() * 0.3f;
            go.transform.localScale = asset != null ? Vector3.one * s : go.transform.localScale;
            return go;
        }

        private static GameObject MakeRacer(GameObject kartAsset, GameObject catAsset, Racer rc, Vector3 pos, Quaternion rot)
        {
            GameObject root = new GameObject(rc.name);
            root.transform.position = pos;
            root.transform.rotation = rot;

            Material paint = Mat("Carrito_" + rc.name, rc.paint, 0.2f, 0.65f);
            Material fur = Mat(rc.fur + "_Carrera", rc.furColor, 0f, 0.2f);

            Transform seat = null;
            if (kartAsset != null)
            {
                GameObject kart = (GameObject)PrefabUtility.InstantiatePrefab(kartAsset);
                kart.name = "Carrito";
                kart.transform.SetParent(root.transform, false);
                kart.transform.localScale = Vector3.one * 1.4f;
                ReplaceMaterial(kart, "CarritoPintura", paint);
                seat = FindDeep(kart.transform, "PuntoPiloto");
            }
            else
            {
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "Carrito";
                box.transform.SetParent(root.transform, false);
                box.transform.localScale = new Vector3(1.4f, 0.5f, 2.4f);
                box.transform.localPosition = new Vector3(0f, 0.35f, 0f);
                box.GetComponent<Renderer>().sharedMaterial = paint;
            }

            if (catAsset != null)
            {
                GameObject cat = (GameObject)PrefabUtility.InstantiatePrefab(catAsset);
                cat.name = "Gato";
                cat.transform.SetParent(root.transform, false);
                Vector3 seatLocal = seat != null ? root.transform.InverseTransformPoint(seat.position) : new Vector3(0f, 0.8f, 0.3f);
                cat.transform.localPosition = seatLocal + new Vector3(0f, -0.05f, 0f);
                cat.transform.localRotation = Quaternion.identity;
                cat.transform.localScale = Vector3.one * 0.95f;
                ReplaceMaterial(cat, "GatoPelaje", fur);
            }
            return root;
        }

        private static GameObject FindCatModel()
        {
            List<string> tries = new List<string> { "Assets/CatSquadRP/Models/Gato.fbx", Root + "/Models/Gato.fbx" };
            int cut = Root.LastIndexOf('/');
            if (cut > 0) tries.Insert(0, Root.Substring(0, cut) + "/Models/Gato.fbx");   // CatSquadRP/Lab06 → CatSquadRP/Models
            foreach (string t in tries)
            {
                GameObject g = AssetDatabase.LoadAssetAtPath<GameObject>(t);
                if (g != null) return g;
            }
            foreach (string guid in AssetDatabase.FindAssets("Gato t:Model"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/Gato.fbx")) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            Debug.LogWarning("[Lab06] No encontré Gato.fbx (CatSquadRP/Models): los karts irán sin gato.");
            return null;
        }

        private static void ReplaceMaterial(GameObject go, string matName, Material m)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] arr = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] != null && arr[i].name.StartsWith(matName)) { arr[i] = m; changed = true; }
                }
                if (changed) r.sharedMaterials = arr;
            }
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        private static Material Mat(string name, Color color, float metallic, float smooth, string texture = null, float tiling = 1f)
        {
            string path = MatDir + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            bool isNew = m == null;
            if (isNew) m = new Material(sh); else m.shader = sh;
            m.SetColor("_BaseColor", color);
            m.SetColor("_Color", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Glossiness", smooth);
            if (texture != null)
            {
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texture);
                if (tex != null)
                {
                    m.SetTexture("_BaseMap", tex);
                    m.SetTexture("_MainTex", tex);
                    m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
                    m.SetTextureScale("_MainTex", new Vector2(tiling, tiling));
                }
            }
            if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        private static void ConfigureTexture(string path)
        {
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.SaveAndReimport();
        }
    }
}
