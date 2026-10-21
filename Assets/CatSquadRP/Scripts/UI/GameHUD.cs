using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Interfaz del juego construida por código (no requiere armar nada a mano):
    /// oxígeno, chatarra, gatitos, diálogo de la guía, avisos, indicaciones contextuales,
    /// resumen de sesión y taller de mejoras.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        public static GameHUD Instance { get; private set; }

        [Header("Apariencia (opcional)")]
        public Sprite roundedSprite;
        public Sprite guidePortrait;
        public TMP_FontAsset font;
        public Color panelColor = new Color(0.03f, 0.09f, 0.16f, 0.82f);
        public Color accent = new Color(1f, 0.72f, 0.2f);
        public Color oxygenColor = new Color(0.35f, 0.85f, 1f);

        private Canvas canvas;
        private RectTransform oxygenFill;
        private Image oxygenFillImage;
        private TextMeshProUGUI oxygenText;
        private TextMeshProUGUI statusText;
        private TextMeshProUGUI materialsText;
        private TextMeshProUGUI kittensText;
        private TextMeshProUGUI upgradesText;
        private GameObject dialogRoot;
        private TextMeshProUGUI dialogName;
        private TextMeshProUGUI dialogText;
        private TextMeshProUGUI toastText;
        private CanvasGroup toastGroup;
        private TextMeshProUGUI promptText;
        private GameObject helpRoot;
        private TextMeshProUGUI helpText;
        private GameObject modalRoot;
        private RectTransform modalContent;
        private float toastTimer;
        private float dialogTypeTimer;
        private string dialogFull = "";
        private int lastMaterials = -1;
        private float materialsPulse;

        private void Awake()
        {
            Instance = this;
            Build();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ================================================================== construcción

        private void Build()
        {
            GameObject canvasGo = new GameObject("HUD_Canvas");
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            if (FindAnyObjectByType<EventSystem>() == null)
            {
                GameObject es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            RectTransform root = canvasGo.GetComponent<RectTransform>();

            // ---- Oxígeno (arriba a la izquierda)
            RectTransform oxy = Panel(root, "Oxigeno", new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(520f, 110f));
            Label(oxy, "Titulo", "OXÍGENO", 26, new Vector2(20f, -12f), new Vector2(300f, 34f), TextAlignmentOptions.TopLeft, accent);
            oxygenText = Label(oxy, "Tiempo", "15:00", 26, new Vector2(-20f, -12f), new Vector2(200f, 34f), TextAlignmentOptions.TopRight, Color.white, new Vector2(1f, 1f));
            RectTransform barBg = Rect(oxy, "Barra", new Vector2(0f, 0f), new Vector2(20f, 18f), new Vector2(480f, 34f));
            Image bgImg = barBg.gameObject.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.5f);
            ApplySprite(bgImg);
            oxygenFill = Rect(barBg, "Relleno", new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(474f, 28f));
            oxygenFillImage = oxygenFill.gameObject.AddComponent<Image>();
            oxygenFillImage.color = oxygenColor;
            ApplySprite(oxygenFillImage);

            statusText = Label(root, "Estado", "", 20, new Vector2(34f, -148f), new Vector2(700f, 30f), TextAlignmentOptions.TopLeft, new Color(1f, 1f, 1f, 0.7f), new Vector2(0f, 1f));

            // ---- Recursos (arriba a la derecha)
            RectTransform res = Panel(root, "Recursos", new Vector2(1f, 1f), new Vector2(-30f, -30f), new Vector2(430f, 150f));
            materialsText = Label(res, "Chatarra", "Chatarra: 0", 34, new Vector2(22f, -14f), new Vector2(390f, 44f), TextAlignmentOptions.TopLeft, accent);
            kittensText = Label(res, "Gatitos", "Gatitos a salvo: 0", 26, new Vector2(22f, -62f), new Vector2(390f, 36f), TextAlignmentOptions.TopLeft, Color.white);
            upgradesText = Label(res, "Mejoras", "", 20, new Vector2(22f, -102f), new Vector2(390f, 32f), TextAlignmentOptions.TopLeft, new Color(0.8f, 0.9f, 1f));

            // ---- Aviso central
            toastText = Label(root, "Aviso", "", 40, new Vector2(0f, -200f), new Vector2(1200f, 60f), TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f));
            toastText.fontStyle = FontStyles.Bold;
            toastText.outlineWidth = 0.2f;
            toastText.outlineColor = new Color32(0, 20, 40, 255);
            toastGroup = toastText.gameObject.AddComponent<CanvasGroup>();
            toastGroup.alpha = 0f;

            // ---- Diálogo de la guía (abajo al centro)
            RectTransform dlg = Panel(root, "Dialogo", new Vector2(0.5f, 0f), new Vector2(0f, 30f), new Vector2(1280f, 190f));
            dialogRoot = dlg.gameObject;
            float textLeft = 30f;
            if (guidePortrait != null)
            {
                RectTransform pr = Rect(dlg, "Retrato", new Vector2(0f, 0.5f), new Vector2(20f, 0f), new Vector2(160f, 160f));
                Image pi = pr.gameObject.AddComponent<Image>();
                pi.sprite = guidePortrait;
                pi.preserveAspect = true;
                textLeft = 200f;
            }
            dialogName = Label(dlg, "Nombre", "Capitana Pelusa", 28, new Vector2(textLeft, -14f), new Vector2(900f, 36f), TextAlignmentOptions.TopLeft, accent);
            dialogText = Label(dlg, "Texto", "", 32, new Vector2(textLeft, -54f), new Vector2(1280f - textLeft - 30f, 125f), TextAlignmentOptions.TopLeft, Color.white);
            dialogText.textWrappingMode = TextWrappingModes.Normal;
            dialogRoot.SetActive(false);

            // ---- Indicación contextual (encima del diálogo)
            promptText = Label(root, "Indicacion", "", 28, new Vector2(0f, 240f), new Vector2(1400f, 44f), TextAlignmentOptions.Center, new Color(1f, 0.95f, 0.6f), new Vector2(0.5f, 0f));
            promptText.outlineWidth = 0.18f;
            promptText.outlineColor = new Color32(0, 10, 20, 255);

            // ---- Ayuda de controles (abajo a la izquierda)
            RectTransform help = Panel(root, "Ayuda", new Vector2(0f, 0.5f), new Vector2(30f, -40f), new Vector2(430f, 250f));
            helpRoot = help.gameObject;
            helpText = Label(help, "Texto", "", 19, new Vector2(18f, -14f), new Vector2(400f, 230f), TextAlignmentOptions.TopLeft, new Color(0.9f, 0.95f, 1f));
            helpText.textWrappingMode = TextWrappingModes.Normal;

            // ---- Ventana modal (resumen / taller)
            RectTransform modal = Rect(root, "Modal", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920f, 1080f));
            Image dim = modal.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0.02f, 0.05f, 0.6f);
            modalContent = Panel(modal, "Contenido", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 760f));
            modalRoot = modal.gameObject;
            modalRoot.SetActive(false);
        }

        private void ApplySprite(Image img)
        {
            if (roundedSprite == null) return;
            img.sprite = roundedSprite;
            img.type = Image.Type.Sliced;
        }

        private RectTransform Rect(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private RectTransform Panel(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            RectTransform rt = Rect(parent, name, anchor, pos, size);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = panelColor;
            ApplySprite(img);
            return rt;
        }

        private TextMeshProUGUI Label(RectTransform parent, string name, string text, float size, Vector2 pos, Vector2 box,
                                      TextAlignmentOptions align, Color color, Vector2? anchor = null)
        {
            Vector2 a = anchor ?? new Vector2(0f, 1f);
            RectTransform rt = Rect(parent, name, a, pos, box);
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

        private Button MakeButton(RectTransform parent, string text, Vector2 anchor, Vector2 pos, Vector2 size, Action onClick, bool enabled = true)
        {
            RectTransform rt = Rect(parent, "Boton_" + text, anchor, pos, size);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = enabled ? accent : new Color(0.4f, 0.4f, 0.45f);
            ApplySprite(img);
            Button b = rt.gameObject.AddComponent<Button>();
            b.interactable = enabled;
            b.onClick.AddListener(() => { if (onClick != null) onClick(); });
            TextMeshProUGUI t = Label(rt, "Texto", text, 30, Vector2.zero, size, TextAlignmentOptions.Center, new Color(0.08f, 0.08f, 0.12f), new Vector2(0.5f, 0.5f));
            t.fontStyle = FontStyles.Bold;
            return b;
        }

        // ================================================================== API pública

        public void ShowDialog(string speaker, string text)
        {
            if (dialogRoot == null) return;
            dialogRoot.SetActive(true);
            dialogName.text = speaker;
            dialogFull = text ?? "";
            dialogTypeTimer = 0f;
            dialogText.text = "";
        }

        public void HideDialog()
        {
            if (dialogRoot != null) dialogRoot.SetActive(false);
        }

        public void Toast(string text, float seconds = 3f)
        {
            if (toastText == null) return;
            toastText.text = text;
            toastTimer = seconds;
        }

        public void ShowSummary(SessionReport r)
        {
            ClearModal();
            modalRoot.SetActive(true);
            GameManager gm = GameManager.Instance;
            CultureInfo ci = CultureInfo.InvariantCulture;

            Label(modalContent, "Titulo", "¡Sesión completada!", 54, new Vector2(0f, -40f), new Vector2(1000f, 70f), TextAlignmentOptions.Center, accent, new Vector2(0.5f, 1f));

            string body;
            if (r != null)
            {
                body =
                    "Chatarra recolectada: <b>" + r.materialsCollected + "</b>\n" +
                    "Cajas abiertas: <b>" + r.cratesOpened + "</b>     Combustible vertido: <b>" + r.fuelPoured.ToString("F1", ci) + "</b>\n" +
                    "Gatitos liberados: <b>" + r.kittensFreed + "</b>     Gatitos a salvo hoy: <b>" + r.kittensRescued + "</b>\n" +
                    (r.secretsFound > 0 ? "¡Cascabeles secretos encontrados: <b>" + r.secretsFound + "</b>!\n" : "") +
                    (r.launches > 0 ? "Latas, lastres y boyas en el blanco: <b>" + r.launchHits + "</b>\n" : "") + "\n" +
                    "<size=26><color=#9fd8ff>Para el terapeuta</color>\n" +
                    "Repeticiones (agarres): " + r.repetitions + "  ·  Intentos de alcance: " + r.attempts + "\n" +
                    "Alcance medio / máx. (brazo " + r.manipSide + "): " + (r.reachMeanManip * 100f).ToString("F0", ci) + "% / " + (r.reachMaxManip * 100f).ToString("F0", ci) + "%\n" +
                    "Tiempo medio por repetición: " + r.meanRepSeconds.ToString("F1", ci) + " s  ·  Picos de velocidad: " + r.meanVelocityPeaks.ToString("F1", ci) + "\n" +
                    "Palanca sostenida (panel " + r.panelSide + "): " + r.leverHoldSeconds.ToString("F0", ci) + " s (máx. " + r.longestLeverHold.ToString("F1", ci) + " s)\n" +
                    "Intentos con compensación de tronco: " + (r.compensationRatio * 100f).ToString("F0", ci) + "%  ·  Uso brazo/panel: " + (r.manipUsageRatio * 100f).ToString("F0", ci) + "% / " + ((1f - r.manipUsageRatio) * 100f).ToString("F0", ci) + "%" +
                    (GameManager.FirstPerson
                        ? "\nConducción " + r.driveMode + ": palanca " + r.manualDriveSeconds.ToString("F0", ci) + " s  ·  riel " + r.railDriveSeconds.ToString("F0", ci) + " s  ·  ayudas del riel: " + r.assistActivations
                          + "\nPanel con pulgares: " + r.panelNavigations + " movimientos  ·  " + r.panelConfirmations + " confirmaciones"
                          + (r.launches > 0 ? "\nLanzador: " + r.launchHits + " aciertos de " + r.launches + " tiros  ·  tirón medio "
                             + (r.launchMeanPower * 100f).ToString("F0", ci) + "%  ·  asistidos: " + r.launchAssisted : "")
                        : "") + "</size>";
            }
            else body = "Sin datos de métricas.";

            TextMeshProUGUI t = Label(modalContent, "Cuerpo", body, 32, new Vector2(60f, -130f), new Vector2(980f, 520f), TextAlignmentOptions.TopLeft, Color.white);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.richText = true;

            MakeButton(modalContent, "Ir al taller", new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(420f, 80f), ShowWorkshop);
            if (gm != null && gm.metrics != null && !string.IsNullOrEmpty(gm.metrics.LastExportFolder))
            {
                Label(modalContent, "Ruta", "Métricas guardadas en: " + gm.metrics.LastExportFolder, 18, new Vector2(0f, 130f),
                      new Vector2(1060f, 30f), TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.5f), new Vector2(0.5f, 0f));
            }
        }

        public void ShowWorkshop()
        {
            ClearModal();
            modalRoot.SetActive(true);
            GameManager gm = GameManager.Instance;
            Label(modalContent, "Titulo", "Taller del submarino", 52, new Vector2(0f, -36f), new Vector2(1000f, 70f), TextAlignmentOptions.Center, accent, new Vector2(0.5f, 1f));
            Label(modalContent, "Chatarra", "Chatarra disponible: " + (gm != null ? gm.Materials : 0), 34, new Vector2(0f, -110f),
                  new Vector2(1000f, 50f), TextAlignmentOptions.Center, Color.white, new Vector2(0.5f, 1f));

            WorkshopRow(UpgradeKind.Autonomia, "Motor (autonomía)", "Más velocidad y la bomba rinde más con cada bidón.", 0);
            WorkshopRow(UpgradeKind.Iluminacion, "Foco (iluminación)", "Ilumina más lejos en las zonas oscuras (y atrae más peces).", 1);
            WorkshopRow(UpgradeKind.Profundidad, "Casco (profundidad)", GameManager.FirstPerson ? "Aguanta más metros bajo la superficie: llegas a las calles antes de drenar." : "Permite bajar más: la fosa y sus gatitos varados.", 2);

            MakeButton(modalContent, "Nueva sesión", new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(420f, 80f), () =>
            {
                if (gm != null && gm.session != null) gm.session.RestartScene();
            });
        }

        private void WorkshopRow(UpgradeKind kind, string title, string description, int index)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;
            int level = gm.GetUpgrade(kind);
            int max = gm.MaxLevel(kind);
            int cost = gm.NextCost(kind);
            float y = -190f - index * 150f;

            RectTransform row = Rect(modalContent, "Fila_" + kind, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(1000f, 130f));
            Image bg = row.gameObject.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0.06f);
            ApplySprite(bg);
            Label(row, "Nombre", title + "   <color=#ffcf5a>Nivel " + level + "/" + max + "</color>",
                  32, new Vector2(24f, -14f), new Vector2(640f, 44f), TextAlignmentOptions.TopLeft, Color.white).richText = true;
            TextMeshProUGUI d = Label(row, "Desc", description, 22, new Vector2(24f, -62f), new Vector2(640f, 60f), TextAlignmentOptions.TopLeft, new Color(0.85f, 0.9f, 1f));
            d.textWrappingMode = TextWrappingModes.Normal;

            bool canBuy = cost >= 0 && gm.Materials >= cost;
            string label = cost < 0 ? "Máximo" : "Mejorar (" + cost + ")";
            MakeButton(row, label, new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(280f, 76f), () =>
            {
                if (gm.TryBuyUpgrade(kind))
                {
                    AudioFx.Play(AudioFx.Instance != null ? AudioFx.Instance.chime : null, Vector3.zero);
                    if (kind == UpgradeKind.Profundidad && gm.guide != null)
                    {
                        gm.guide.Say("¡Casco reforzado! En la próxima inmersión podremos bajar más profundo.");
                    }
                }
                ShowWorkshop();
            }, canBuy);
        }

        private void ClearModal()
        {
            if (modalContent == null) return;
            for (int i = modalContent.childCount - 1; i >= 0; i--) Destroy(modalContent.GetChild(i).gameObject);
        }

        public bool ModalOpen { get { return modalRoot != null && modalRoot.activeSelf; } }

        // ================================================================== actualización

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            GameManager gm = GameManager.Instance;
            SessionManager session = gm != null ? gm.session : null;

            // Oxígeno
            float oxy = session != null ? session.Oxygen01 : 1f;
            if (oxygenFill != null) oxygenFill.sizeDelta = new Vector2(474f * oxy, 28f);
            if (oxygenFillImage != null) oxygenFillImage.color = oxy > 0.2f ? oxygenColor : Color.Lerp(new Color(1f, 0.5f, 0.2f), oxygenColor, Mathf.PingPong(Time.time, 1f));
            if (oxygenText != null && session != null)
            {
                int secs = Mathf.CeilToInt(session.OxygenLeft);
                oxygenText.text = (secs / 60).ToString("00") + ":" + (secs % 60).ToString("00");
            }

            // Recursos
            if (gm != null)
            {
                if (gm.Materials != lastMaterials)
                {
                    if (lastMaterials >= 0 && gm.Materials > lastMaterials) materialsPulse = 1f;
                    lastMaterials = gm.Materials;
                    materialsText.text = "Chatarra: " + gm.Materials;
                }
                materialsPulse = Mathf.MoveTowards(materialsPulse, 0f, dt * 3f);
                materialsText.transform.localScale = Vector3.one * (1f + materialsPulse * 0.18f);
                kittensText.text = "Gatitos a salvo: " + gm.KittensRescued;
                upgradesText.text = "Motor " + gm.GetUpgrade(UpgradeKind.Autonomia) + "  ·  Foco " + gm.GetUpgrade(UpgradeKind.Iluminacion) + "  ·  Casco " + gm.GetUpgrade(UpgradeKind.Profundidad);
            }

            if (statusText != null && BimanualInput.Instance != null)
            {
                string phase = session != null ? session.Phase.ToString() : "";
                string drive = "";
                if (GameManager.FirstPerson && gm != null)
                {
                    drive = "   ·   " + (gm.Settings.driveMode == DriveMode.Automatico ? "Automático" : "Manual")
                            + (RailPilot.Instance != null && RailPilot.Instance.AssistActive ? " (ayuda del riel)" : "");
                    if (gm.SecretsFound > 0) drive += "   ·   Cascabeles: " + gm.SecretsFound + "/" + SecretBell.TotalInScene;
                    if (gm.submarine != null)
                    {
                        Vector3 sp = gm.submarine.transform.position;
                        CityDistrict district = CityDistrict.At(sp);
                        WaterZone zone = WaterZone.FindAt(sp);
                        if (district != null)
                            drive = "   ·   Nivel " + district.levelNumber + ": " + district.displayName
                                    + (district.levelNumber > 0 && district.Freed ? " (liberado)" : "") + drive;
                        else if (zone != null && zone.isLockChamber)
                            drive = "   ·   " + zone.displayName + drive;
                    }
                }
                statusText.text = BimanualInput.Instance.StatusText() + "   ·   " + phase + drive + "   ·   F1: panel del terapeuta";
            }

            // Aviso
            if (toastTimer > 0f)
            {
                toastTimer -= dt;
                toastGroup.alpha = Mathf.Clamp01(toastTimer * 2f);
            }
            else if (toastGroup != null) toastGroup.alpha = 0f;

            // Texto del diálogo con efecto máquina de escribir
            if (dialogRoot != null && dialogRoot.activeSelf && dialogText.text.Length < dialogFull.Length)
            {
                dialogTypeTimer += dt * 55f;
                int n = Mathf.Clamp(Mathf.FloorToInt(dialogTypeTimer), 0, dialogFull.Length);
                dialogText.text = dialogFull.Substring(0, n);
            }

            // Ayuda
            bool showHelp = gm == null || gm.Settings.showControlsHelp;
            if (Input.GetKeyDown(KeyCode.H) && gm != null) gm.Settings.showControlsHelp = !gm.Settings.showControlsHelp;
            if (helpRoot != null)
            {
                helpRoot.SetActive(showHelp && !ModalOpen);
                if (showHelp) helpText.text = HelpText();
            }

            if (promptText != null) promptText.text = ModalOpen ? "" : ContextPrompt();
        }

        private string HelpText()
        {
            bool webcam = BimanualInput.Instance != null && BimanualInput.Instance.UsingWebcam;
            if (webcam && GameManager.FirstPerson)
            {
                return "<b>Controles (webcam)</b>\n" +
                       "Mano del PANEL (timón):\n" +
                       "  · palma abajo: quieto · a los lados: gira\n" +
                       "  · subirla: sube (y palanca)\n" +
                       "  · abrirla: baja\n" +
                       "  · de canto: avanza · + pulgar: retrocede\n" +
                       "Juntar las manos: panel de la cabina\n" +
                       "Palmas abajo + un pulgar: elegir · ambos: confirmar\n" +
                       "Mano del BRAZO: pinza · cerrar = sujetar\n" +
                       "H: ocultar ayuda · F1: terapeuta";
            }
            if (webcam)
            {
                return "<b>Controles (webcam)</b>\n" +
                       "Brazo del panel: mover el submarino\n" +
                       "  · subir y mantener = palanca\n" +
                       "Brazo hábil: mover la pinza\n" +
                       "  · cerrar la mano = sujetar\n" +
                       "  · girar la muñeca = verter\n" +
                       "H: ocultar ayuda";
            }
            GameManager gm = GameManager.Instance;
            if (GameManager.FirstPerson)
            {
                return "<b>Controles (teclado + mouse)</b>\n" +
                       "W avanzar · S retroceder · A/D girar\n" +
                       "Espacio subir (y palanca de bomba)\n" +
                       "Shift o C bajar\n" +
                       "Mouse: pinza · Clic izq.: sujetar\n" +
                       "E / Q o clic der.: girar muñeca\n" +
                       "M: plegar/desplegar el panel del mapa\n" +
                       "Z / X: pulgar izq. / der. (elegir botón)\n" +
                       "Z+X o Enter: confirmar\n" +
                       "R: modo " + (gm != null && gm.Settings.driveMode == DriveMode.Automatico ? "MANUAL" : "AUTOMÁTICO (riel)") + "\n" +
                       "L: simular compensación · H/F1";
            }
            return "<b>Controles (teclado + mouse)</b>\n" +
                   "WASD / flechas: panel (mover)\n" +
                   "Espacio: palanca de la bomba\n" +
                   "Mouse: pinza · Clic izq.: sujetar\n" +
                   "E / Q o clic der.: girar muñeca\n" +
                   "L: simular compensación de tronco\n" +
                   "H: ocultar ayuda · F1: terapeuta";
        }

        private string ContextPrompt()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.submarine == null || gm.arm == null) return "";
            bool webcam = BimanualInput.Instance != null && BimanualInput.Instance.UsingWebcam;
            SubmarineController sub = gm.submarine;
            ManipulatorArm arm = gm.arm;

            if (sub.IsOnAutopilot) return "Piloto automático...";
            ProjectileLauncher launcher = ProjectileLauncher.Instance;
            if (launcher != null && launcher.IsOpen) return launcher.PromptText(webcam);
            if (RailPilot.Instance != null && RailPilot.Instance.IsDriving)
            {
                string rail = RailPilot.Instance.PromptText;
                if (!string.IsNullOrEmpty(rail)) return rail;
            }

            FuelCanister fuel = arm.Held as FuelCanister;
            if (fuel != null)
            {
                if (fuel.WantsToPourButValveClosed) return webcam ? "Mantén ARRIBA la mano del panel para abrir la válvula" : "Mantén ESPACIO para abrir la válvula";
                if (fuel.IsPouring) return "¡Vertiendo combustible!";
                if (fuel.NearbyPump != null) return webcam ? "Gira la muñeca para verter" : "Mantén E para girar la pinza y verter";
                return "Lleva el bidón al embudo de una bomba";
            }

            LockStation lockHere = sub.State == SubState.Acoplado ? sub.AnchorOwner as LockStation : null;
            if (lockHere != null) return lockHere.PromptText(webcam);

            if (sub.DockedPump != null)
            {
                PumpStation p = sub.DockedPump;
                if (p.IsPumping) return "¡Bombeando! " + Mathf.RoundToInt(p.Progress * 100f) + "%";
                if (p.Tank > 0.01f) return webcam ? "Mantén arriba la mano del panel para bombear" : "Mantén ESPACIO para bombear";
                if (GameManager.FirstPerson) return "Acoplado a la " + p.displayName.ToLower() + ": trae un bidón · para soltarte, gira o avanza";
                return "Acoplado a la " + p.displayName.ToLower() + ": trae un bidón y viértelo en el embudo";
            }

            if (sub.AnchorOwner is ReachAnchor && arm.Held == null)
            {
                return webcam ? "¡Anclado! Estira el brazo hacia el objetivo y cierra la mano"
                              : "¡Anclado! Lleva la pinza al objetivo y haz clic";
            }

            if (arm.Held != null)
            {
                return webcam ? "Abre la mano para soltar" : "Suelta el clic para soltar";
            }

            if (arm.HoverCandidate != null)
            {
                if (arm.DwellProgress > 0.01f) return "Mantén la pinza quieta... " + Mathf.RoundToInt(arm.DwellProgress * 100f) + "%";
                return (webcam ? "Cierra la mano" : "Clic izquierdo") + " para sujetar: " + arm.HoverCandidate.displayName;
            }
            if (launcher != null && launcher.CanOpen && sub.Velocity.magnitude < 1.5f)
            {
                return sub.IsSurfaced ? "¡Gatitos con hambre cerca! Abre el LANZADOR en el panel" + (webcam ? "" : " (o G)")
                                      : "Hay un blanco para el lanzador cerca: LANZAR en el panel" + (webcam ? "" : " (o G)");
            }
            return "";
        }
    }
}
