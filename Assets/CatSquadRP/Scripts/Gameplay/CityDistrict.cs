using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Barrio de la ciudad inundada (primera persona). Cada barrio está bajo el nivel del mar, detrás de
    /// un dique. Su bomba lo DRENA: el agua baja, los edificios emergen y el barrio queda "liberado":
    /// se encienden las ventanas y los faroles. El número de nivel (0 = mar, 1, 2, 3...) se muestra en el HUD.
    /// </summary>
    public class CityDistrict : MonoBehaviour
    {
        private static readonly List<CityDistrict> all = new List<CityDistrict>();
        public static IReadOnlyList<CityDistrict> All { get { return all; } }
        public static event Action<CityDistrict> AnyFreed;

        public int levelNumber;
        public string displayName = "Barrio";
        public WaterZone zone;
        [Tooltip("Bomba de drenaje del barrio (el mar abierto no tiene)")]
        public PumpStation pump;

        [Header("Al liberarse")]
        public Renderer[] windows = new Renderer[0];
        public Light[] lamps = new Light[0];
        public Color windowGlow = new Color(1.6f, 1.25f, 0.6f);

        public bool Freed { get; private set; }

        private MaterialPropertyBlock block;

        private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        private void OnDisable() { all.Remove(this); }

        private void Start()
        {
            Freed = pump == null || pump.Complete;
            ApplyLights(Freed ? 1f : 0f);
        }

        private float glow;

        private void Update()
        {
            if (!Freed && pump != null && pump.Complete)
            {
                Freed = true;
                if (AnyFreed != null) AnyFreed(this);
                if (GameHUD.Instance != null) GameHUD.Instance.Toast("¡" + displayName + " se liberó del agua!", 4f);
            }
            float target = Freed ? 1f : 0f;
            if (!Mathf.Approximately(glow, target))
            {
                glow = Mathf.MoveTowards(glow, target, Time.deltaTime * 0.5f);
                ApplyLights(glow);
            }
        }

        private void ApplyLights(float k)
        {
            glow = k;
            if (block == null) block = new MaterialPropertyBlock();
            block.SetColor("_EmissionColor", windowGlow * k);
            foreach (Renderer r in windows) if (r != null) r.SetPropertyBlock(block);
            foreach (Light l in lamps) if (l != null) l.enabled = k > 0.05f;
        }

        /// <summary>Barrio (o mar) donde está el punto, según su zona de agua.</summary>
        public static CityDistrict At(Vector3 p)
        {
            WaterZone z = WaterZone.FindAt(p);
            if (z == null) return null;
            foreach (CityDistrict d in all) if (d != null && d.zone == z) return d;
            return null;
        }

        public static CityDistrict ForZone(WaterZone z)
        {
            foreach (CityDistrict d in all) if (d != null && d.zone == z) return d;
            return null;
        }
    }
}
