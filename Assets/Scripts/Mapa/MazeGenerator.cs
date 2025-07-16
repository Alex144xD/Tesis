using UnityEngine;
using System.Collections.Generic;

public class MultiFloorDynamicMapManager : MonoBehaviour
{
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
    public GameObject doorPrefab;              // ← puerta de salida/transición
    public List<GameObject> enemyPrefabs;

    [Header("Dinámica")]
    public float regenerationInterval = 30f;
    public int safeRadiusCells = 5;
    public Transform player;

    // ------- estado interno -------
    private int[,,] maze;                    // [floor, x, y]
    private GameObject[,,] wallObjects;      // instancias de muros
    private List<Vector2Int>[] freeCells;    // por piso: celdas libres
    private List<GameObject>[] spawnedEntities;
    private float regenTimer;
    private int currentFloor = -1;

    void Start()
    {
        // asegurar dimensiones impares
        if (width % 2 == 0) width++;
        if (height % 2 == 0) height++;

        // inicializar estructuras
        maze = new int[floors, width, height];
        wallObjects = new GameObject[floors, width, height];
        freeCells = new List<Vector2Int>[floors];
        spawnedEntities = new List<GameObject>[floors];
        for (int f = 0; f < floors; f++)
            spawnedEntities[f] = new List<GameObject>();

        // 1) generar laberintos
        GenerateAllFloors();
        // 2) instanciar muros y pisos
        InstantiateAllFloors();
        // 3) colocar sólo en el piso 0 inicialmente
        ChangeFloor(0);
    }

    void Update()
    {
        // regeneración parcial de muros
        regenTimer += Time.deltaTime;
        if (regenTimer >= regenerationInterval)
        {
            regenTimer = 0f;
            PartialRegenerate();
        }

        // detectar si cambió de piso el jugador
        int pf = GetPlayerFloor();
        if (pf != currentFloor)
            ChangeFloor(pf);
    }

    // calcula en qué piso está el jugador según su Y
    int GetPlayerFloor()
    {
        int pf = Mathf.Clamp(
            Mathf.RoundToInt(-player.position.y / floorHeight),
            0, floors - 1
        );
        return pf;
    }

    // destruye entidades del piso anterior y coloca en el nuevo
    void ChangeFloor(int newFloor)
    {
        // 1) limpiar pickups/enemigos/puertas del piso anterior
        if (currentFloor >= 0)
        {
            foreach (var go in spawnedEntities[currentFloor])
                if (go) Destroy(go);
            spawnedEntities[currentFloor].Clear();
        }

        // 2) colocar en el piso actual
        PlaceEntitiesOnFloor(newFloor);
        currentFloor = newFloor;
    }

    // coloca sólo en el piso indicado: jugador, fragmentos de alma, baterías, puertas y enemigos
    void PlaceEntitiesOnFloor(int floor)
    {
        var used = new HashSet<Vector2Int>();

        // —————— REPOSICIONAR JUGADOR (sólo en piso 0) ——————
        if (floor == 0)
        {
            var choices = freeCells[floor];
            var cell = choices[Random.Range(0, choices.Count)];
            used.Add(cell);
            float y0 = -floor * floorHeight + 0.1f;
            player.position = new Vector3(
                cell.x * cellSize,
                y0,
                cell.y * cellSize
            );
        }

        // —————— CANTIDADES PARA ESTE PISO ——————
        int soulCount = 1;
        int batteryCount = 2 + floor;
        int enemyCount = Mathf.FloorToInt(
            freeCells[floor].Count * (0.03f + 0.02f * floor)
        );

        // —————— HELPER PARA SPAWNEAR PICKUPS/ENEMIGOS ——————
        System.Action<GameObject> SpawnAtFreeCell = go =>
        {
            var avail = new List<Vector2Int>();
            foreach (var c in freeCells[floor])
                if (!used.Contains(c))
                    avail.Add(c);

            if (avail.Count == 0) { Destroy(go); return; }

            var choice = avail[Random.Range(0, avail.Count)];
            used.Add(choice);
            float y = -floor * floorHeight + 0.1f;
            go.transform.position = new Vector3(
                choice.x * cellSize,
                y,
                choice.y * cellSize
            );
            spawnedEntities[floor].Add(go);
        };

        // —————— SPAWNEAR FRAGMENTOS DE ALMA ——————
        for (int i = 0; i < soulCount; i++)
            SpawnAtFreeCell(Instantiate(soulFragmentPrefab, transform));

        // —————— SPAWNEAR BATERÍAS ——————
        for (int i = 0; i < batteryCount; i++)
            SpawnAtFreeCell(Instantiate(batteryPrefab, transform));

        // —————— SPAWNEAR PUERTA EN UN MURO ——————
        // 1) Elegimos una celda libre al azar
        Vector2Int doorCell = freeCells[floor][Random.Range(0, freeCells[floor].Count)];
        // 2) Barajamos las 4 direcciones para buscar un muro contiguo
        var dirs = new List<Vector2Int> {
        Vector2Int.up, Vector2Int.down,
        Vector2Int.left, Vector2Int.right
    };
        for (int i = 0; i < dirs.Count; i++)
        {
            int r = Random.Range(i, dirs.Count);
            var tmp = dirs[i]; dirs[i] = dirs[r]; dirs[r] = tmp;
        }
        foreach (var d in dirs)
        {
            int mx = doorCell.x + d.x;
            int my = doorCell.y + d.y;
            // si está en rango y hay un muro...
            if (mx >= 0 && mx < width && my >= 0 && my < height &&
                maze[floor, mx, my] == 1 &&
                wallObjects[floor, mx, my] != null)
            {
                // destruimos ese muro
                Destroy(wallObjects[floor, mx, my]);
                wallObjects[floor, mx, my] = null;
                maze[floor, mx, my] = 0;

                // instanciamos la puerta ahí
                float wy = -floor * floorHeight + cellSize / 2f;
                var door = Instantiate(doorPrefab,
                    new Vector3(mx * cellSize, wy, my * cellSize),
                    Quaternion.identity,
                    transform
                );

                // rotación: si el muro era horizontal (left/right), giramos 90°
                if (d == Vector2Int.left || d == Vector2Int.right)
                    door.transform.Rotate(0, 90, 0);

                spawnedEntities[floor].Add(door);
                break;
            }
        }

        // —————— SPAWNEAR ENEMIGOS ——————
        for (int i = 0; i < enemyCount; i++)
        {
            var prefab = enemyPrefabs[Random.Range(0, enemyPrefabs.Count)];
            SpawnAtFreeCell(Instantiate(prefab, transform));
        }
    }


