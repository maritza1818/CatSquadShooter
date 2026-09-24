using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Genera un cubo cuyas UV dependen de su tamaño en el mundo, para que la textura
    /// de las paredes no se estire al fusionar muchas celdas en un solo bloque.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    public class WorldUVBox : MonoBehaviour
    {
        public float tileSize = 2f;
        private Vector3 builtScale;
        private Mesh mesh;

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnDisable()
        {
            if (mesh != null)
            {
                if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
                mesh = null;
            }
        }

        private void Update()
        {
            if (!Application.isPlaying && transform.lossyScale != builtScale) Rebuild();
        }

        public void Rebuild()
        {
            Vector3 s = transform.lossyScale;
            builtScale = s;
            float k = 1f / Mathf.Max(0.01f, tileSize);

            if (mesh == null)
            {
                mesh = new Mesh { name = "WorldUVBox" };
                mesh.hideFlags = HideFlags.DontSave;
            }
            mesh.Clear();

            Vector3[] n = { Vector3.forward, Vector3.back, Vector3.up, Vector3.down, Vector3.right, Vector3.left };
            Vector3[] verts = new Vector3[24];
            Vector3[] normals = new Vector3[24];
            Vector2[] uvs = new Vector2[24];
            int[] tris = new int[36];

            for (int f = 0; f < 6; f++)
            {
                Vector3 normal = n[f];
                Vector3 u = Mathf.Abs(normal.y) > 0.5f ? Vector3.right : Vector3.Cross(Vector3.up, normal).normalized;
                if (u.sqrMagnitude < 0.5f) u = Vector3.right;
                Vector3 v = Vector3.Cross(normal, u);

                Vector3 c = normal * 0.5f;
                Vector3[] corners =
                {
                    c - u * 0.5f - v * 0.5f,
                    c + u * 0.5f - v * 0.5f,
                    c + u * 0.5f + v * 0.5f,
                    c - u * 0.5f + v * 0.5f
                };
                for (int i = 0; i < 4; i++)
                {
                    int idx = f * 4 + i;
                    verts[idx] = corners[i];
                    normals[idx] = normal;
                    Vector3 world = Vector3.Scale(corners[i], s) + transform.position;
                    // UV en coordenadas de mundo para que bloques vecinos casen
                    float wu = Vector3.Dot(world, u);
                    float wv = Vector3.Dot(world, v);
                    uvs[idx] = new Vector2(wu * k, wv * k);
                }

                int t = f * 6;
                int b = f * 4;
                // Unity: caras frontales en sentido horario vistas desde fuera
                tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
                tris[t + 3] = b; tris[t + 4] = b + 2; tris[t + 5] = b + 3;
            }

            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }
    }
}
