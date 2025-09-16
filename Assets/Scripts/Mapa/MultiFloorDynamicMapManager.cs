using UnityEngine;
using System;
using System.Collections.Generic;

public class MultiFloorDynamicMapManager : MonoBehaviour
{
    public static MultiFloorDynamicMapManager Instance;

    [Header("Mapa")]
    public int width = 31;
    public int height = 31;
    [Tooltip("Se mantiene por compatibilidad. Se usa el piso 0.")]
    public int floors = 1;

    [Header("Escala")]
    public float cellSize = 1f;
    public float floorHeight = 4f;
    [Min(1f)] public float wallHeightMultiplier = 2f;
    [Min(0.25f)] public float minFloorHeadroom = 0.75f;

    [Header("Prefabs (Maze / Muros por Bioma)")]
    public GameObject floorPrefab;
    public GameObject wallPrefab;     // Verde (por defecto)
    public GameObject wallPrefabRed;  // Rojo
    public GameObject wallPrefabBlue; // Azul
    public GameObject wallTorchPrefab;

    [Header("Prefabs (Entidades)")]
    public GameObject soulFragmentPrefab;
    public List<GameObject> enemyPrefabs;        // fallback
    public List<GameObject> enemyType1Prefabs;   // oleadas
    public List<GameObject> enemyType2Prefabs;
    public List<GameObject> enemyType3Prefabs;

    [Header("Baterías por Bioma (dead-ends)")]
    public GameObject batteryRedPrefab;   // bioma Rojo
    public GameObject batteryGreenPrefab; // bioma Verde
    public GameObject batteryBluePrefab;  // bioma Azul

    [Header("Jugador")]
    public Transform player;
    public GameObject playerPrefab;
    public bool spawnPlayerIfMissing = true;

    [Header("Dinámica")]
    public float regenerationInterval = 30f;
    public int safeRadiusCells = 5;

    [Header("Colocación de pickups")]
    public float pickupLiftEpsilon = 0.02f;

    [Header("Fragmentos Secuenciales")]
    public bool useSequentialFragments = true;
    [Min(1)] public int targetFragments = 5;      // clamp 1..9 en SetTargetFragments
    public int fragmentsCollected = 0;

    [Header("Antorchas decorativas")]
    public int decorativeTorchesNearPath = 3;
    public int torchPathSkip = 5;

    [Header("Cambio de Mapa (sonido)")]
    public AudioClip bellClip;
    [Range(0f, 1f)] public float bellVolume = 0.9f;

    [Header("Oleadas Progresivas (por campana)")]
    public bool progressiveEnemyWaves = true;

    [Header("Biomas")]
    [Tooltip("Ancho mínimo de cada franja de bioma (en celdas).")]
    public int biomeMinWidth = 5;

    // ===== Internos =====
    private int regenCount = 0;
    private List<GameObject>[] spawnedEnemies;
    private AudioSource sfx;

    // maze: 0=pasillo, 1=muro
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

    // Límites de biomas (verticales, por X). Tres franjas: [0..split1-1]=Rojo, [split1..split2-1]=Verde, [split2..width-1]=Azul
    private int splitX1;
    private int splitX2;

    public event Action OnMapUpdated;

    public enum Biome { Red, Green, Blue }

    // ====== Biomas ======
    void ComputeBiomeSplits()
    {
        int minW = Mathf.Clamp(biomeMinWidth, 1, Mathf.Max(1, width / 3 - 1));
        int leftMinEnd = minW;                 // split1 mínimo
        int leftMaxEnd = width - (minW * 2) - 1;
        if (leftMaxEnd < leftMinEnd) leftMaxEnd = leftMinEnd;

        splitX1 = UnityEngine.Random.Range(leftMinEnd, leftMaxEnd + 1); // incluye leftMaxEnd

        int midMinEnd = splitX1 + minW;
        int midMaxEnd = width - minW - 1;
        if (midMaxEnd < midMinEnd) midMaxEnd = midMinEnd;

        splitX2 = UnityEngine.Random.Range(midMinEnd, midMaxEnd + 1);

        // Seguridad por si algo queda fuera
        splitX1 = Mathf.Clamp(splitX1, 1, width - 2);
        splitX2 = Mathf.Clamp(splitX2, splitX1 + 1, width - 1);
    }

