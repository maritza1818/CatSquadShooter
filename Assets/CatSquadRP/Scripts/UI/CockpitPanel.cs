using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Panel de la cabina junto a la palanca (primera persona), manejado con los PULGARES:
    ///  - Juntar las manos (tecla M o Tab) lo despliega o lo pliega.
    ///  - Con ambas manos relajadas y la palma abajo: pulgar DERECHO arriba = selección a la derecha,
    ///    pulgar IZQUIERDO arriba = a la izquierda (el terapeuta puede invertirlos).
    ///  - AMBOS pulgares arriba = confirmar (opcional: confirmar quedándose quieto en un botón).
    /// Arriba muestra el nivel y la profundidad; en el centro, el mapa lateral; abajo, los botones.
    /// En teclado: Z = pulgar izquierdo, X = pulgar derecho, Z+X o Enter = confirmar.
    /// </summary>
    public class CockpitPanel : MonoBehaviour
    {
        public CockpitRig rig;
        [Tooltip("Pantalla del mapa (el quad con la cámara lateral); el panel se dibuja encima")]
        public Renderer screen;
        public TMP_FontAsset font;
        public Color accent = new Color(1f, 0.72f, 0.2f);
        public Color idle = new Color(0.05f, 0.12f, 0.2f, 0.85f);

        public static event Action<string> AnyPressed;

        private class PanelButton
        {
            public string title;
            public Func<string> state;
            public Func<bool> available;
            public Func<string> press;
            public Image bg;
            public Image dwellFill;
            public TextMeshProUGUI titleText;
            public TextMeshProUGUI stateText;
            public RectTransform rect;
        }

        private readonly List<PanelButton> buttons = new List<PanelButton>();
        private int selected;
        private Canvas canvas;
        private TextMeshProUGUI levelText;
        private TextMeshProUGUI depthText;
        private TextMeshProUGUI flashText;
        private CanvasGroup flashGroup;
        private Image thumbLeft, thumbRight, bothFill;
        private float flashTimer;
        private float dwellTimer;
        private bool dwellArmed;
        private float pressPulse;
        private float pinkyTime;
        private float buttonWidth = 144f;
        private bool pinkyUsed;

        private void Start()
        {
            if (screen == null) { enabled = false; return; }
            BuildButtons();
            BuildCanvas();
            Refresh();
        }

        // ------------------------------------------------------------------ botones

        private void BuildButtons()
        {
            buttons.Add(new PanelButton
            {
                title = "AUTO",
                state = () => GameManager.Instance != null && GameManager.Instance.Settings.driveMode == DriveMode.Automatico ? "AUTOMÁTICO" : "MANUAL",
                available = () => GameManager.Instance == null || GameManager.Instance.Settings.allowChildAutoToggle,
                press = () =>
                {
                    if (RailPilot.Instance == null) return "Sin rieles";
                    RailPilot.Instance.ToggleDriveMode();
                    return GameManager.Instance.Settings.driveMode == DriveMode.Automatico ? "¡MODO AUTOMÁTICO!" : "¡MODO MANUAL!";
                }
            });
            buttons.Add(new PanelButton
            {
                title = "LUCES",
                state = () => Sub != null && Sub.HighBeam ? "ALTAS" : "NORMAL",
                available = () => Sub != null,
                press = () => { Sub.HighBeam = !Sub.HighBeam; return Sub.HighBeam ? "¡LUCES ALTAS!" : "LUCES NORMALES"; }
            });
            buttons.Add(new PanelButton
            {
                title = "PISTA",
                state = () => "GUÍA",
                available = () => GameManager.Instance != null && GameManager.Instance.guide != null,
                press = () => { GameManager.Instance.guide.GiveHint(); return "¡PISTA!"; }
            });
            buttons.Add(new PanelButton
            {
                title = "ZOOM",
                state = () => SideMap != null ? SideMap.ZoomName.ToUpper() : "-",
                available = () => SideMap != null,
                press = () => "MAPA " + SideMap.CycleZoom().ToUpper()
            });
            buttons.Add(new PanelButton
            {
                title = "LANZAR",
                state = () => ProjectileLauncher.Instance != null ? ProjectileLauncher.Instance.StatusLabel : "-",
                available = () => ProjectileLauncher.Instance != null && (ProjectileLauncher.Instance.IsOpen || ProjectileLauncher.Instance.CanOpen),
                press = () => ProjectileLauncher.Instance.Toggle()
            });
            buttons.Add(new PanelButton
            {
                title = "BALSA",
                state = () => "VOLVER",
                available = () => Sub != null && RailNetwork.Instance != null,
                press = GoToRaft
            });
        }

        private static SubmarineController Sub { get { return GameManager.Instance != null ? GameManager.Instance.submarine : null; } }

        private SideMapCamera sideMap;
        private SideMapCamera SideMap
        {
            get
            {
                if (sideMap == null) sideMap = FindAnyObjectByType<SideMapCamera>();
                return sideMap;
            }
        }

        /// <summary>BALSA: el riel lleva al submarino a la balsa de rescate de su barrio (Seek + Arrive).</summary>
        private string GoToRaft()
        {
            SubmarineController sub = Sub;
            GameManager gm = GameManager.Instance;
            if (sub == null || gm == null) return "-";
            if (sub.State != SubState.Libre || !sub.InputEnabled) return "PRIMERO SUÉLTATE";
            if (gm.session != null && gm.session.Phase != SessionPhase.Jugando) return "AHORA NO";
            Vector3 pos = sub.transform.position;
            Dock raft = Dock.FindForZone(WaterZone.FindAt(pos), pos);
            List<Vector3> path = new List<Vector3>();
            if (raft == null || !RailNetwork.Instance.PathTo(pos, raft.SubPoint, path)) return "NO HAY CAMINO";
            sub.StartAutopilot(path, () => sub.Unfreeze());
            return "RUMBO A LA BALSA";
        }

        // ------------------------------------------------------------------ interfaz

        private RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            GameObject go = new GameObject(name);
            go.layer = gameObject.layer;
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private Image Box(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color c)
        {
            RectTransform rt = Rect(parent, name, anchor, pos, size);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        private TextMeshProUGUI Text(Transform parent, string name, string text, float size, Vector2 anchor, Vector2 pos, Vector2 box,
                                     TextAlignmentOptions align, Color color)
        {
            RectTransform rt = Rect(parent, name, anchor, pos, box);
            TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        private void BuildCanvas()
        {
            // Lienzo en el espacio del mundo, pegado a la pantalla (800 x 520 "píxeles" = el tamaño del quad)
            GameObject go = new GameObject("PanelUI");
            go.layer = screen.gameObject.layer;
            go.transform.SetParent(screen.transform.parent, false);
            go.transform.localPosition = screen.transform.localPosition + new Vector3(0f, 0f, -0.004f);
            go.transform.localRotation = screen.transform.localRotation;
            go.transform.localScale = Vector3.one * (screen.transform.localScale.x / 800f);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform root = go.GetComponent<RectTransform>();
            root.sizeDelta = new Vector2(800f, 520f);

            // barra superior: nivel y profundidad
            Box(root, "BarraSuperior", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(800f, 58f), new Color(0.02f, 0.06f, 0.1f, 0.8f));
            levelText = Text(root, "Nivel", "", 26, new Vector2(0f, 1f), new Vector2(14f, -10f), new Vector2(570f, 40f), TextAlignmentOptions.Left, Color.white);
            depthText = Text(root, "Profundidad", "", 26, new Vector2(1f, 1f), new Vector2(-14f, -10f), new Vector2(200f, 40f), TextAlignmentOptions.Right, new Color(0.6f, 0.9f, 1f));

            // barra inferior: botones
            Box(root, "BarraInferior", new Vector2(0.5f, 0f), Vector2.zero, new Vector2(800f, 150f), new Color(0.02f, 0.06f, 0.1f, 0.8f));
            float gap = buttons.Count > 5 ? 8f : 12f;
            float w = Mathf.Min(144f, (776f - gap * (buttons.Count - 1)) / buttons.Count);
            buttonWidth = w;
            float titleSize = w < 135f ? 28f : 34f;
            float x0 = -(buttons.Count * w + (buttons.Count - 1) * gap) * 0.5f + w * 0.5f;
            for (int i = 0; i < buttons.Count; i++)
            {
                PanelButton b = buttons[i];
                b.bg = Box(root, "Boton_" + b.title, new Vector2(0.5f, 0f), new Vector2(x0 + i * (w + gap), 36f), new Vector2(w, 100f), idle);
                b.rect = b.bg.rectTransform;
                b.rect.pivot = new Vector2(0.5f, 0f);
                b.rect.anchoredPosition = new Vector2(x0 + i * (w + gap), 36f);
                b.dwellFill = Box(b.rect, "Espera", new Vector2(0f, 0f), Vector2.zero, new Vector2(0f, 8f), new Color(1f, 1f, 1f, 0.8f));
                b.titleText = Text(b.rect, "Titulo", b.title, titleSize, new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(w, 42f), TextAlignmentOptions.Center, Color.white);
                b.stateText = Text(b.rect, "Estado", "", w < 135f ? 19f : 22f, new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(w, 30f), TextAlignmentOptions.Center, new Color(0.75f, 0.9f, 1f));
            }

            // pulgares: se encienden al levantarlos; la barra del centro se llena con ambos arriba
            thumbLeft = Box(root, "PulgarIzq", new Vector2(0f, 0f), new Vector2(16f, 8f), new Vector2(120f, 22f), new Color(1f, 1f, 1f, 0.15f));
            Text(thumbLeft.rectTransform, "T", "< IZQ", 18, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 22f), TextAlignmentOptions.Center, Color.white);
            thumbRight = Box(root, "PulgarDer", new Vector2(1f, 0f), new Vector2(-16f, 8f), new Vector2(120f, 22f), new Color(1f, 1f, 1f, 0.15f));
            Text(thumbRight.rectTransform, "T", "DER >", 18, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 22f), TextAlignmentOptions.Center, Color.white);
            Box(root, "ConfirmarFondo", new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(360f, 12f), new Color(1f, 1f, 1f, 0.12f));
            bothFill = Box(root, "Confirmar", new Vector2(0.5f, 0f), new Vector2(-180f, 12f), new Vector2(0f, 12f), accent);
            bothFill.rectTransform.pivot = new Vector2(0f, 0f);
            bothFill.rectTransform.anchoredPosition = new Vector2(-180f, 12f);

            // aviso grande al presionar
            RectTransform flash = Rect(root, "Aviso", new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(760f, 90f));
            flashGroup = flash.gameObject.AddComponent<CanvasGroup>();
            Image fb = flash.gameObject.AddComponent<Image>();
            fb.color = new Color(0.02f, 0.06f, 0.1f, 0.75f);
            fb.raycastTarget = false;
            flashText = Text(flash, "Texto", "", 48, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 90f), TextAlignmentOptions.Center, accent);
            flashGroup.alpha = 0f;
        }

        // ------------------------------------------------------------------ bucle

        private void Update()
        {
            float dt = Time.deltaTime;
            bool visible = screen != null && screen.enabled;
            if (canvas != null && canvas.enabled != visible) canvas.enabled = visible;

            BimanualFrame f = BimanualInput.Instance != null ? BimanualInput.Instance.Current : new BimanualFrame();
            bool modal = GameHUD.Instance != null && GameHUD.Instance.ModalOpen;
            bool usable = visible && rig != null && rig.MapOpen && !modal;
            GameSettings settings = GameManager.Instance != null ? GameManager.Instance.Settings : null;

            if (usable && f.panelNavigate != 0)
            {
                selected = (selected + f.panelNavigate + buttons.Count) % buttons.Count;
                dwellTimer = 0f;
                dwellArmed = true;
                AudioFx.Play(AudioFx.Instance != null ? AudioFx.Instance.bubblePop : null, transform.position, 0.5f, 1.3f);
                if (GameManager.Instance != null && GameManager.Instance.metrics != null) GameManager.Instance.metrics.RecordPanel(false);
            }

            bool confirm = usable && f.panelConfirm;
            if (usable && settings != null && settings.panelDwellConfirm && dwellArmed && f.panelHandsReady && !f.thumbLeft && !f.thumbRight)
            {
                dwellTimer += dt;
                if (dwellTimer >= Mathf.Max(0.5f, settings.panelDwellSeconds)) { confirm = true; dwellArmed = false; }
            }
            if (confirm) Press();

            // Atajo opcional: meñique arriba (mano del panel) = pista de la guía
            if (settings != null && settings.pinkyHint && f.pinkyPanel && !modal)
            {
                pinkyTime += dt;
                if (!pinkyUsed && pinkyTime >= 0.6f && GameManager.Instance.guide != null)
                {
                    pinkyUsed = true;
                    GameManager.Instance.guide.GiveHint();
                    flashText.text = "¡PISTA!";
                    flashTimer = 1.2f;
                }
            }
            else { pinkyTime = 0f; pinkyUsed = false; }

            if (flashTimer > 0f) flashTimer -= dt;
            if (flashGroup != null) flashGroup.alpha = Mathf.Clamp01(flashTimer * 2f);
            pressPulse = Mathf.MoveTowards(pressPulse, 0f, dt * 4f);

            if (visible) Refresh(f, settings);
        }

        private void Press()
        {
            PanelButton b = buttons[selected];
            dwellArmed = false;
            dwellTimer = 0f;
            string msg;
            if (!b.available()) msg = "NO DISPONIBLE";
            else
            {
                msg = b.press();
                pressPulse = 1f;
                AudioFx.Play(AudioFx.Instance != null ? AudioFx.Instance.chime : null, transform.position, 0.6f, 1.1f);
                if (AnyPressed != null) AnyPressed(b.title);
            }
            if (GameManager.Instance != null && GameManager.Instance.metrics != null) GameManager.Instance.metrics.RecordPanel(true);
            flashText.text = msg;
            flashTimer = 1.4f;
        }

        private void Refresh() { Refresh(new BimanualFrame(), GameManager.Instance != null ? GameManager.Instance.Settings : null); }

        private void Refresh(BimanualFrame f, GameSettings settings)
        {
            if (canvas == null) return;
            SubmarineController sub = Sub;
            if (sub != null && levelText != null)
            {
                Vector3 p = sub.transform.position;
                CityDistrict d = CityDistrict.At(p);
                WaterZone z = WaterZone.FindAt(p);
                if (d != null) levelText.text = "NIVEL " + d.levelNumber + " · " + d.displayName + (d.levelNumber > 0 && d.Freed ? " (LIBERADO)" : "");
                else levelText.text = z != null ? z.displayName : "";
                float surface = WaterZone.SurfaceAt(p);
                float max = GameManager.Instance != null ? GameManager.Instance.MaxDepth : 0f;
                if (!float.IsInfinity(surface))
                {
                    float depth = Mathf.Max(0f, surface - p.y);
                    depthText.text = depth.ToString("F0") + " m / " + max.ToString("F0") + " m";
                    depthText.color = depth > max - 3f ? new Color(1f, 0.45f, 0.35f) : new Color(0.6f, 0.9f, 1f);
                }
            }

            for (int i = 0; i < buttons.Count; i++)
            {
                PanelButton b = buttons[i];
                bool sel = i == selected;
                bool ok = b.available();
                b.bg.color = sel ? accent : (ok ? idle : new Color(0.15f, 0.15f, 0.18f, 0.7f));
                b.titleText.color = sel ? new Color(0.08f, 0.08f, 0.12f) : (ok ? Color.white : new Color(1f, 1f, 1f, 0.4f));
                b.stateText.color = sel ? new Color(0.15f, 0.12f, 0.1f) : new Color(0.75f, 0.9f, 1f, ok ? 1f : 0.4f);
                b.stateText.text = b.state();
                float scale = sel ? 1.08f + 0.12f * pressPulse : 1f;
                b.rect.localScale = new Vector3(scale, scale, 1f);
                float dwell = sel && dwellArmed && settings != null && settings.panelDwellConfirm
                    ? Mathf.Clamp01(dwellTimer / Mathf.Max(0.5f, settings.panelDwellSeconds)) : 0f;
                b.dwellFill.rectTransform.sizeDelta = new Vector2(buttonWidth * dwell, 8f);
            }

            if (thumbLeft != null)
            {
                thumbLeft.color = f.thumbLeft ? accent : new Color(1f, 1f, 1f, f.panelHandsReady ? 0.25f : 0.08f);
                thumbRight.color = f.thumbRight ? accent : new Color(1f, 1f, 1f, f.panelHandsReady ? 0.25f : 0.08f);
                bothFill.rectTransform.sizeDelta = new Vector2(360f * f.bothThumbs01, 12f);
            }
        }
    }
}
