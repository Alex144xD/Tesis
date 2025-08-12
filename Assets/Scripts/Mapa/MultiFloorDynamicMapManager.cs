using UnityEngine;
using System;
using System.Collections.Generic;

public class MultiFloorDynamicMapManager : MonoBehaviour
{
    public static MultiFloorDynamicMapManager Instance;

    [Header("Configuración de mapa")]
    public int width = 21;
    public int height = 21;
    public int floors = 3;
    public float cellSize = 1f;
    public float floorHeight = 4f;

    [Header("Prefabs Maze")]
    public GameObject wallPrefab;
    public GameObject floorPrefab; // Quad o Plane

    [Header("Prefabs Entidades")]
    public GameObject batteryPrefab;
    public GameObject soulFragmentPrefab;
    public GameObject doorPrefab;
    public List<GameObject> enemyPrefabs;

    [Header("Antorchas (fijo por piso)")]
    public GameObject torchPrefab;
    [Tooltip("Al iniciar cada piso: número aleatorio de antorchas entre min y max (incl.).")]
    public int torchesMinPerFloor = 1;
    public int torchesMaxPerFloor = 6;
    [Range(0.0f, 0.49f)] public float torchWallInset = 0.04f;  
    [Range(0.0f, 1.0f)] public float torchHeightFactor = 0.70f; 

    [Header("Auto-scaling por tamaño de mapa")]
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

    public event Action OnMapUpdated;

    // --- Internos ---
    private int[,,] maze;              
    private bool[,,] walkableGrid;
    private bool[,,] sanctuaryGrid;      

    private GameObject[,,] wallObjects;
    private GameObject[] floorContainers;
    private List<Vector2Int>[] freeCells;

    private List<GameObject>[] spawnedEntities;  
    private List<GameObject>[] spawnedTorches;

    private int[] torchesRemaining;     

    private float regenTimer;
    private int currentFloor = -1;
    private System.Random _rng = new System.Random();

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void Start()
    {
        if (width % 2 == 0) width++;
        if (height % 2 == 0) height++;

        maze = new int[floors, width, height];
        wallObjects = new GameObject[floors, width, height];
        walkableGrid = new bool[floors, width, height];
        sanctuaryGrid = new bool[floors, width, height];

        freeCells = new List<Vector2Int>[floors];
        spawnedEntities = new List<GameObject>[floors];
        spawnedTorches = new List<GameObject>[floors];
        floorContainers = new GameObject[floors];
        torchesRemaining = new int[floors];

        for (int f = 0; f < floors; f++)
        {
            spawnedEntities[f] = new List<GameObject>();
            spawnedTorches[f] = new List<GameObject>();
            int a = Mathf.Min(torchesMinPerFloor, torchesMaxPerFloor);
            int b = Mathf.Max(torchesMinPerFloor, torchesMaxPerFloor) + 1;
            torchesRemaining[f] = UnityEngine.Random.Range(a, b);
        }

        GenerateAllFloors();
        InstantiateAllFloors();
        UpdateWalkableGrid();
        UpdateFreeCells();

      
        for (int f = 0; f < floors; f++)
        {
            SpawnTorchesOnFloor(f);
            RespawnPickupsOnFloor(f);
            SpawnEnemiesOnFloor(f);
        }

        ChangeFloor(0);

        var inventory = player.GetComponent<PlayerInventory>();
        if (inventory != null) inventory.totalLevels = floors;
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
        if (pf != currentFloor)
            ChangeFloor(pf);
    }

   
    int GetPlayerFloor()
    {
        return Mathf.Clamp(Mathf.RoundToInt(-player.position.y / floorHeight), 0, floors - 1);
    }

