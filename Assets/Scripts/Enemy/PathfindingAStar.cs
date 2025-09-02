using UnityEngine;
using System.Collections.Generic;

public class PathfindingAStar : MonoBehaviour
{
    public static PathfindingAStar Instance;

    [Header("A*")]
    [Tooltip("Permitir movimientos diagonales.")]
    public bool allowDiagonals = true;

    [Tooltip("Si hay diagonales, impedir cortar esquinas entre obstáculos.")]
    public bool preventCornerCut = true;

    [Tooltip("Intentar suavizar el camino con línea de vista en la grilla.")]
    public bool smoothWithLOS = true;

    [Tooltip("Desempate leve para rutas más rectas (f = g + h * (1+eps)).")]
    [Range(0f, 0.02f)] public float heuristicTieBreak = 0.001f;

    [Header("Waypoints / Centro de pasillo")]
    [Tooltip("Usar puntos ajustados para ir por el eje del pasillo.")]
    public bool useInset = true;

    [Tooltip("Altura Y para puntos de ruta generados (offset sobre el piso).")]
    public float waypointYOffset = 0.1f;

    [Tooltip("Colocar los waypoints como las baterías (ancla de celda).")]
    public bool waypointsUseBatteryAnchor = true;

    [Header("Centro de pasillo (medial axis)")]
    [Tooltip("Hasta cuántas celdas escanear hacia cada lado para medir el ancho de pasillo.")]
    [Range(1, 6)] public int medialProbeCells = 3;

    [Tooltip("Desplazamiento máximo desde el centro de la celda (en múltiplos del cellSize).")]
    [Range(0f, 0.49f)] public float medialInsetMax = 0.35f;

    [Tooltip("Atenúa el empuje cuando la celda está en esquina o T-intersección.")]
    [Range(0f, 1f)] public float cornerAttenuation = 0.65f;

    [Header("Esquinas (anti-pega)")]
    [Tooltip("Empuje mínimo adicional en esquinas internas, como fracción del cellSize.")]
    [Range(0f, 0.49f)] public float cornerInsetMin = 0.22f;

    [Tooltip("Mezcla entre empuje medial (0) y empuje de esquina (1).")]
    [Range(0f, 1f)] public float cornerPull = 0.65f;

    [Header("Suavizado por LOS con margen")]
    [Tooltip("Celdas laterales libres requeridas durante el trazo LOS (0 = sin margen).")]
    [Range(0, 2)] public int losSideClearanceCells = 1;

    [Header("Densificar ruta en pasillos")]
    [Tooltip("Añade puntos intermedios centrados cada N celdas a lo largo del corredor.")]
    public bool addCorridorSamples = true;

    [Tooltip("Cada cuántas celdas insertar un punto intermedio (Bresenham).")]
    [Range(2, 8)] public int subdivideEveryCells = 4;

    [Header("Anti-embotellamiento (opcional)")]
    [Tooltip("Penaliza celdas por donde pasó tráfico recientemente para diversificar rutas.")]
    public bool useTrafficCost = true;
    [Tooltip("Peso del costo por tráfico añadido al gCost.")]
    public float trafficCostFactor = 1.0f;
    [Tooltip("Decaimiento por segundo del calor de tráfico.")]
    public float trafficDecayPerSecond = 1.5f;
    [Tooltip("Jitter por agente para romper empates y que no elijan todas la misma ruta.")]
    [Range(0f, 0.02f)] public float perAgentJitter = 0.004f;

    [Header("Debug")]
    public bool drawGizmos = false;
    public Color gizmoColor = new Color(0.2f, 0.8f, 1f, 0.8f);
    public float gizmoSize = 0.08f;

    // ---- Buffers reutilizables dimensionados a w*h ----
    bool[] closed;
    bool[] inOpen;
    int[] parentX, parentY;
    float[] gCost;
    float[] fScore;
    int capW, capH;

    // Open list (binary heap)
    int[] heap;        // ids
    int[] heapPos;     // posiciones
    int heapCount;

    // BFS ligero para normalizar semillas
    int[] queueX, queueY;

    // cache último path para gizmos
    static readonly List<Vector3> _lastPath = new List<Vector3>(64);

