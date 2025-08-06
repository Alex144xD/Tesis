using UnityEngine;
using System.Collections.Generic;

public class PathfindingBFS : MonoBehaviour
{
    public static PathfindingBFS Instance;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    public List<Vector3> FindPath(int floor, Vector3 startPos, Vector3 targetPos)
    {
        var map = MultiFloorDynamicMapManager.Instance;
        bool[,,] walkableGrid = map.GetWalkableGrid();

        Vector2Int start = WorldToGrid(startPos);
        Vector2Int target = WorldToGrid(targetPos);

        if (!IsWalkable(map, walkableGrid, floor, start))
            start = FindNearestWalkable(map, walkableGrid, floor, start);

        if (!IsWalkable(map, walkableGrid, floor, target))
            target = FindNearestWalkable(map, walkableGrid, floor, target);

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        queue.Enqueue(start);
        cameFrom[start] = start;

        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == target) break;

            for (int i = 0; i < 4; i++)
            {
                int nx = current.x + dx[i];
                int ny = current.y + dy[i];
                var next = new Vector2Int(nx, ny);

                if (nx >= 0 && nx < map.width && ny >= 0 && ny < map.height &&
                    walkableGrid[floor, nx, ny] &&
                    !cameFrom.ContainsKey(next))
                {
                    queue.Enqueue(next);
                    cameFrom[next] = current;
                }
            }
        }

        if (!cameFrom.ContainsKey(target)) return new List<Vector3>();

        List<Vector3> path = new List<Vector3>();
        Vector2Int currentPos = target;

        while (currentPos != start)
        {
            path.Add(GridToWorld(currentPos, floor));
            currentPos = cameFrom[currentPos];
        }

        path.Reverse();
        return path;
    }

    private Vector2Int WorldToGrid(Vector3 worldPos)
    {
        var map = MultiFloorDynamicMapManager.Instance;
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / map.cellSize),
            Mathf.FloorToInt(worldPos.z / map.cellSize)
        );
    }

    private Vector3 GridToWorld(Vector2Int gridPos, int floor)
    {
        var map = MultiFloorDynamicMapManager.Instance;
        return new Vector3(
            gridPos.x * map.cellSize,
            -floor * map.floorHeight + 0.1f,
            gridPos.y * map.cellSize
        );
    }

    private bool IsWalkable(MultiFloorDynamicMapManager map, bool[,,] grid, int floor, Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < map.width &&
               cell.y >= 0 && cell.y < map.height &&
               grid[floor, cell.x, cell.y];
    }

    private Vector2Int FindNearestWalkable(MultiFloorDynamicMapManager map, bool[,,] grid, int floor, Vector2Int origin)
    {
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        queue.Enqueue(origin);
        visited.Add(origin);

        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            if (IsWalkable(map, grid, floor, current))
                return current;

            for (int i = 0; i < 4; i++)
            {
                Vector2Int next = new Vector2Int(current.x + dx[i], current.y + dy[i]);
                if (!visited.Contains(next) &&
                    next.x >= 0 && next.x < map.width &&
                    next.y >= 0 && next.y < map.height)
                {
                    visited.Add(next);
                    queue.Enqueue(next);
                }
            }
        }

        return origin; // No walkable found, return original
    }
}
