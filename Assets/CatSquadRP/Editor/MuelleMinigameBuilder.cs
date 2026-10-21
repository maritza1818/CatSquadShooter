using System.Collections.Generic;
using CatSquad.Muelle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CatSquad.RescateProfundo.EditorTools
{
    /// <summary>
    /// LAB 05 · Construye el minijuego 2D "El Muelle" (escena Muelle2D) con los pasos del enunciado:
    ///  muros con Box Collider 2D · cesta (la "taza") con Rigidbody 2D + Edge Collider 2D · ovillo (la "bola") con
    ///  Rigidbody 2D + Circle Collider 2D · Physics Material 2D "Rebote" en el ovillo · Ball.cs · GameManager con
    ///  la trayectoria y el arrastre. Ejercicio 2: segundo blanco (balde). Ejercicio 3: puntuación.
    /// </summary>
    public static class MuelleMinigameBuilder
    {
        private static string root;
        private static string Root
        {
            get
            {
                if (root != null) return root;
                foreach (string guid in AssetDatabase.FindAssets("MuelleMinigameBuilder t:MonoScript"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                    const string tail = "/Editor/MuelleMinigameBuilder.cs";
                    if (path.EndsWith(tail)) { root = path.Substring(0, path.Length - tail.Length); return root; }
                }
                root = "Assets/CatSquadRP";
                return root;
            }
        }
        private static string SpriteDir { get { return Root + "/Minijuego2D/Sprites"; } }
        private static string AssetDir { get { return Root + "/Minijuego2D"; } }
        private static string ScenePath { get { return Root + "/Scenes/Muelle2D.unity"; } }

        private const float WaterY = -2.5f;

        [MenuItem("CatSquad/Lab 05: Construir minijuego 2D (El Muelle)", priority = 3)]
        public static void Build()
        {
            root = null;
            if (!AssetDatabase.IsValidFolder(Root + "/Scenes")) AssetDatabase.CreateFolder(Root, "Scenes");

            // ---------- importar sprites
            Sprite fondo = SpriteAt("MuelleFondo.png", 90f);
            Sprite ovillo = SpriteAt("Ovillo.png", 183f);
            Sprite punto = SpriteAt("Punto.png", 100f);
            Sprite muro = SpriteAt("Muro.png", 64f, true);
            Sprite tablas = SpriteAt("Tablas.png", 128f, true);
            Sprite cesta = SpriteAt("Cesta.png", 160f);
            Sprite balsa = SpriteAt("Balsa.png", 100f);
            Sprite balde = SpriteAt("Balde.png", 200f);
            Sprite faro = SpriteAt("Faro.png", 120f);
            Sprite edificio = SpriteAt("Edificio.png", 100f);
            Sprite gatito = SpriteAt("Gatito.png", 160f);
            Sprite gatitoGris = SpriteAt("GatitoGris.png", 160f);

            // ---------- Physics Material 2D "Rebote" (paso 7 del lab)
            string matPath = AssetDir + "/Rebote.physicsMaterial2D";
            PhysicsMaterial2D rebote = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(matPath);
            if (rebote == null)
            {
                rebote = new PhysicsMaterial2D("Rebote");
                AssetDatabase.CreateAsset(rebote, matPath);
            }
            rebote.bounciness = 0.6f;
            rebote.friction = 0.4f;
            EditorUtility.SetDirty(rebote);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------- cámara
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 6f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.75f, 0.92f);
            camGo.AddComponent<AudioListener>();

            SpriteObj("Fondo", fondo, null, new Vector2(0f, 0f), Vector2.one, -100);

            // ---------- muros (paso 4: Box Collider 2D)
            GameObject walls = new GameObject("Muros");
            Wall(walls.transform, "MuroIzquierdo", muro, new Vector2(-10.95f, 4f), new Vector2(0.7f, 20f), rebote);
            Wall(walls.transform, "MuroDerecho", muro, new Vector2(10.95f, 4f), new Vector2(0.7f, 20f), rebote);

            // muelle (donde está el ovillo)
            GameObject dock = new GameObject("Muelle");
            GameObject deck = SpriteObj("Tablas", tablas, dock.transform, new Vector2(-8.4f, -2.15f), Vector2.one, 1);
            SpriteRenderer dsr = deck.GetComponent<SpriteRenderer>();
            dsr.drawMode = SpriteDrawMode.Tiled;
            dsr.size = new Vector2(4.6f, 0.35f);
            BoxCollider2D dbox = deck.AddComponent<BoxCollider2D>();
            dbox.size = dsr.size;
            dbox.sharedMaterial = rebote;
            foreach (float px in new[] { -10f, -8.4f, -6.6f })
            {
                GameObject pile = SpriteObj("Pilote", tablas, dock.transform, new Vector2(px, -3.6f), Vector2.one, 0);
                SpriteRenderer psr = pile.GetComponent<SpriteRenderer>();
                psr.drawMode = SpriteDrawMode.Tiled;
                psr.size = new Vector2(0.3f, 2.8f);
            }
            SpriteObj("CapitanaGris", gatitoGris, dock.transform, new Vector2(-9.7f, -1.45f), Vector2.one, 2);

            // faro en medio: hay que pasarle por encima o rebotar
            GameObject lighthouse = SpriteObj("Faro", faro, null, new Vector2(0.4f, 0.17f), Vector2.one, 0);
            BoxCollider2D fbox = lighthouse.AddComponent<BoxCollider2D>();
            fbox.size = new Vector2(0.95f, 5.3f);
            fbox.offset = new Vector2(0f, 0f);
            fbox.sharedMaterial = rebote;

            // ---------- blanco 1: la cesta sobre la balsa (paso 5: Rigidbody 2D + Edge Collider 2D)
            GameObject raft = new GameObject("Balsa_Cesta");
            raft.transform.position = new Vector3(4.3f, WaterY + 0.05f, 0f);
            Rigidbody2D rrb = raft.AddComponent<Rigidbody2D>();
            rrb.bodyType = RigidbodyType2D.Kinematic;
            rrb.interpolation = RigidbodyInterpolation2D.Interpolate;
            SpriteObj("Balsa", balsa, raft.transform, new Vector2(0f, 0f), Vector2.one, 1);
            BoxCollider2D raftBox = raft.AddComponent<BoxCollider2D>();
            raftBox.size = new Vector2(3.4f, 0.5f);
            raftBox.offset = new Vector2(0f, -0.05f);
            raftBox.sharedMaterial = rebote;
            GameObject basket = SpriteObj("Cesta", cesta, raft.transform, new Vector2(0.3f, 0.7f), Vector2.one, 2);
            basket.transform.localPosition = new Vector3(0.3f, 0.7f, 0f);
            EdgeCollider2D bedge = basket.AddComponent<EdgeCollider2D>();
            bedge.points = new[] { new Vector2(-0.8f, 0.42f), new Vector2(-0.64f, -0.45f), new Vector2(0.64f, -0.45f), new Vector2(0.8f, 0.42f) };
            bedge.edgeRadius = 0.04f;
            BoxCollider2D btrig = basket.AddComponent<BoxCollider2D>();
            btrig.isTrigger = true;
            btrig.size = new Vector2(1.1f, 0.55f);
            btrig.offset = new Vector2(0f, -0.15f);
            GameObject raftKitten = SpriteObj("Gatito", gatito, raft.transform, new Vector2(-1.15f, 0.72f), Vector2.one * 0.9f, 3);
            raftKitten.transform.localPosition = new Vector3(-1.15f, 0.72f, 0f);
            GameObject aim1 = new GameObject("CentroBlanco");
            aim1.transform.SetParent(basket.transform, false);
            aim1.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            MuelleTarget t1 = raft.AddComponent<MuelleTarget>();
            t1.displayName = "Cesta de la balsa";
            t1.points = 1;
            t1.floating = true;
            t1.driftAmplitude = 1.0f;
            t1.kitten = raftKitten.transform;
            t1.aimPoint = aim1.transform;

            // ---------- blanco 2 (ejercicio 2): el balde en la azotea
            GameObject building = SpriteObj("Edificio", edificio, null, new Vector2(8.9f, -2.32f), new Vector2(1f, 1.4f), 0);
            BoxCollider2D ebox = building.AddComponent<BoxCollider2D>();
            ebox.size = new Vector2(3.0f, 5.44f);
            ebox.offset = Vector2.zero;   // azotea en y ≈ 1.49
            ebox.sharedMaterial = rebote;
            GameObject bucketRoot = new GameObject("Azotea_Balde");
            bucketRoot.transform.position = new Vector3(8.4f, 1.95f, 0f);
            Rigidbody2D brb = bucketRoot.AddComponent<Rigidbody2D>();
            brb.bodyType = RigidbodyType2D.Static;
            GameObject bucket = SpriteObj("Balde", balde, bucketRoot.transform, Vector2.zero, Vector2.one, 2);
            bucket.transform.localPosition = Vector3.zero;
            EdgeCollider2D kedge = bucket.AddComponent<EdgeCollider2D>();
            kedge.points = new[] { new Vector2(-0.5f, 0.36f), new Vector2(-0.38f, -0.44f), new Vector2(0.38f, -0.44f), new Vector2(0.5f, 0.36f) };
            kedge.edgeRadius = 0.04f;
            BoxCollider2D ktrig = bucket.AddComponent<BoxCollider2D>();
            ktrig.isTrigger = true;
            ktrig.size = new Vector2(0.62f, 0.5f);
            ktrig.offset = new Vector2(0f, -0.15f);
            GameObject roofKitten = SpriteObj("GatitoAzotea", gatito, bucketRoot.transform, new Vector2(1.2f, 0f), Vector2.one * 0.9f, 3);
            roofKitten.transform.localPosition = new Vector3(1.2f, 0f, 0f);
            GameObject aim2 = new GameObject("CentroBlanco");
            aim2.transform.SetParent(bucket.transform, false);
            aim2.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            MuelleTarget t2 = bucketRoot.AddComponent<MuelleTarget>();
            t2.displayName = "Balde de la azotea";
            t2.points = 3;
            t2.kitten = roofKitten.transform;
            t2.aimPoint = aim2.transform;

            // ---------- el ovillo (paso 6: Rigidbody 2D + Circle Collider 2D; paso 8: material Rebote; paso 9: Ball.cs)
            GameObject spawn = new GameObject("PuntoDeLanzamiento");
            spawn.transform.position = new Vector3(-7.6f, -1.62f, 0f);
            GameObject ballGo = SpriteObj("Ovillo", ovillo, null, spawn.transform.position, Vector2.one, 3);
            Rigidbody2D ballRb = ballGo.AddComponent<Rigidbody2D>();
            ballRb.mass = 1f;
            ballRb.gravityScale = 1f;
            ballRb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            ballRb.interpolation = RigidbodyInterpolation2D.Interpolate;
            ballRb.sharedMaterial = rebote;
            CircleCollider2D ballCol = ballGo.AddComponent<CircleCollider2D>();
            ballCol.radius = 0.34f;
            ballCol.sharedMaterial = rebote;
            Ball ball = ballGo.AddComponent<Ball>();

            // ---------- trayectoria (puntos) con su prefab
            GameObject dotTemplate = new GameObject("Punto");
            SpriteRenderer dotSr = dotTemplate.AddComponent<SpriteRenderer>();
            dotSr.sprite = punto;
            dotSr.color = new Color(1f, 1f, 1f, 0.92f);
            dotSr.sortingOrder = 5;
            string prefabPath = AssetDir + "/Punto.prefab";
            GameObject dotPrefab = PrefabUtility.SaveAsPrefabAsset(dotTemplate, prefabPath);
            Object.DestroyImmediate(dotTemplate);

            GameObject trajGo = new GameObject("Trayectoria");
            GameObject dotsParent = new GameObject("Puntos");
            dotsParent.transform.SetParent(trajGo.transform, false);
            Trajectory traj = trajGo.AddComponent<Trajectory>();
            SerializedObject so = new SerializedObject(traj);
            so.FindProperty("dotsParent").objectReferenceValue = dotsParent;
            so.FindProperty("dotPrefab").objectReferenceValue = dotPrefab;
            so.ApplyModifiedProperties();
            TrajectorySimulator2D sim = trajGo.AddComponent<TrajectorySimulator2D>();
            sim.ball = ball;
            traj.simulator = sim;
            traj.waterY = WaterY;

            // arco guía (solución de disparo)
            GameObject ghostGo = new GameObject("ArcoGuia");
            LineRenderer lr = ghostGo.AddComponent<LineRenderer>();
            string lineMatPath = AssetDir + "/ArcoGuia2D.mat";
            Material lineMat = AssetDatabase.LoadAssetAtPath<Material>(lineMatPath);
            if (lineMat == null)
            {
                lineMat = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(lineMat, lineMatPath);
            }
            lr.sharedMaterial = lineMat;
            lr.startColor = lr.endColor = new Color(0.5f, 0.95f, 1f, 0.7f);
            lr.widthMultiplier = 0.07f;
            lr.sortingOrder = 4;
            lr.useWorldSpace = true;
            lr.enabled = false;

            // salpicadura
            GameObject splashGo = new GameObject("Salpicadura");
            ParticleSystem ps = splashGo.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = 0.7f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            main.gravityModifier = 1f;
            main.startColor = new Color(0.85f, 0.95f, 1f, 0.9f);
            ParticleSystem.EmissionModule em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)24) });
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 25f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            ParticleSystemRenderer psr2 = splashGo.GetComponent<ParticleSystemRenderer>();
            psr2.sharedMaterial = lineMat;
            psr2.sortingOrder = 6;

            // ---------- GameManager (paso 11)
            GameObject gmGo = new GameObject("GameManager");
            MuelleGameManager gm = gmGo.AddComponent<MuelleGameManager>();
            gm.ball = ball;
            gm.trajectory = traj;
            gm.spawnPoint = spawn.transform;
            gm.waterY = WaterY;
            gm.maxDragDistance = 4f;
            gm.targets = new[] { t1, t2 };
            gm.ghostLine = lr;
            gm.splash = ps;
            AudioSource src = gmGo.AddComponent<AudioSource>();
            src.playOnAwake = false;
            gm.audioSource = src;
            gm.launchClip = Clip("pop.wav");
            gm.scoreClip = Clip("ding.wav");
            gm.meowClip = Clip("meow.wav");
            gm.splashClip = Clip("crate_open.wav");
            gm.bounceClip = Clip("chime.wav");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            List<EditorBuildSettingsScene> list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            bool present = false;
            foreach (EditorBuildSettingsScene s in list) if (s.path == ScenePath) present = true;
            if (!present) { list.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = list.ToArray(); }
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = gmGo;
            EditorUtility.DisplayDialog("Lab 05 · El Muelle",
                "Escena creada en " + ScenePath + ".\n\nPulsa Play: presiona, arrastra hacia atrás y suelta.\n" +
                "A = arco guía · T = puntos con rebotes · 1/2 = blanco · Espacio = tiro automático · R = reiniciar.", "¡A lanzar!");
        }

        // ------------------------------------------------------------------ utilidades

        private static Sprite SpriteAt(string file, float ppu, bool tiled = false)
        {
            string path = SpriteDir + "/" + file;
            TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti != null)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.spritePixelsPerUnit = ppu;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.wrapMode = tiled ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                TextureImporterSettings st = new TextureImporterSettings();
                ti.ReadTextureSettings(st);
                st.spriteMeshType = tiled ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
                ti.SetTextureSettings(st);
                ti.SaveAndReimport();
            }
            Sprite s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s == null) Debug.LogWarning("[CatSquad] No encuentro el sprite " + path);
            return s;
        }

        private static GameObject SpriteObj(string name, Sprite sprite, Transform parent, Vector2 pos, Vector2 scale, int order)
        {
            GameObject go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return go;
        }

        private static void Wall(Transform parent, string name, Sprite sprite, Vector2 pos, Vector2 size, PhysicsMaterial2D mat)
        {
            GameObject go = SpriteObj(name, sprite, parent, pos, Vector2.one, 1);
            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            sr.drawMode = SpriteDrawMode.Tiled;
            sr.size = size;
            BoxCollider2D box = go.AddComponent<BoxCollider2D>();
            box.size = size;
            box.sharedMaterial = mat;
        }

        private static AudioClip Clip(string file)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/" + file);
        }
    }
}
