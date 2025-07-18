using UnityEngine;
using System.Collections.Generic;

public class MultiFloorDynamicMapManager : MonoBehaviour
{
    [Header("Configuración de mapa")]
    [Min(3)] public int width = 21;
    [Min(3)] public int height = 21;
    [Min(1)] public int floors = 3;
    public float cellSize = 1f;    // recalculado en Start()
    public float floorHeight = 4f; // recalculado en Start()

    [Header("Prefabs Maze")]
    public GameObject wallPrefab;
    public GameObject floorPrefab;

    [Header("Prefabs Entidades")]
    public GameObject batteryPrefab;
    public GameObject soulFragmentPrefab;
    public GameObject doorPrefab;
    public List<GameObject> enemyPrefabs;

    [Header("Dinámica")]
    public float baseRegenInterval = 30f;
    [Min(0)] public int safeRadiusCells = 5;
    public Transform player;

    // — estado interno —
    private int[,,] maze;
    private GameObject[,,] wallObjects;
    private List<Vector2Int>[] freeCells;
    private List<GameObject>[] spawnedEntities;
    private GameObject[] floorParents;
    private float regenTimer;
    private float regenerationInterval;
    private int currentFloor = -1;

    // direcciones fijas
    private static readonly Vector2Int[] DIRS = {
        Vector2Int.up, Vector2Int.down,
        Vector2Int.left, Vector2Int.right
    };
    private System.Random rng = new System.Random();

    void Start()
    {
        // — 1) Auto‑detectar tamaños —
        if (wallPrefab != null && wallPrefab.TryGetComponent<MeshRenderer>(out var mrW))
            cellSize = mrW.bounds.size.x;
        if (floorPrefab != null && floorPrefab.TryGetComponent<MeshRenderer>(out var mrF))
            floorHeight = mrF.bounds.size.y;

        // — 2) Asegurar dimensiones impares —
        width |= 1;
        height |= 1;

        // — 3) Inicializar arrays y padres por piso —
        maze = new int[floors, width, height];
        wallObjects = new GameObject[floors, width, height];
        freeCells = new List<Vector2Int>[floors];
        spawnedEntities = new List<GameObject>[floors];
        floorParents = new GameObject[floors];

        for (int f = 0; f < floors; f++)
        {
            spawnedEntities[f] = new List<GameObject>();
            floorParents[f] = new GameObject($"Floor_{f}");
            floorParents[f].transform.SetParent(transform, false);
        }

        // — 4) Generar + Instanciar + Mostrar piso 0 —
        GenerateAllFloors();
        InstantiateAllFloors();
        ChangeFloor(0);
    }

    void Update()
    {
        // evitar NullRef si algo no terminó de inicializarse
        if (freeCells == null || freeCells.Length != floors) return;

        int pf = GetPlayerFloor();
        var cells = freeCells[pf];
        if (cells == null || cells.Count == 0) return;

        // ajustar intervalo de regeneración
        float sf = cells.Count / (float)(width * height);
        regenerationInterval = Mathf.Lerp(
            baseRegenInterval * 1.5f,
            baseRegenInterval * 0.5f,
            sf
        );

        regenTimer += Time.deltaTime;
        if (regenTimer >= regenerationInterval)
        {
            regenTimer = 0f;
            PartialRegenerate();
        }

        if (pf != currentFloor)
            ChangeFloor(pf);
    }

    int GetPlayerFloor()
        => Mathf.Clamp(
            Mathf.RoundToInt(-player.position.y / floorHeight),
            0, floors - 1
        );

    void ChangeFloor(int newFloor)
    {
        // 1) Limpiar entidades del piso anterior
        if (currentFloor >= 0)
        {
            foreach (var go in spawnedEntities[currentFloor])
                if (go) Destroy(go);
            spawnedEntities[currentFloor].Clear();
        }

        // 2) Activar sólo el padre de este piso
        for (int f = 0; f < floors; f++)
            floorParents[f].SetActive(f == newFloor);

        // 3) Generar pickups/enemigos/puerta
        PlaceEntitiesOnFloor(newFloor);
        currentFloor = newFloor;
    }