    public void GoToNextFloor()
    {
        int nextFloor = Mathf.Min(currentFloor + 1, floors - 1);
        ChangeFloor(nextFloor);
        Debug.Log("Cambio al piso: " + nextFloor);
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

    void InstantiateAllFloors()
    {
        for (int f = 0; f < floors; f++)
        {
            float yOff = -f * floorHeight;
            freeCells[f] = new List<Vector2Int>();

            GameObject floorContainer = new GameObject($"Floor_{f}");
            floorContainer.transform.parent = transform;
            floorContainers[f] = floorContainer;

            // Suelo dimensionado al laberinto
            var floorGO = Instantiate(
                floorPrefab,
                new Vector3((width - 1) * cellSize / 2f, yOff, (height - 1) * cellSize / 2f),
                Quaternion.identity,
                floorContainer.transform
            );
            SetupFloorSize(floorGO);

            // Muros
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (maze[f, x, y] == 1)
                    {
                        var wall = Instantiate(
                            wallPrefab,
                            new Vector3(x * cellSize, yOff + cellSize / 2f, y * cellSize),
                            Quaternion.identity,
                            floorContainer.transform
                        );
                        wall.transform.localScale = Vector3.one * cellSize;
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
            floorGO.transform.localScale = new Vector3(sx * 0.1f, 1f, sz * 0.1f); 
        else
            floorGO.transform.localScale = new Vector3(sx, 1f, sz);              
    }

  
    void GenerateAllFloors()
    {
        for (int f = 0; f < floors; f++) GenerateMazeForFloor(f);

        for (int a = 0; a < floors; a++)
            for (int b = a + 1; b < floors; b++)
                if (FloorsAreIdentical(a, b)) GenerateMazeForFloor(b);
    }

    void GenerateMazeForFloor(int f)
    {
        int[,] grid = new int[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++) grid[x, y] = 1;

        CarveDFS(1, 1, grid, _rng);
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++) maze[f, x, y] = grid[x, y];
    }

    bool FloorsAreIdentical(int a, int b)
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (maze[a, x, y] != maze[b, x, y]) return false;
        return true;
    }

    void CarveDFS(int cx, int cy, int[,] grid, System.Random rng)
    {
        grid[cx, cy] = 0;
        var dirs = new List<Vector2Int> { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        for (int i = 0; i < dirs.Count; i++)
        {
            int r = rng.Next(i, dirs.Count);
            (dirs[i], dirs[r]) = (dirs[r], dirs[i]);
        }
        foreach (var d in dirs)
        {
            int nx = cx + d.x * 2, ny = cy + d.y * 2;
            if (nx > 0 && nx < width - 1 && ny > 0 && ny < height - 1 && grid[nx, ny] == 1)
            {
                grid[cx + d.x, cy + d.y] = 0;
                CarveDFS(nx, ny, grid, rng);
            }
        }
    }

    
    void PartialRegenerate()
    {
        int pf = GetPlayerFloor();
        int px = Mathf.RoundToInt(player.position.x / cellSize);
        int py = Mathf.RoundToInt(player.position.z / cellSize);

        for (int f = 0; f < floors; f++)
        {
            bool[,] safe = new bool[width, height];
            if (f == pf) MarkSafeRegion(px, py, safe);

            int[,] buf = new int[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++) buf[x, y] = 1;

            CarveDFS(1, 1, buf, _rng);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (f == pf && safe[x, y]) continue;

                    bool wasWall = maze[f, x, y] == 1;
                    bool willBeWall = buf[x, y] == 1;

                    if (wasWall != willBeWall)
                    {
                        float yPos = -f * floorHeight + cellSize / 2f;
                        Vector3 pos = new Vector3(x * cellSize, yPos, y * cellSize);

                        if (willBeWall)
                        {
                            if (wallObjects[f, x, y] == null)
                            {
                                var w = Instantiate(wallPrefab, pos, Quaternion.identity, floorContainers[f].transform);
                                w.transform.localScale = Vector3.one * cellSize;
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

            // Recolocar todo
            ClearTorches(f);
            SpawnTorchesOnFloor(f);
            RespawnPickupsOnFloor(f);
            SpawnEnemiesOnFloor(f);
        }

        UpdateWalkableGrid();
        UpdateFreeCells();
        OnMapUpdated?.Invoke();
    }

    void MarkSafeRegion(int sx, int sy, bool[,] safe)
    {
        var q = new Queue<Vector2Int>();
        var next = new Queue<Vector2Int>();
        int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };

        sx = Mathf.Clamp(sx, 0, width - 1);
        sy = Mathf.Clamp(sy, 0, height - 1);

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
                    if (nx >= 0 && nx < width && ny >= 0 && ny < height && !safe[nx, ny])
                    {
                        safe[nx, ny] = true;
                        next.Enqueue(new Vector2Int(nx, ny));
                    }
                }
            }
            depth++;
            (q, next) = (next, q);
        }
    }

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

    void ClearTorches(int floor)
    {
        if (spawnedTorches == null) return;
        foreach (var t in spawnedTorches[floor])
            if (t) Destroy(t);
        spawnedTorches[floor].Clear();

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                sanctuaryGrid[floor, x, y] = false;
    }

    
    void SpawnTorchesOnFloor(int f)
    {
        if (torchPrefab == null) return;

        int desired = Mathf.Max(0, torchesRemaining[f]);
        if (desired == 0) return;

        var candidates = new List<(Vector2Int wall, Vector2Int corridor)>();
        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        for (int x = 1; x < width - 1; x++)
            for (int y = 1; y < height - 1; y++)
            {
                if (maze[f, x, y] != 1) continue;
                int corridorCount = 0; Vector2Int cNeighbor = default;
                for (int i = 0; i < 4; i++)
                {
                    int nx = x + dx[i], ny = y + dy[i];
                    if (maze[f, nx, ny] == 0) { corridorCount++; cNeighbor = new Vector2Int(nx, ny); }
                }
                if (corridorCount == 1) candidates.Add((new Vector2Int(x, y), cNeighbor));
            }

        // barajar
        for (int i = 0; i < candidates.Count; i++)
        {
            int r = _rng.Next(i, candidates.Count);
            (candidates[i], candidates[r]) = (candidates[r], candidates[i]);
        }

        int placed = 0;
        float yOff = -f * floorHeight;

        foreach (var c in candidates)
        {
            if (placed >= desired) break;

            var wall = c.wall;
            var corr = c.corridor;

            Vector3 wallCenter = new Vector3(wall.x * cellSize, yOff + cellSize * 0.5f, wall.y * cellSize);
            Vector3 corrCenter = new Vector3(corr.x * cellSize, yOff + cellSize * 0.5f, corr.y * cellSize);

            Vector3 n = (corrCenter - wallCenter); n.y = 0f; n.Normalize();

            float fromCenter = (cellSize * 0.5f) - torchWallInset;
            Vector3 torchPos = wallCenter + n * fromCenter;
            torchPos.y = yOff + cellSize * torchHeightFactor;

            var t = Instantiate(torchPrefab, torchPos, Quaternion.LookRotation(n, Vector3.up), floorContainers[f].transform);
            spawnedTorches[f].Add(t);

            sanctuaryGrid[f, corr.x, corr.y] = true;
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

        Vector2Int playerCell = WorldToCell(player.position);
        bool playerOnThis = (GetPlayerFloor() == f);

        cells.RemoveAll(c =>
            sanctuaryGrid[f, c.x, c.y] ||
            (playerOnThis && (Mathf.Abs(c.x - playerCell.x) + Mathf.Abs(c.y - playerCell.y) <= 2))
        );

        int batteries = autoScaleByMapSize
            ? ScaledCount(batteryDensity, minBatteries, maxBatteries)
            : minBatteries;

        int fragments = autoScaleByMapSize
            ? ScaledCount(fragmentDensity, minFragments, maxFragments)
            : minFragments;

        for (int i = 0; i < batteries && cells.Count > 0 && batteryPrefab; i++)
        {
            int idx = _rng.Next(cells.Count);
            var pos = CellToWorld(cells[idx], f);
            spawnedEntities[f].Add(Instantiate(batteryPrefab, pos, Quaternion.identity, floorContainers[f].transform));
            cells.RemoveAt(idx);
        }

        for (int i = 0; i < fragments && cells.Count > 0 && soulFragmentPrefab; i++)
        {
            int idx = _rng.Next(cells.Count);
            var pos = CellToWorld(cells[idx], f);
            spawnedEntities[f].Add(Instantiate(soulFragmentPrefab, pos, Quaternion.identity, floorContainers[f].transform));
            cells.RemoveAt(idx);
        }
    }


    void SpawnEnemiesOnFloor(int f)
    {
        if (enemyPrefabs == null || enemyPrefabs.Count == 0) return;

        var cells = GetFreeCells(f);
        if (cells.Count == 0) return;

        Vector2Int playerCell = WorldToCell(player.position);
        bool playerOnThis = (GetPlayerFloor() == f);

        cells.RemoveAll(c =>
            sanctuaryGrid[f, c.x, c.y] ||
            (playerOnThis && (Mathf.Abs(c.x - playerCell.x) + Mathf.Abs(c.y - playerCell.y) <= 4))
        );

        int enemies = autoScaleByMapSize
            ? ScaledCount(enemyDensity, minEnemies, maxEnemies)
            : minEnemies;

        for (int i = 0; i < enemies && cells.Count > 0; i++)
        {
            var prefab = enemyPrefabs[_rng.Next(enemyPrefabs.Count)];
            int idx = _rng.Next(cells.Count);
            var pos = CellToWorld(cells[idx], f);
            var e = Instantiate(prefab, pos, Quaternion.identity, floorContainers[f].transform);
            spawnedEntities[f].Add(e);
            cells.RemoveAt(idx);
        }
    }

    int Area() => width * height;
    int ScaledCount(float density, int min, int max)
        => Mathf.Clamp(Mathf.RoundToInt(Area() * density), min, max);


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
        return new Vector3(cell.x * cellSize, -floor * floorHeight + 0.1f, cell.y * cellSize);
    }

    public Vector2Int WorldToCell(Vector3 worldPos)
    {
        int x = Mathf.FloorToInt(worldPos.x / cellSize);
        int y = Mathf.FloorToInt(worldPos.z / cellSize);
        return new Vector2Int(x, y);
    }

    public bool IsWalkable(int floor, int x, int y)
    {
        if (floor < 0 || floor >= floors || x < 0 || x >= width || y < 0 || y >= height)
            return false;
        return walkableGrid[floor, x, y];
    }

    public int GetCurrentFloor() => currentFloor;
}
