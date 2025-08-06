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

    public event Action OnMapUpdated;

    private int[,,] maze;
    private bool[,,] walkableGrid;
    private GameObject[,,] wallObjects;
    private GameObject[] floorContainers;
    private List<Vector2Int>[] freeCells;
    private List<GameObject>[] spawnedEntities;
    private float regenTimer;
    private int currentFloor = -1;

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
        freeCells = new List<Vector2Int>[floors];
        spawnedEntities = new List<GameObject>[floors];
        floorContainers = new GameObject[floors];

        for (int f = 0; f < floors; f++)
            spawnedEntities[f] = new List<GameObject>();

        GenerateAllFloors();
        InstantiateAllFloors();
        UpdateWalkableGrid();
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

            Instantiate(
                floorPrefab,
                new Vector3((width - 1) * cellSize / 2f, yOff, (height - 1) * cellSize / 2f),
                Quaternion.identity,
                floorContainer.transform
            );

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (maze[f, x, y] == 1)
                    {
                        var wall = Instantiate(wallPrefab,
                            new Vector3(x * cellSize, yOff + cellSize / 2f, y * cellSize),
                            Quaternion.identity,
                            floorContainer.transform);
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

        CarveDFS(1, 1, grid, new System.Random());
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

            CarveDFS(1, 1, buf, new System.Random());

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
        }
        UpdateWalkableGrid();
        OnMapUpdated?.Invoke();
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
            (q, next) = (next, q);
        }
    }

    void UpdateWalkableGrid()
    {
        for (int f = 0; f < floors; f++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    walkableGrid[f, x, y] = (maze[f, x, y] == 0);
                }
            }
        }
    }

    // ---------------- Métodos públicos ----------------

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

    public int GetCurrentFloor()
    {
        return currentFloor;
    }
}