    void PlaceEntitiesOnFloor(int floor)
    {
        var used = new HashSet<Vector2Int>();
        var cells = freeCells[floor];

        // reposicionar jugador en piso 0
        if (floor == 0)
        {
            var c = cells[rng.Next(cells.Count)];
            used.Add(c);
            float y0 = -floor * floorHeight + 0.1f;
            player.position = new Vector3(c.x * cellSize, y0, c.y * cellSize);
        }

        int soulCount = 1;
        int batteryCount = 2 + floor;
        int enemyCount = Mathf.FloorToInt(cells.Count * (0.03f + 0.02f * floor));

        // helper local
        void SpawnAtFreeCell(GameObject go)
        {
            var avail = new List<Vector2Int>();
            foreach (var cc in cells)
                if (!used.Contains(cc))
                    avail.Add(cc);
            if (avail.Count == 0) { Destroy(go); return; }

            var pick = avail[rng.Next(avail.Count)];
            used.Add(pick);
            float y = -floor * floorHeight + 0.1f;
            go.transform.position = new Vector3(pick.x * cellSize, y, pick.y * cellSize);
            spawnedEntities[floor].Add(go);
        }

        for (int i = 0; i < soulCount; i++) SpawnAtFreeCell(Instantiate(soulFragmentPrefab, transform));
        for (int i = 0; i < batteryCount; i++) SpawnAtFreeCell(Instantiate(batteryPrefab, transform));

        // abrir puerta
        var doorCell = cells[rng.Next(cells.Count)];
        var dirs = new List<Vector2Int>(DIRS);
        Shuffle(dirs);
        foreach (var d in dirs)
        {
            int mx = doorCell.x + d.x, my = doorCell.y + d.y;
            if (mx < 0 || mx >= width || my < 0 || my >= height) continue;
            if (maze[floor, mx, my] != 1) continue;
            var w = wallObjects[floor, mx, my];
            if (!w) continue;

            Destroy(w);
            wallObjects[floor, mx, my] = null;
            maze[floor, mx, my] = 0;

            float wy = -floor * floorHeight + cellSize / 2f;
            var door = Instantiate(doorPrefab,
                new Vector3(mx * cellSize, wy, my * cellSize),
                Quaternion.identity, transform);
            if (d.x != 0) door.transform.Rotate(0, 90, 0);
            spawnedEntities[floor].Add(door);
            break;
        }

        for (int i = 0; i < enemyCount; i++)
        {
            var prefab = enemyPrefabs[rng.Next(enemyPrefabs.Count)];
            SpawnAtFreeCell(Instantiate(prefab, transform));
        }
    }

    void GenerateAllFloors()
    {
        for (int f = 0; f < floors; f++)
            GenerateMazeForFloor(f);

        // asegurar que no queden dos pisos idénticos
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

        CarveDFS(1, 1, grid);

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

    // —— CarveDFS usando copia local de DIRS ——
    void CarveDFS(int cx, int cy, int[,] grid)
    {
        grid[cx, cy] = 0;
        // **importante**: nueva lista en cada llamada
        var dirs = new List<Vector2Int>(DIRS);
        Shuffle(dirs);
        foreach (var d in dirs)
        {
            int nx = cx + d.x * 2, ny = cy + d.y * 2;
            if (nx <= 0 || nx >= width - 1 || ny <= 0 || ny >= height - 1) continue;
            if (grid[nx, ny] != 1) continue;

            grid[cx + d.x, cy + d.y] = 0;
            CarveDFS(nx, ny, grid);
        }
    }

    void InstantiateAllFloors()
    {
        for (int f = 0; f < floors; f++)
        {
            float yOff = -f * floorHeight;
            freeCells[f] = new List<Vector2Int>(width * height / 2);

            // suelo
            var floorObj = Instantiate(floorPrefab,
                new Vector3((width - 1) * cellSize / 2f, yOff, (height - 1) * cellSize / 2f),
                Quaternion.identity, floorParents[f].transform);
            floorObj.transform.localScale = new Vector3(width * cellSize, 1, height * cellSize);

            // muros y lista de libres
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    if (maze[f, x, y] == 1)
                    {
                        var w = Instantiate(wallPrefab,
                            new Vector3(x * cellSize, yOff + cellSize / 2f, y * cellSize),
                            Quaternion.identity, floorParents[f].transform);
                        w.transform.localScale = Vector3.one * cellSize;
                        wallObjects[f, x, y] = w;
                    }
                    else
                    {
                        freeCells[f].Add(new Vector2Int(x, y));
                    }
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
                for (int y = 0; y < height; y++)
                    buf[x, y] = 1;

            CarveDFS(1, 1, buf);

            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    if (f == pf && safe[x, y]) continue;
                    bool was = maze[f, x, y] == 1;
                    bool will = buf[x, y] == 1;
                    if (was == will) continue;

                    if (will)
                    {
                        var w = Instantiate(wallPrefab,
                            new Vector3(x * cellSize, -f * floorHeight + cellSize / 2f, y * cellSize),
                            Quaternion.identity, floorParents[f].transform);
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

    void MarkSafeRegion(int sx, int sy, bool[,] safe)
    {
        var q = new Queue<Vector2Int>();
        var next = new Queue<Vector2Int>();
        safe[sx, sy] = true;
        q.Enqueue(new Vector2Int(sx, sy));
        int depth = 0;

        while (q.Count > 0 && depth < safeRadiusCells)
        {
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var d in DIRS)
                {
                    int nx = c.x + d.x, ny = c.y + d.y;
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height || safe[nx, ny]) continue;
                    safe[nx, ny] = true;
                    next.Enqueue(new Vector2Int(nx, ny));
                }
            }
            depth++;
            (q, next) = (next, q);
        }
    }

    void Shuffle(List<Vector2Int> list)
    {
        int n = list.Count;
        for (int i = 0; i < n - 1; i++)
        {
            int r = rng.Next(i, n);
            var tmp = list[i];
            list[i] = list[r];
            list[r] = tmp;
        }
    }
}