    // ——————————————————————————————————————
    // 1) Generación de laberintos con DFS + Fisher-Yates
    // ——————————————————————————————————————
    void GenerateAllFloors()
    {
        for (int f = 0; f < floors; f++)
            GenerateMazeForFloor(f);

        // asegurarse de que no haya pisos idénticos
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
        var dirs = new List<Vector2Int> {
            Vector2Int.up, Vector2Int.down,
            Vector2Int.left, Vector2Int.right
        };
        // Fisher–Yates shuffle
        for (int i = 0; i < dirs.Count; i++)
        {
            int r = rng.Next(i, dirs.Count);
            var tmp = dirs[i]; dirs[i] = dirs[r]; dirs[r] = tmp;
        }
        foreach (var d in dirs)
        {
            int nx = cx + d.x * 2, ny = cy + d.y * 2;
            if (nx > 0 && nx < width - 1 &&
                ny > 0 && ny < height - 1 &&
                grid[nx, ny] == 1)
            {
                grid[cx + d.x, cy + d.y] = 0;
                CarveDFS(nx, ny, grid, rng);
            }
        }
    }

    // ——————————————————————————————————————
    // 2) Instanciar muros y pisos
    // ——————————————————————————————————————
    void InstantiateAllFloors()
    {
        for (int f = 0; f < floors; f++)
        {
            float yOff = -f * floorHeight;
            freeCells[f] = new List<Vector2Int>();

            // piso
            var floorObj = Instantiate(floorPrefab,
                new Vector3((width - 1) * cellSize / 2f,
                            yOff,
                            (height - 1) * cellSize / 2f),
                Quaternion.identity, transform);
            floorObj.transform.localScale =
                new Vector3(width * cellSize, 1, height * cellSize);

            // muros y lista de celdas libres
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    if (maze[f, x, y] == 1)
                    {
                        var w = Instantiate(wallPrefab,
                            new Vector3(x * cellSize,
                                        yOff + cellSize / 2f,
                                        y * cellSize),
                            Quaternion.identity, transform);
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

    // ——————————————————————————————————————
    // 3) Regeneración parcial de muros
    // ——————————————————————————————————————
    void PartialRegenerate()
    {
        int pf = GetPlayerFloor();
        int px = Mathf.RoundToInt(player.position.x / cellSize);
        int py = Mathf.RoundToInt(player.position.z / cellSize);

        for (int f = 0; f < floors; f++)
        {
            // marcamos safe-region alrededor del jugador sólo en su piso
            bool[,] safe = new bool[width, height];
            if (f == pf)
                MarkSafeRegion(px, py, safe);

            // generamos un buffer DFS
            int[,] buf = new int[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    buf[x, y] = 1;
            CarveDFS(1, 1, buf, new System.Random());

            // parcheamos muros
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    if (f == pf && safe[x, y]) continue;
                    bool was = maze[f, x, y] == 1;
                    bool will = buf[x, y] == 1;
                    if (was != will)
                    {
                        if (will)
                        {
                            var w = Instantiate(wallPrefab,
                                new Vector3(x * cellSize,
                                            -f * floorHeight + cellSize / 2f,
                                            y * cellSize),
                                Quaternion.identity, transform);
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
    }

    // marking BFS para la región segura
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
                    if (nx >= 0 && nx < width &&
                        ny >= 0 && ny < height &&
                        !safe[nx, ny])
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
}