    Biome GetBiome(int x, int y)
    {
        if (x < splitX1) return Biome.Red;
        if (x < splitX2) return Biome.Green;
        return Biome.Blue;
    }

    GameObject GetWallPrefabForBiome(Biome b)
    {
        if (b == Biome.Red && wallPrefabRed) return wallPrefabRed;
        if (b == Biome.Blue && wallPrefabBlue) return wallPrefabBlue;
        return wallPrefab; // Verde o fallback
    }

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        sfx = GetComponent<AudioSource>();
        if (!sfx) sfx = gameObject.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 0f;
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

        // Calcula las franjas de bioma (siempre habrá 3)
        ComputeBiomeSplits();

        AllocGrids(width, height, floors);

        GenerateAllFloors();
        InstantiateAllFloors();
        UpdateWalkableGrid();
        UpdateFreeCells();

        for (int f = 0; f < floors; f++)
            SpawnTorchWallsOnFloor(f, false);

        ChangeFloor(0);

        if (useSequentialFragments) BeginRun();

        if (progressiveEnemyWaves)
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
        targetFragments = Mathf.Clamp(n, 1, 9); // límite duro 1..9
        if (useSequentialFragments)
            fragmentsCollected = Mathf.Clamp(fragmentsCollected, 0, targetFragments);
    }

    // ==================== GENERACIÓN BÁSICA ====================
    void AllocGrids(int w, int h, int floorsCount)
    {
        maze = new int[floorsCount, w, h];
        walkableGrid = new bool[floorsCount, w, h];
        wallObjects = new GameObject[floorsCount, w, h];
        freeCells = new List<Vector2Int>[floorsCount];
        spawnedEntities = new List<GameObject>[floorsCount];
        spawnedTorchWalls = new List<GameObject>[floorsCount];
        floorContainers = new Transform[floorsCount];
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
        int f = 0;
        int[,] grid = new int[width, height];

        // Llenar con muros
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                grid[x, y] = 1;

        // DFS simple (1=muro, 0=pasillo)
        System.Random R = new System.Random();
        CarveDFS(1, 1, grid, R);

        // Copiar a maze piso 0
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                maze[f, x, y] = grid[x, y];

        // Duplicar si hubiera más pisos
        for (int ff = 1; ff < floors; ff++)
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    maze[ff, x, y] = maze[0, x, y];
    }

    // DFS iterativo 4-dir
    void CarveDFS(int cx, int cy, int[,] grid, System.Random R)
    {
        int w = grid.GetLength(0), h = grid.GetLength(1);
        cx = Mathf.Clamp(cx, 1, w - 2);
        cy = Mathf.Clamp(cy, 1, h - 2);
        if (cx % 2 == 0) cx--;
        if (cy % 2 == 0) cy--;

        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        grid[cx, cy] = 0;
        stack.Push(new Vector2Int(cx, cy));

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        while (stack.Count > 0)
        {
            var c = stack.Peek();

            // mezclar dirs
            for (int i = 0; i < dirs.Length; i++)
            {
                int r = R.Next(i, dirs.Length);
                (dirs[i], dirs[r]) = (dirs[r], dirs[i]);
            }

            bool moved = false;
            foreach (var d in dirs)
            {
                int nx = c.x + d.x * 2;
                int ny = c.y + d.y * 2;

                if (nx > 0 && nx < w - 1 && ny > 0 && ny < h - 1 && grid[nx, ny] == 1)
                {
                    grid[c.x + d.x, c.y + d.y] = 0; // romper pared intermedia
                    grid[nx, ny] = 0;               // abrir celda
                    stack.Push(new Vector2Int(nx, ny));
                    moved = true;
                    break;
                }
            }
            if (!moved) stack.Pop();
        }
    }

    // ==================== INSTANCIADO / BIOMAS / PILAS ====================
    void InstantiateAllFloors()
    {
        for (int f = 0; f < floors; f++)
        {
            float yOff = -f * floorHeight;

            var container = new GameObject($"Floor_{f}").transform;
            container.SetParent(transform);
            floorContainers[f] = container;

            freeCells[f].Clear();

            // piso físico
            var floorGO = Instantiate(
                floorPrefab,
                new Vector3((width - 1) * cellSize / 2f, yOff, (height - 1) * cellSize / 2f),
                Quaternion.identity,
                container
            );
            SetupFloorSize(floorGO);

            // muros y pasillos
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (maze[f, x, y] == 1)
                    {
                        var biome = GetBiome(x, y);
                        var wp = GetWallPrefabForBiome(biome);

                        var wall = Instantiate(
                            wp,
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

            // baterías en dead-ends por bioma (garantiza roja/verde/azul si hay)
            PlaceBiomeBatteriesAtDeadEnds(f);
        }

        UpdateWalkableGrid();
        OnMapUpdated?.Invoke();
    }

    void PlaceBiomeBatteriesAtDeadEnds(int floor)
    {
        if (spawnedEntities[floor] == null) spawnedEntities[floor] = new List<GameObject>();

        // recolectar dead-ends por bioma
        List<Vector2Int> redEnds = new List<Vector2Int>();
        List<Vector2Int> greenEnds = new List<Vector2Int>();
        List<Vector2Int> blueEnds = new List<Vector2Int>();

        for (int x = 1; x < width - 1; x++)
        {
            for (int y = 1; y < height - 1; y++)
            {
                if (maze[floor, x, y] != 0) continue;
                int nWalk = CountWalkableNeighbors(floor, x, y);
                if (nWalk == 1) // dead-end
                {
                    var b = GetBiome(x, y);
                    if (b == Biome.Red) redEnds.Add(new Vector2Int(x, y));
                    else if (b == Biome.Green) greenEnds.Add(new Vector2Int(x, y));
                    else blueEnds.Add(new Vector2Int(x, y));
                }
            }
        }

        // Garantizar 1 de cada color si hay dead-ends disponibles
        if (batteryRedPrefab && redEnds.Count > 0)
            SpawnBatteryOnWall(floor, redEnds[UnityEngine.Random.Range(0, redEnds.Count)], batteryRedPrefab);

        if (batteryGreenPrefab && greenEnds.Count > 0)
            SpawnBatteryOnWall(floor, greenEnds[UnityEngine.Random.Range(0, greenEnds.Count)], batteryGreenPrefab);

        if (batteryBluePrefab && blueEnds.Count > 0)
            SpawnBatteryOnWall(floor, blueEnds[UnityEngine.Random.Range(0, blueEnds.Count)], batteryBluePrefab);

        // Extras (opcionales) ~30% de cada bioma
        float extraChance = 0.30f;
        foreach (var c in redEnds)
            if (batteryRedPrefab && UnityEngine.Random.value < extraChance)
                SpawnBatteryOnWall(floor, c, batteryRedPrefab);

        foreach (var c in greenEnds)
            if (batteryGreenPrefab && UnityEngine.Random.value < extraChance)
                SpawnBatteryOnWall(floor, c, batteryGreenPrefab);

        foreach (var c in blueEnds)
            if (batteryBluePrefab && UnityEngine.Random.value < extraChance)
                SpawnBatteryOnWall(floor, c, batteryBluePrefab);
    }

    int CountWalkableNeighbors(int floor, int x, int y)
    {
        int n = 0;
        if (IsWalkable(floor, x + 1, y)) n++;
        if (IsWalkable(floor, x - 1, y)) n++;
        if (IsWalkable(floor, x, y + 1)) n++;
        if (IsWalkable(floor, x, y - 1)) n++;
        return n;
    }

    void SpawnBatteryOnWall(int f, Vector2Int cell, GameObject prefab)
    {
        if (!prefab) return;

        // Detecta dirección hacia el único vecino caminable, para “pegar” la batería al muro opuesto
        Vector2Int dir = Vector2Int.zero;
        int walkCount = 0;
        if (IsWalkable(f, cell.x + 1, cell.y)) { walkCount++; dir = new Vector2Int(-1, 0); }
        if (IsWalkable(f, cell.x - 1, cell.y)) { walkCount++; dir = new Vector2Int(1, 0); }
        if (IsWalkable(f, cell.x, cell.y + 1)) { walkCount++; dir = new Vector2Int(0, -1); }
        if (IsWalkable(f, cell.x, cell.y - 1)) { walkCount++; dir = new Vector2Int(0, 1); }

        Vector3 basePos = CellCenterToWorld(cell, f);

        if (walkCount != 1)
        {
            var go = Instantiate(prefab, basePos, Quaternion.identity, floorContainers[f]);
            SnapToFloorByRendererHeight(go.transform, f);
            spawnedEntities[f].Add(go);
            return;
        }

        Vector3 offset = new Vector3(dir.x, 0f, dir.y) * (cellSize * 0.45f);
        Vector3 pos = basePos + offset;

        Vector3 lookAt = basePos;
        Vector3 forward = (lookAt - pos); forward.y = 0f;
        Quaternion rot = forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward.normalized, Vector3.up) : Quaternion.identity;

        var go2 = Instantiate(prefab, pos, rot, floorContainers[f]);
        // altura a mitad del muro para “pegada”
        go2.transform.position = new Vector3(go2.transform.position.x, -f * floorHeight + wallHeight * 0.5f, go2.transform.position.z);
        spawnedEntities[f].Add(go2);
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

    // ==================== REGENERACIÓN PARCIAL ====================
    void PartialRegenerate()
    {
        int f = 0;

        Vector2Int playerCell = ClampToBounds(WorldToCell(player ? player.position : Vector3.zero));
        Vector2Int fragCell = anchorB; if (!Inside(fragCell)) fragCell = playerCell;

        bool[,] preserve = new bool[width, height];
        MarkPreserveDisk(preserve, playerCell, safeRadiusCells);
        MarkPreserveDisk(preserve, fragCell, 2);

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
                if (wasWall != willWall)
                {
                    Vector3 pos = new Vector3(x * cellSize, yPos, y * cellSize);
                    if (willWall)
                    {
                        if (wallObjects[f, x, y] == null)
                        {
                            var biome = GetBiome(x, y);
                            var wp = GetWallPrefabForBiome(biome);
                            var w = Instantiate(wp, pos, Quaternion.identity, floorContainers[f]);
                            w.transform.localScale = new Vector3(cellSize, wallHeight, cellSize);
                            wallObjects[f, x, y] = w;
                        }
                    }
                    else
                    {
                        var w = wallObjects[f, x, y];
                        if (w != null) { Destroy(w); wallObjects[f, x, y] = null; }
                    }
                }
                maze[f, x, y] = buf[x, y];
            }
        }

        UpdateWalkableGrid();
        UpdateFreeCells();

        // Limpiar baterías previas (roja/verde/azul)
        CleanupBatteriesOnFloor(f);
        // Recolocar según dead-ends y biomas
        PlaceBiomeBatteriesAtDeadEnds(f);

        // Conectividad player-fragment
        EnsureConnectivityBetween(f, playerCell, fragCell, 1.0f);

        OnMapUpdated?.Invoke();
        if (sfx && bellClip) sfx.PlayOneShot(bellClip, bellVolume);

        if (progressiveEnemyWaves)
        {
            regenCount = Mathf.Clamp(regenCount + 1, 0, int.MaxValue);
            HandleWaveAfterBell();
        }
    }

    void CleanupBatteriesOnFloor(int f)
    {
        if (spawnedEntities[f] == null) return;

        for (int i = spawnedEntities[f].Count - 1; i >= 0; i--)
        {
            var go = spawnedEntities[f][i];
            if (!go) { spawnedEntities[f].RemoveAt(i); continue; }

            string n = go.name.Replace("(Clone)", "");
            bool isRed = (batteryRedPrefab && n == batteryRedPrefab.name);
            bool isGreen = (batteryGreenPrefab && n == batteryGreenPrefab.name);
            bool isBlue = (batteryBluePrefab && n == batteryBluePrefab.name);

            if (isRed || isGreen || isBlue)
            {
                Destroy(go);
                spawnedEntities[f].RemoveAt(i);
            }
        }
    }

    void MarkPreserveDisk(bool[,] mask, Vector2Int center, int radius)
    {
        int r2 = radius * radius;
        for (int x = Mathf.Max(0, center.x - radius); x <= Mathf.Min(width - 1, center.x + radius); x++)
            for (int y = Mathf.Max(0, center.y - radius); y <= Mathf.Min(height - 1, center.y + radius); y++)
            {
                int dx = x - center.x, dy = y - center.y;
                if (dx * dx + dy * dy <= r2) mask[x, y] = true;
            }
    }

    // ==================== A*, CONECTIVIDAD, TORCHES ====================
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
            if (current == goal) return Reconstruct(came, current);

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
        while (came.ContainsKey(current)) { current = came[current]; path.Add(current); }
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
        Vector2Int cur = a; path.Add(cur);

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

    // ==================== FRAGMENTOS / RUN ====================
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

        EnsureConnectivityBetween(f, anchorA, anchorB, 1.0f);
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

    void PlaceActiveFragmentAt(int floor, Vector2Int cell)
    {
        if (activeFragmentGO) { Destroy(activeFragmentGO); activeFragmentGO = null; }

        hasActiveFragment = true;
        anchorB = cell;

        if (!soulFragmentPrefab) return;

        var go = Instantiate(soulFragmentPrefab, CellCenterToWorld(cell, floor), Quaternion.identity, floorContainers[floor]);
        SnapToFloorByRendererHeight(go.transform, floor);
        activeFragmentGO = go;

        var ct = go.GetComponent<CompassTarget>();
        if (!ct) ct = go.AddComponent<CompassTarget>();
        ct.isPrimary = true;
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

    public List<Vector2Int> GetFreeCells(int floor) => new List<Vector2Int>(freeCells[floor]);

    public Vector3 CellToWorld(Vector2Int cell, int floor)
        => new Vector3(cell.x * cellSize, -floor * floorHeight, cell.y * cellSize);

    public Vector3 CellCenterToWorld(Vector2Int cell, int floor)
        => new Vector3(cell.x * cellSize, -floor * floorHeight, cell.y * cellSize);

    public Vector3 BatteryAnchorToWorld(Vector2Int cell, int floor)
        => new Vector3(cell.x * cellSize, -floor * floorHeight, cell.y * cellSize);

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

    public int GetCurrentFloor() => currentFloor;

    public bool Inside(Vector2Int c) => (c.x >= 0 && c.x < width && c.y >= 0 && c.y < height);

    Vector2Int ClampToBounds(Vector2Int c)
        => new Vector2Int(Mathf.Clamp(c.x, 0, width - 1), Mathf.Clamp(c.y, 0, height - 1));

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

    int GetPlayerFloor()
    {
        if (!player) return Mathf.Clamp(currentFloor, 0, floors - 1);
        return Mathf.Clamp(Mathf.RoundToInt(-player.position.y / floorHeight), 0, floors - 1);
    }

    public void GoToNextFloor() { ChangeFloor(0); }

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

                if (maze[floor, nx, ny] == 0) return nb;

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
                if (pri < heap[idx].pri) { heap[idx] = (item, pri); Up(idx); }
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

    // ==================== OLEADAS SIMPLES ====================
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

        // limitar 1..3 por estética
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

        int count = Mathf.Clamp(exactPrefabsToSpawn.Count, 1, 3);

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

    void SpawnTorchWallsOnFloor(int f, bool decorative)
    {
        if (spawnedTorchWalls[f] == null) spawnedTorchWalls[f] = new List<GameObject>();
    }

    // ===== utilidades extra =====
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
}