using UnityEngine;
using System.Collections.Generic;

public class PathfindingBFS : MonoBehaviour
{
    public static PathfindingBFS Instance;

    // Buffers reutilizables
    bool[] visited;
    int[] parentX, parentY;
    int[] queueX, queueY;
    int capW, capH;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void EnsureCapacity(int w, int h)
    {
        if (visited != null && capW == w && capH == h) return;
        int n = w * h;
        visited = new bool[n];
        parentX = new int[n];
        parentY = new int[n];
        queueX = new int[n];
        queueY = new int[n];
        capW = w; capH = h;
    }

    static int Idx(int x, int y, int w) => y * w + x;

    // Pública: dos pasadas (evita santuario; si falla, permite)
    public List<Vector3> FindPath(int floor, Vector3 startPos, Vector3 targetPos)
    {
        return FindPathInternal(floor, startPos, targetPos, strictAvoidSanctuary: false);
    }

    public bool TryFindPath(int floor, Vector3 startPos, Vector3 targetPos, out List<Vector3> path)
    {
        path = FindPath(floor, startPos, targetPos);
        return path != null && path.Count > 0;
    }

    // Pública: ESTRICTA (nunca atraviesa santuario)
    public List<Vector3> FindPathStrict(int floor, Vector3 startPos, Vector3 targetPos)
    {
        return FindPathInternal(floor, startPos, targetPos, strictAvoidSanctuary: true);
    }

    public bool TryFindPathStrict(int floor, Vector3 startPos, Vector3 targetPos, out List<Vector3> path)
    {
        path = FindPathStrict(floor, startPos, targetPos);
        return path != null && path.Count > 0;
    }

    // Fallback para deambular
    public Vector3 FindWanderTarget(int floor, Vector3 fromWorld, Transform player, float minDistCells = 4f)
    {
        var map = MultiFloorDynamicMapManager.Instance;
        var free = map.GetFreeCells(floor);
        if (free == null || free.Count == 0) return fromWorld;

        Vector2Int fromCell = map.WorldToCell(fromWorld);
        Vector2Int playerCell = player ? map.WorldToCell(player.position) : fromCell;

        Vector2Int best = free[0];
        float bestScore = -1f;

        foreach (var c in free)
        {
            if (map.IsSanctuary(floor, c)) continue; // mejor no meterse a santuarios
            float dFrom = Mathf.Abs(c.x - fromCell.x) + Mathf.Abs(c.y - fromCell.y);
            float dPlr = Mathf.Abs(c.x - playerCell.x) + Mathf.Abs(c.y - playerCell.y);
            float score = dPlr + 0.25f * dFrom;
            if (dFrom < minDistCells) score *= 0.5f;
            if (score > bestScore) { bestScore = score; best = c; }
        }

        return map.CellCenterToWorld(best, floor);
    }

    // ---------------- Internos ----------------

    List<Vector3> FindPathInternal(int floor, Vector3 startPos, Vector3 targetPos, bool strictAvoidSanctuary)
    {
        var map = MultiFloorDynamicMapManager.Instance;
        int w = map.width, h = map.height;
        EnsureCapacity(w, h);

        Vector2Int start = WorldToGrid(startPos);
        Vector2Int target = WorldToGrid(targetPos);
        ClampToBounds(ref start, w, h);
        ClampToBounds(ref target, w, h);

        start = NormalizeSeed(map, floor, start, w, h, avoidSanctuary: true);
        target = NormalizeSeed(map, floor, target, w, h, avoidSanctuary: true);

        var path = BfsPath(map, floor, w, h, start, target, avoidSanctuary: true);
        if (path.Count > 0 || strictAvoidSanctuary) return path;

        // Segunda pasada solo si no es estricta
        start = NormalizeSeed(map, floor, start, w, h, avoidSanctuary: false);
        target = NormalizeSeed(map, floor, target, w, h, avoidSanctuary: false);
        return BfsPath(map, floor, w, h, start, target, avoidSanctuary: false);
    }

