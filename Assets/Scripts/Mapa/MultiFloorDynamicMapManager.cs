using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class MultiFloorDynamicMapManager : MonoBehaviour
{
    public static MultiFloorDynamicMapManager Instance;

  
    [Header("Mapa")]
    public int width = 21;
    public int height = 21;
    [Tooltip("Se mantiene por compatibilidad. Se usa el piso 0.")]
    public int floors = 1;

    [Header("Escala")]
    public float cellSize = 1f;
    public float floorHeight = 4f;
    [Min(1f)] public float wallHeightMultiplier = 2f;
    [Min(0.25f)] public float minFloorHeadroom = 0.75f;

    [Header("Prefabs (Maze)")]
    public GameObject floorPrefab;
    public GameObject wallPrefab;
    public GameObject wallTorchPrefab; 

    [Header("Prefabs (Entidades)")]
    public List<GameObject> batteryPrefabs;
    public GameObject soulFragmentPrefab;

   
    public List<GameObject> enemyPrefabs;

    [Header("Jugador")]
    public Transform player;
    public GameObject playerPrefab;
    public bool spawnPlayerIfMissing = true;

    [Header("Auto-scaling entities")]
    public bool autoScaleByMapSize = true;
    [Range(0f, 0.05f)] public float enemyDensity = 0.0075f;   
    [Range(0f, 0.05f)] public float batteryDensity = 0.0060f;
    [Range(0f, 0.02f)] public float fragmentDensity = 0.0f; 
    public int minEnemies = 2, maxEnemies = 25;              
    public int minBatteries = 1, maxBatteries = 20;

    [Header("Dinámica")]
    public float regenerationInterval = 30f;
    public int safeRadiusCells = 5;

    [Header("Colocación de pickups")]
    public bool oneFragmentPerFloor = true; 
    public float pickupLiftEpsilon = 0.02f;


    [Header("Fragmentos Secuenciales")]
    public bool useSequentialFragments = true;
    [Min(1)] public int targetFragments = 5;
    public int fragmentsCollected = 0;


    [Min(6)] public int fragmentMinRing = 8;
    [Min(6)] public int fragmentMaxRing = 18;

    [Header("Antorchas decorativas")]
    public int decorativeTorchesNearPath = 3;
    public int torchPathSkip = 5;

    [Header("Cambio de Mapa (sonido)")]
    public AudioClip bellClip;
    [Range(0f, 1f)] public float bellVolume = 0.9f;

   
    [Header("Oleadas Progresivas (por campana)")]
    public bool progressiveEnemyWaves = true;
    [Tooltip("Si usas oleadas, deja esto en false para empezar sin enemigos.")]
    public bool spawnEnemiesAtStart = false;

    [Header("Prefabs por tipo de enemigo (asignar en Inspector)")]
    public List<GameObject> enemyType1Prefabs; 
    public List<GameObject> enemyType2Prefabs; 
    public List<GameObject> enemyType3Prefabs; 

    [Tooltip("Forzar límite de 1 a 3 enemigos en cualquier spawn/oleada.")]
    public bool limitEnemiesToOneToThree = true;

    private int regenCount = 0; 
    private List<GameObject>[] spawnedEnemies; 

   
    private AudioSource sfx;

 
    private int[,,] maze;
    private bool[,,] walkableGrid;
    private GameObject[,,] wallObjects;
    private Transform[] floorContainers;
    private List<Vector2Int>[] freeCells;
    private List<GameObject>[] spawnedEntities;
    private List<GameObject>[] spawnedTorchWalls;

    private float wallHeight;
    private float regenTimer;
    private int currentFloor = -1;

   
    private Vector2Int anchorA;
    private Vector2Int anchorB;
    private bool hasActiveFragment = false;
    private GameObject activeFragmentGO = null;

    public event Action OnMapUpdated;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        if (width % 2 == 0) width++;
        if (height % 2 == 0) height++;
        floors = Mathf.Max(1, floors);

        wallHeight = Mathf.Max(cellSize, cellSize * wallHeightMultiplier);
        float minFH = wallHeight + minFloorHeadroom;
        if (floorHeight < minFH)
        {
            Debug.LogWarning($"[Map] floorHeight {floorHeight} < requerido {minFH}. Ajustado.");
            floorHeight = minFH;
        }

        sfx = GetComponent<AudioSource>();
        if (!sfx) sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 0f;

        AllocGrids(width, height, floors);

        GenerateAllFloors();
        InstantiateAllFloors();
        UpdateWalkableGrid();
        UpdateFreeCells();

        for (int f = 0; f < floors; f++)
        {
            SpawnTorchWallsOnFloor(f, false);
            RespawnPickupsOnFloor(f, placeFragment: !useSequentialFragments);

          
            if (!progressiveEnemyWaves || spawnEnemiesAtStart)
                SpawnEnemiesOnFloor(f);
        }

        ChangeFloor(0);

        if (useSequentialFragments)
        {
            BeginRun();
        }


        if (progressiveEnemyWaves && spawnEnemiesAtStart)
        {
            regenCount = 1; 
            HandleWaveAfterBell();
        }
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


    public void SetTargetFragments(int n)
    {
        targetFragments = Mathf.Max(1, n);
        if (useSequentialFragments)
            fragmentsCollected = Mathf.Clamp(fragmentsCollected, 0, targetFragments);
    }

    public void SetGenerationTuning(float enemyDens, float batteryDens, int fragMinRing, int fragMaxRing)
    {
        enemyDensity = Mathf.Clamp(enemyDens, 0f, 0.05f);
        batteryDensity = Mathf.Clamp(batteryDens, 0f, 0.05f);
        fragmentMinRing = Mathf.Max(1, fragMinRing);
        fragmentMaxRing = Mathf.Max(fragmentMinRing, fragMaxRing);
    }
   
    public void BeginRun()
    {
        int f = 0;

        anchorA = ChooseStartCellNearEdge(f);

        if (!player)
        {
            if (spawnPlayerIfMissing && playerPrefab)
            {
                var go = Instantiate(playerPrefab, CellCenterToWorld(anchorA, f), Quaternion.identity);
                player = go.transform;
                SnapToFloorY(player, f, 0.02f);
            }
            else
            {
                var maybe = GameObject.FindGameObjectWithTag("Player");
                if (maybe) player = maybe.transform;
            }
        }

        if (player) TeleportPlayerToCell(player, anchorA, f);

        anchorB = ChooseGoalCellOppositeSide(f, anchorA);
        PlaceActiveFragmentAt(f, anchorB);

        EnsureConnectivityBetween(f, anchorA, anchorB, openRatio: 1.0f);
        DecoratePathToGoal(f, anchorA, anchorB);

        OnMapUpdated?.Invoke();
    }

    public void OnFragmentCollected()
    {
        if (!useSequentialFragments) return;

      
        if (activeFragmentGO)
        {
            var prevCt = activeFragmentGO.GetComponent<CompassTarget>();
            if (prevCt) prevCt.isPrimary = false;
        }

        fragmentsCollected = Mathf.Clamp(fragmentsCollected + 1, 0, targetFragments);
        TrySpawnExtraBatteryIfUnderCap();

        if (fragmentsCollected >= targetFragments)
        {
            hasActiveFragment = false;
            if (activeFragmentGO) { Destroy(activeFragmentGO); activeFragmentGO = null; }
            Debug.Log("[Map] ¡Todos los fragmentos recolectados!");
            return;
        }

        int f = 0;
        anchorA = FindNearestWalkableCell(f, ClampToBounds(WorldToCell(player ? player.position : Vector3.zero)));
        anchorB = ChooseGoalCellOppositeSide(f, anchorA);
        PlaceActiveFragmentAt(f, anchorB);

        PartialRegenerate();
        EnsureConnectivityBetween(f, anchorA, anchorB, 1.0f);
        DecoratePathToGoal(f, anchorA, anchorB);

        OnMapUpdated?.Invoke();
    }

    public void RequestResize(int newWidth, int newHeight, bool keepAnchors = true)
    {
        if (newWidth % 2 == 0) newWidth++;
        if (newHeight % 2 == 0) newHeight++;

        bool hadB = hasActiveFragment;

        DestroyAllInstantiated();

        width = Mathf.Max(7, newWidth);
        height = Mathf.Max(7, newHeight);
        AllocGrids(width, height, floors);

        GenerateAllFloors();
        InstantiateAllFloors();
        UpdateWalkableGrid();
        UpdateFreeCells();

        for (int f = 0; f < floors; f++)
        {
            SpawnTorchWallsOnFloor(f, false);
            RespawnPickupsOnFloor(f, placeFragment: (!useSequentialFragments));

            if (!progressiveEnemyWaves || spawnEnemiesAtStart)
                SpawnEnemiesOnFloor(f);
        }

        if (keepAnchors)
        {
            int f = 0;
            anchorA = FindNearestWalkableCell(f, ClampToBounds(WorldToCell(player ? player.position : Vector3.zero)));
            if (hadB)
            {
                anchorB = ChooseGoalCellOppositeSide(f, anchorA);
                PlaceActiveFragmentAt(f, anchorB);
                EnsureConnectivityBetween(f, anchorA, anchorB, 1.0f);
                DecoratePathToGoal(f, anchorA, anchorB);
            }
        }

        ChangeFloor(0);
        OnMapUpdated?.Invoke();
    }


    void AllocGrids(int w, int h, int floorsCount)
    {
        maze = new int[floorsCount, w, h];
        walkableGrid = new bool[floorsCount, w, h];
        wallObjects = new GameObject[floorsCount, w, h];
        freeCells = new List<Vector2Int>[floorsCount];
        spawnedEntities = new List<GameObject>[floorsCount];
        spawnedTorchWalls = new List<GameObject>[floorsCount];
        floorContainers = new Transform[floorsCount];

        // Oleadas
        spawnedEnemies = new List<GameObject>[floorsCount];

        for (int f = 0; f < floorsCount; f++)
        {
            freeCells[f] = new List<Vector2Int>();
            spawnedEntities[f] = new List<GameObject>();
            spawnedTorchWalls[f] = new List<GameObject>();
            spawnedEnemies[f] = new List<GameObject>();
        }
    }

    void GenerateAllFloors()
    {
        System.Random R = new System.Random();
        for (int f = 0; f < floors; f++)
        {
            int[,] grid = new int[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    grid[x, y] = 1;

            CarveDFS(1, 1, grid, R);

            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    maze[f, x, y] = grid[x, y];
        }
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

            freeCells[f].Clear();

            var floorGO = Instantiate(
                floorPrefab,
                new Vector3((width - 1) * cellSize / 2f, yOff, (height - 1) * cellSize / 2f),
                Quaternion.identity,
                container
            );
            SetupFloorSize(floorGO);

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
        float sx = width * cellSize, sz = height * cellSize;

        if (mf && mf.sharedMesh && mf.sharedMesh.name.ToLowerInvariant().Contains("plane"))
            floorGO.transform.localScale = new Vector3(sx * 0.1f, 1f, sz * 0.1f);
        else
            floorGO.transform.localScale = new Vector3(sx, 1f, sz);
    }

    void DestroyAllInstantiated()
    {
        if (floorContainers != null)
        {
            for (int f = 0; f < floorContainers.Length; f++)
            {
                if (floorContainers[f] != null)
                    DestroyImmediate(floorContainers[f].gameObject);
            }
        }
    }

    void PartialRegenerate()
    {
        int f = 0;

        anchorA = FindNearestWalkableCell(f, ClampToBounds(WorldToCell(player ? player.position : Vector3.zero)));
        if (!hasActiveFragment && useSequentialFragments && fragmentsCollected < targetFragments)
        {
            anchorB = ChooseGoalCellOppositeSide(f, anchorA);
            PlaceActiveFragmentAt(f, anchorB);
        }

        bool[,] preserve = new bool[width, height];
        MarkPreserveDisk(preserve, anchorA, safeRadiusCells);
        MarkPreserveDisk(preserve, anchorB, 2);

        System.Random R = new System.Random();
        int[,] buf = new int[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                buf[x, y] = 1;
        CarveDFS(1, 1, buf, R);

        float yPos = -f * floorHeight + wallHeight * 0.5f;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (preserve[x, y]) continue;

                bool wasWall = (maze[f, x, y] == 1);
                bool willWall = (buf[x, y] == 1);
                if (wasWall == willWall) continue;

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
                else
                {
                    var w = wallObjects[f, x, y];
                    if (w != null) { Destroy(w); wallObjects[f, x, y] = null; }
                }
                maze[f, x, y] = buf[x, y];
            }
        }

        UpdateWalkableGrid();
        UpdateFreeCells();

        EnsureConnectivityBetween(f, anchorA, anchorB, 1.0f);
        DecoratePathToGoal(f, anchorA, anchorB);

        OnMapUpdated?.Invoke();

   
        if (sfx && bellClip) sfx.PlayOneShot(bellClip, bellVolume);

  
        if (progressiveEnemyWaves)
        {
            regenCount = Mathf.Clamp(regenCount + 1, 0, int.MaxValue);
            HandleWaveAfterBell();
        }
    }

    void MarkPreserveDisk(bool[,] mask, Vector2Int center, int radius)
    {
        int r2 = radius * radius;
        for (int x = Mathf.Max(0, center.x - radius); x <= Mathf.Min(width - 1, center.x + radius); x++)
        {
            for (int y = Mathf.Max(0, center.y - radius); y <= Mathf.Min(height - 1, center.y + radius); y++)
            {
                int dx = x - center.x, dy = y - center.y;
                if (dx * dx + dy * dy <= r2) mask[x, y] = true;
            }
        }
    }

    // ==================== A* y CONECTIVIDAD ====================
    List<Vector2Int> AStarPath(int floor, Vector2Int start, Vector2Int goal)
    {
        if (!Inside(start) || !Inside(goal)) return null;
        if (maze[floor, start.x, start.y] == 1 || maze[floor, goal.x, goal.y] == 1) return null;

        var open = new PriorityQueue<Vector2Int>();
        var came = new Dictionary<Vector2Int, Vector2Int>();
        var g = new Dictionary<Vector2Int, int>();
        var fScore = new Dictionary<Vector2Int, int>();

        g[start] = 0;
        fScore[start] = Heuristic(start, goal);
        open.Enqueue(start, fScore[start]);

        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        while (open.Count > 0)
        {
            var current = open.Dequeue();

            if (current == goal)
                return Reconstruct(came, current);

            for (int i = 0; i < 4; i++)
            {
                Vector2Int nb = new Vector2Int(current.x + dx[i], current.y + dy[i]);
                if (!Inside(nb)) continue;
                if (maze[floor, nb.x, nb.y] == 1) continue;

                int tentative = g[current] + 1;
                if (!g.ContainsKey(nb) || tentative < g[nb])
                {
                    came[nb] = current;
                    g[nb] = tentative;
                    fScore[nb] = tentative + Heuristic(nb, goal);
                    open.EnqueueOrDecrease(nb, fScore[nb]);
                }
            }
        }
        return null;
    }

    int Heuristic(Vector2Int a, Vector2Int b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    List<Vector2Int> Reconstruct(Dictionary<Vector2Int, Vector2Int> came, Vector2Int current)
    {
        var path = new List<Vector2Int> { current };
        while (came.ContainsKey(current))
        {
            current = came[current];
            path.Add(current);
        }
        path.Reverse();
        return path;
    }

    void EnsureConnectivityBetween(int floor, Vector2Int a, Vector2Int b, float openRatio = 1.0f)
    {
        if (!Inside(a) || !Inside(b)) return;
        var path = AStarPath(floor, a, b);
        if (path != null && path.Count > 0) { DecoratePathToGoal(floor, a, b, path); return; }

        var carved = CarveGreedyCorridor(floor, a, b);
        if (carved != null && carved.Count > 0)
        {
            DecoratePathToGoal(floor, a, b, carved);
            UpdateWalkableGrid();
            UpdateFreeCells();
            OnMapUpdated?.Invoke();
        }
    }

    List<Vector2Int> CarveGreedyCorridor(int floor, Vector2Int a, Vector2Int b)
    {
        var path = new List<Vector2Int>();
        Vector2Int cur = a;
        path.Add(cur);

        int guard = width * height * 4;
        while (cur != b && guard-- > 0)
        {
            Vector2Int best = cur;
            int bestH = Heuristic(cur, b);

            Vector2Int[] dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            foreach (var d in dirs)
            {
                Vector2Int nb = cur + d;
                if (!Inside(nb)) continue;
                int h = Heuristic(nb, b);
                if (h < bestH) { best = nb; bestH = h; }
            }

            if (best == cur) break;

            if (maze[floor, best.x, best.y] == 1)
            {
                var w = wallObjects[floor, best.x, best.y];
                if (w) { Destroy(w); wallObjects[floor, best.x, best.y] = null; }
                maze[floor, best.x, best.y] = 0;
            }

            cur = best;
            path.Add(cur);
        }

        return (cur == b) ? path : null;
    }

    void DecoratePathToGoal(int floor, Vector2Int a, Vector2Int b, List<Vector2Int> knownPath = null)
    {
        ClearDecorativeTorches(floor);

        List<Vector2Int> path = knownPath ?? AStarPath(floor, a, b);
        if (path == null || path.Count < 3 || wallTorchPrefab == null) return;

        int count = Mathf.Clamp(decorativeTorchesNearPath, 0, 12);
        int placed = 0;
        for (int i = path.Count - 2; i >= 1 && placed < count; i -= Mathf.Max(1, torchPathSkip))
        {
            Vector2Int corridorCell = path[i];
            if (TryPlaceTorchOnAdjacentWall(floor, corridorCell)) placed++;
        }
    }

    void ClearDecorativeTorches(int floor)
    {
        if (spawnedTorchWalls == null || spawnedTorchWalls[floor] == null) return;
        foreach (var t in spawnedTorchWalls[floor]) if (t) Destroy(t);
        spawnedTorchWalls[floor].Clear();
    }

    bool TryPlaceTorchOnAdjacentWall(int floor, Vector2Int corridorCell)
    {
        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        float yOff = -floor * floorHeight;
        for (int i = 0; i < 4; i++)
        {
            int wx = corridorCell.x + dx[i];
            int wy = corridorCell.y + dy[i];
            if (!Inside(new Vector2Int(wx, wy))) continue;
            if (maze[floor, wx, wy] != 1) continue; // debe ser muro

            Vector3 wallCenter = new Vector3(wx * cellSize, yOff + wallHeight * 0.5f, wy * cellSize);
            Vector3 corrCenter = new Vector3(corridorCell.x * cellSize, yOff + wallHeight * 0.5f, corridorCell.y * cellSize);
            Vector3 n = (corrCenter - wallCenter); n.y = 0f; n.Normalize();
            Quaternion rot = Quaternion.LookRotation(n, Vector3.up);

            var t = Instantiate(wallTorchPrefab, wallCenter, rot, floorContainers[floor]);
            spawnedTorchWalls[floor].Add(t);
            return true;
        }
        return false;
    }


    Vector2Int ChooseGoalCellOppositeSide(int floor, Vector2Int a)
    {
        Vector2 mid = new Vector2((width - 1) * 0.5f, (height - 1) * 0.5f);

        int sxA = Mathf.Sign(a.x - mid.x) >= 0 ? 1 : -1;
        int syA = Mathf.Sign(a.y - mid.y) >= 0 ? 1 : -1;

        var free = GetFreeCells(floor);
        if (free.Count == 0) return a;

        int PerimeterScore(Vector2Int c)
        {
            int toLeft = c.x;
            int toRight = (width - 1) - c.x;
            int toDown = c.y;
            int toUp = (height - 1) - c.y;
            return Mathf.Min(toLeft, toRight, toDown, toUp);
        }

        var opposite = new List<Vector2Int>(free.Count);
        foreach (var c in free)
        {
            int sxC = Mathf.Sign(c.x - mid.x) >= 0 ? 1 : -1;
            int syC = Mathf.Sign(c.y - mid.y) >= 0 ? 1 : -1;
            bool oppositeSide = (sxC != sxA) || (syC != syA);
            if (oppositeSide) opposite.Add(c);
        }

        var pool = (opposite.Count > 0) ? opposite : free;
        pool.Sort((p, q) => PerimeterScore(p).CompareTo(PerimeterScore(q)));

        int maxCheck = Mathf.Min(pool.Count, 64);
        Vector2Int bestCell = a;
        int bestLen = -1;

        for (int i = 0; i < maxCheck; i++)
        {
            var c = pool[i];
            var path = AStarPath(floor, a, c);
            int len = (path != null) ? path.Count : 0;
            if (len > bestLen) { bestLen = len; bestCell = c; }
        }

        if (bestLen <= 0)
        {
            float bestDist = -1f;
            foreach (var c in pool)
            {
                float d = Mathf.Abs(c.x - a.x) + Mathf.Abs(c.y - a.y);
                if (d > bestDist) { bestDist = d; bestCell = c; }
            }
        }
        return bestCell;
    }

    void PlaceActiveFragmentAt(int floor, Vector2Int cell)
    {
        if (activeFragmentGO) { Destroy(activeFragmentGO); activeFragmentGO = null; }

        hasActiveFragment = true;
        anchorB = cell;

        if (!soulFragmentPrefab) return;

        var go = Instantiate(soulFragmentPrefab, CellCenterToWorld(cell, floor), Quaternion.identity, floorContainers[floor]);
        SnapToFloorByRendererHeight(go.transform, floor);
        activeFragmentGO = go;

        // BRÚJULA: marcar objetivo primario
        var ct = go.GetComponent<CompassTarget>();
        if (!ct) ct = go.AddComponent<CompassTarget>();
        ct.isPrimary = true;
    }

    void TrySpawnExtraBatteryIfUnderCap()
    {
        int worldCount = CountBatteriesAllFloors();
        int playerCount = EstimatePlayerBatteryCount();
        int total = worldCount + playerCount;
        if (total >= 15) return;

        var ring = GetFreeCells(0);
        if (ring.Count == 0 || batteryPrefabs == null || batteryPrefabs.Count == 0) return;

        var cell = ring[UnityEngine.Random.Range(0, ring.Count)];
        var prefab = batteryPrefabs[UnityEngine.Random.Range(0, batteryPrefabs.Count)];
        var go = Instantiate(prefab, BatteryAnchorToWorld(cell, 0), Quaternion.identity, floorContainers[0]);
        SnapToFloorByRendererHeight(go.transform, 0);
        spawnedEntities[0].Add(go);
    }

    int EstimatePlayerBatteryCount() => 0; 

    
    void RespawnPickupsOnFloor(int f, bool placeFragment)
    {
        if (spawnedEntities[f] == null) spawnedEntities[f] = new List<GameObject>();
        foreach (var go in spawnedEntities[f]) if (go) Destroy(go);
        spawnedEntities[f].Clear();

        var cells = GetFreeCells(f);
        if (cells.Count == 0) return;

        int batteries = autoScaleByMapSize ? ScaledCount(batteryDensity, minBatteries, maxBatteries) : minBatteries;
        for (int i = 0; i < batteries && cells.Count > 0 && batteryPrefabs != null && batteryPrefabs.Count > 0; i++)
        {
            int idx = UnityEngine.Random.Range(0, cells.Count);
            var cell = cells[idx];
            var prefab = batteryPrefabs[i % batteryPrefabs.Count];

            var go = Instantiate(prefab, BatteryAnchorToWorld(cell, f), Quaternion.identity, floorContainers[f]);
            SnapToFloorByRendererHeight(go.transform, f);
            spawnedEntities[f].Add(go);
            cells.RemoveAt(idx);
        }

        if (placeFragment && soulFragmentPrefab)
        {
            int idx = UnityEngine.Random.Range(0, cells.Count);
            var cell = cells[idx];
            var go = Instantiate(soulFragmentPrefab, CellCenterToWorld(cell, f), Quaternion.identity, floorContainers[f]);
            SnapToFloorByRendererHeight(go.transform, f);
            spawnedEntities[f].Add(go);

       
            var ct = go.GetComponent<CompassTarget>() ?? go.AddComponent<CompassTarget>();
            ct.isPrimary = false;
        }
    }

   
    void SpawnEnemiesOnFloor(int f)
    {
        if (progressiveEnemyWaves && !spawnEnemiesAtStart) return;

        var cells = GetFreeCells(f);
        if (cells.Count == 0) return;

        // Random 1..3 sin usar densidad
        int enemies = UnityEngine.Random.Range(1, 4); // 1,2,3
        if (limitEnemiesToOneToThree) enemies = Mathf.Clamp(enemies, 1, 3);

        bool hasTyped =
            (enemyType1Prefabs != null && enemyType1Prefabs.Count > 0) ||
            (enemyType2Prefabs != null && enemyType2Prefabs.Count > 0) ||
            (enemyType3Prefabs != null && enemyType3Prefabs.Count > 0);

        DespawnEnemiesOnFloor(f);

        for (int i = 0; i < enemies && cells.Count > 0; i++)
        {
            int idx = UnityEngine.Random.Range(0, cells.Count);
            var cell = cells[idx];

            GameObject prefab = null;
            if (hasTyped)
            {
                int pick = UnityEngine.Random.Range(1, 4);
                List<GameObject> pool = (pick == 1) ? enemyType1Prefabs :
                                        (pick == 2) ? enemyType2Prefabs :
                                                      enemyType3Prefabs;
                if (pool != null && pool.Count > 0)
                    prefab = pool[UnityEngine.Random.Range(0, pool.Count)];
            }
            if (!prefab && enemyPrefabs != null && enemyPrefabs.Count > 0)
                prefab = enemyPrefabs[UnityEngine.Random.Range(0, enemyPrefabs.Count)];

            if (!prefab) break;

            var e = Instantiate(prefab, CellCenterToWorld(cell, f), Quaternion.identity, floorContainers[f]);
            SnapToFloorByRendererHeight(e.transform, f);

            spawnedEnemies[f].Add(e);
            spawnedEntities[f].Add(e);
            cells.RemoveAt(idx);
        }
    }

    void SpawnTorchWallsOnFloor(int f, bool decorative)
    {
        if (spawnedTorchWalls[f] == null) spawnedTorchWalls[f] = new List<GameObject>();
    }

    // ==================== WALKABLE / FREECELLS / HELPERS ====================
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
            var list = freeCells[f];
            list.Clear();
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    if (maze[f, x, y] == 0) list.Add(new Vector2Int(x, y));
        }
    }

    public bool[,,] GetWalkableGrid()
    {
        var copy = new bool[floors, width, height];
        Array.Copy(walkableGrid, copy, walkableGrid.Length);
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

    public Vector3 CellCenterToWorld(Vector2Int cell, int floor)
    {
        return new Vector3(cell.x * cellSize, -floor * floorHeight, cell.y * cellSize);
    }

    public Vector3 BatteryAnchorToWorld(Vector2Int cell, int floor)
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

    public bool IsSanctuary(int floor, int x, int y) => false;
    public bool IsSanctuary(int floor, Vector2Int cell) => false;

    public int GetCurrentFloor() => currentFloor;

    public void SetSanctuaryCell(int floor, Vector2Int cell, bool value) { /* noop */ }

    public bool Inside(Vector2Int c) => (c.x >= 0 && c.x < width && c.y >= 0 && c.y < height);
    Vector2Int ClampToBounds(Vector2Int c) =>
        new Vector2Int(Mathf.Clamp(c.x, 0, width - 1), Mathf.Clamp(c.y, 0, height - 1));

    void SnapToFloorY(Transform t, int floor, float lift = 0.02f)
    {
        if (!t) return;
        var p = t.position;
        p.y = -floor * floorHeight + lift;
        t.position = p;
    }

    void TeleportPlayerToCell(Transform who, Vector2Int cell, int floor)
    {
        if (!who) return;
        var cc = who.GetComponent<CharacterController>();
        bool had = cc && cc.enabled;
        if (cc) cc.enabled = false;

        who.position = CellCenterToWorld(cell, floor);
        SnapToFloorY(who, floor, 0.02f);

        if (cc) cc.enabled = had;
    }

    void SnapToFloorByRendererHeight(Transform t, int floor)
    {
        if (!t) return;
        float baseY = -floor * floorHeight;

        Renderer rend = t.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            float h = rend.bounds.size.y;
            t.position = new Vector3(t.position.x, baseY + (h * 0.5f) + pickupLiftEpsilon, t.position.z);
            return;
        }

        Collider col = t.GetComponentInChildren<Collider>();
        if (col != null)
        {
            float h = col.bounds.size.y;
            t.position = new Vector3(t.position.x, baseY + (h * 0.5f) + pickupLiftEpsilon, t.position.z);
            return;
        }

        t.position = new Vector3(t.position.x, baseY + pickupLiftEpsilon, t.position.z);
    }

    int Area() => width * height;
    int ScaledCount(float density, int min, int max)
        => Mathf.Clamp(Mathf.RoundToInt(Area() * density), min, max);

    public int CountBatteriesAllFloors()
    {
        var all = FindObjectsOfType<BatteryPickup>(includeInactive: false);
        return all?.Length ?? 0;
    }

    int GetPlayerFloor()
    {
        if (!player) return Mathf.Clamp(currentFloor, 0, floors - 1);
        return Mathf.Clamp(Mathf.RoundToInt(-player.position.y / floorHeight), 0, floors - 1);
    }

    public void GoToNextFloor()
    {
        ChangeFloor(0);
    }

    void ChangeFloor(int newFloor)
    {
        if (currentFloor >= 0 && currentFloor < floors && spawnedEntities[currentFloor] != null)
        {
            foreach (var go in spawnedEntities[currentFloor]) if (go) Destroy(go);
            spawnedEntities[currentFloor].Clear();
        }
        currentFloor = Mathf.Clamp(newFloor, 0, floors - 1);
    }

    Vector2Int ChooseStartCellNearEdge(int floor)
    {
        var free = GetFreeCells(floor);
        if (free.Count == 0) return new Vector2Int(width / 2, height / 2);

        int BestScore(Vector2Int c)
        {
            int toLeft = c.x;
            int toRight = (width - 1) - c.x;
            int toDown = c.y;
            int toUp = (height - 1) - c.y;
            return Mathf.Min(toLeft, toRight, toDown, toUp);
        }

        int best = int.MaxValue;
        List<Vector2Int> cand = new List<Vector2Int>();
        foreach (var c in free)
        {
            int s = BestScore(c);
            if (s < best) { best = s; cand.Clear(); cand.Add(c); }
            else if (s == best) cand.Add(c);
        }
        return cand[UnityEngine.Random.Range(0, cand.Count)];
    }

    Vector2Int FindNearestWalkableCell(int floor, Vector2Int from)
    {
        from = ClampToBounds(from);
        if (maze[floor, from.x, from.y] == 0) return from;

        bool[,] visited = new bool[width, height];
        Queue<Vector2Int> q = new Queue<Vector2Int>();
        visited[from.x, from.y] = true;
        q.Enqueue(from);

        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        while (q.Count > 0)
        {
            var c = q.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                int nx = c.x + dx[i], ny = c.y + dy[i];
                var nb = new Vector2Int(nx, ny);
                if (!Inside(nb) || visited[nx, ny]) continue;
                visited[nx, ny] = true;

                if (maze[floor, nx, ny] == 0)
                    return nb;

                q.Enqueue(nb);
            }
        }
        return ClampToBounds(from);
    }

    // ==================== PRIORITY QUEUE PARA A* ====================
    class PriorityQueue<T>
    {
        List<(T item, int pri)> heap = new List<(T, int)>();
        Dictionary<T, int> loc = new Dictionary<T, int>();
        public int Count => heap.Count;

        public void Enqueue(T item, int pri)
        {
            heap.Add((item, pri));
            loc[item] = heap.Count - 1;
            Up(heap.Count - 1);
        }

        public void EnqueueOrDecrease(T item, int pri)
        {
            if (loc.TryGetValue(item, out int idx))
            {
                if (pri < heap[idx].pri)
                {
                    heap[idx] = (item, pri);
                    Up(idx);
                }
                return;
            }
            Enqueue(item, pri);
        }

        public T Dequeue()
        {
            var root = heap[0].item;
            Swap(0, heap.Count - 1);
            loc.Remove(root);
            heap.RemoveAt(heap.Count - 1);
            Down(0);
            return root;
        }

        void Up(int i)
        {
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (heap[i].pri >= heap[p].pri) break;
                Swap(i, p); i = p;
            }
        }

        void Down(int i)
        {
            int n = heap.Count;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, s = i;
                if (l < n && heap[l].pri < heap[s].pri) s = l;
                if (r < n && heap[r].pri < heap[s].pri) s = r;
                if (s == i) break;
                Swap(i, s); i = s;
            }
        }

        void Swap(int a, int b)
        {
            if (a == b) return;
            var tmp = heap[a]; heap[a] = heap[b]; heap[b] = tmp;
            loc[heap[a].item] = a; loc[heap[b].item] = b;
        }
    }

    enum EnemyType { E1, E2, E3 }

    List<GameObject> PickFromType(EnemyType t)
    {
        List<GameObject> pool = null;
        switch (t)
        {
            case EnemyType.E1: pool = enemyType1Prefabs; break;
            case EnemyType.E2: pool = enemyType2Prefabs; break;
            case EnemyType.E3: pool = enemyType3Prefabs; break;
        }
        if (pool == null || pool.Count == 0) return null;
        return new List<GameObject> { pool[UnityEngine.Random.Range(0, pool.Count)] };
    }


    List<GameObject> BuildWavePrefabsFor(int waveIndex1to9)
    {
        int w = Mathf.Clamp(waveIndex1to9, 1, 9);
   
        EnemyType[][] pattern = new EnemyType[][]
        {
            new []{ EnemyType.E1 },
            new []{ EnemyType.E2 },
            new []{ EnemyType.E3 },
            new []{ EnemyType.E2, EnemyType.E1 },
            new []{ EnemyType.E3, EnemyType.E1 },
            new []{ EnemyType.E3, EnemyType.E2 },
            new []{ EnemyType.E1, EnemyType.E2, EnemyType.E3 },
            new []{ EnemyType.E2, EnemyType.E3, EnemyType.E1 },
            new []{ EnemyType.E3, EnemyType.E1, EnemyType.E2 },
        };

        var types = pattern[w - 1];
        var result = new List<GameObject>(types.Length);

        foreach (var t in types)
        {
            var picked = PickFromType(t);
            if (picked != null && picked.Count > 0)
                result.Add(picked[0]);
        }

        if (result.Count == 0 && enemyPrefabs != null && enemyPrefabs.Count > 0)
            result.Add(enemyPrefabs[UnityEngine.Random.Range(0, enemyPrefabs.Count)]);

        if (limitEnemiesToOneToThree)
            result = result.GetRange(0, Mathf.Clamp(result.Count, 1, 3));

        return result;
    }

    void DespawnEnemiesOnFloor(int f)
    {
        if (spawnedEnemies == null || spawnedEnemies[f] == null) return;
        foreach (var e in spawnedEnemies[f]) if (e) Destroy(e);
        spawnedEnemies[f].Clear();
    }

    void SpawnEnemiesWaveOnFloor(int f, List<GameObject> exactPrefabsToSpawn)
    {
        if (exactPrefabsToSpawn == null || exactPrefabsToSpawn.Count == 0) return;

        var cells = GetFreeCells(f);
        if (cells.Count == 0) return;

        int count = exactPrefabsToSpawn.Count;
        if (limitEnemiesToOneToThree) count = Mathf.Clamp(count, 1, 3);

        DespawnEnemiesOnFloor(f);

        for (int i = 0; i < count && cells.Count > 0; i++)
        {
            var prefab = exactPrefabsToSpawn[i];
            if (!prefab) continue;

            int idx = UnityEngine.Random.Range(0, cells.Count);
            var cell = cells[idx];

            var e = Instantiate(prefab, CellCenterToWorld(cell, f), Quaternion.identity, floorContainers[f]);
            SnapToFloorByRendererHeight(e.transform, f);

            spawnedEnemies[f].Add(e);
            spawnedEntities[f].Add(e);
            cells.RemoveAt(idx);
        }
    }

    void HandleWaveAfterBell()
    {
        int f = 0; 
        int waveIndex = ((regenCount - 1) % 9) + 1;

        var exactPrefabs = BuildWavePrefabsFor(waveIndex);
        SpawnEnemiesWaveOnFloor(f, exactPrefabs);
    }
}