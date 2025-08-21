using UnityEngine;
using System;
using System.Collections.Generic;

public class MultiFloorDynamicMapManager : MonoBehaviour
{
    public static MultiFloorDynamicMapManager Instance;

    [Header("Mapa")]
    public int width = 21;
    public int height = 21;
    public int floors = 3;

    [Header("Escala")]
    public float cellSize = 1f;
    public float floorHeight = 4f;
    [Min(1f)] public float wallHeightMultiplier = 2f;
    [Min(0.25f)] public float minFloorHeadroom = 0.75f;

    [Header("Prefabs (Maze)")]
    public GameObject floorPrefab;         // piso
    public GameObject wallPrefab;          // muro normal
    public GameObject wallTorchPrefab;     // muro con antorcha integrada (lleva TorchSafeZone)

    [Header("Prefabs (Entidades)")]
    public GameObject batteryPrefab;
    public GameObject soulFragmentPrefab;
    public List<GameObject> enemyPrefabs;

    [Header("Antorchas por piso")]
    public int torchesMinPerFloor = 6;
    public int torchesMaxPerFloor = 6;

    [Header("Auto-scaling entities")]
    public bool autoScaleByMapSize = true;
    [Range(0f, 0.05f)] public float enemyDensity = 0.0075f;
    [Range(0f, 0.05f)] public float batteryDensity = 0.0060f;
    [Range(0f, 0.02f)] public float fragmentDensity = 0.0020f;
    public int minEnemies = 2, maxEnemies = 25;
    public int minBatteries = 1, maxBatteries = 20;
    public int minFragments = 0, maxFragments = 9;

    [Header("Dinámica")]
    public float regenerationInterval = 30f;
    public int safeRadiusCells = 5;
    public Transform player;

    [Header("Colocación de pickups")]
    public bool oneFragmentPerFloor = true;     // ← NUEVO: 1 fragmento por piso
    public float pickupLiftEpsilon = 0.02f;     // ← NUEVO: elevación mínima para evitar z-fighting

    public event Action OnMapUpdated;

    // --- Interno ---
    // 0: pasillo, 1: muro
    private int[,,] maze;
    private bool[,,] walkableGrid;
    private bool[,,] sanctuaryGrid;

    private GameObject[,,] wallObjects;
    private Transform[] floorContainers;
    private List<Vector2Int>[] freeCells;

    private List<GameObject>[] spawnedEntities;
    private List<GameObject>[] spawnedTorchWalls;

    private int[] torchesRemaining;
    private float wallHeight;
    private float regenTimer;
    private int currentFloor = -1;

    private System.Random rng = new System.Random();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        if (width % 2 == 0) width++;
        if (height % 2 == 0) height++;

        wallHeight = Mathf.Max(cellSize, cellSize * wallHeightMultiplier);
        float minFH = wallHeight + minFloorHeadroom;
        if (floorHeight < minFH)
        {
            Debug.LogWarning($"[Map] floorHeight {floorHeight} < requerido {minFH}. Ajustado.");
            floorHeight = minFH;
        }

        maze = new int[floors, width, height];
        walkableGrid = new bool[floors, width, height];
        sanctuaryGrid = new bool[floors, width, height];
        wallObjects = new GameObject[floors, width, height];

        freeCells = new List<Vector2Int>[floors];
        spawnedEntities = new List<GameObject>[floors];
        spawnedTorchWalls = new List<GameObject>[floors];
        floorContainers = new Transform[floors];
        torchesRemaining = new int[floors];

        for (int f = 0; f < floors; f++)
        {
            spawnedEntities[f] = new List<GameObject>();
            spawnedTorchWalls[f] = new List<GameObject>();

            int a = Mathf.Min(torchesMinPerFloor, torchesMaxPerFloor);
            int b = Mathf.Max(torchesMinPerFloor, torchesMaxPerFloor) + 1;
            torchesRemaining[f] = UnityEngine.Random.Range(a, b);
        }