    // ---- Tráfico (heatmap) ----
    float[,,] traffic; // [floor, x, y]
    int tF, tW, tH;
    float lastTrafficDecayTime = 0f;

    // ---- Contexto de agente (jitter) ----
    int agentSeed = 0;
    bool hasAgentSeed = false;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void EnsureCapacity(int w, int h)
    {
        if (capW == w && capH == h && closed != null) return;
        int n = Mathf.Max(1, w * h);

        closed = new bool[n];
        inOpen = new bool[n];
        parentX = new int[n];
        parentY = new int[n];
        gCost = new float[n];
        fScore = new float[n];

        heap = new int[n];
        heapPos = new int[n];
        heapCount = 0;

        queueX = new int[n];
        queueY = new int[n];

        capW = w; capH = h;
    }

    void EnsureTrafficCapacity(MultiFloorDynamicMapManager map)
    {
        if (!useTrafficCost) return;
        if (traffic != null && tF == map.floors && tW == map.width && tH == map.height) return;
        traffic = new float[map.floors, map.width, map.height];
        tF = map.floors; tW = map.width; tH = map.height;
        lastTrafficDecayTime = Time.time;
    }

    static int Idx(int x, int y, int w) => y * w + x;

    // ======================= API pública =======================

    public List<Vector3> FindPath(int floor, Vector3 startPos, Vector3 targetPos)
        => FindPathInternal(floor, startPos, targetPos, strictAvoidSanctuary: false);

    public bool TryFindPath(int floor, Vector3 startPos, Vector3 targetPos, out List<Vector3> path)
    {
        path = FindPath(floor, startPos, targetPos);
        return path != null && path.Count > 0;
    }

    public List<Vector3> FindPathStrict(int floor, Vector3 startPos, Vector3 targetPos)
        => FindPathInternal(floor, startPos, targetPos, strictAvoidSanctuary: true);

