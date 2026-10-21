using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CatSquad.Muelle
{
    /// <summary>
    /// Herramienta de Unity para predecir trayectorias CON choques: una escena de física 2D paralela
    /// (LocalPhysicsMode.Physics2D) con copias invisibles de los muros y un ovillo fantasma. Para predecir,
    /// se coloca el fantasma, se le da la velocidad inicial y se avanza la física a mano con
    /// PhysicsScene2D.Simulate(dt), anotando la posición: así los puntos siguen también los rebotes.
    /// (Es lo que permite el material "Rebote", que la fórmula de la parábola no sabe modelar.)
    /// </summary>
    public class TrajectorySimulator2D : MonoBehaviour
    {
        [Tooltip("El ovillo real (de él se copian el radio, la masa, el material y la escala de gravedad)")]
        public Ball ball;

        private Scene simScene;
        private PhysicsScene2D physics;
        private Rigidbody2D ghost;
        private readonly List<Transform> originals = new List<Transform>();
        private readonly List<Transform> copies = new List<Transform>();
        private bool ready;

        private void Start()
        {
            Build();
        }

        private void Build()
        {
            if (ready || ball == null) return;
            simScene = SceneManager.CreateScene("SimulacionTrayectoria_" + GetInstanceID(), new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            physics = simScene.GetPhysicsScene2D();

            // copias de todos los colliders sólidos (no triggers) salvo el ovillo
            foreach (Collider2D c in FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
            {
                if (c.isTrigger || c.attachedRigidbody == ball.rb || c.GetComponentInParent<Ball>() != null) continue;
                GameObject go = new GameObject("Copia_" + c.name);
                go.transform.position = c.transform.position;
                go.transform.rotation = c.transform.rotation;
                go.transform.localScale = c.transform.lossyScale;
                if (!Copy(c, go)) { Destroy(go); continue; }
                SceneManager.MoveGameObjectToScene(go, simScene);
                originals.Add(c.transform);
                copies.Add(go.transform);
            }

            // ovillo fantasma
            GameObject g = new GameObject("OvilloFantasma");
            CircleCollider2D gc = g.AddComponent<CircleCollider2D>();
            gc.radius = ball.col.radius;
            gc.sharedMaterial = ball.col.sharedMaterial != null ? ball.col.sharedMaterial : ball.rb.sharedMaterial;
            g.transform.localScale = ball.transform.lossyScale;
            ghost = g.AddComponent<Rigidbody2D>();
            ghost.mass = ball.rb.mass;
            ghost.gravityScale = ball.rb.gravityScale;
            ghost.linearDamping = ball.rb.linearDamping;
            ghost.angularDamping = ball.rb.angularDamping;
            ghost.sharedMaterial = ball.rb.sharedMaterial;
            ghost.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            SceneManager.MoveGameObjectToScene(g, simScene);
            ready = true;
        }

        private static bool Copy(Collider2D src, GameObject dst)
        {
            Collider2D made = null;
            BoxCollider2D box = src as BoxCollider2D;
            EdgeCollider2D edge = src as EdgeCollider2D;
            CircleCollider2D circle = src as CircleCollider2D;
            if (box != null)
            {
                BoxCollider2D b = dst.AddComponent<BoxCollider2D>();
                b.size = box.size;
                b.offset = box.offset;
                b.edgeRadius = box.edgeRadius;
                made = b;
            }
            else if (edge != null)
            {
                EdgeCollider2D e = dst.AddComponent<EdgeCollider2D>();
                e.points = edge.points;
                e.offset = edge.offset;
                e.edgeRadius = edge.edgeRadius;
                made = e;
            }
            else if (circle != null)
            {
                CircleCollider2D ci = dst.AddComponent<CircleCollider2D>();
                ci.radius = circle.radius;
                ci.offset = circle.offset;
                made = ci;
            }
            if (made == null) return false;
            made.sharedMaterial = src.sharedMaterial;
            return true;
        }

        /// <summary>
        /// Simula <paramref name="count"/> puntos, cada uno <paramref name="stepsPerPoint"/> pasos de física después del anterior.
        /// </summary>
        public void Simulate(Vector2 start, Vector2 v0, int count, int stepsPerPoint, List<Vector2> output)
        {
            output.Clear();
            Build();
            if (!ready) return;
            // las copias siguen a sus originales (la balsa se mueve con las olas)
            for (int i = 0; i < originals.Count; i++)
            {
                if (originals[i] == null) continue;
                copies[i].position = originals[i].position;
                copies[i].rotation = originals[i].rotation;
            }
            ghost.position = start;
            ghost.transform.position = start;
            ghost.linearVelocity = v0;
            ghost.angularVelocity = 0f;
            float dt = Time.fixedDeltaTime;
            for (int i = 0; i < count; i++)
            {
                for (int s = 0; s < stepsPerPoint; s++) physics.Simulate(dt);
                output.Add(ghost.position);
            }
        }

        private void OnDestroy()
        {
            if (ready && simScene.IsValid()) SceneManager.UnloadSceneAsync(simScene);
        }
    }
}
