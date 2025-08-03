using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
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
    public GameObject floorPrefab;

    [Header("Prefabs Entidades")]
    public GameObject batteryPrefab;
    public GameObject soulFragmentPrefab;
    public GameObject doorPrefab;
    public List<GameObject> enemyPrefabs;

    [Header("Dinámica")]
    public float regenerationInterval = 30f;
    public int safeRadiusCells = 5;
    public Transform player;

    private int[,,] maze;
    private GameObject[,,] wallObjects;
    private List<Vector2Int>[] freeCells;
    private List<GameObject>[] spawnedEntities;
    private float regenTimer;
    private int currentFloor = -1;
    private NavMeshSurface[] navSurfaces;

    [System.Serializable]
    public class PatrolArea
    {
        public List<Transform> patrolPoints = new List<Transform>();
    }

    public List<PatrolArea>[] patrolAreas;

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
        freeCells = new List<Vector2Int>[floors];
        spawnedEntities = new List<GameObject>[floors];
        patrolAreas = new List<PatrolArea>[floors];

        for (int f = 0; f < floors; f++)
        {
            spawnedEntities[f] = new List<GameObject>();
            patrolAreas[f] = new List<PatrolArea>();
        }

        GenerateAllFloors();
        InstantiateAllFloors();
        CreatePatrolAreas();
        ChangeFloor(0);

        var inventory = player.GetComponent<PlayerInventory>();
        if (inventory != null)
        {
            inventory.totalLevels = floors;
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
        if (pf != currentFloor)
            ChangeFloor(pf);
    }

    int GetPlayerFloor()
    {
        return Mathf.Clamp(
            Mathf.RoundToInt(-player.position.y / floorHeight),
            0, floors - 1
        );
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
        PlaceEntitiesOnFloor(newFloor);
        currentFloor = newFloor;
    }

    void InstantiateAllFloors()
    {
        navSurfaces = new NavMeshSurface[floors];

        for (int f = 0; f < floors; f++)
        {
            float yOff = -f * floorHeight;
            freeCells[f] = new List<Vector2Int>();

            GameObject floorContainer = new GameObject($"Floor_{f}");
            floorContainer.transform.parent = transform;

            var floorObj = Instantiate(
                floorPrefab,
                new Vector3((width - 1) * cellSize / 2f, yOff, (height - 1) * cellSize / 2f),
                Quaternion.identity,
                floorContainer.transform
            );

            floorObj.transform.localScale = new Vector3(width * cellSize / 10f, 1, height * cellSize / 10f);

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

            NavMeshSurface surface = floorContainer.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.layerMask = LayerMask.GetMask("Floor");
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.overrideTileSize = true;
            surface.tileSize = 32;

            navSurfaces[f] = surface;
            surface.BuildNavMesh();
        }
    }

    void GenerateAllFloors()
    {
        for (int f = 0; f < floors; f++)
            GenerateMazeForFloor(f);

        for (int a = 0; a < floors; a++)
            for (int b = a + 1; b < floors; b++)
                if (FloorsAreIdentical(a, b))
                    GenerateMazeForFloor(b);
    }

    void GenerateMazeForFloor(int f)
    {
        int[,] grid = new int[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                grid[x, y] = 1;

        CarveDFS(1, 1, grid, new System.Random());

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                maze[f, x, y] = grid[x, y];
    }

    bool FloorsAreIdentical(int a, int b)
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (maze[a, x, y] != maze[b, x, y])
                    return false;
        return true;
    }

    void CarveDFS(int cx, int cy, int[,] grid, System.Random rng)
    {
        grid[cx, cy] = 0;
        var dirs = new List<Vector2Int> { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        for (int i = 0; i < dirs.Count; i++)
        {
            int r = rng.Next(i, dirs.Count);
            var tmp = dirs[i]; dirs[i] = dirs[r]; dirs[r] = tmp;
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

    void PlaceEntitiesOnFloor(int floor)
    {
        var used = new HashSet<Vector2Int>();

        if (floor == 0 && player != null)
        {
            var cell = freeCells[floor][Random.Range(0, freeCells[floor].Count)];
            used.Add(cell);
            float y0 = -floor * floorHeight + 0.1f;
            player.position = new Vector3(cell.x * cellSize, y0, cell.y * cellSize);
        }

        int soulCount = 1;
        int batteryCount = 2 + floor;
        int enemyCount = Mathf.FloorToInt(freeCells[floor].Count * (0.03f + 0.02f * floor));

        System.Action<GameObject> Spawn = go =>
        {
            var avail = new List<Vector2Int>();
            foreach (var c in freeCells[floor])
                if (!used.Contains(c))
                    avail.Add(c);

            if (avail.Count == 0) { Destroy(go); return; }

            var choice = avail[Random.Range(0, avail.Count)];
            used.Add(choice);
            float y = -floor * floorHeight + 0.1f;
            Vector3 spawnPos = new Vector3(choice.x * cellSize, y, choice.y * cellSize);

            if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
            {
                go.transform.position = hit.position;
                spawnedEntities[floor].Add(go);
            }
            else
            {
                Destroy(go);
            }
        };

        for (int i = 0; i < soulCount; i++) Spawn(Instantiate(soulFragmentPrefab, transform));
        for (int i = 0; i < batteryCount; i++) Spawn(Instantiate(batteryPrefab, transform));
        for (int i = 0; i < enemyCount; i++)
        {
            var prefab = enemyPrefabs[Random.Range(0, enemyPrefabs.Count)];
            Spawn(Instantiate(prefab, transform));
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
                for (int y = 0; y < height; y++)
                    buf[x, y] = 1;

            CarveDFS(1, 1, buf, new System.Random());

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (f == pf && safe[x, y]) continue;

                    bool was = maze[f, x, y] == 1;
                    bool will = buf[x, y] == 1;

                    if (was != will)
                    {
                        float yPos = -f * floorHeight + cellSize / 2f;
                        Vector3 pos = new Vector3(x * cellSize, yPos, y * cellSize);

                        if (will)
                        {
                            var w = Instantiate(wallPrefab, pos, Quaternion.identity, transform.Find($"Floor_{f}"));
                            w.transform.localScale = Vector3.one * cellSize;
                            wallObjects[f, x, y] = w;
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

            if (navSurfaces[f] != null)
            {
                navSurfaces[f].BuildNavMesh();
            }
        }
    }

    void MarkSafeRegion(int sx, int sy, bool[,] safe)
    {
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
                    if (nx >= 0 && nx < width && ny >= 0 && ny < height && !safe[nx, ny])
                    {
                        safe[nx, ny] = true;
                        next.Enqueue(new Vector2Int(nx, ny));
                    }
                }
            }
            depth++;
            var tmp = q; q = next; next = tmp;
        }
    }

    // Crear múltiples áreas de patrulla
    void CreatePatrolAreas()
    {
        int numAreas = 3; // cantidad de áreas por piso

        for (int f = 0; f < floors; f++)
        {
            patrolAreas[f] = new List<PatrolArea>();
            int cellsPerArea = Mathf.CeilToInt((float)freeCells[f].Count / numAreas);

            for (int a = 0; a < numAreas; a++)
            {
                PatrolArea area = new PatrolArea();
                int start = a * cellsPerArea;
                int end = Mathf.Min(start + cellsPerArea, freeCells[f].Count);

                for (int i = start; i < end; i++)
                {
                    var cell = freeCells[f][i];
                    var pointObj = new GameObject($"PatrolPoint_F{f}_A{a}_{cell.x}_{cell.y}");
                    pointObj.transform.position = new Vector3(cell.x * cellSize, -f * floorHeight + 0.1f, cell.y * cellSize);
                    area.patrolPoints.Add(pointObj.transform);
                }

                patrolAreas[f].Add(area);
            }
        }
    }

    public PatrolArea GetPatrolArea(int floorIndex, int areaIndex)
    {
        if (patrolAreas == null || floorIndex < 0 || floorIndex >= patrolAreas.Length)
            return null;
        if (areaIndex < 0 || areaIndex >= patrolAreas[floorIndex].Count)
            return null;

        return patrolAreas[floorIndex][areaIndex];
    }

    public int GetTotalPatrolAreas()
    {
        if (patrolAreas == null || patrolAreas[0] == null)
            return 0;
        return patrolAreas[0].Count;
    }
}