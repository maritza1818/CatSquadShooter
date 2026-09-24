using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CatSquad.RescateProfundo.EditorTools
{
    /// <summary>
    /// Construye la escena completa de "Cat Squad: Rescate Profundo" desde el mapa ASCII.
    /// Menú: CatSquad > Construir escena Rescate Profundo.
    /// Todo queda como objetos normales en la escena: después se puede editar a mano.
    /// </summary>
    public static class RescateProfundoSceneBuilder
    {
        private const string Root = "Assets/CatSquadRP";
        private const string MatDir = Root + "/Materials";
        private const string SceneDir = Root + "/Scenes";
        private const string ScenePath = SceneDir + "/RescateProfundo.unity";
        private const float C = LevelLayout.CellSize;
        private const float WallFront = -1.2f;
        private const float WallBack = 3.0f;

        private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        private static string[] rows;
        private static int W, H;

        // ================================================================== menú

        [MenuItem("CatSquad/Construir escena Rescate Profundo", priority = 1)]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            try
            {
                EditorUtility.DisplayProgressBar("Cat Squad", "Preparando recursos...", 0.1f);
                Build();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("CatSquad/Borrar progreso guardado", priority = 20)]
        public static void DeleteSave()
        {
            string p = Path.Combine(Application.persistentDataPath, "catsquad_rescate_profundo.json");
            if (File.Exists(p)) File.Delete(p);
            EditorUtility.DisplayDialog("Cat Squad", "Progreso borrado. La próxima partida empezará con el tutorial.", "OK");
        }

        [MenuItem("CatSquad/Abrir carpeta de métricas", priority = 21)]
        public static void OpenMetrics()
        {
            string folder = Path.Combine(Application.persistentDataPath, "CatSquadSesiones");
            Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder);
        }

        // ================================================================== construcción

        private static void Build()
        {
            rows = LevelLayout.Rows;
            H = rows.Length;
            W = 0;
            foreach (string r in rows) W = Mathf.Max(W, r.Length);

            EnsureFolder(Root, "Materials");
            EnsureFolder(Root, "Scenes");
            ConfigureImporters();
            CreateMaterials();

            EditorUtility.DisplayProgressBar("Cat Squad", "Creando escena...", 0.3f);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject world = new GameObject("== MUNDO ==");
            GameObject level = Child(world, "Nivel");
            GameObject waterRoot = Child(world, "Agua");
            GameObject itemsRoot = Child(world, "Objetos");
            GameObject decoRoot = Child(world, "Decoracion");
            GameObject pointsRoot = Child(world, "Puntos");
            GameObject actorsRoot = new GameObject("== ACTORES ==");
            GameObject systemsRoot = new GameObject("== SISTEMAS ==");

            BuildEnvironment(systemsRoot);
            BuildWalls(level);
            BuildGrates(level);

            Dictionary<char, Transform> points = BuildPoints(pointsRoot);
            Vector3 subSpawn = CellCenter(FindChar('S')) ;
            subSpawn.y = -5.6f;

            EditorUtility.DisplayProgressBar("Cat Squad", "Agua y compartimentos...", 0.45f);
            Dictionary<string, WaterZone> zones = BuildWater(waterRoot);

            EditorUtility.DisplayProgressBar("Cat Squad", "Objetos del nivel...", 0.55f);
            LevelItems items = BuildItems(itemsRoot, decoRoot, zones);

            EditorUtility.DisplayProgressBar("Cat Squad", "Submarino, brazo y gatos...", 0.7f);
            SubmarineController sub = BuildSubmarine(actorsRoot, subSpawn);
            ManipulatorArm arm = BuildArm(actorsRoot, sub);
            GuideCat guide = BuildGuide(actorsRoot, subSpawn + new Vector3(16f, 0.3f, 0f));
            Dock dock = BuildDock(world, subSpawn);

            EditorUtility.DisplayProgressBar("Cat Squad", "Sistemas...", 0.85f);
            Camera cam = BuildCamera(systemsRoot, sub.transform);
            BuildSystems(systemsRoot, sub, arm, guide, dock, cam, zones, items, points, subSpawn);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuild(ScenePath);
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = sub.gameObject;
            EditorUtility.DisplayDialog("Cat Squad: Rescate Profundo",
                "Escena creada en " + ScenePath + ".\n\nPulsa Play para jugar.\nF1 abre el panel del terapeuta.", "¡Vamos!");
        }

        // ================================================================== utilidades

        private static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
        }

        private static GameObject Child(GameObject parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static char At(int col, int row)
        {
            if (row < 0 || row >= H || col < 0) return '#';
            string r = rows[row];
            return col < r.Length ? r[col] : '#';
        }

        private static bool IsSolid(char c) { return c == '#' || c == 'G'; }

        private static Vector3 CellCenter(Vector2Int cell)
        {
            return new Vector3(cell.x * C, -cell.y * C, 0f);
        }

        private static Vector2Int FindChar(char ch)
        {
            for (int r = 0; r < H; r++)
            {
                int c = rows[r].IndexOf(ch);
                if (c >= 0) return new Vector2Int(c, r);
            }
            return new Vector2Int(0, 0);
        }

        private static List<Vector2Int> FindAll(char ch)
        {
            List<Vector2Int> list = new List<Vector2Int>();
            for (int r = 0; r < H; r++)
                for (int c = 0; c < rows[r].Length; c++)
                    if (rows[r][c] == ch) list.Add(new Vector2Int(c, r));
            return list;
        }

        /// <summary>Y del suelo bajo la celda (borde inferior) si hay pared debajo; si no, el centro.</summary>
        private static float FloorY(Vector2Int cell, float halfHeight)
        {
            float center = -cell.y * C;
            char below = At(cell.x, cell.y + 1);
            if (IsSolid(below)) return center - C * 0.5f + halfHeight;
            return center;
        }

        /// <summary>Y del primer suelo sólido debajo de la celda (para decoración apoyada en el fondo).</summary>
        private static float FloorBelow(Vector2Int cell)
        {
            for (int r = cell.y + 1; r < H; r++)
            {
                if (IsSolid(At(cell.x, r))) return -r * C + C * 0.5f;
            }
            return -cell.y * C;
        }

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material m, bool keepCollider = false)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            if (!keepCollider)
            {
                Collider col = go.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
            }
            Renderer r = go.GetComponent<Renderer>();
            if (r != null && m != null) r.sharedMaterial = m;
            return go;
        }

        // ================================================================== importadores y materiales

        private static void ConfigureImporters()
        {
            string portrait = Root + "/Textures/RetratoGuia.png";
            TextureImporter ti = AssetImporter.GetAtPath(portrait) as TextureImporter;
            if (ti != null && ti.textureType != TextureImporterType.Sprite)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.SaveAndReimport();
            }
        }

        private static Shader LitShader()
        {
            Shader s = Shader.Find("Universal Render Pipeline/Lit");
            if (s == null) s = Shader.Find("Standard");
            return s;
        }

        private static Material Mat(string name, Color color, float metallic = 0f, float smooth = 0.4f,
                                    Color? emission = null, bool transparent = false, string texture = null, float tiling = 1f)
        {
            string path = MatDir + "/" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(LitShader());
            else m.shader = LitShader();

            m.SetColor("_BaseColor", color);
            m.SetColor("_Color", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Glossiness", smooth);

            if (texture != null)
            {
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + texture);
                if (tex != null)
                {
                    m.SetTexture("_BaseMap", tex);
                    m.SetTexture("_MainTex", tex);
                    m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
                }
            }

            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }

            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_AlphaClip", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetShaderPassEnabled("ShadowCaster", false);
                m.SetShaderPassEnabled("DepthOnly", false);
            }
            else
            {
                m.SetFloat("_Surface", 0f);
                m.SetFloat("_ZWrite", 1f);
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                m.SetOverrideTag("RenderType", "Opaque");
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = -1;
                m.SetShaderPassEnabled("ShadowCaster", true);
                m.SetShaderPassEnabled("DepthOnly", true);
            }

            if (isNew) AssetDatabase.CreateAsset(m, path);
            else EditorUtility.SetDirty(m);
            mats[name] = m;
            return m;
        }

        private static Material UnlitMat(string name, Color color, bool transparent)
        {
            string path = MatDir + "/" + name + ".mat";
            Shader s = Shader.Find(transparent ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Unlit");
            if (s == null) s = Shader.Find("Sprites/Default");
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = m == null;
            if (isNew) m = new Material(s); else m.shader = s;
            m.SetColor("_BaseColor", color);
            m.SetColor("_Color", color);
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            mats[name] = m;
            return m;
        }

        private static void CreateMaterials()
        {
            mats.Clear();
            // Entorno
            Mat("ParedMetal", Color.white, 0.35f, 0.35f, null, false, "PanelMetal.png");
            Mat("ParedRoca", Color.white, 0f, 0.15f, null, false, "RocaMarina.png");
            Mat("FondoAzulejos", new Color(0.8f, 0.9f, 0.95f), 0f, 0.3f, null, false, "FondoAzulejos.png");
            Mat("CompuertaFranjas", Color.white, 0.4f, 0.4f, null, false, "Franjas.png");
            Mat("Agua", new Color(0.1f, 0.5f, 0.7f, 0.2f), 0f, 0.9f, null, true);
            Mat("AguaSuperficie", new Color(0.7f, 0.95f, 1f, 0.55f), 0f, 0.95f, new Color(0.25f, 0.45f, 0.55f), true);
            Mat("Rejilla", new Color(0.45f, 0.5f, 0.55f), 0.8f, 0.5f);
            Mat("LimitePresion", new Color(1f, 0.25f, 0.2f, 0.45f), 0f, 0.5f, new Color(1f, 0.2f, 0.15f), true);
            Mat("Fragmento", new Color(1f, 0.85f, 0.3f), 0f, 0.8f, new Color(2.5f, 1.8f, 0.4f));
            Mat("Burbuja", new Color(0.7f, 0.95f, 1f, 0.35f), 0f, 0.95f, new Color(0.2f, 0.4f, 0.5f), true);
            Mat("MarcadorMano", new Color(1f, 1f, 1f, 0.35f), 0f, 0.5f, new Color(0.4f, 0.4f, 0.4f), true);
            UnlitMat("ChorroCombustible", new Color(1f, 0.55f, 0.1f, 0.9f), true);
            UnlitMat("ParticulaBurbuja", new Color(0.85f, 0.95f, 1f, 0.6f), true);
            UnlitMat("Cielo", new Color(0.62f, 0.84f, 0.97f), false);
            UnlitMat("AnilloCorriente", new Color(0.7f, 0.95f, 1f, 0.35f), true);

            // Paleta de los modelos (se reasigna por nombre de material del FBX)
            Mat("SubCasco", new Color(1f, 0.74f, 0.12f), 0.2f, 0.65f);
            Mat("SubOscuro", new Color(0.2f, 0.22f, 0.27f), 0.7f, 0.55f);
            Mat("SubVidrio", new Color(0.7f, 0.92f, 1f, 0.28f), 0f, 0.95f, null, true);
            Mat("SubLuz", new Color(1f, 0.95f, 0.75f), 0f, 0.9f, new Color(3f, 2.6f, 1.6f));
            Mat("SubAcento", new Color(0.92f, 0.28f, 0.17f), 0.1f, 0.5f);
            Mat("SubOrejaInt", new Color(0.98f, 0.6f, 0.66f), 0f, 0.3f);
            Mat("GatoPelaje", new Color(0.97f, 0.56f, 0.18f), 0f, 0.2f);
            Mat("GatoClaro", new Color(0.99f, 0.95f, 0.88f), 0f, 0.2f);
            Mat("GatoOjo", new Color(0.03f, 0.03f, 0.05f), 0f, 0.9f);
            Mat("GatoBrillo", Color.white, 0f, 0.9f, new Color(1.5f, 1.5f, 1.5f));
            Mat("GatoNariz", new Color(0.95f, 0.45f, 0.55f), 0f, 0.5f);
            Mat("GatoOrejaInt", new Color(0.98f, 0.66f, 0.72f), 0f, 0.3f);
            Mat("PelajeGris", new Color(0.55f, 0.58f, 0.64f), 0f, 0.2f);
            Mat("PelajeNegro", new Color(0.13f, 0.13f, 0.16f), 0f, 0.25f);
            Mat("PelajeBlanco", new Color(0.95f, 0.95f, 0.97f), 0f, 0.2f);
            Mat("PelajeCanela", new Color(0.78f, 0.5f, 0.3f), 0f, 0.2f);
            Mat("PelajeCrema", new Color(0.98f, 0.82f, 0.55f), 0f, 0.2f);
            Mat("GorraAzul", new Color(0.08f, 0.14f, 0.38f), 0f, 0.35f);
            Mat("GorraBlanca", new Color(0.96f, 0.96f, 0.98f), 0f, 0.4f);
            Mat("GorraOro", new Color(1f, 0.78f, 0.25f), 0.85f, 0.7f);
            Mat("Bufanda", new Color(0.88f, 0.14f, 0.18f), 0f, 0.2f);
            Mat("EscafandraVidrio", new Color(0.75f, 0.93f, 1f, 0.18f), 0f, 0.95f, null, true);
            Mat("PezCuerpo", new Color(0.2f, 0.75f, 0.98f), 0f, 0.6f, new Color(0.02f, 0.08f, 0.1f));
            Mat("PezAleta", new Color(1f, 0.55f, 0.2f), 0f, 0.5f);
            Mat("PezOjo", new Color(0.02f, 0.02f, 0.03f), 0f, 0.9f);
            Mat("AlgaVerde", new Color(0.22f, 0.62f, 0.3f), 0f, 0.3f);
            Mat("RocaGris", new Color(0.38f, 0.4f, 0.45f), 0f, 0.15f);
            Mat("CoralRosa", new Color(1f, 0.45f, 0.58f), 0f, 0.35f, new Color(0.08f, 0.02f, 0.03f));
            Mat("CajaMetal", new Color(0.45f, 0.53f, 0.58f), 0.6f, 0.5f);
            Mat("CajaBorde", new Color(0.96f, 0.66f, 0.15f), 0.4f, 0.5f);
            Mat("Cristal", new Color(1f, 0.85f, 0.3f), 0f, 0.9f, new Color(2.2f, 1.6f, 0.35f));
            Mat("BidonRojo", new Color(0.88f, 0.13f, 0.1f), 0.2f, 0.55f);
            Mat("BidonTapa", new Color(0.15f, 0.15f, 0.17f), 0.3f, 0.4f);
            Mat("BidonFranja", new Color(1f, 0.8f, 0.1f), 0.1f, 0.5f);
            Mat("EscombroConcreto", new Color(0.58f, 0.55f, 0.5f), 0f, 0.1f);
            Mat("EscombroFierro", new Color(0.45f, 0.25f, 0.15f), 0.5f, 0.3f);
            Mat("BombaCuerpo", new Color(0.12f, 0.58f, 0.58f), 0.4f, 0.55f);
            Mat("BombaMetal", new Color(0.62f, 0.65f, 0.68f), 0.8f, 0.6f);
            Mat("BombaEmbudo", new Color(0.88f, 0.62f, 0.22f), 0.8f, 0.65f);
            Mat("BombaPalanca", new Color(0.92f, 0.15f, 0.12f), 0.1f, 0.6f);
            Mat("BombaPanel", new Color(0.08f, 0.09f, 0.11f), 0.2f, 0.4f);
            Mat("BombaBarra", new Color(0.2f, 1f, 0.45f), 0f, 0.8f, new Color(0.4f, 2.2f, 0.9f));
            Mat("BombaTanque", new Color(1f, 0.4f, 0.1f), 0f, 0.8f, new Color(2.2f, 0.8f, 0.2f));
            Mat("BombaLuz", new Color(1f, 0.6f, 0.1f), 0f, 0.8f, new Color(2f, 1.2f, 0.2f));
            Mat("JaulaMetal", new Color(0.55f, 0.6f, 0.68f), 0.85f, 0.6f);
            Mat("JaulaBurbuja", new Color(0.6f, 0.9f, 1f, 0.22f), 0f, 0.95f, new Color(0.05f, 0.12f, 0.15f), true);
            Mat("Cerrojo", new Color(1f, 0.8f, 0.1f), 0.5f, 0.6f, new Color(0.35f, 0.25f, 0.02f));
            Mat("Madera", new Color(0.58f, 0.38f, 0.21f), 0f, 0.2f);
            Mat("MaderaOscura", new Color(0.35f, 0.22f, 0.12f), 0f, 0.15f);
            Mat("FaroBlanco", new Color(0.95f, 0.95f, 0.95f), 0f, 0.4f);
            Mat("FaroRojo", new Color(0.88f, 0.15f, 0.12f), 0f, 0.4f);
            Mat("FaroLuz", new Color(1f, 0.9f, 0.5f), 0f, 0.9f, new Color(3f, 2.4f, 1f));
            AssetDatabase.SaveAssets();
        }

        /// <summary>Instancia un modelo FBX, reasigna sus materiales por nombre y lo ajusta a un tamaño.</summary>
        private static GameObject Model(string file, Transform parent, Vector3 pos, float targetSize,
                                        Dictionary<string, string> overrides = null, bool sizeByHeight = false)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/" + file);
            GameObject go;
            if (asset != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                go.transform.SetParent(parent, false);
            }
            else
            {
                Debug.LogWarning("[CatSquad] No se encontró el modelo " + file + "; se usa una primitiva.");
                go = Primitive(PrimitiveType.Capsule, file, parent, Vector3.zero, Vector3.one, null);
            }
            go.transform.position = pos;

            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] list = r.sharedMaterials;
                bool anyTransparent = false;
                for (int i = 0; i < list.Length; i++)
                {
                    string key = list[i] != null ? CleanName(list[i].name) : "";
                    string mapped;
                    if (overrides != null && overrides.TryGetValue(key, out mapped)) key = mapped;
                    Material m;
                    if (mats.TryGetValue(key, out m))
                    {
                        list[i] = m;
                        if (m.renderQueue >= (int)RenderQueue.Transparent) anyTransparent = true;
                    }
                }
                r.sharedMaterials = list;
                if (anyTransparent) r.shadowCastingMode = ShadowCastingMode.Off;
            }

            if (targetSize > 0f) FitSize(go, targetSize, sizeByHeight);
            return go;
        }

        private static string CleanName(string n)
        {
            int dot = n.IndexOf('.');
            if (dot > 0) n = n.Substring(0, dot);
            int sp = n.IndexOf(' ');
            if (sp > 0) n = n.Substring(0, sp);
            return n;
        }

        private static void FitSize(GameObject go, float target, bool byHeight)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            float current = byHeight ? b.size.y : Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (current < 1e-4f) return;
            go.transform.localScale *= target / current;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        // ================================================================== entorno

        private static void BuildEnvironment(GameObject systems)
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.6f, 0.7f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.16f, 0.42f, 0.55f);
            RenderSettings.fogDensity = 0.012f;

            GameObject sun = Child(systems, "Sol");
            Light l = sun.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            l.color = new Color(0.85f, 0.95f, 1f);
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.6f;
            sun.transform.rotation = Quaternion.Euler(55f, -25f, 0f);
        }

        // ================================================================== paredes (fusión greedy)

        private static void BuildWalls(GameObject level)
        {
            // Dos clases: roca (lecho marino, filas < 9) y metal (complejo)
            bool[,] used = new bool[W, H];
            int blocks = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                bool rockPass = pass == 0;
                for (int r = 0; r < H; r++)
                {
                    for (int c = 0; c < W; c++)
                    {
                        if (used[c, r] || !IsWallCell(c, r, rockPass)) continue;
                        int w = 1;
                        while (c + w < W && !used[c + w, r] && IsWallCell(c + w, r, rockPass)) w++;
                        int h = 1;
                        bool canGrow = true;
                        while (canGrow && r + h < H)
                        {
                            for (int k = 0; k < w; k++)
                            {
                                if (used[c + k, r + h] || !IsWallCell(c + k, r + h, rockPass)) { canGrow = false; break; }
                            }
                            if (canGrow) h++;
                        }
                        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) used[c + x, r + y] = true;
                        CreateWallBlock(level.transform, c, r, w, h, rockPass);
                        blocks++;
                    }
                }
            }

            // Límites invisibles del mar abierto
            BoundsWall(level.transform, "LimiteIzq", new Vector3(-1.5f, 0f, 0f), new Vector3(1f, 40f, 6f));
            BoundsWall(level.transform, "LimiteDer", new Vector3(W * C - 0.5f, 0f, 0f), new Vector3(1f, 40f, 6f));

            // Pared del fondo del complejo
            float top = -8.5f * C;
            float bottom = -(H - 0.5f) * C;
            GameObject back = Primitive(PrimitiveType.Cube, "ParedFondo", level.transform,
                new Vector3((W * C) * 0.5f - 1f, (top + bottom) * 0.5f, WallBack + 0.1f),
                new Vector3(W * C, top - bottom, 0.2f), mats["FondoAzulejos"]);
            back.AddComponent<WorldUVBox>().tileSize = 3f;
            back.isStatic = true;

            // Cielo detrás de la superficie del mar
            GameObject sky = Primitive(PrimitiveType.Quad, "Cielo", level.transform, new Vector3(W * C * 0.5f, 12f, 40f),
                new Vector3(W * C + 120f, 34f, 1f), mats["Cielo"]);
            sky.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            Debug.Log("[CatSquad] Paredes: " + blocks + " bloques fusionados (antes serían cientos de tiles).");
        }

        private static bool IsWallCell(int c, int r, bool rockPass)
        {
            if (At(c, r) != '#') return false;
            bool rock = r < 9;
            return rock == rockPass;
        }

        private static void CreateWallBlock(Transform parent, int c, int r, int w, int h, bool rock)
        {
            float x0 = c * C - C * 0.5f;
            float y0 = -r * C + C * 0.5f;
            Vector3 center = new Vector3(x0 + w * C * 0.5f, y0 - h * C * 0.5f, (WallFront + WallBack) * 0.5f);
            GameObject go = Primitive(PrimitiveType.Cube, (rock ? "Roca_" : "Muro_") + c + "_" + r, parent, center,
                new Vector3(w * C, h * C, WallBack - WallFront), mats[rock ? "ParedRoca" : "ParedMetal"], true);
            go.AddComponent<WorldUVBox>().tileSize = rock ? 5f : 2f;
            go.isStatic = true;
        }

        private static void BoundsWall(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            BoxCollider b = go.AddComponent<BoxCollider>();
            b.size = size;
        }

        // ================================================================== rejillas

        private static void BuildGrates(GameObject level)
        {
            foreach (Vector2Int cell in FindAll('|')) CreateGrate(level.transform, cell, true);
            foreach (Vector2Int cell in FindAll('-')) CreateGrate(level.transform, cell, false);
        }

        private static void CreateGrate(Transform parent, Vector2Int cell, bool vertical)
        {
            Vector3 p = CellCenter(cell);
            GameObject g = new GameObject("Rejilla_" + cell.x + "_" + cell.y);
            g.transform.SetParent(parent, false);
            g.transform.position = p;
            g.AddComponent<Grate>();
            BoxCollider bc = g.AddComponent<BoxCollider>();
            bc.size = vertical ? new Vector3(0.35f, C, 2.4f) : new Vector3(C, 0.35f, 2.4f);

            // Barrotes que atraviesan la profundidad (se ven de frente como una fila de puntos)
            int bars = 5;
            for (int i = 0; i < bars; i++)
            {
                float t = (i + 0.5f) / bars - 0.5f;
                Vector3 offset = vertical ? new Vector3(0f, t * C, 0f) : new Vector3(t * C, 0f, 0f);
                GameObject b = Primitive(PrimitiveType.Cylinder, "Barrote", g.transform, p + offset, new Vector3(0.1f, 1.2f, 0.1f), mats["Rejilla"]);
                b.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                b.isStatic = true;
            }
            // marco delantero y trasero
            for (int k = 0; k < 2; k++)
            {
                float z = k == 0 ? -1.15f : 1.15f;
                GameObject frame = Primitive(PrimitiveType.Cube, "Marco", g.transform, p + new Vector3(0f, 0f, z),
                    vertical ? new Vector3(0.1f, C, 0.1f) : new Vector3(C, 0.1f, 0.1f), mats["Rejilla"]);
                frame.isStatic = true;
            }
        }

        // ================================================================== puntos

        private static Dictionary<char, Transform> BuildPoints(GameObject root)
        {
            Dictionary<char, Transform> d = new Dictionary<char, Transform>();
            for (int r = 0; r < H; r++)
            {
                for (int c = 0; c < rows[r].Length; c++)
                {
                    char ch = rows[r][c];
                    if (ch < 'a' || ch > 'k' || ch == 'f') continue;
                    GameObject p = Child(root, "Punto_" + ch);
                    p.transform.position = CellCenter(new Vector2Int(c, r));
                    d[ch] = p.transform;
                }
            }
            return d;
        }

        // ================================================================== agua

        private static Dictionary<string, WaterZone> BuildWater(GameObject root)
        {
            Dictionary<string, WaterZone> z = new Dictionary<string, WaterZone>();
            z["mar"] = Zone(root, "mar", "Mar", 0, 59, 0, 8, -5f, 1000f);
            z["muelle"] = Zone(root, "muelle", "Sala del muelle", 1, 25, 9, 21, -5f, 1000f);
            z["pozo"] = Zone(root, "pozo", "Pozo", 26, 31, 9, 30, -30.6f, -17.4f);
            z["almacen"] = Zone(root, "almacen", "Almacén", 32, 58, 8, 17, -36f, -17.4f);
            z["fosa"] = Zone(root, "fosa", "Fosa", 32, 58, 18, 43, -5f, 1000f);
            z["almacen"].connectedTo = z["pozo"];
            z["almacen"].connectionY = -25f;
            return z;
        }

        private static WaterZone Zone(GameObject root, string id, string display, int c0, int c1, int r0, int r1, float baseLevel, float maxLevel)
        {
            GameObject go = Child(root, "Zona_" + display);
            WaterZone wz = go.AddComponent<WaterZone>();
            wz.zoneId = id;
            wz.displayName = display;
            wz.min = new Vector2(c0 * C - C * 0.5f, -r1 * C - C * 0.5f);
            wz.max = new Vector2(c1 * C + C * 0.5f, -r0 * C + C * 0.5f);
            wz.baseLevel = baseLevel;
            wz.maxLevel = maxLevel;
            wz.visualZFront = WallFront + 0.05f;
            wz.visualZBack = WallBack - 0.05f;

            GameObject body = Primitive(PrimitiveType.Cube, "Volumen", go.transform, Vector3.zero, Vector3.one, mats["Agua"]);
            body.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            GameObject surf = Primitive(PrimitiveType.Cube, "Superficie", go.transform, Vector3.zero, Vector3.one, mats["AguaSuperficie"]);
            surf.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            wz.waterBody = body.transform;
            wz.surface = surf.transform;

            // colocación inicial para verla en el editor
            float top = Mathf.Min(baseLevel, wz.max.y + 0.6f);
            float height = Mathf.Max(0.01f, top - wz.min.y);
            body.transform.position = new Vector3((wz.min.x + wz.max.x) * 0.5f, wz.min.y + height * 0.5f, (wz.visualZFront + wz.visualZBack) * 0.5f);
            body.transform.localScale = new Vector3(wz.max.x - wz.min.x, height, wz.visualZBack - wz.visualZFront);
            surf.transform.position = new Vector3(body.transform.position.x, baseLevel, body.transform.position.z);
            surf.transform.localScale = new Vector3(wz.max.x - wz.min.x, 0.12f, wz.visualZBack - wz.visualZFront);
            return wz;
        }

        // ================================================================== objetos

        private class LevelItems
        {
            public MaterialCrate tutorialCrate;
            public List<Grabbable> tutorialDebris = new List<Grabbable>();
            public List<FuelCanister> tutorialFuel = new List<FuelCanister>();
            public PumpStation pump1;
            public PumpStation pump2;
            public FloodGate gate;
        }

        private static LevelItems BuildItems(GameObject itemsRoot, GameObject decoRoot, Dictionary<string, WaterZone> zones)
        {
            LevelItems items = new LevelItems();
            Transform t = itemsRoot.transform;
            Vector2Int tutorialCrateCell = new Vector2Int(2, 13);

            foreach (Vector2Int cell in FindAll('M'))
            {
                MaterialCrate crate = CreateCrate(t, cell);
                if (cell == tutorialCrateCell) { crate.isTutorialCrate = true; items.tutorialCrate = crate; crate.name += "_Tutorial"; }
            }

            foreach (Vector2Int cell in FindAll('F'))
            {
                FuelCanister f = CreateFuel(t, cell);
                if (cell.y >= 20 && cell.x <= 12) items.tutorialFuel.Add(f);
            }

            foreach (Vector2Int cell in FindAll('D'))
            {
                Grabbable d = CreateDebris(t, cell);
                if (cell.y >= 20 && cell.x <= 12) items.tutorialDebris.Add(d);
            }

            items.gate = CreateGate(t);
            items.pump1 = CreatePump(t, FindChar('P'), "bomba1", "Bomba del pozo");
            items.pump1.waterTargets = new[] { new PumpWaterTarget { zone = zones["pozo"], levelFrom = -30.6f, levelTo = -17.4f } };
            items.pump2 = CreatePump(t, FindChar('Q'), "bomba2", "Bomba de la compuerta");
            items.pump2.gates = items.gate != null ? new[] { items.gate } : new FloodGate[0];

            string[] names = { "Mishi", "Pelusín", "Canela", "Nube", "Tofu", "Bigotes" };
            string[] furs = { "PelajeGris", "PelajeBlanco", "PelajeCanela", "PelajeNegro", "PelajeCrema", "GatoPelaje" };
            int k = 0;
            foreach (Vector2Int cell in FindAll('K'))
            {
                CreateKittenCage(t, cell, "gatito_" + (k + 1), names[k % names.Length], furs[k % furs.Length]);
                k++;
            }

            // Decoración y vida
            System.Random rnd = new System.Random(11);
            foreach (Vector2Int cell in FindAll('x')) CreateFish(decoRoot.transform, cell, rnd);
            foreach (Vector2Int cell in FindAll('w'))
            {
                for (int i = 0; i < 2; i++)
                {
                    Vector3 p = new Vector3(cell.x * C + (float)(rnd.NextDouble() - 0.5) * 1.4f, FloorBelow(cell), 0.8f + (float)rnd.NextDouble() * 1.8f);
                    GameObject a = Model("Alga.fbx", decoRoot.transform, p, 1.6f + (float)rnd.NextDouble() * 1.4f, null, true);
                    a.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                }
            }
            foreach (Vector2Int cell in FindAll('o'))
            {
                Vector3 p = new Vector3(cell.x * C, FloorBelow(cell) + 0.2f, 1.4f);
                GameObject rock = Model("Roca.fbx", decoRoot.transform, p, 1.6f + (float)rnd.NextDouble());
                rock.transform.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                Vector3 cp = p + new Vector3(1f, 0f, 0.6f);
                cp.y = FloorBelow(cell);
                Model("Coral.fbx", decoRoot.transform, cp, 1.1f + (float)rnd.NextDouble() * 0.6f, null, true);
            }
            return items;
        }

        /// <summary>
        /// Radio de la corriente alrededor de cada objetivo. Con el brazo de 3.4 m y el hombro
        /// desplazado ~0.75 m del centro del casco, el hombro queda a 2.3–3.0 m del objetivo:
        /// entre el 70 % y el 90 % del alcance.
        /// </summary>
        private const float FieldRadius = 2.2f;

        private static GameObject CreateCurrentField(Transform parent, Vector3 center, float radius, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(center.x, center.y, 0f);
            SphereCollider sc = go.AddComponent<SphereCollider>();
            sc.radius = radius;
            go.AddComponent<Grate>();
            CurrentField cf = go.AddComponent<CurrentField>();
            cf.ringMaterial = mats["AnilloCorriente"];
            return go;
        }

        private static GameObject GrabbableRoot(Transform parent, string name, Vector3 pos, float mass)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.useGravity = false;
            rb.linearDamping = 2f;
            rb.angularDamping = 2f;
            rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
            return root;
        }

        private static MaterialCrate CreateCrate(Transform parent, Vector2Int cell)
        {
            Vector3 pos = new Vector3(cell.x * C, FloorY(cell, 0.45f), 0f);
            GameObject root = GrabbableRoot(parent, "CajaChatarra_" + cell.x + "_" + cell.y, pos, 2f);
            Model("CajaChatarra.fbx", root.transform, pos + Vector3.down * 0.45f, 1.15f);
            BoxCollider bc = root.AddComponent<BoxCollider>();
            bc.size = new Vector3(1.1f, 0.9f, 0.9f);
            bc.center = new Vector3(0f, 0f, 0f);
            MaterialCrate crate = root.AddComponent<MaterialCrate>();
            crate.kind = GrabKind.Material;
            crate.displayName = "chatarra brillante";
            crate.shardMaterial = mats["Fragmento"];
            Light glow = new GameObject("Brillo").AddComponent<Light>();
            glow.transform.SetParent(root.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0.6f, -0.8f);
            glow.type = LightType.Point;
            glow.range = 2.6f;
            glow.intensity = 1.4f;
            glow.color = new Color(1f, 0.8f, 0.3f);
            crate.currentField = CreateCurrentField(parent, pos, FieldRadius, "Corriente_" + root.name);
            return crate;
        }

        private static FuelCanister CreateFuel(Transform parent, Vector2Int cell)
        {
            Vector3 pos = new Vector3(cell.x * C, FloorY(cell, 0.42f), 0f);
            GameObject root = GrabbableRoot(parent, "Bidon_" + cell.x + "_" + cell.y, pos, 1f);
            GameObject model = Model("Bidon.fbx", root.transform, pos, 0.95f);
            BoxCollider bc = root.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.75f, 0.85f, 0.45f);
            FuelCanister f = root.AddComponent<FuelCanister>();
            f.kind = GrabKind.Combustible;
            f.displayName = "bidón de combustible";
            f.streamMaterial = mats["ChorroCombustible"];
            f.holdOffset = new Vector3(0f, -0.45f, 0f);
            Transform spout = FindDeep(model.transform, "PuntoBoquilla");
            f.spout = spout != null ? spout : root.transform;
            return f;
        }

        private static Grabbable CreateDebris(Transform parent, Vector2Int cell)
        {
            Vector3 pos = new Vector3(cell.x * C, -cell.y * C, 0f);
            GameObject root = GrabbableRoot(parent, "Escombro_" + cell.x + "_" + cell.y, pos, 3f);
            GameObject model = Model("Escombro.fbx", root.transform, pos, 1.7f);
            model.transform.rotation = Quaternion.Euler(0f, 0f, (cell.x * 37 % 20) - 10f);
            BoxCollider bc = root.AddComponent<BoxCollider>();
            bc.size = new Vector3(1.55f, 0.95f, 0.9f);
            Grabbable g = root.AddComponent<Grabbable>();
            g.kind = GrabKind.Escombro;
            g.displayName = "escombro";
            g.followStrength = 7f;
            g.maxCarrySpeed = 4f;
            g.holdOffset = new Vector3(0f, -0.55f, 0f);
            return g;
        }

        private static PumpStation CreatePump(Transform parent, Vector2Int cell, string id, string display)
        {
            float floor = -cell.y * C - C * 0.5f;
            Vector3 pos = new Vector3(cell.x * C, floor, 0f);
            GameObject root = new GameObject("Bomba_" + id);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            GameObject model = Model("Bomba.fbx", root.transform, pos, 2.7f, null, true);

            BoxCollider bc = root.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 1.1f, 0f);
            bc.size = new Vector3(2.3f, 2.1f, 1.4f);

            PumpStation p = root.AddComponent<PumpStation>();
            p.pumpId = id;
            p.displayName = display;
            p.lever = FindDeep(model.transform, "Palanca");
            p.progressFill = FindDeep(model.transform, "BarraProgreso");
            p.tankFill = FindDeep(model.transform, "BarraTanque");
            Transform luz = FindDeep(model.transform, "Luz");
            p.statusLight = luz != null ? luz.GetComponent<Renderer>() : null;

            Transform intakeModel = FindDeep(model.transform, "PuntoToma");
            GameObject intake = new GameObject("Toma");
            intake.transform.SetParent(root.transform, false);
            intake.transform.position = intakeModel != null ? intakeModel.position : pos + new Vector3(0.6f, 2.3f, 0f);
            intake.transform.position = new Vector3(intake.transform.position.x, intake.transform.position.y, 0f);
            p.intake = intake.transform;

            // el submarino se acopla del lado del embudo, mirando hacia él
            float side = Mathf.Sign(intake.transform.position.x - pos.x + 0.0001f);
            GameObject dock = new GameObject("PuntoAcople");
            dock.transform.SetParent(root.transform, false);
            dock.transform.position = new Vector3(intake.transform.position.x + side * 2.7f, intake.transform.position.y + 0.5f, 0f);
            p.dockPoint = dock.transform;

            GameObject light = new GameObject("LuzBomba");
            light.transform.SetParent(root.transform, false);
            light.transform.position = pos + new Vector3(0f, 2.8f, -1.2f);
            Light l = light.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 5f;
            l.intensity = 1.2f;
            l.color = new Color(0.6f, 1f, 0.9f);
            return p;
        }

        private static FloodGate CreateGate(Transform parent)
        {
            List<Vector2Int> cells = FindAll('G');
            if (cells.Count == 0) return null;
            int minRow = int.MaxValue, maxRow = int.MinValue;
            foreach (Vector2Int c in cells) { minRow = Mathf.Min(minRow, c.y); maxRow = Mathf.Max(maxRow, c.y); }
            int col = cells[0].x;
            float top = -minRow * C + C * 0.5f;
            float bottom = -maxRow * C - C * 0.5f;

            GameObject root = new GameObject("Compuerta");
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(col * C, (top + bottom) * 0.5f, 0f);
            GameObject door = Primitive(PrimitiveType.Cube, "Hoja", root.transform, root.transform.position,
                new Vector3(C * 0.8f, top - bottom, 2.2f), mats["CompuertaFranjas"], true);
            door.AddComponent<WorldUVBox>().tileSize = 2f;
            FloodGate g = root.AddComponent<FloodGate>();
            g.door = door.transform;
            g.blocker = door.GetComponent<Collider>();
            g.openHeight = (top - bottom) + 0.4f;
            g.displayName = "Compuerta de la fosa";
            return g;
        }

        private static void CreateKittenCage(Transform parent, Vector2Int cell, string id, string name, string fur)
        {
            float floor = -cell.y * C - C * 0.5f;
            Vector3 pos = new Vector3(cell.x * C, floor, 0f);
            GameObject root = new GameObject("Jaula_" + name);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            GameObject cageModel = Model("Jaula.fbx", root.transform, pos, 1.55f, null, true);
            SphereCollider sc = root.AddComponent<SphereCollider>();
            sc.center = new Vector3(0f, 0.8f, 0f);
            sc.radius = 0.75f;

            KittenCage cage = root.AddComponent<KittenCage>();
            cage.door = FindDeep(cageModel.transform, "Puerta");
            cage.bubbleShell = FindDeep(cageModel.transform, "Burbuja");
            cage.doorOpenEuler = new Vector3(0f, -100f, 0f);

            // gatito dentro de la jaula
            GameObject kitten = new GameObject("Gatito_" + name);
            kitten.transform.SetParent(parent, false);
            kitten.transform.position = pos + new Vector3(0f, 0.35f, 0f);
            GameObject visual = Model("Gato.fbx", kitten.transform, kitten.transform.position, 0.72f,
                new Dictionary<string, string> { { "GatoPelaje", fur } }, true);
            visual.name = "Visual";
            SteeringAgent agent = kitten.AddComponent<SteeringAgent>();
            agent.visual = visual.transform;
            agent.body.maxSpeed = 6.5f;
            agent.body.maxForce = 16f;
            agent.body.mass = 0.5f;
            agent.bobAmplitude = 0.06f;
            RescuedKitten rk = kitten.AddComponent<RescuedKitten>();
            rk.kittenId = id;
            rk.kittenName = name;
            cage.kitten = rk;

            // cerrojo del lado con agua libre
            int side = At(cell.x - 1, cell.y) == '.' || At(cell.x - 1, cell.y) == 'w' ? -1 : 1;
            Vector3 latchPos = pos + new Vector3(side * 0.95f, 0.75f, -0.2f);
            GameObject latchRoot = GrabbableRoot(parent, "Cerrojo_" + name, latchPos, 0.5f);
            Model("Cerrojo.fbx", latchRoot.transform, latchPos, 0.55f);
            BoxCollider lc = latchRoot.AddComponent<BoxCollider>();
            lc.size = new Vector3(0.6f, 0.35f, 0.35f);
            lc.isTrigger = true;
            CageLatch latch = latchRoot.AddComponent<CageLatch>();
            latch.displayName = "cerrojo de la jaula";
            latch.cage = cage;
            latch.holdOffset = Vector3.zero;
            cage.latch = latch;
            cage.currentField = CreateCurrentField(parent, latchPos, FieldRadius, "Corriente_" + name);
        }

        private static void CreateFish(Transform parent, Vector2Int cell, System.Random rnd)
        {
            int n = 2 + rnd.Next(2);
            for (int i = 0; i < n; i++)
            {
                Vector3 pos = CellCenter(cell) + new Vector3((float)(rnd.NextDouble() - 0.5) * 2f, (float)(rnd.NextDouble() - 0.5) * 1.5f, -0.4f + (float)rnd.NextDouble() * 2f);
                GameObject fish = new GameObject("Pez");
                fish.transform.SetParent(parent, false);
                fish.transform.position = pos;
                GameObject v = Model("Pez.fbx", fish.transform, pos, 0.6f + (float)rnd.NextDouble() * 0.3f);
                v.name = "Visual";
                SteeringAgent a = fish.AddComponent<SteeringAgent>();
                a.visual = v.transform;
                a.body.maxSpeed = 1.5f;
                a.body.maxForce = 5f;
                a.yawWhenMovingRight = 95f;
                a.bobAmplitude = 0.05f;
                a.pitchByVerticalSpeed = 20f;
                fish.AddComponent<CuriousFish>();
            }
        }

        // ================================================================== submarino y brazo

        private static SubmarineController BuildSubmarine(GameObject parent, Vector3 spawn)
        {
            GameObject root = new GameObject("Submarino");
            root.transform.SetParent(parent.transform, false);
            root.transform.position = spawn;
            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 5f;
            rb.useGravity = false;
            rb.linearDamping = 0.5f;
            rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            CapsuleCollider cap = root.AddComponent<CapsuleCollider>();
            cap.direction = 0;
            cap.radius = 0.62f;
            cap.height = 2.75f;
            PhysicsMaterial slide = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(MatDir + "/SubDeslizante.asset");
            if (slide == null)
            {
                slide = new PhysicsMaterial("SubDeslizante") { dynamicFriction = 0f, staticFriction = 0f, bounciness = 0.05f, frictionCombine = PhysicsMaterialCombine.Minimum };
                AssetDatabase.CreateAsset(slide, MatDir + "/SubDeslizante.asset");
            }
            cap.sharedMaterial = slide;

            GameObject visual = Child(root, "Visual");
            visual.transform.localRotation = Quaternion.Euler(0f, 100f, 0f);
            GameObject pivot = Child(visual, "ModelPivot");
            GameObject model = Model("Submarino.fbx", pivot.transform, spawn, 2.9f);
            model.transform.localPosition = Vector3.zero;

            // piloto: el gato de Cat Squad dentro de la cúpula
            Transform seat = FindDeep(model.transform, "AsientoPiloto");
            Vector3 seatPos = seat != null ? seat.position : spawn + new Vector3(0.6f, 0.2f, 0f);
            GameObject pilot = Model("Gato.fbx", seat != null ? seat : pivot.transform, seatPos, 0.62f,
                new Dictionary<string, string> { { "GatoPelaje", "PelajeGris" } }, true);
            pilot.name = "GatoPiloto";
            pilot.transform.localRotation = Quaternion.identity;
            pilot.transform.position = seatPos + Vector3.down * 0.28f;

            SubmarineController sub = root.AddComponent<SubmarineController>();
            sub.visual = visual.transform;
            sub.propeller = FindDeep(model.transform, "Helice");

            Transform armPoint = FindDeep(model.transform, "PuntoBrazo");
            Transform cargo = FindDeep(model.transform, "PuntoCarga");
            Transform lamp = FindDeep(model.transform, "PuntoFoco");
            sub.armMount = armPoint != null ? armPoint : visual.transform;
            sub.cargoPort = cargo != null ? cargo : root.transform;

            GameObject head = new GameObject("Foco");
            head.transform.SetParent(lamp != null ? lamp : visual.transform, false);
            Light spot = head.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.range = 9f;
            spot.spotAngle = 70f;
            spot.intensity = 4f;
            spot.color = new Color(1f, 0.95f, 0.8f);
            spot.shadows = LightShadows.None;
            sub.headlight = spot;

            GameObject glow = new GameObject("BrilloCabina");
            glow.transform.SetParent(visual.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0.6f, 0.4f);
            Light g = glow.AddComponent<Light>();
            g.type = LightType.Point;
            g.range = 4.5f;
            g.intensity = 1.6f;
            g.color = new Color(1f, 0.85f, 0.6f);

            sub.bubbles = CreateBubbles(visual.transform, sub.propeller != null ? sub.propeller.position : spawn);
            return sub;
        }

        private static ParticleSystem CreateBubbles(Transform parent, Vector3 pos)
        {
            GameObject go = new GameObject("Burbujas");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.startLifetime = 1.6f;
            main.startSpeed = 0.4f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
            main.gravityModifier = -0.08f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;
            ParticleSystem.EmissionModule em = ps.emission;
            em.rateOverTime = 3f;
            ParticleSystemRenderer psr = go.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = mats["ParticulaBurbuja"];
            return ps;
        }

        private static ManipulatorArm BuildArm(GameObject parent, SubmarineController sub)
        {
            GameObject root = new GameObject("BrazoManipulador");
            root.transform.SetParent(parent.transform, false);
            ManipulatorArm arm = root.AddComponent<ManipulatorArm>();
            arm.submarine = sub;
            arm.mount = sub.armMount;
            arm.reach = 3.4f;

            Material dark = mats["SubOscuro"];
            Material acc = mats["SubAcento"];
            Material hull = mats["SubCasco"];
            Vector3 p = sub.armMount.position;

            arm.upperArm = Primitive(PrimitiveType.Cylinder, "Brazo", root.transform, p, Vector3.one * 0.2f, dark).transform;
            arm.foreArm = Primitive(PrimitiveType.Cylinder, "Antebrazo", root.transform, p, Vector3.one * 0.2f, dark).transform;
            arm.elbowJoint = Primitive(PrimitiveType.Sphere, "Codo", root.transform, p, Vector3.one * 0.3f, hull).transform;

            GameObject claw = new GameObject("Pinza");
            claw.transform.SetParent(root.transform, false);
            claw.transform.position = p;
            arm.claw = claw.transform;
            GameObject palm = Primitive(PrimitiveType.Cube, "Palma", claw.transform, p, new Vector3(0.42f, 0.18f, 0.3f), acc);
            palm.transform.localPosition = Vector3.zero;

            arm.fingerA = Finger(claw.transform, -0.17f, acc, hull);
            arm.fingerB = Finger(claw.transform, 0.17f, acc, hull);

            GameObject marker = Primitive(PrimitiveType.Cylinder, "MarcadorMano", root.transform, p, new Vector3(0.45f, 0.01f, 0.45f), mats["MarcadorMano"]);
            marker.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            marker.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            arm.targetMarker = marker.transform;
            return arm;
        }

        private static Transform Finger(Transform claw, float x, Material m, Material tip)
        {
            GameObject pivot = new GameObject(x < 0 ? "DedoA" : "DedoB");
            pivot.transform.SetParent(claw, false);
            pivot.transform.localPosition = new Vector3(x, 0.08f, 0f);
            GameObject seg = Primitive(PrimitiveType.Cube, "Segmento", pivot.transform, Vector3.zero, new Vector3(0.08f, 0.42f, 0.14f), m);
            seg.transform.localPosition = new Vector3(0f, 0.21f, 0f);
            GameObject nail = Primitive(PrimitiveType.Cube, "Punta", pivot.transform, Vector3.zero, new Vector3(0.12f, 0.1f, 0.14f), tip);
            nail.transform.localPosition = new Vector3(-Mathf.Sign(x) * 0.04f, 0.42f, 0f);
            return pivot.transform;
        }

        // ================================================================== guía, muelle, cámara

        private static GuideCat BuildGuide(GameObject parent, Vector3 pos)
        {
            GameObject root = new GameObject("CapitanaPelusa");
            root.transform.SetParent(parent.transform, false);
            root.transform.position = new Vector3(pos.x, pos.y, -0.6f);
            GameObject visual = Model("GatoGuia.fbx", root.transform, root.transform.position, 1.35f, null, true);
            visual.name = "Visual";
            SteeringAgent agent = root.AddComponent<SteeringAgent>();
            agent.visual = visual.transform;
            agent.body.maxSpeed = 4.5f;
            agent.body.maxForce = 11f;
            agent.body.mass = 0.5f;
            agent.bobAmplitude = 0.12f;
            agent.yawWhenMovingRight = 120f;
            GuideCat guide = root.AddComponent<GuideCat>();

            GameObject arrow = Primitive(PrimitiveType.Cylinder, "Flecha", root.transform, root.transform.position, new Vector3(0.18f, 0.35f, 0.18f), mats["Cristal"]);
            GameObject tip = Primitive(PrimitiveType.Sphere, "Punta", arrow.transform, arrow.transform.position, Vector3.one, mats["Cristal"]);
            tip.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            tip.transform.localScale = new Vector3(1.8f, 0.9f, 1.8f);
            guide.pointerArrow = arrow.transform;

            GameObject l = new GameObject("LuzGuia");
            l.transform.SetParent(root.transform, false);
            l.transform.localPosition = new Vector3(0f, 0.8f, -1f);
            Light light = l.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 3.5f;
            light.intensity = 1.3f;
            light.color = new Color(1f, 0.85f, 0.65f);
            return guide;
        }

        private static Dock BuildDock(GameObject world, Vector3 subSpawn)
        {
            GameObject root = new GameObject("Muelle");
            root.transform.SetParent(world.transform, false);
            Vector3 pos = new Vector3(3.8f, -4.55f, 0.4f);
            root.transform.position = pos;
            GameObject model = Model("Muelle.fbx", root.transform, pos, 10f);
            model.transform.localPosition = Vector3.zero;
            BoxCollider bc = root.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, -1.2f, -0.4f);
            bc.size = new Vector3(9.4f, 3f, 3f);

            Dock dock = root.AddComponent<Dock>();
            List<Transform> seats = new List<Transform>();
            for (int i = 0; i < 6; i++)
            {
                Transform s = FindDeep(model.transform, "Asiento" + i);
                if (s != null) seats.Add(s);
            }
            if (seats.Count == 0)
            {
                for (int i = 0; i < 6; i++)
                {
                    GameObject s = new GameObject("Asiento" + i);
                    s.transform.SetParent(root.transform, false);
                    s.transform.position = pos + new Vector3(-2.2f + i * 0.95f, 0.35f, -0.4f);
                    seats.Add(s.transform);
                }
            }
            dock.seats = seats.ToArray();
            GameObject entry = new GameObject("EntradaAgua");
            entry.transform.SetParent(root.transform, false);
            entry.transform.position = new Vector3(pos.x + 5.6f, -5.4f, 0f);
            dock.waterEntry = entry.transform;
            return dock;
        }

        private static Camera BuildCamera(GameObject systems, Transform target)
        {
            GameObject go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            go.transform.SetParent(systems.transform, false);
            Camera cam = go.AddComponent<Camera>();
            cam.fieldOfView = 38f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 250f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.42f, 0.55f);
            go.AddComponent<AudioListener>();
            CameraFollow2_5D follow = go.AddComponent<CameraFollow2_5D>();
            follow.target = target;
            follow.offset = new Vector3(0f, 1.2f, -19f);
            follow.minBounds = new Vector2(6f, -84f);
            follow.maxBounds = new Vector2(W * C - 8f, 2f);
            go.transform.position = target.position + follow.offset;
            return cam;
        }

        // ================================================================== sistemas

        private static void BuildSystems(GameObject root, SubmarineController sub, ManipulatorArm arm, GuideCat guide, Dock dock,
                                         Camera cam, Dictionary<string, WaterZone> zones, LevelItems items,
                                         Dictionary<char, Transform> points, Vector3 subSpawn)
        {
            GameObject gmGo = Child(root, "GameManager");
            GameManager gm = gmGo.AddComponent<GameManager>();

            GameObject inputGo = Child(root, "EntradaBimanual");
            BimanualInput input = inputGo.AddComponent<BimanualInput>();
            input.keyboard = inputGo.AddComponent<KeyboardMouseInputProvider>();
            input.webcam = inputGo.AddComponent<WebcamPoseInputProvider>();
            input.gameCamera = cam;

            GameObject sessionGo = Child(root, "Sesion");
            SessionManager session = sessionGo.AddComponent<SessionManager>();
            SessionMetrics metrics = sessionGo.AddComponent<SessionMetrics>();
            CalibrationSequence calib = sessionGo.AddComponent<CalibrationSequence>();
            calib.bubbleMaterial = mats["Burbuja"];
            TutorialDirector tutorial = sessionGo.AddComponent<TutorialDirector>();

            GameObject dockPoint = Child(root, "PuntoMuelle");
            dockPoint.transform.position = subSpawn;

            Transform dive1 = Child(root, "Bajada1").transform;
            dive1.position = new Vector3(subSpawn.x, -12f, 0f);
            Transform dive2 = Child(root, "Bajada2").transform;
            dive2.position = new Vector3(subSpawn.x, -18.5f, 0f);

            session.tutorial = tutorial;
            session.calibration = calib;
            session.calibrationPoint = Get(points, 'a');
            session.diveRoute = new[] { dive1, dive2 };

            tutorial.session = session;
            tutorial.hallZone = zones["muelle"];
            tutorial.storageZone = zones["almacen"];
            tutorial.tutorialCrate = items.tutorialCrate;
            tutorial.tutorialDebris = items.tutorialDebris.ToArray();
            tutorial.tutorialFuel = items.tutorialFuel.ToArray();
            tutorial.tutorialPump = items.pump1;
            GameObject gStart = Child(root, "GuiaInicio");
            gStart.transform.position = subSpawn + new Vector3(18f, 0.6f, -0.6f);
            GameObject gDock = Child(root, "GuiaMuelle");
            gDock.transform.position = subSpawn + new Vector3(3.6f, 0.2f, 0f);
            tutorial.guideStart = gStart.transform;
            tutorial.guideDock = gDock.transform;
            tutorial.diveRoute = new[] { dive1, dive2 };
            tutorial.crateSpot = Get(points, 'b');
            tutorial.debrisSpot = Get(points, 'c');
            tutorial.pumpSpot = Get(points, 'd');
            tutorial.shaftRoute = new[] { Get(points, 'e'), Get(points, 'g'), Get(points, 'h'), Get(points, 'i') };

            guide.transform.position = gStart.transform.position;

            GameObject hudGo = Child(root, "HUD");
            GameHUD hud = hudGo.AddComponent<GameHUD>();
            hud.roundedSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            hud.guidePortrait = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/Textures/RetratoGuia.png");
            hudGo.AddComponent<TherapistPanel>();

            GameObject audioGo = Child(root, "Audio");
            AudioFx fx = audioGo.AddComponent<AudioFx>();
            fx.bubblePop = Clip("pop.wav");
            fx.shardCollect = Clip("ding.wav");
            fx.crateOpen = Clip("crate_open.wav");
            fx.pumpLoop = Clip("pump_loop.wav");
            fx.pourLoop = Clip("pour_loop.wav");
            fx.gateOpen = Clip("gate_open.wav");
            fx.kittenMeow = Clip("meow.wav");
            fx.chime = Clip("chime.wav");
            fx.ambientLoop = Clip("ambient_loop.wav");

            DepthAmbience amb = Child(root, "Ambiente").AddComponent<DepthAmbience>();
            amb.target = sub.transform;
            amb.cam = cam;
            GameObject sunGo = GameObject.Find("Sol");
            amb.sun = sunGo != null ? sunGo.GetComponent<Light>() : null;

            GameObject pressure = Primitive(PrimitiveType.Cube, "LimitePresion", root.transform, Vector3.zero, Vector3.one, mats["LimitePresion"]);
            pressure.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            PressureLimitVisual plv = pressure.AddComponent<PressureLimitVisual>();
            plv.minX = 62f;
            plv.maxX = W * C - 1f;

            gm.submarine = sub;
            gm.arm = arm;
            gm.guide = guide;
            gm.input = input;
            gm.session = session;
            gm.metrics = metrics;
            gm.hud = hud;
            gm.dockPoint = dockPoint.transform;
        }

        private static Transform Get(Dictionary<char, Transform> d, char c)
        {
            Transform t;
            return d.TryGetValue(c, out t) ? t : null;
        }

        private static AudioClip Clip(string file)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/" + file);
        }

        private static void AddSceneToBuild(string path)
        {
            List<EditorBuildSettingsScene> list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (EditorBuildSettingsScene s in list) if (s.path == path) return;
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
