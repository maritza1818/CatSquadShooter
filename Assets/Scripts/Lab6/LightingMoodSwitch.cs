using UnityEngine;

// LAB 6 - Cambia el ambiente de la sala entre MISTERIO y CÁLIDO/REVELADO.
// El jugador mira el interruptor y presiona E (raycast). Va en el objeto "Jugador"
// o en un objeto vacío "GameManager".
public class LightingMoodSwitch : MonoBehaviour
{
    [Header("Grupos de luces (un objeto padre por ambiente)")]
    public GameObject mysteryLights;   // luces del modo Misterio
    public GameObject warmLights;      // luces del modo cálido/revelado

    [Header("Ambiente MISTERIO")]
    public Color mysteryAmbient = new Color(0.05f, 0.07f, 0.12f);
    public Color mysteryFog = new Color(0.04f, 0.06f, 0.10f);
    public float mysteryFogDensity = 0.06f;

    [Header("Ambiente CÁLIDO")]
    public Color warmAmbient = new Color(0.35f, 0.28f, 0.18f);
    public Color warmFog = new Color(0.20f, 0.16f, 0.10f);
    public float warmFogDensity = 0.015f;

    [Header("Interacción")]
    public Camera cam;                 // la cámara del jugador
    public float reach = 3f;           // alcance para tocar el interruptor
    public string switchTag = "Switch";
    public KeyCode key = KeyCode.E;

    private bool mystery = true;

    void Start()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        Apply();
    }

    void Update()
    {
        if (!Input.GetKeyDown(key)) return;

        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, reach))
        {
            if (hit.collider.CompareTag(switchTag))
            {
                mystery = !mystery;   // alterna el ambiente
                Apply();
            }
        }
    }

    void Apply()
    {
        if (mysteryLights != null) mysteryLights.SetActive(mystery);
        if (warmLights != null) warmLights.SetActive(!mystery);

        RenderSettings.ambientLight = mystery ? mysteryAmbient : warmAmbient;
        RenderSettings.fogColor = mystery ? mysteryFog : warmFog;
        RenderSettings.fogDensity = mystery ? mysteryFogDensity : warmFogDensity;
    }
}