    List<Vector3> BfsPath(MultiFloorDynamicMapManager map, int floor, int w, int h,
                          Vector2Int start, Vector2Int target, bool avoidSanctuary)
    {
        System.Array.Clear(visited, 0, visited.Length);

        int head = 0, tail = 0;
        int sx = start.x, sy = start.y;
        int tx = target.x, ty = target.y;

        if (!IsWalkablePolicy(map, floor, sx, sy, avoidSanctuary) ||
            !IsWalkablePolicy(map, floor, tx, ty, avoidSanctuary))
            return new List<Vector3>();

        int sid = Idx(sx, sy, w);
        visited[sid] = true;
        parentX[sid] = sx; parentY[sid] = sy;
        queueX[tail] = sx; queueY[tail] = sy; tail++;

        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };
        bool found = false;

        while (head != tail)
        {
            int cx = queueX[head];
            int cy = queueY[head];
            head++;

            if (cx == tx && cy == ty) { found = true; break; }

            for (int i = 0; i < 4; i++)
            {
                int nx = cx + dx[i];
                int ny = cy + dy[i];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;

                int nid = Idx(nx, ny, w);
                if (visited[nid]) continue;
                if (!IsWalkablePolicy(map, floor, nx, ny, avoidSanctuary)) continue;

                visited[nid] = true;
                parentX[nid] = cx; parentY[nid] = cy;
                queueX[tail] = nx; queueY[tail] = ny; tail++;
            }
        }

        if (!found) return new List<Vector3>();

        var path = new List<Vector3>(32);
        int px = tx, py = ty;
        while (!(px == sx && py == sy))
        {
            path.Add(GridToWorld(new Vector2Int(px, py), floor));
            int pid = Idx(px, py, w);
            int ppx = parentX[pid];
            int ppy = parentY[pid];
            px = ppx; py = ppy;
        }
        path.Reverse();
        return path;
    }

    Vector2Int NormalizeSeed(MultiFloorDynamicMapManager map, int floor, Vector2Int cell, int w, int h, bool avoidSanctuary)
    {
        ClampToBounds(ref cell, w, h);
        if (IsWalkablePolicy(map, floor, cell.x, cell.y, avoidSanctuary)) return cell;

        System.Array.Clear(visited, 0, visited.Length);
        int head = 0, tail = 0;

        int sx = cell.x, sy = cell.y;
        int sid = Idx(sx, sy, w);
        visited[sid] = true;
        queueX[tail] = sx; queueY[tail] = sy; tail++;

        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        while (head != tail)
        {
            int cx = queueX[head];
            int cy = queueY[head];
            head++;

            if (IsWalkablePolicy(map, floor, cx, cy, avoidSanctuary))
                return new Vector2Int(cx, cy);

            for (int i = 0; i < 4; i++)
            {
                int nx = cx + dx[i];
                int ny = cy + dy[i];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                int nid = Idx(nx, ny, w);
                if (visited[nid]) continue;

                visited[nid] = true;
                queueX[tail] = nx; queueY[tail] = ny; tail++;
            }
        }

        return new Vector2Int(sx, sy);
    }

    static void ClampToBounds(ref Vector2Int c, int w, int h)
    {
        c.x = Mathf.Clamp(c.x, 0, w - 1);
        c.y = Mathf.Clamp(c.y, 0, h - 1);
    }

    Vector2Int WorldToGrid(Vector3 worldPos)
    {
        var map = MultiFloorDynamicMapManager.Instance;
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / map.cellSize),
            Mathf.FloorToInt(worldPos.z / map.cellSize)
        );
    }

    Vector3 GridToWorld(Vector2Int gridPos, int floor)
    {
        var map = MultiFloorDynamicMapManager.Instance;
        // Centro de celda para navegación suave
        float x = gridPos.x * map.cellSize + map.cellSize * 0.5f;
        float z = gridPos.y * map.cellSize + map.cellSize * 0.5f;
        return new Vector3(x, -floor * map.floorHeight + 0.1f, z);
    }

    bool IsWalkablePolicy(MultiFloorDynamicMapManager map, int floor, int x, int y, bool avoidSanctuary)
    {
        if (!map.IsWalkable(floor, x, y)) return false;
        if (avoidSanctuary && map.IsSanctuary(floor, x, y)) return false;
        return true;
    }
}