        GenerateAllFloors();
        InstantiateAllFloors();
        UpdateWalkableGrid();
        UpdateFreeCells();

        // Spawns iniciales por piso
        for (int f = 0; f < floors; f++)
        {
            SpawnTorchWallsOnFloor(f);
            RespawnPickupsOnFloor(f);
            SpawnEnemiesOnFloor(f);
        }

        ChangeFloor(0);

        var inv = player ? player.GetComponent<PlayerInventory>() : null;
        if (inv != null) inv.totalLevels = floors;
    }

    void Update()
    {
        regenTimer += Time.deltaTime;
        if (regenTimer >= regenerationInterval)
        {
            regenTimer = 0f;
            PartialRegenerate();
        }

        int pf = GetPlayerFloor();
        if (pf != currentFloor) ChangeFloor(pf);
    }

    // =================== PISOS / GENERACIÓN ===================

    int GetPlayerFloor()
    {
        if (!player) return Mathf.Clamp(currentFloor, 0, floors - 1);
        return Mathf.Clamp(Mathf.RoundToInt(-player.position.y / floorHeight), 0, floors - 1);
    }

    public void GoToNextFloor()
    {
        int next = Mathf.Min(currentFloor + 1, floors - 1);
        ChangeFloor(next);
        Debug.Log("Cambio al piso: " + next);
    }

    void ChangeFloor(int newFloor)
    {
        if (currentFloor >= 0)
        {
            foreach (var go in spawnedEntities[currentFloor])
                if (go) Destroy(go);
            spawnedEntities[currentFloor].Clear();
        }
        currentFloor = newFloor;
    }

    void GenerateAllFloors()
    {
        for (int f = 0; f < floors; f++) GenerateMazeForFloor(f);

        // evitar duplicados exactos
        for (int a = 0; a < floors; a++)
            for (int b = a + 1; b < floors; b++)
                if (FloorsAreIdentical(a, b)) GenerateMazeForFloor(b);
    }

    void GenerateMazeForFloor(int f)
    {
        int[,] grid = new int[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                grid[x, y] = 1;

        CarveDFS(1, 1, grid, rng);

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                maze[f, x, y] = grid[x, y];
    }

    bool FloorsAreIdentical(int a, int b)
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (maze[a, x, y] != maze[b, x, y]) return false;
        return true;
    }

    void CarveDFS(int cx, int cy, int[,] grid, System.Random R)
    {
        grid[cx, cy] = 0;
        var dirs = new List<Vector2Int> { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        for (int i = 0; i < dirs.Count; i++)
        {
            int r = R.Next(i, dirs.Count);
            (dirs[i], dirs[r]) = (dirs[r], dirs[i]);
        }
        foreach (var d in dirs)
        {
            int nx = cx + d.x * 2, ny = cy + d.y * 2;
            if (nx > 0 && nx < width - 1 && ny > 0 && ny < height - 1 && grid[nx, ny] == 1)
            {
                grid[cx + d.x, cy + d.y] = 0;
                CarveDFS(nx, ny, grid, R);
            }
        }
    }

    void InstantiateAllFloors()
    {
        for (int f = 0; f < floors; f++)
        {
            float yOff = -f * floorHeight;

            var container = new GameObject($"Floor_{f}").transform;
            container.SetParent(transform);
            floorContainers[f] = container;

            freeCells[f] = new List<Vector2Int>();

            // Suelo: malla única escalada
            var floorGO = Instantiate(
                floorPrefab,
                new Vector3((width - 1) * cellSize / 2f, yOff, (height - 1) * cellSize / 2f),
                Quaternion.identity,
                container
            );
            SetupFloorSize(floorGO);

            // Muros y free-cells
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (maze[f, x, y] == 1)
                    {
                        var wall = Instantiate(
                            wallPrefab,
                            new Vector3(x * cellSize, yOff + wallHeight * 0.5f, y * cellSize),
                            Quaternion.identity,
                            container
                        );
                        wall.transform.localScale = new Vector3(cellSize, wallHeight, cellSize);
                        wallObjects[f, x, y] = wall;
                    }
                    else
                    {
                        freeCells[f].Add(new Vector2Int(x, y));
                    }
                }
            }
        }
        UpdateWalkableGrid();
        OnMapUpdated?.Invoke();
    }

    void SetupFloorSize(GameObject floorGO)
    {
        if (!floorGO) return;
        var mf = floorGO.GetComponent<MeshFilter>();
        float sx = width * cellSize;
        float sz = height * cellSize;

        if (mf && mf.sharedMesh && mf.sharedMesh.name.ToLowerInvariant().Contains("plane"))
            floorGO.transform.localScale = new Vector3(sx * 0.1f, 1f, sz * 0.1f); // Unity plane 10x10
        else
            floorGO.transform.localScale = new Vector3(sx, 1f, sz);
    }

    // =================== REGENERACIÓN PARCIAL ===================

    void PartialRegenerate()
    {
        int pf = GetPlayerFloor();

        Vector2Int playerCell = WorldToCell(player ? player.position : Vector3.zero);
        playerCell = ClampToBounds(playerCell);
        Vector2Int safeSeed = FindNearestWalkableCell(pf, playerCell);

        bool[,] safe = new bool[width, height];
        MarkSafeRegionWalkable(pf, safeSeed.x, safeSeed.y, safe);

        for (int f = 0; f < floors; f++)
        {
            int[,] buf = new int[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    buf[x, y] = 1;

            CarveDFS(1, 1, buf, rng);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (f == pf && safe[x, y]) continue;

                    bool wasWall = maze[f, x, y] == 1;
                    bool willWall = buf[x, y] == 1;

                    if (wasWall != willWall)
                    {
                        float yPos = -f * floorHeight + wallHeight * 0.5f;
                        Vector3 pos = new Vector3(x * cellSize, yPos, y * cellSize);

                        if (willWall)
                        {
                            if (wallObjects[f, x, y] == null)
                            {
                                var w = Instantiate(wallPrefab, pos, Quaternion.identity, floorContainers[f]);
                                w.transform.localScale = new Vector3(cellSize, wallHeight, cellSize);
                                wallObjects[f, x, y] = w;
                            }
                        }
                        else if (wallObjects[f, x, y] != null)
                        {
                            Destroy(wallObjects[f, x, y]);
                            wallObjects[f, x, y] = null;
                        }

                        maze[f, x, y] = buf[x, y];
                    }
                }
            }

            torchesRemaining[f] = Mathf.Max(0, torchesRemaining[f] - 1);
            ClearTorchWalls(f);
            SpawnTorchWallsOnFloor(f);
            RespawnPickupsOnFloor(f);
            SpawnEnemiesOnFloor(f);
        }

        UpdateWalkableGrid();
        UpdateFreeCells();
        OnMapUpdated?.Invoke();
    }

    void MarkSafeRegionWalkable(int floor, int sx, int sy, bool[,] safe)
    {
        if (!Inside(new Vector2Int(sx, sy))) return;
        if (maze[floor, sx, sy] == 1) return;

        var q = new Queue<Vector2Int>();
        var next = new Queue<Vector2Int>();
        int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };

        safe[sx, sy] = true;
        q.Enqueue(new Vector2Int(sx, sy));
        int depth = 0;

        while (q.Count > 0 && depth < safeRadiusCells)
        {
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    int nx = c.x + dx[i], ny = c.y + dy[i];
                    if (!Inside(new Vector2Int(nx, ny))) continue;
                    if (maze[floor, nx, ny] != 0) continue;
                    if (safe[nx, ny]) continue;

                    safe[nx, ny] = true;
                    next.Enqueue(new Vector2Int(nx, ny));
                }
            }
            depth++;
            (q, next) = (next, q);
        }
    }

    Vector2Int FindNearestWalkableCell(int floor, Vector2Int from)
    {
        from = ClampToBounds(from);
        if (maze[floor, from.x, from.y] == 0) return from;

        var visited = new bool[width, height];
        var q = new Queue<Vector2Int>();
        visited[from.x, from.y] = true; q.Enqueue(from);

        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        while (q.Count > 0)
        {
            var c = q.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                int nx = c.x + dx[i], ny = c.y + dy[i];
                if (!Inside(new Vector2Int(nx, ny)) || visited[nx, ny]) continue;
                visited[nx, ny] = true;
                if (maze[floor, nx, ny] == 0) return new Vector2Int(nx, ny);
                q.Enqueue(new Vector2Int(nx, ny));
            }
        }
        return ClampToBounds(from);
    }

    // =================== SPAWNS ===================

    void ClearTorchWalls(int floor)
    {
        if (spawnedTorchWalls == null) return;
        foreach (var t in spawnedTorchWalls[floor])
            if (t) Destroy(t);
        spawnedTorchWalls[floor].Clear();

        // limpiar santuarios; las antorchas activas los volverán a marcar cuando enciendan
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                sanctuaryGrid[floor, x, y] = false;
    }

    void SpawnTorchWallsOnFloor(int f)
    {
        if (wallTorchPrefab == null) return;

        int desired = Mathf.Max(0, torchesRemaining[f]);
        if (desired == 0) return;

        var candidates = new List<(Vector2Int wall, Vector2Int corridor)>();
        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        // paredes con 1 pasillo vecino
        for (int x = 1; x < width - 1; x++)
        {
            for (int y = 1; y < height - 1; y++)
            {
                if (maze[f, x, y] != 1) continue;
                int corridorCount = 0; Vector2Int cNeighbor = default;
                for (int i = 0; i < 4; i++)
                {
                    int nx = x + dx[i], ny = y + dy[i];
                    if (maze[f, nx, ny] == 0) { corridorCount++; cNeighbor = new Vector2Int(nx, ny); }
                }
                if (corridorCount == 1)
                    candidates.Add((new Vector2Int(x, y), cNeighbor));
            }
        }

        // barajar
        for (int i = 0; i < candidates.Count; i++)
        {
            int r = rng.Next(i, candidates.Count);
            (candidates[i], candidates[r]) = (candidates[r], candidates[i]);
        }

        int placed = 0;
        float yOff = -f * floorHeight;

        foreach (var c in candidates)
        {
            if (placed >= desired) break;

            var wallCell = c.wall;
            var corridorCell = c.corridor;

            if (wallObjects[f, wallCell.x, wallCell.y] != null)
            {
                Destroy(wallObjects[f, wallCell.x, wallCell.y]);
                wallObjects[f, wallCell.x, wallCell.y] = null;
            }

            Vector3 wallCenter = new Vector3(wallCell.x * cellSize, yOff + wallHeight * 0.5f, wallCell.y * cellSize);
            Vector3 corrCenter = new Vector3(corridorCell.x * cellSize, yOff + wallHeight * 0.5f, corridorCell.y * cellSize);
            Vector3 n = (corrCenter - wallCenter); n.y = 0f; n.Normalize();
            Quaternion rot = Quaternion.LookRotation(n, Vector3.up);

            var torchWall = Instantiate(wallTorchPrefab, wallCenter, rot, floorContainers[f]);
            torchWall.transform.localScale = new Vector3(cellSize, wallHeight, cellSize);
            wallObjects[f, wallCell.x, wallCell.y] = torchWall;
            spawnedTorchWalls[f].Add(torchWall);

            var safe = torchWall.GetComponentInChildren<TorchSafeZone>(true);
            if (safe != null)
            {
                safe.floorIndex = f;
                safe.corridorCell = corridorCell;
            }

            placed++;
        }
    }

    void RespawnPickupsOnFloor(int f)
    {
        if (spawnedEntities[f] == null) spawnedEntities[f] = new List<GameObject>();
        foreach (var go in spawnedEntities[f]) if (go) Destroy(go);
        spawnedEntities[f].Clear();

        var cells = GetFreeCells(f);
        if (cells.Count == 0) return;

        Vector2Int playerCell = WorldToCell(player ? player.position : Vector3.zero);
        bool playerOnThis = (GetPlayerFloor() == f);

        cells.RemoveAll(c =>
            sanctuaryGrid[f, c.x, c.y] ||
            (playerOnThis && (Mathf.Abs(c.x - playerCell.x) + Mathf.Abs(c.y - playerCell.y) <= 2))
        );

        // ---- BATERÍAS (por densidad/escala) ----
        int batteries = autoScaleByMapSize ? ScaledCount(batteryDensity, minBatteries, maxBatteries) : minBatteries;

        for (int i = 0; i < batteries && cells.Count > 0 && batteryPrefab; i++)
        {
            int idx = rng.Next(cells.Count);
            var cell = cells[idx];

            var go = Instantiate(batteryPrefab, CellCenterToWorld(cell, f), Quaternion.identity, floorContainers[f]);
            SnapToFloorByRendererHeight(go.transform, f); // ← altura real de Renderer
            spawnedEntities[f].Add(go);

            cells.RemoveAt(idx);
        }

        // ---- FRAGMENTO DE ALMA ----
        int fragments = oneFragmentPerFloor ? 1 :
            (autoScaleByMapSize ? ScaledCount(fragmentDensity, minFragments, maxFragments) : minFragments);

        for (int i = 0; i < fragments && cells.Count > 0 && soulFragmentPrefab; i++)
        {
            int idx = rng.Next(cells.Count);
            var cell = cells[idx];

            var go = Instantiate(soulFragmentPrefab, CellCenterToWorld(cell, f), Quaternion.identity, floorContainers[f]);
            SnapToFloorByRendererHeight(go.transform, f); // ← altura real de Renderer
            spawnedEntities[f].Add(go);

            cells.RemoveAt(idx);
        }
    }

    void SpawnEnemiesOnFloor(int f)
    {
        if (enemyPrefabs == null || enemyPrefabs.Count == 0) return;

        var cells = GetFreeCells(f);
        if (cells.Count == 0) return;

        Vector2Int playerCell = WorldToCell(player ? player.position : Vector3.zero);
        bool playerOnThis = (GetPlayerFloor() == f);

        cells.RemoveAll(c =>
            sanctuaryGrid[f, c.x, c.y] ||
            (playerOnThis && (Mathf.Abs(c.x - playerCell.x) + Mathf.Abs(c.y - playerCell.y) <= 4))
        );

        int enemies = autoScaleByMapSize ? ScaledCount(enemyDensity, minEnemies, maxEnemies) : minEnemies;

        for (int i = 0; i < enemies && cells.Count > 0; i++)
        {
            var prefab = enemyPrefabs[rng.Next(enemyPrefabs.Count)];
            int idx = rng.Next(cells.Count);
            var cell = cells[idx];
            var e = Instantiate(prefab, CellCenterToWorld(cell, f), Quaternion.identity, floorContainers[f]);

            // Usar altura por Renderer para que no queden hundidos
            SnapToFloorByRendererHeight(e.transform, f);

            spawnedEntities[f].Add(e);
            cells.RemoveAt(idx);
        }
    }

    // =================== GRIDS / UTILS ===================

    void UpdateWalkableGrid()
    {
        for (int f = 0; f < floors; f++)
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    walkableGrid[f, x, y] = (maze[f, x, y] == 0);
    }

    void UpdateFreeCells()
    {
        for (int f = 0; f < floors; f++)
        {
            if (freeCells[f] == null) freeCells[f] = new List<Vector2Int>();
            else freeCells[f].Clear();

            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    if (maze[f, x, y] == 0) freeCells[f].Add(new Vector2Int(x, y));
        }
    }

    public bool[,,] GetWalkableGrid()
    {
        var copy = new bool[floors, width, height];
        Array.Copy(walkableGrid, copy, walkableGrid.Length);
        return copy;
    }

    public bool[,,] GetSanctuaryGrid()
    {
        var copy = new bool[floors, width, height];
        Array.Copy(sanctuaryGrid, copy, sanctuaryGrid.Length);
        return copy;
    }

    public List<Vector2Int> GetFreeCells(int floor)
    {
        return new List<Vector2Int>(freeCells[floor]);
    }

    public Vector3 CellToWorld(Vector2Int cell, int floor)
    {
        return new Vector3(cell.x * cellSize, -floor * floorHeight, cell.y * cellSize);
    }

    // Centro lógico (usa las coordenadas de celda; si tus prefabs de piso están centrados en esquinas, funciona bien)
    public Vector3 CellCenterToWorld(Vector2Int cell, int floor)
    {
        return new Vector3(cell.x * cellSize, -floor * floorHeight, cell.y * cellSize);
    }

    public Vector2Int WorldToCell(Vector3 worldPos)
    {
        int x = Mathf.RoundToInt(worldPos.x / cellSize);
        int y = Mathf.RoundToInt(worldPos.z / cellSize);
        return new Vector2Int(x, y);
    }

    public bool IsWalkable(int floor, int x, int y)
    {
        if (floor < 0 || floor >= floors || x < 0 || x >= width || y < 0 || y >= height) return false;
        return walkableGrid[floor, x, y];
    }

    public bool IsWalkable(int floor, Vector2Int cell) => IsWalkable(floor, cell.x, cell.y);

    public bool IsSanctuary(int floor, int x, int y)
    {
        if (floor < 0 || floor >= floors || x < 0 || x >= width || y < 0 || y >= height) return false;
        return sanctuaryGrid[floor, x, y];
    }
    public bool IsSanctuary(int floor, Vector2Int cell) => IsSanctuary(floor, cell.x, cell.y);

    public int GetCurrentFloor() => currentFloor;

    public void SetSanctuaryCell(int floor, Vector2Int cell, bool value)
    {
        if (floor < 0 || floor >= floors) return;
        if (!Inside(cell)) return;
        sanctuaryGrid[floor, cell.x, cell.y] = value;
        OnMapUpdated?.Invoke();
    }

    public bool Inside(Vector2Int c) => (c.x >= 0 && c.x < width && c.y >= 0 && c.y < height);
    Vector2Int ClampToBounds(Vector2Int c) =>
        new Vector2Int(Mathf.Clamp(c.x, 0, width - 1), Mathf.Clamp(c.y, 0, height - 1));

    // Ajuste de Y simple (legacy)
    void SnapToFloorY(Transform t, int floor, float lift = 0.02f)
    {
        if (!t) return;
        var p = t.position;
        p.y = -floor * floorHeight + lift;
        t.position = p;
    }

    // NUEVO: coloca por altura real (Renderer.bounds) para no atravesar el piso
    void SnapToFloorByRendererHeight(Transform t, int floor)
    {
        if (!t) return;

        float baseY = -floor * floorHeight;

        // Intentar con Renderer
        Renderer rend = t.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            float h = rend.bounds.size.y;
            t.position = new Vector3(t.position.x, baseY + (h * 0.5f) + pickupLiftEpsilon, t.position.z);
            return;
        }

        // Si no hay renderer, intentar con Collider
        Collider col = t.GetComponentInChildren<Collider>();
        if (col != null)
        {
            float h = col.bounds.size.y;
            t.position = new Vector3(t.position.x, baseY + (h * 0.5f) + pickupLiftEpsilon, t.position.z);
            return;
        }

        // Fallback
        t.position = new Vector3(t.position.x, baseY + pickupLiftEpsilon, t.position.z);
    }

    int Area() => width * height;
    int ScaledCount(float density, int min, int max)
        => Mathf.Clamp(Mathf.RoundToInt(Area() * density), min, max);

    // Cuenta baterías activas (para lógica de TorchSafeZone)
    public int CountBatteriesAllFloors()
    {
        var all = FindObjectsOfType<BatteryPickup>(includeInactive: false);
        return all?.Length ?? 0;
    }
}
