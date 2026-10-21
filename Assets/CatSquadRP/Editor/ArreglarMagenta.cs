using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CatSquad.EditorTools
{
    /// <summary>
    /// Arregla los materiales en magenta: activa URP en Graphics y Quality (lo crea si hace falta) y pone
    /// en todos los materiales del proyecto un shader que funcione con el pipeline activo.
    /// Menú: CatSquad → Arreglar materiales magenta (URP).
    /// No depende del paquete URP al compilar (usa reflexión), así que compila aunque URP no esté instalado.
    /// </summary>
    public static class ArreglarMagenta
    {
        const string UrpAssetType = "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime";
        const string UrpRendererType = "UnityEngine.Rendering.Universal.UniversalRendererData, Unity.RenderPipelines.Universal.Runtime";

        [MenuItem("CatSquad/Arreglar materiales magenta (URP)", priority = 30)]
        public static void Fix()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Arreglar magenta", "Primero detén el Play y vuelve a intentarlo.", "OK");
                return;
            }

            Type urpType = Type.GetType(UrpAssetType);
            string report = "";
            RenderPipelineAsset urp = null;

            if (urpType != null)
            {
                // 1. Buscar un asset de URP que ya exista en el proyecto
                foreach (string guid in AssetDatabase.FindAssets("t:RenderPipelineAsset"))
                {
                    var a = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                    if (a != null && urpType.IsInstanceOfType(a)) { urp = a; break; }
                }
                // 2. Si no hay ninguno, crearlo (con su Universal Renderer)
                if (urp == null)
                {
                    if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");
                    Type rendererType = Type.GetType(UrpRendererType);
                    ScriptableObject renderer = rendererType != null ? ScriptableObject.CreateInstance(rendererType) : null;
                    if (renderer != null) AssetDatabase.CreateAsset(renderer, "Assets/Settings/CatSquad_Renderer.asset");
                    var create = urpType.GetMethods().FirstOrDefault(mi => mi.Name == "Create" && mi.IsStatic);
                    urp = create != null ? create.Invoke(null, new object[] { renderer }) as RenderPipelineAsset : null;
                    if (urp != null)
                    {
                        AssetDatabase.CreateAsset(urp, "Assets/Settings/CatSquad_URP.asset");
                        report += "• Creé Assets/Settings/CatSquad_URP.asset\n";
                    }
                }
                // 3. Activarlo en Graphics y en TODOS los niveles de Quality
                if (urp != null)
                {
                    GraphicsSettings.defaultRenderPipeline = urp;
                    int current = QualitySettings.GetQualityLevel();
                    for (int i = 0; i < QualitySettings.names.Length; i++)
                    {
                        QualitySettings.SetQualityLevel(i, false);
                        QualitySettings.renderPipeline = urp;
                    }
                    QualitySettings.SetQualityLevel(current, true);
                    report += "• URP activo en Graphics y en los " + QualitySettings.names.Length + " niveles de Quality (" + urp.name + ")\n";
                }
            }
            else
            {
                report += "• El paquete URP no está instalado: dejo el pipeline normal (Built-in)\n";
            }

            // 4. Shaders de los materiales según el pipeline activo
            bool urpActive = GraphicsSettings.currentRenderPipeline != null && urpType != null && urpType.IsInstanceOfType(GraphicsSettings.currentRenderPipeline);
            Shader lit = Shader.Find(urpActive ? "Universal Render Pipeline/Lit" : "Standard");
            Shader unlit = Shader.Find(urpActive ? "Universal Render Pipeline/Unlit" : "Unlit/Color");
            Shader particles = Shader.Find(urpActive ? "Universal Render Pipeline/Particles/Unlit" : "Sprites/Default");
            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".mat")) continue;   // los materiales dentro de un FBX no se pueden editar
                Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || m.shader == null) continue;
                string sh = m.shader.name;
                bool broken = sh.StartsWith("Hidden/InternalErrorShader") || !m.shader.isSupported;
                bool wrongPipeline = urpActive ? (sh == "Standard" || sh == "Unlit/Color" || sh == "Unlit/Texture")
                                               : sh.StartsWith("Universal Render Pipeline/");
                if (!broken && !wrongPipeline) continue;

                Color color = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : (m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white);
                Texture tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : (m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null);
                Vector2 tiling = m.HasProperty("_BaseMap") ? m.GetTextureScale("_BaseMap") : (m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one);
                bool transparent = color.a < 0.99f || sh.Contains("Particles") || sh.Contains("Sprites");
                Shader target = sh.Contains("Unlit") || sh.Contains("Particles") || sh.Contains("Sprites") ? (transparent ? particles : unlit) : lit;
                if (target == null) continue;
                m.shader = target;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
                if (m.HasProperty("_Color")) m.SetColor("_Color", color);
                if (tex != null)
                {
                    if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", tiling); }
                    if (m.HasProperty("_MainTex")) { m.SetTexture("_MainTex", tex); m.SetTextureScale("_MainTex", tiling); }
                }
                EditorUtility.SetDirty(m);
                changed++;
            }
            AssetDatabase.SaveAssets();
            report += "• Materiales corregidos: " + changed + " (pipeline " + (urpActive ? "URP" : "Built-in") + ")\n";
            Debug.Log("[CatSquad] Arreglar magenta:\n" + report);
            EditorUtility.DisplayDialog("Arreglar magenta", report + "\nDale Play de nuevo. Si algo sigue magenta, vuelve a construir la escena desde el menú CatSquad.", "OK");
        }
    }
}