    public bool TryFindPathStrict(int floor, Vector3 startPos, Vector3 targetPos, out List<Vector3> path)
    {
        path = FindPathStrict(floor, startPos, targetPos);
        return path != null && path.Count > 0;
    }

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
            float dFrom = Mathf.Abs(c.x - fromCell.x) + Mathf.Abs(c.y - fromCell.y);
            float dPlr = Mathf.Abs(c.x - playerCell.x) + Mathf.Abs(c.y - playerCell.y);
            float score = dPlr + 0.25f * dFrom;
            if (dFrom < minDistCells) score *= 0.5f;
            if (score > bestScore) { bestScore = score; best = c; }
        }

        return WaypointWorld(best, floor);
    }

    /// Ayuda a la FSM: decide si conviene recalcular según distancia en celdas
    public bool ShouldRepathCells(Vector3 lastTargetWorld, Vector3 newTargetWorld, float cellDistanceThreshold)
    {
        var map = MultiFloorDynamicMapManager.Instance;
        Vector2Int a = map.WorldToCell(lastTargetWorld);
        Vector2Int b = map.WorldToCell(newTargetWorld);
        int manhattan = Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        return manhattan >= Mathf.Max(1, Mathf.RoundToInt(cellDistanceThreshold));
    }

    // -------- API extra para EnemyFSM --------

    /// Registra que un agente atravesó una celda (para diversificar rutas).
    public void RegisterTraversal(int floor, Vector2Int cell, float weight)
    {
        if (!useTrafficCost) return;
        var map = MultiFloorDynamicMapManager.Instance;
        EnsureTrafficCapacity(map);
        if (floor < 0 || floor >= map.floors) return;
        if (cell.x < 0 || cell.x >= map.width || cell.y < 0 || cell.y >= map.height) return;
        traffic[floor, cell.x, cell.y] += Mathf.Max(0f, weight);
    }

    /// Define semilla por agente para aplicar jitter (romper empates).
    public void SetAgentContext(int agentId)
    {
        hasAgentSeed = true;
        agentSeed = agentId != 0 ? agentId : 1337;
    }

    public void ClearAgentContext()
    {
        hasAgentSeed = false;
        agentSeed = 0;
    }

    // ======================= Núcleo interno =======================

    List<Vector3> FindPathInternal(int floor, Vector3 startPos, Vector3 targetPos, bool strictAvoidSanctuary)
    {
        var map = MultiFloorDynamicMapManager.Instance;
        if (!map) return new List<Vector3>();

        int w = map.width, h = map.height;
        if (w <= 0 || h <= 0) return new List<Vector3>();
        floor = Mathf.Clamp(floor, 0, Mathf.Max(0, map.floors - 1));

        EnsureCapacity(w, h);
        if (useTrafficCost) { EnsureTrafficCapacity(map); DecayTrafficGrid(); }

        Vector2Int start = WorldToGrid(startPos);
        Vector2Int target = WorldToGrid(targetPos);
        ClampToBounds(ref start, w, h);
        ClampToBounds(ref target, w, h);

        if (start == target)
        {
            var single = new List<Vector3>(1) { WaypointWorld(target, floor) };
            if (drawGizmos) SaveLastPath(single);
            return single;
        }

        // Primera pasada
        start = NormalizeSeed(map, floor, start, w, h, avoidSanctuary: true);
        target = NormalizeSeed(map, floor, target, w, h, avoidSanctuary: true);

        var path = AStarPath(map, floor, w, h, start, target, avoidSanctuary: true);
        if (path.Count > 0 || strictAvoidSanctuary)
        {
            PostProcess(map, floor, path, avoidSanctuary: true);
            if (drawGizmos) SaveLastPath(path);
            return path;
        }

        // Segunda pasada (idéntica ahora que no hay santuarios)
        start = NormalizeSeed(map, floor, start, w, h, avoidSanctuary: false);
        target = NormalizeSeed(map, floor, target, w, h, avoidSanctuary: false);

        path = AStarPath(map, floor, w, h, start, target, avoidSanctuary: false);
        PostProcess(map, floor, path, avoidSanctuary: false);
        if (drawGizmos) SaveLastPath(path);
        return path;
    }

    void PostProcess(MultiFloorDynamicMapManager map, int floor, List<Vector3> path, bool avoidSanctuary)
    {
        if (path == null || path.Count == 0) return;
        if (smoothWithLOS) SmoothPathInPlace(map, floor, path, avoidSanctuary);
        if (addCorridorSamples) DensifyPathInPlace(map, floor, path, avoidSanctuary, subdivideEveryCells);
    }

    // ======================= A* =======================

    List<Vector3> AStarPath(MultiFloorDynamicMapManager map, int floor, int w, int h,
                            Vector2Int start, Vector2Int target, bool avoidSanctuary)
    {
        System.Array.Clear(closed, 0, closed.Length);
        System.Array.Clear(inOpen, 0, inOpen.Length);
        heapCount = 0;

        int sx = start.x, sy = start.y;
        int tx = target.x, ty = target.y;

        if (!IsWalkablePolicy(map, floor, sx, sy, avoidSanctuary) ||
            !IsWalkablePolicy(map, floor, tx, ty, avoidSanctuary))
            return new List<Vector3>();

        for (int i = 0; i < gCost.Length; i++) gCost[i] = float.PositiveInfinity;

        int sid = Idx(sx, sy, w);
        gCost[sid] = 0f;
        HeapPush(sid, Heuristic(sx, sy, tx, ty));

        var neigh = allowDiagonals ? _neigh8 : _neigh4;

        while (heapCount > 0)
        {
            int currentId = HeapPop();
            int cx = currentId % w;
            int cy = currentId / w;

            if (cx == tx && cy == ty)
                return ReconstructPath(w, floor, sx, sy, tx, ty);

            closed[currentId] = true;

            for (int i = 0; i < neigh.Length; i++)
            {
                int nx = cx + neigh[i].x;
                int ny = cy + neigh[i].y;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;

                int nid = Idx(nx, ny, w);
                if (closed[nid]) continue;
                if (!IsWalkablePolicy(map, floor, nx, ny, avoidSanctuary)) continue;

                // anti-corner-cut en diagonal
                if (allowDiagonals && preventCornerCut && neigh[i].x != 0 && neigh[i].y != 0)
                {
                    int ax = cx + neigh[i].x; // horizontal
                    int ay = cy;
                    int bx = cx;
                    int by = cy + neigh[i].y; // vertical
                    if (!IsWalkablePolicy(map, floor, ax, ay, avoidSanctuary) ||
                        !IsWalkablePolicy(map, floor, bx, by, avoidSanctuary))
                        continue;
                }

                float stepCost = (neigh[i].x == 0 || neigh[i].y == 0) ? 1f : SQRT2;

                // costo adicional por tráfico
                float trafficCost = 0f;
                if (useTrafficCost && traffic != null &&
                    nx >= 0 && nx < tW && ny >= 0 && ny < tH && floor >= 0 && floor < tF)
                {
                    trafficCost = traffic[floor, nx, ny] * Mathf.Max(0f, trafficCostFactor);
                }

                float tentativeG = gCost[currentId] + stepCost + trafficCost;

                if (tentativeG < gCost[nid])
                {
                    gCost[nid] = tentativeG;
                    parentX[nid] = cx;
                    parentY[nid] = cy;

                    float hCost = Heuristic(nx, ny, tx, ty);

                    // pequeño jitter por agente para romper empates
                    float jitter = hasAgentSeed ? perAgentJitter * PRand01(nx, ny, agentSeed) : 0f;

                    float f = tentativeG + hCost * (1f + heuristicTieBreak) + jitter;

                    if (inOpen[nid]) HeapUpdate(nid, f);
                    else HeapPush(nid, f);
                }
            }
        }

        return new List<Vector3>();
    }

    const float SQRT2 = 1.41421356f;

    float Heuristic(int x, int y, int tx, int ty)
    {
        int dx = Mathf.Abs(x - tx);
        int dy = Mathf.Abs(y - ty);
        if (allowDiagonals)
        {
            // Octile
            return (dx + dy) + (SQRT2 - 2f) * Mathf.Min(dx, dy);
        }
        else
        {
            // Manhattan
            return dx + dy;
        }
    }

    static float PRand01(int x, int y, int seed)
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + x;
            h = h * 31 + y;
            h = h * 31 + seed;
            h ^= (h << 13);
            h ^= (h >> 7);
            h ^= (h << 17);
            return ((h & 0x7FFFFFFF) / 2147483648f);
        }
    }

    List<Vector3> ReconstructPath(int w, int floor, int sx, int sy, int tx, int ty)
    {
        var path = new List<Vector3>(32);
        int px = tx, py = ty;

        while (!(px == sx && py == sy))
        {
            var cell = new Vector2Int(px, py);
            path.Add(WaypointWorld(cell, floor));

            int pid = Idx(px, py, w);
            int ppx = parentX[pid];
            int ppy = parentY[pid];
            px = ppx; py = ppy;
        }

        path.Reverse();
        return path;
    }

    Vector3 WaypointWorld(Vector2Int cell, int floor)
    {
        var map = MultiFloorDynamicMapManager.Instance;

        if (waypointsUseBatteryAnchor)
        {
            Vector3 p = map.CellToWorld(cell, floor);
            return new Vector3(p.x, p.y + waypointYOffset, p.z);
        }
        else
        {
            return useInset ? GridToWorldInset(cell, floor) : CellCenterToWorld(cell, floor);
        }
    }

    Vector2Int NormalizeSeed(MultiFloorDynamicMapManager map, int floor, Vector2Int cell, int w, int h, bool avoidSanctuary)
    {
        ClampToBounds(ref cell, w, h);
        if (IsWalkablePolicy(map, floor, cell.x, cell.y, avoidSanctuary)) return cell;

        // BFS ligero hasta celda válida
        System.Array.Clear(closed, 0, closed.Length);
        int head = 0, tail = 0;

        int sx = cell.x, sy = cell.y;
        int sid = Idx(sx, sy, w);
        closed[sid] = true;
        queueX[tail] = sx; queueY[tail] = sy; tail++;

        var neigh = allowDiagonals ? _neigh8 : _neigh4;

        while (head != tail)
        {
            int cx = queueX[head];
            int cy = queueY[head];
            head++;

            if (IsWalkablePolicy(map, floor, cx, cy, avoidSanctuary))
                return new Vector2Int(cx, cy);

            for (int i = 0; i < neigh.Length; i++)
            {
                int nx = cx + neigh[i].x;
                int ny = cy + neigh[i].y;
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;

                int nid = Idx(nx, ny, w);
                if (closed[nid]) continue;

                closed[nid] = true;
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

    Vector2Int WorldToCellRaw(Vector3 worldPos)
    {
        var map = MultiFloorDynamicMapManager.Instance;
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / map.cellSize),
            Mathf.FloorToInt(worldPos.z / map.cellSize)
        );
    }

    Vector2Int WorldToGrid(Vector3 worldPos) => WorldToCellRaw(worldPos);

    Vector3 CellCenterToWorld(Vector2Int cell, int floor)
    {
        var map = MultiFloorDynamicMapManager.Instance;
        float x = cell.x * map.cellSize + map.cellSize * 0.5f;
        float z = cell.y * map.cellSize + map.cellSize * 0.5f;
        return new Vector3(x, -floor * map.floorHeight + waypointYOffset, z);
    }

    Vector3 GridToWorldInset(Vector2Int cell, int floor)
    {
        var map = MultiFloorDynamicMapManager.Instance;
        float half = map.cellSize * 0.5f;

        float baseX = cell.x * map.cellSize + half;
        float baseZ = cell.y * map.cellSize + half;

        int L = CountFreeDir(map, floor, cell, -1, 0, medialProbeCells);
        int R = CountFreeDir(map, floor, cell, +1, 0, medialProbeCells);
        int D = CountFreeDir(map, floor, cell, 0, -1, medialProbeCells);
        int U = CountFreeDir(map, floor, cell, 0, +1, medialProbeCells);

        if ((L + R + D + U) == 0)
        {
            float y0 = -floor * map.floorHeight + waypointYOffset;
            return new Vector3(baseX, y0, baseZ);
        }

        float denomX = Mathf.Max(1e-3f, (float)(L + R));
        float denomZ = Mathf.Max(1e-3f, (float)(D + U));
        float offX_medial = ((float)R - (float)L) / denomX;
        float offZ_medial = ((float)U - (float)D) / denomZ;

        bool cornerish =
            ((L == 0 || R == 0) && (D == 0 || U == 0)) ||
            (L == 0 && (U + D) > 0) || (R == 0 && (U + D) > 0) ||
            (D == 0 && (L + R) > 0) || (U == 0 && (L + R) > 0);

        bool wallL = !map.IsWalkable(floor, cell.x - 1, cell.y);
        bool wallR = !map.IsWalkable(floor, cell.x + 1, cell.y);
        bool wallD = !map.IsWalkable(floor, cell.x, cell.y - 1);
        bool wallU = !map.IsWalkable(floor, cell.x, cell.y + 1);

        Vector2 awayCorner = Vector2.zero;
        if (wallL) awayCorner += Vector2.right;
        if (wallR) awayCorner += Vector2.left;
        if (wallD) awayCorner += Vector2.up;
        if (wallU) awayCorner += Vector2.down;

        float maxMedial = map.cellSize * Mathf.Clamp01(medialInsetMax);
        float minCorner = map.cellSize * Mathf.Clamp01(cornerInsetMin);

        Vector2 pushMedial = new Vector2(offX_medial, offZ_medial) * maxMedial;

        Vector2 pushCorner = Vector2.zero;
        if (cornerish && awayCorner.sqrMagnitude > 1e-5f)
        {
            awayCorner = awayCorner.normalized;
            pushCorner = awayCorner * Mathf.Max(minCorner, maxMedial * 0.5f);
        }

        float t = cornerish ? Mathf.Clamp01(cornerPull) : 0f;
        Vector2 push = Vector2.Lerp(pushMedial, pushCorner, t);

        if (cornerish)
            push *= Mathf.Lerp(1f, Mathf.Clamp01(cornerAttenuation), 0.5f);

        float maxAbs = Mathf.Max(maxMedial, minCorner);
        if (push.sqrMagnitude > (maxAbs * maxAbs))
            push = push.normalized * maxAbs;

        float y = -floor * map.floorHeight + waypointYOffset;
        return new Vector3(baseX + push.x, y, baseZ + push.y);
    }

    int CountFreeDir(MultiFloorDynamicMapManager map, int floor, Vector2Int c, int dx, int dy, int maxSteps)
    {
        int cnt = 0;
        int x = c.x, y = c.y;
        for (int s = 1; s <= maxSteps; s++)
        {
            int nx = x + dx * s;
            int ny = y + dy * s;
            if (!map.IsWalkable(floor, nx, ny)) break;
            cnt++;
        }
        return cnt;
    }

    // Ya no se evalúan santuarios; solo caminabilidad.
    bool IsWalkablePolicy(MultiFloorDynamicMapManager map, int floor, int x, int y, bool avoidSanctuary)
    {
        return map.IsWalkable(floor, x, y);
    }

    void SmoothPathInPlace(MultiFloorDynamicMapManager map, int floor, List<Vector3> path, bool avoidSanctuary)
    {
        if (path == null || path.Count < 3) return;

        _cellCache.Clear();
        for (int i = 0; i < path.Count; i++)
            _cellCache.Add(map.WorldToCell(path[i]));

        _tmpPath.Clear();
        _tmpPath.Add(path[0]);

        int iStart = 0;
        while (iStart < _cellCache.Count - 1)
        {
            int iBest = iStart + 1;
            for (int j = _cellCache.Count - 1; j > iStart; j--)
            {
                if (HasGridLineOfSightBuffered(map, floor, _cellCache[iStart], _cellCache[j], avoidSanctuary))
                {
                    iBest = j;
                    break;
                }
            }
            _tmpPath.Add(WaypointWorld(_cellCache[iBest], floor));
            iStart = iBest;
        }

        path.Clear();
        path.AddRange(_tmpPath);
    }

    bool HasGridLineOfSightBuffered(MultiFloorDynamicMapManager map, int floor, Vector2Int a, Vector2Int b, bool avoidSanctuary)
    {
        int x0 = a.x, y0 = a.y;
        int x1 = b.x, y1 = b.y;

        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = (x0 < x1) ? 1 : -1;
        int sy = (y0 < y1) ? 1 : -1;
        int err = dx - dy;

        if (!IsWalkablePolicy(map, floor, x0, y0, avoidSanctuary)) return false;

        while (!(x0 == x1 && y0 == y1))
        {
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }

            if (!IsWalkablePolicy(map, floor, x0, y0, avoidSanctuary)) return false;

            if (losSideClearanceCells > 0)
            {
                for (int ox = -losSideClearanceCells; ox <= losSideClearanceCells; ox++)
                {
                    for (int oy = -losSideClearanceCells; oy <= losSideClearanceCells; oy++)
                    {
                        if (ox == 0 && oy == 0) continue;
                        if (!map.IsWalkable(floor, x0 + ox, y0 + oy)) return false;
                    }
                }
            }
        }
        return true;
    }

    void DensifyPathInPlace(MultiFloorDynamicMapManager map, int floor, List<Vector3> path, bool avoidSanctuary, int everyCells)
    {
        if (path == null || path.Count < 2) return;

        _cellCache.Clear();
        for (int i = 0; i < path.Count; i++)
            _cellCache.Add(map.WorldToCell(path[i]));

        _tmpCells.Clear();
        _tmpCells.Add(_cellCache[0]);

        for (int i = 0; i < _cellCache.Count - 1; i++)
        {
            Vector2Int a = _cellCache[i];
            Vector2Int b = _cellCache[i + 1];

            List<Vector2Int> line = _bresenham;
            line.Clear();
            GetBresenham(a, b, line);

            int step = Mathf.Max(2, everyCells);
            for (int k = step; k < line.Count - 1; k += step)
                _tmpCells.Add(line[k]);

            _tmpCells.Add(b);
        }

        path.Clear();
        for (int i = 0; i < _tmpCells.Count; i++)
            path.Add(WaypointWorld(_tmpCells[i], floor));
    }

    static readonly List<Vector2Int> _bresenham = new List<Vector2Int>(128);
    void GetBresenham(Vector2Int a, Vector2Int b, List<Vector2Int> outCells)
    {
        int x0 = a.x, y0 = a.y;
        int x1 = b.x, y1 = b.y;

        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = (x0 < x1) ? 1 : -1;
        int sy = (y0 < y1) ? 1 : -1;
        int err = dx - dy;

        outCells.Add(new Vector2Int(x0, y0));
        while (!(x0 == x1 && y0 == y1))
        {
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 < dx) { err += dx; y0 += sy; }
            outCells.Add(new Vector2Int(x0, y0));
        }
    }

    void HeapPush(int id, float f)
    {
        fScore[id] = f;
        int i = heapCount++;
        heap[i] = id;
        heapPos[id] = i;
        inOpen[id] = true;
        HeapSiftUp(i);
    }

    int HeapPop()
    {
        int topId = heap[0];
        inOpen[topId] = false;

        int last = --heapCount;
        if (last >= 0)
        {
            heap[0] = heap[last];
            heapPos[heap[0]] = 0;
            HeapSiftDown(0);
        }
        return topId;
    }

    void HeapUpdate(int id, float newF)
    {
        int i = heapPos[id];
        float oldF = fScore[id];
        fScore[id] = newF;

        if (newF < oldF) HeapSiftUp(i);
        else HeapSiftDown(i);
    }

    void HeapSiftUp(int i)
    {
        while (i > 0)
        {
            int p = (i - 1) >> 1;
            if (fScore[heap[i]] >= fScore[heap[p]]) break;
            HeapSwap(i, p);
            i = p;
        }
    }

    void HeapSiftDown(int i)
    {
        while (true)
        {
            int l = i * 2 + 1;
            int r = l + 1;
            int smallest = i;

            if (l < heapCount && fScore[heap[l]] < fScore[heap[smallest]]) smallest = l;
            if (r < heapCount && fScore[heap[r]] < fScore[heap[smallest]]) smallest = r;
            if (smallest == i) break;
            HeapSwap(i, smallest);
            i = smallest;
        }
    }

    void HeapSwap(int a, int b)
    {
        int va = heap[a], vb = heap[b];
        heap[a] = vb; heap[b] = va;
        heapPos[va] = b; heapPos[vb] = a;
    }

    static readonly Vector2Int[] _neigh4 = new Vector2Int[]
    {
        new Vector2Int( 1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int( 0, 1),
        new Vector2Int( 0,-1),
    };

    static readonly Vector2Int[] _neigh8 = new Vector2Int[]
    {
        new Vector2Int( 1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int( 0, 1),
        new Vector2Int( 0,-1),
        new Vector2Int( 1, 1),
        new Vector2Int( 1,-1),
        new Vector2Int(-1, 1),
        new Vector2Int(-1,-1),
    };

    static readonly List<Vector3> _tmpPath = new List<Vector3>(64);
    static readonly List<Vector2Int> _cellCache = new List<Vector2Int>(64);
    static readonly List<Vector2Int> _tmpCells = new List<Vector2Int>(128);

    void SaveLastPath(List<Vector3> p)
    {
        _lastPath.Clear();
        if (p == null) return;
        _lastPath.AddRange(p);
    }

    void OnDrawGizmos()
    {
        if (!drawGizmos || _lastPath.Count == 0) return;
        Gizmos.color = gizmoColor;
        for (int i = 0; i < _lastPath.Count; i++)
        {
            Vector3 v = _lastPath[i];
            Gizmos.DrawSphere(v, gizmoSize);
            if (i + 1 < _lastPath.Count)
                Gizmos.DrawLine(v, _lastPath[i + 1]);
        }
    }

    void DecayTrafficGrid()
    {
        if (!useTrafficCost || traffic == null) return;
        float now = Time.time;
        float dt = Mathf.Max(0f, now - lastTrafficDecayTime);
        if (dt <= 0f) return;

        float dec = trafficDecayPerSecond * dt;
        var map = MultiFloorDynamicMapManager.Instance;
        for (int f = 0; f < map.floors; f++)
            for (int x = 0; x < map.width; x++)
                for (int y = 0; y < map.height; y++)
                {
                    float v = traffic[f, x, y] - dec;
                    traffic[f, x, y] = (v > 0f) ? v : 0f;
                }

        lastTrafficDecayTime = now;
    }
}