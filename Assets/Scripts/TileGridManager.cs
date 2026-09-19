using UnityEngine;
using System.Collections.Generic;

public class TileGridManager : MonoBehaviour
{
    public static TileGridManager Instance;

    public float tileSize = 4f;

    public GameObject floorPrefab;
    public GameObject wallPrefab;
    public GameObject coverPrefab;
    public GameObject pushableBoxPrefab;
    public GameObject damageZonePrefab;

    public Transform player;
    public Transform generatedParent;

    private Dictionary<Vector2Int, GameObject> occupiedCells = new Dictionary<Vector2Int, GameObject>();

    private int[,] map =
    {
        {1,1,1,1,1,1,1,1,1,1,1},
        {1,4,0,0,2,0,0,3,0,5,1},
        {1,0,0,0,0,0,0,0,0,0,1},
        {1,0,2,0,3,0,2,0,6,0,1},
        {1,0,0,0,0,0,0,0,0,0,1},
        {1,3,0,2,0,6,0,2,0,3,1},
        {1,0,0,0,0,0,0,0,0,0,1},
        {1,5,0,6,2,0,3,0,0,4,1},
        {1,1,1,1,1,1,1,1,1,1,1}
    };

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        GenerateMap();
    }

    void GenerateMap()
    {
        if (generatedParent == null)
        {
            GameObject parentObject = new GameObject("Generated_Tile_Map");
            generatedParent = parentObject.transform;
        }

        int rows = map.GetLength(0);
        int columns = map.GetLength(1);

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                Vector2Int cell = new Vector2Int(col, row);
                Vector3 position = CellToWorld(cell);

                CreateFloor(position);

                int tileType = map[row, col];

                if (tileType == 1)
                {
                    Quaternion wallRotation = GetWallRotation(cell);
                    CreateObject(wallPrefab, position + Vector3.up * 1.5f, wallRotation, cell, true);
                }
                else if (tileType == 2)
                {
                    CreateObject(coverPrefab, position + Vector3.up * 0.6f, Quaternion.identity, cell, true);
                }
                else if (tileType == 3)
                {
                    GameObject box = CreateObject(pushableBoxPrefab, position + Vector3.up * 0.75f, Quaternion.identity, cell, true);

                    if (box != null)
                    {
                        PushableBox pushableBox = box.GetComponent<PushableBox>();

                        if (pushableBox != null)
                        {
                            pushableBox.SetCurrentCell(cell);
                        }
                    }
                }
                else if (tileType == 4)
                {
                    if (player != null)
                    {
                        CharacterController controller = player.GetComponent<CharacterController>();

                        if (controller != null)
                        {
                            controller.enabled = false;
                        }

                        player.position = position + Vector3.up * 1f;

                        if (controller != null)
                        {
                            controller.enabled = true;
                        }
                    }
                }
                else if (tileType == 6)
                {
                    CreateObject(damageZonePrefab, position + Vector3.up * 0.25f, Quaternion.identity, cell, false);
                }
            }
        }
    }

    void CreateFloor(Vector3 position)
    {
        if (floorPrefab != null)
        {
            GameObject floor = Instantiate(floorPrefab, position, Quaternion.identity);
            floor.transform.SetParent(generatedParent);
        }
    }

    GameObject CreateObject(GameObject prefab, Vector3 position, Quaternion rotation, Vector2Int cell, bool occupiesCell)
    {
        if (prefab == null)
        {
            return null;
        }

        GameObject obj = Instantiate(prefab, position, rotation);
        obj.transform.SetParent(generatedParent);

        if (occupiesCell)
        {
            occupiedCells[cell] = obj;
        }

        return obj;
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        return new Vector3(cell.x * tileSize, 0, -cell.y * tileSize);
    }

    public Vector2Int WorldToCell(Vector3 worldPosition)
    {
        int col = Mathf.RoundToInt(worldPosition.x / tileSize);
        int row = Mathf.RoundToInt(-worldPosition.z / tileSize);

        return new Vector2Int(col, row);
    }

    public bool IsInsideGrid(Vector2Int cell)
    {
        int rows = map.GetLength(0);
        int columns = map.GetLength(1);

        return cell.x >= 0 && cell.x < columns && cell.y >= 0 && cell.y < rows;
    }

    public bool IsCellFree(Vector2Int cell)
    {
        if (!IsInsideGrid(cell))
        {
            return false;
        }

        int tileType = map[cell.y, cell.x];

        if (tileType == 1 || tileType == 2)
        {
            return false;
        }

        if (occupiedCells.ContainsKey(cell))
        {
            return false;
        }

        return true;
    }

    public void MoveOccupant(Vector2Int oldCell, Vector2Int newCell, GameObject obj)
    {
        if (occupiedCells.ContainsKey(oldCell))
        {
            occupiedCells.Remove(oldCell);
        }

        occupiedCells[newCell] = obj;
    }

    public int GetTileType(Vector2Int cell)
    {
        if (!IsInsideGrid(cell))
        {
            return -1;
        }

        return map[cell.y, cell.x];
    }

    public int GetRows()
    {
        return map.GetLength(0);
    }

    public int GetColumns()
    {
        return map.GetLength(1);
    }

    public bool IsCellAvailableForDrop(Vector2Int cell)
    {
        if (!IsCellFree(cell))
        {
            return false;
        }

        int tileType = GetTileType(cell);

        if (tileType == 0 || tileType == 4 || tileType == 5)
        {
            return true;
        }

        return false;
    }
    Quaternion GetWallRotation(Vector2Int cell)
    {
        int rows = map.GetLength(0);
        int columns = map.GetLength(1);

        bool isTopBorder = cell.y == 0;
        bool isBottomBorder = cell.y == rows - 1;
        bool isLeftBorder = cell.x == 0;
        bool isRightBorder = cell.x == columns - 1;

        if (isTopBorder)
        {
            return Quaternion.Euler(0, -90, 0);
        }

        if (isBottomBorder)
        {
            return Quaternion.Euler(0, 90, 0);
        }

        if (isLeftBorder)
        {
            return Quaternion.Euler(0, 180, 0);
        }

        if (isRightBorder)
        {
            return Quaternion.Euler(0, 01, 0);
        }

        return Quaternion.identity;
    }
}