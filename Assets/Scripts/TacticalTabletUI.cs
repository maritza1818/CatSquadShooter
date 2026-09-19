using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TacticalTabletUI : MonoBehaviour
{
    public enum TabletMode
    {
        Supply,
        Missile
    }

    public TabletMode currentMode = TabletMode.Supply;

    public GameObject tabletPanel;
    public Transform gridContainer;
    public GameObject tileButtonPrefab;

    public GameObject supplyCratePrefab;
    public GameObject missilePrefab;
    public GameObject missileWarningPrefab;

    public Button supplyModeButton;
    public Button missileModeButton;
    public TMP_Text currentModeText;

    public Color freeColor = Color.green;
    public Color blockedColor = Color.gray;
    public Color damageColor = Color.red;
    public Color spawnColor = Color.cyan;
    public Color missileModeColor = Color.yellow;

    private bool isOpen = false;

    void Start()
    {
        if (tabletPanel != null)
        {
            tabletPanel.SetActive(false);
        }

        if (supplyModeButton != null)
        {
            supplyModeButton.onClick.AddListener(SetSupplyMode);
        }

        if (missileModeButton != null)
        {
            missileModeButton.onClick.AddListener(SetMissileMode);
        }

        UpdateModeText();
        GenerateGridButtons();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            ToggleTablet();
        }
    }

    void ToggleTablet()
    {
        isOpen = !isOpen;

        if (tabletPanel != null)
        {
            tabletPanel.SetActive(isOpen);
        }

        if (isOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 0f;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Time.timeScale = 1f;
        }
    }

    void SetSupplyMode()
    {
        currentMode = TabletMode.Supply;
        UpdateModeText();
        RefreshGridColors();
    }

    void SetMissileMode()
    {
        currentMode = TabletMode.Missile;
        UpdateModeText();
        RefreshGridColors();
    }

    void UpdateModeText()
    {
        if (currentModeText == null)
        {
            return;
        }

        if (currentMode == TabletMode.Supply)
        {
            currentModeText.text = "Modo actual: Suministros";
        }
        else
        {
            currentModeText.text = "Modo actual: Misil táctico";
        }
    }

    void GenerateGridButtons()
    {
        if (TileGridManager.Instance == null)
        {
            Debug.LogWarning("No existe TileGridManager en la escena.");
            return;
        }

        int rows = TileGridManager.Instance.GetRows();
        int columns = TileGridManager.Instance.GetColumns();

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                Vector2Int cell = new Vector2Int(col, row);

                GameObject buttonObject = Instantiate(tileButtonPrefab, gridContainer);
                buttonObject.name = "Cell_" + col + "_" + row;

                Button button = buttonObject.GetComponent<Button>();
                Image image = buttonObject.GetComponent<Image>();
                TMP_Text text = buttonObject.GetComponentInChildren<TMP_Text>();

                int tileType = TileGridManager.Instance.GetTileType(cell);
                bool available = TileGridManager.Instance.IsCellAvailableForDrop(cell);

                if (text != null)
                {
                    text.text = "";
                }

                if (image != null)
                {
                    image.color = GetColorForTile(tileType, available);
                }

                if (button != null)
                {
                    button.interactable = available;

                    Vector2Int selectedCell = cell;
                    button.onClick.AddListener(() => SelectCell(selectedCell));
                }
            }
        }
    }

    void RefreshGridColors()
    {
        if (TileGridManager.Instance == null)
        {
            return;
        }

        foreach (Transform child in gridContainer)
        {
            string[] parts = child.name.Split('_');

            if (parts.Length != 3)
            {
                continue;
            }

            int col = int.Parse(parts[1]);
            int row = int.Parse(parts[2]);

            Vector2Int cell = new Vector2Int(col, row);
            int tileType = TileGridManager.Instance.GetTileType(cell);
            bool available = TileGridManager.Instance.IsCellAvailableForDrop(cell);

            Image image = child.GetComponent<Image>();

            if (image != null)
            {
                image.color = GetColorForTile(tileType, available);
            }
        }
    }

    Color GetColorForTile(int tileType, bool available)
    {
        if (tileType == 1 || tileType == 2 || tileType == 3)
        {
            return blockedColor;
        }

        if (tileType == 6)
        {
            return damageColor;
        }

        if (tileType == 4 || tileType == 5)
        {
            return spawnColor;
        }

        if (available)
        {
            if (currentMode == TabletMode.Missile)
            {
                return missileModeColor;
            }

            return freeColor;
        }

        return blockedColor;
    }

    void SelectCell(Vector2Int cell)
    {
        if (TileGridManager.Instance == null)
        {
            return;
        }

        if (!TileGridManager.Instance.IsCellAvailableForDrop(cell))
        {
            Debug.Log("Celda no disponible.");
            return;
        }

        if (currentMode == TabletMode.Supply)
        {
            RequestSupplyDrop(cell);
        }
        else
        {
            RequestMissileStrike(cell);
        }

        ToggleTablet();
    }

    void RequestSupplyDrop(Vector2Int cell)
    {
        Vector3 dropPosition = TileGridManager.Instance.CellToWorld(cell) + Vector3.up * 12f;

        if (supplyCratePrefab != null)
        {
            Instantiate(supplyCratePrefab, dropPosition, Quaternion.identity);
            Debug.Log("Suministro solicitado en celda: " + cell);
        }
    }

    void RequestMissileStrike(Vector2Int cell)
    {
        Vector3 worldPosition = TileGridManager.Instance.CellToWorld(cell);

        if (missileWarningPrefab != null)
        {
            Instantiate(missileWarningPrefab, worldPosition + Vector3.up * 0.08f, Quaternion.identity);
        }

        Vector3 missilePosition = worldPosition + Vector3.up * 18f;

        if (missilePrefab != null)
        {
            Instantiate(missilePrefab, missilePosition, Quaternion.identity);
            Debug.Log("Misil táctico solicitado en celda: " + cell);
        }
    }
}