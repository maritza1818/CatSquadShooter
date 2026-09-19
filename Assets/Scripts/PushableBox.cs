using UnityEngine;
using System.Collections;

public class PushableBox : MonoBehaviour
{
    public float moveSpeed = 6f;

    private Vector2Int currentCell;
    private bool isMoving = false;

    public void SetCurrentCell(Vector2Int cell)
    {
        currentCell = cell;
    }

    public bool TryPush(Vector3 pushDirection)
    {
        if (isMoving)
        {
            return false;
        }

        Vector2Int directionCell = DirectionToCell(pushDirection);
        Vector2Int nextCell = currentCell + directionCell;

        if (TileGridManager.Instance == null)
        {
            return false;
        }

        if (!TileGridManager.Instance.IsCellFree(nextCell))
        {
            Debug.Log("La caja no puede moverse. Celda ocupada o bloqueada.");
            return false;
        }

        Vector3 targetPosition = TileGridManager.Instance.CellToWorld(nextCell) + Vector3.up * 0.75f;

        TileGridManager.Instance.MoveOccupant(currentCell, nextCell, gameObject);
        currentCell = nextCell;

        StartCoroutine(MoveToCell(targetPosition));

        return true;
    }

    Vector2Int DirectionToCell(Vector3 direction)
    {
        direction.y = 0;
        direction.Normalize();

        if (Mathf.Abs(direction.x) > Mathf.Abs(direction.z))
        {
            if (direction.x > 0)
            {
                return Vector2Int.right;
            }
            else
            {
                return Vector2Int.left;
            }
        }
        else
        {
            if (direction.z > 0)
            {
                return new Vector2Int(0, -1);
            }
            else
            {
                return new Vector2Int(0, 1);
            }
        }
    }

    IEnumerator MoveToCell(Vector3 targetPosition)
    {
        isMoving = true;

        while (Vector3.Distance(transform.position, targetPosition) > 0.05f)
        {
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
            yield return null;
        }

        transform.position = targetPosition;
        isMoving = false;
    }
}