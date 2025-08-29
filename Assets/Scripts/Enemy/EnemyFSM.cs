using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
public class EnemyFSM : MonoBehaviour
{
    public enum EnemyState { Idle, Patrol, Chase, Attack, Flee }
    public enum EnemyKind { Basic, Heavy, Runner }

    [System.Flags]
    public enum ScareMask { None = 0, Green = 1, Red = 2, Blue = 4 }

    [Header("Tipo de enemigo")]
    public EnemyKind kind = EnemyKind.Basic;

    [Header("Referencias")]
    public CharacterController controller;
    public Animator animator;

    // ===================== Animación (crossfade por nombre) =====================
    [Header("Animación (crossfade por nombre)")]
    public bool useCrossfadeFallback = true;
    public bool logAnimDebug = false;
    public float animFade = 0.12f;
    public int animLayer = 0;

    [SerializeField] string idleState = "root|Anim_monster_scavenger_idle";
    [SerializeField] string walkState = "root|Anim_monster_scavenger_walk";
    [SerializeField] string runState = "root|Anim_monster_scavenger_walk";
    [SerializeField] string attackState = "root|Anim_monster_scavenger_attack";
    int _lastAnimHash = 0;
    int _targetAnimHash = 0;
    // ===========================================================================

    [Header("Detección")]
    public float detectionRange = 6f;
    public float attackRange = 1.5f;
    public bool requireLineOfSightToChase = false;
    public LayerMask losObstacles = ~0;
    public float recalcPathCellThreshold = 2.0f;
    public float recalculatePathInterval = 1.0f;

    [Header("Memoria de última posición vista")]
    public float lastSeenMemorySeconds = 3.0f;

    [Header("Movimiento")]
    public float patrolSpeed = 2f;
    public float chaseSpeed = 3.5f;
    public float turnSpeedDeg = 540f;

    [Header("Gravedad")]
    public float gravity = 18f;
    public float groundSnap = 2f;
    private float verticalVel = 0f;

    [Header("Daño")]
    public float attackDamage = 10f;
    public float attackCooldown = 1f;

    [Header("Separación anti-colisiones")]
    public float separationRadius = 0.8f;
    public float separationForce = 2.0f;
    public LayerMask enemyLayer = ~0;

    [Header("Anti-atascos")]
    public float stuckRecalcTime = 1.2f;
    public float minMoveSqr = 0.01f;
    public float nudgeProbeFactor = 0.40f;
    public float nudgePush = 0.12f;

    [Header("Mapa dinámico")]
    public float onMapChangedMinInterval = 0.15f;

    [Header("Debug")]
    public bool drawPathGizmos = false;
    public Color pathColor = new Color(0.2f, 0.9f, 1f, 0.8f);

    // ---------- Giro visual ----------
    [Header("Giro visual")]
    [Tooltip("Si asignas el hijo visual (modelo), sólo gira ese. Si lo dejas vacío, gira el GameObject raíz.")]
    public Transform visualRoot;
    [Tooltip("Offset en grados si tu modelo está volteado (ej. 180 si mira hacia atrás).")]
    public float visualYawOffset = 0f;
    [Tooltip("Hacer que el enemigo mire hacia su dirección de movimiento real.")]
    public bool faceByVelocity = true;
    [Tooltip("Velocidad mínima (m/s) para considerar que está moviéndose y girar al frente.")]
    public float faceVelThreshold = 0.05f;

    // ---------- Audio: ÚNICO LOOP ----------
    [Header("Audio (único loop)")]
    public bool enableAudio = true;
    public AudioClip monsterLoop;
    [Range(0f, 1f)] public float loopVolumeIdle = 0f;     // 0 = apagado en Idle
    [Range(0f, 1f)] public float loopVolumeActive = 0.6f; // Walk/Run/Attack/Flee
    public float loopPitch = 1.0f;
    public float pitchJitter = 0.03f;
    public float loopFade = 0.08f;
    public bool spatial3D = true;
    public float spatialBlend = 1f; // 1 = 3D, 0 = 2D
    public float minDistance = 3f;
    public float maxDistance = 25f;
    public AudioRolloffMode rolloff = AudioRolloffMode.Linear;

    private AudioSource loopSrc;
    private Coroutine loopFadeCo;

    // ---- estado interno
    private EnemyState currentState;
    private static Transform player;
    private MultiFloorDynamicMapManager map;

    private List<Vector3> currentPath;
    private int pathIndex;
    private float recalcTimer;
    private float lastAttackTime;

    private float stuckTimer;
    private Vector3 lastPos;

    public int floorIndex = 0;

    private static HashSet<Vector3Int> occupiedPatrolCells = new HashSet<Vector3Int>();
    private Vector2Int reservedCell;
    private bool hasReservedCell = false;

    private Vector3 lastChaseTargetWorld;

    private Vector3 lastSeenPos;
    private float lastSeenTimer;

    private float onMapChangedCooldown = 0f;

    // deslizamiento
    private Vector3 lastHitNormal = Vector3.zero;
    private float lastHitTime = -999f;
    public float hitMemory = 0.15f;

    // jitter por agente
    private int myId;

    // ===================== LUZ / ESPANTO (exposición) =====================
    [Header("Luz y Espanto")]
    [Tooltip("Qué tipos de batería pueden espantar a este enemigo.")]
    public ScareMask scareByBatteries = ScareMask.Green;

    [Tooltip("Umbral base de exposición para espantar.")]
    public float requiredExposure = 1.0f;

    [Tooltip("Multiplicador de umbral cuando el modo es LOW (si este enemigo acepta LOW).")]
    public float requiredExposureLowMultiplier = 1.4f;

    [Tooltip("Exposición por segundo en HIGH (si este enemigo acepta HIGH).")]
    public float exposurePerSecondHigh = 1.0f;

    [Tooltip("Exposición por segundo en LOW (si este enemigo acepta LOW).")]
    public float exposurePerSecondLow = 0.6f;

    [Tooltip("Cuánto decae la exposición por segundo cuando deja de recibir luz.")]
    public float exposureDecayPerSecond = 0.5f;

    [Tooltip("Tiempo de enfriamiento tras espantarse.")]
    public float scareCooldown = 3.0f;

    [Tooltip("Ventana de inmunidad tras espantarse.")]
    public float postScareImmunity = 1.0f;

    [Header("Bonos y multiplicadores")]
    [Range(0f, 1f)] public float centerBonusFactor = 0.25f;
    public float greenBatteryMult = 1.15f;
    public float redBatteryMult = 1.25f;
    public float blueBatteryMult = 1.10f;

    [Header("LOW aplica slow si no espanta")]
    public bool lowModeAppliesSlow = true;
    [Range(0.2f, 1f)] public float lowSlowFactor = 0.75f;
    public float lowSlowDuration = 0.3f;

    [Header("Balance automático por tipo")]
    public bool useKindAutoStats = true;
    [Range(0.4f, 1.25f)] public float globalSpeedScale = 0.80f;
    public float basicPatrol = 1.6f, basicChase = 2.4f, basicDamage = 10f;
    public float heavyPatrol = 1.1f, heavyChase = 1.6f, heavyDamage = 14f;
    public float runnerPatrol = 2.6f, runnerChase = 3.8f, runnerDamage = 6f;

    // runtime exposición/slow
    private float exposure = 0f;
    private float scareLockUntil = -999f;
    private float slowTimer = 0f;
    private float lastLightHitTime = -999f;

 
    private bool IsModeAllowed(PlayerLightController.FlashlightUIMode mode)
    {
        if (mode == PlayerLightController.FlashlightUIMode.Off) return false;

        switch (kind)
        {
            case EnemyKind.Basic:
                return mode == PlayerLightController.FlashlightUIMode.Low ||
                       mode == PlayerLightController.FlashlightUIMode.High;
            case EnemyKind.Heavy:
                return mode == PlayerLightController.FlashlightUIMode.High;
            case EnemyKind.Runner:
                return mode == PlayerLightController.FlashlightUIMode.Low;
        }
        return false;
    }

    private bool IsBatteryAllowed(BatteryType b)
    {
        return (b == BatteryType.Green && (scareByBatteries & ScareMask.Green) != 0) ||
               (b == BatteryType.Red && (scareByBatteries & ScareMask.Red) != 0) ||
               (b == BatteryType.Blue && (scareByBatteries & ScareMask.Blue) != 0);
    }

    // ===================== Unity lifecycle =====================

    void Awake()
    {
        if (!controller) controller = GetComponent<CharacterController>();
        if (!animator) animator = GetComponent<Animator>();
        if (!animator) animator = GetComponentInChildren<Animator>(true);

        if (animator)
        {
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.speed = 1f;
            if (animator.layerCount > animLayer)
                animator.SetLayerWeight(animLayer, 1f);
        }

        if (enableAudio)
        {
            loopSrc = gameObject.AddComponent<AudioSource>();
            loopSrc.playOnAwake = false;
            loopSrc.loop = true;
            loopSrc.spatialBlend = spatial3D ? spatialBlend : 0f;
            loopSrc.minDistance = minDistance;
            loopSrc.maxDistance = maxDistance;
            loopSrc.rolloffMode = rolloff;
            loopSrc.dopplerLevel = 0f;
            loopSrc.volume = 0f;
            if (monsterLoop) loopSrc.clip = monsterLoop;
        }

        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p) player = p.transform;
        }

        myId = GetInstanceID();
    }

    void Start()
    {
        map = MultiFloorDynamicMapManager.Instance;

        if (map)
        {
            floorIndex = Mathf.Clamp(Mathf.RoundToInt(-transform.position.y / map.floorHeight), 0, map.floors - 1);
            map.OnMapUpdated += OnMapChanged;
        }

        currentState = EnemyState.Idle;
        Invoke(nameof(StartPatrolling), 0.75f);

        lastPos = transform.position;
        lastChaseTargetWorld = transform.position;
        lastSeenPos = transform.position;
        lastSeenTimer = 0f;

        if (useKindAutoStats)
        {
            switch (kind)
            {
                case EnemyKind.Basic:
                    patrolSpeed = basicPatrol;
                    chaseSpeed = basicChase;
                    attackDamage = basicDamage;
                    if (scareByBatteries == ScareMask.None) scareByBatteries = ScareMask.Green | ScareMask.Red;
                    break;
                case EnemyKind.Heavy:
                    patrolSpeed = heavyPatrol;
                    chaseSpeed = heavyChase;
                    attackDamage = heavyDamage;
                    if (scareByBatteries == ScareMask.None) scareByBatteries = ScareMask.Red;
                    break;
                case EnemyKind.Runner:
                    patrolSpeed = runnerPatrol;
                    chaseSpeed = runnerChase;
                    attackDamage = runnerDamage;
                    if (scareByBatteries == ScareMask.None) scareByBatteries = ScareMask.Blue;
                    break;
            }
            patrolSpeed *= globalSpeedScale;
            chaseSpeed *= globalSpeedScale;
        }

        UpdateLoopByLogical("Idle");
    }

    void LateUpdate()
    {
        if (!map) return;
        int fNow = Mathf.Clamp(Mathf.RoundToInt(-transform.position.y / map.floorHeight), 0, map.floors - 1);
        if (fNow != floorIndex) floorIndex = fNow;
        if (onMapChangedCooldown > 0f) onMapChangedCooldown -= Time.deltaTime;

        if (faceByVelocity && controller)
        {
            Vector3 v = controller.velocity; v.y = 0f;
            if (v.sqrMagnitude > faceVelThreshold * faceVelThreshold)
                FaceDirection(v, 1f);
        }
    }

    void Update()
    {
        if (Time.time - lastLightHitTime > 0.05f)
            DecayExposure(Time.deltaTime);

        if (slowTimer > 0f) slowTimer -= Time.deltaTime;

        switch (currentState)
        {
            case EnemyState.Idle: StateIdle(); break;
            case EnemyState.Patrol: StatePatrol(); break;
            case EnemyState.Chase: StateChase(); break;
            case EnemyState.Attack: StateAttack(); break;
            case EnemyState.Flee: StateFlee(); break;
        }

        stuckTimer += Time.deltaTime;
        if ((transform.position - lastPos).sqrMagnitude > minMoveSqr)
        {
            stuckTimer = 0f;
            lastPos = transform.position;
        }
        else if (stuckTimer > stuckRecalcTime)
        {
            RecoverFromStuck();
            stuckTimer = 0f;
        }
    }

    // ================== ESTADOS ==================

    void StateIdle()
    {
        SetAnimation("Idle");
        FaceTargetIf(player, 0.5f);
        if (UpdatePlayerSensing()) SwitchToChase();
        ApplyGravity();
    }

    void StatePatrol()
    {
        SetAnimation("Walk");
        MoveAlongPath(patrolSpeed * GetCurrentSpeedMultiplier());

        if (UpdatePlayerSensing())
        {
            ClearReservedCell();
            SwitchToChase();
            return;
        }

        if (ReachedPathEnd())
        {
            ClearReservedCell();
            GoToRandomPatrolPoint();
        }
    }

    void StateChase()
    {
        SetAnimation("Run");

        bool seesNow = UpdatePlayerSensing();
        Vector3 target;

        if (seesNow && player)
            target = player.position;
        else if (lastSeenTimer > 0f)
        {
            target = lastSeenPos;
            lastSeenTimer -= Time.deltaTime;
        }
        else
        {
            currentState = EnemyState.Patrol;
            GoToRandomPatrolPoint();
            return;
        }

        bool needRepath = PathfindingAStar.Instance.ShouldRepathCells(lastChaseTargetWorld, target, recalcPathCellThreshold);
        recalcTimer += Time.deltaTime;

        if (ReachedPathEnd()) recalcTimer = recalculatePathInterval;
        if (needRepath || recalcTimer >= recalculatePathInterval)
        {
            RecalcPathTo(target);
            lastChaseTargetWorld = target;
        }

        MoveAlongPath(chaseSpeed * GetCurrentSpeedMultiplier());
        FaceTowardsPathOrPlayer();

        if (player && seesNow && Vector3.Distance(transform.position, player.position) <= attackRange)
            currentState = EnemyState.Attack;
    }

    void StateAttack()
    {
        SetAnimation("Attack");

        if (!player)
        {
            currentState = EnemyState.Patrol;
            return;
        }

        bool seesNow = UpdatePlayerSensing();
        if (!seesNow && Vector3.Distance(transform.position, player.position) > attackRange)
        {
            currentState = EnemyState.Chase;
            return;
        }

        Vector3 dir = (player.position - transform.position);
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
        {
            dir.Normalize();
            dir += ComputeSeparationLimited() * separationForce;
            dir.y = 0f;
            dir = dir.normalized;
            SlideMove(dir * (chaseSpeed * 0.5f * GetCurrentSpeedMultiplier()));
        }

        FaceTarget(player.position, 1f);

        if (Time.time >= lastAttackTime + attackCooldown)
        {
            var hp = player.GetComponent<PlayerHealth>();
            if (hp != null) hp.TakeDamage(attackDamage);
            lastAttackTime = Time.time;
        }

        if (Vector3.Distance(transform.position, player.position) > attackRange)
            currentState = EnemyState.Chase;

        ApplyGravity();
    }

    void StateFlee()
    {
        SetAnimation("Run");
        MoveAlongPath(chaseSpeed * 1.3f * GetCurrentSpeedMultiplier());
        FaceTowardsPathOrPlayer();

        if (!player) { ApplyGravity(); return; }

        if (Vector3.Distance(transform.position, player.position) > detectionRange * 2f)
        {
            currentState = EnemyState.Patrol;
            GoToRandomPatrolPoint();
        }
        else if (ReachedPathEnd())
        {
            ChooseFleeDestination();
        }
    }

    // ================== MOVIMIENTO / PATH ==================

    void MoveAlongPath(float speed)
    {
        if (currentPath == null || pathIndex >= currentPath.Count)
        {
            if (currentState == EnemyState.Chase) recalcTimer = recalculatePathInterval;
            ApplyGravity();
            return;
        }

        Vector3 targetPos = currentPath[pathIndex];
        targetPos.y = transform.position.y;

        Vector3 to = targetPos - transform.position;
        to.y = 0f;

        float reachDist = MultiFloorDynamicMapManager.Instance.cellSize * 0.45f;
        if (to.sqrMagnitude <= reachDist * reachDist)
        {
            pathIndex++;
            if (pathIndex >= currentPath.Count && currentState == EnemyState.Chase)
                recalcTimer = recalculatePathInterval;

            ApplyGravity();
            return;
        }

        Vector3 dir = to.normalized;
        dir += ComputeSeparationLimited() * separationForce;
        dir.y = 0f;
        dir = dir.normalized;

        SlideMove(dir * speed);

        FaceDirection(dir, 1f);

        if (map != null)
        {
            Vector2Int here = map.WorldToCell(transform.position);
            PathfindingAStar.Instance.RegisterTraversal(floorIndex, here, 0.5f);
        }

        if ((Time.time - lastHitTime) >= hitMemory && to.magnitude < 0.05f)
        {
            Vector3 nudge = ComputeCornerNudge(
                probe: MultiFloorDynamicMapManager.Instance.cellSize * nudgeProbeFactor,
                push: nudgePush
            );
            if (nudge.sqrMagnitude > 0.0001f)
                controller.SimpleMove(nudge / Mathf.Max(Time.deltaTime, 0.0001f));
        }

        ApplyGravity();
    }

    bool ReachedPathEnd() => currentPath == null || pathIndex >= currentPath.Count;

    void RecalcPathTo(Vector3 worldTarget)
    {
        PathfindingAStar.Instance.SetAgentContext(myId);

        var path = PathfindingAStar.Instance.FindPathStrict(floorIndex, transform.position, worldTarget);
        if (path != null && path.Count > 0)
        {
            currentPath = path;
            pathIndex = 0;
            recalcTimer = 0f;
        }
    }

    void GoToRandomPatrolPoint()
    {
        var free = MultiFloorDynamicMapManager.Instance.GetFreeCells(floorIndex);
        if (free.Count == 0) return;

        free.RemoveAll(c => occupiedPatrolCells.Contains(new Vector3Int(c.x, c.y, floorIndex)));
        if (free.Count == 0) return;

        reservedCell = free[Random.Range(0, free.Count)];
        hasReservedCell = true;
        occupiedPatrolCells.Add(new Vector3Int(reservedCell.x, reservedCell.y, floorIndex));

        Vector3 patrolPos = MultiFloorDynamicMapManager.Instance.CellCenterToWorld(reservedCell, floorIndex);
        currentPath = PathfindingAStar.Instance.FindPathStrict(floorIndex, transform.position, patrolPos);
        pathIndex = 0;
        currentState = EnemyState.Patrol;
    }

    void StartPatrolling() => GoToRandomPatrolPoint();

    void ClearReservedCell()
    {
        if (hasReservedCell)
        {
            occupiedPatrolCells.Remove(new Vector3Int(reservedCell.x, reservedCell.y, floorIndex));
            hasReservedCell = false;
        }
    }

    // ================== SLIDE + GRAVEDAD ==================

    void SlideMove(Vector3 horizVelocity)
    {
        if ((Time.time - lastHitTime) < hitMemory && lastHitNormal != Vector3.zero)
            horizVelocity = Vector3.ProjectOnPlane(horizVelocity, lastHitNormal);

        controller.Move(horizVelocity * Time.deltaTime);
    }

    void ApplyGravity()
    {
        if (controller.isGrounded)
        {
            verticalVel = -groundSnap; // pegado al suelo
        }
        else
        {
            verticalVel -= gravity * Time.deltaTime;
        }

        controller.Move(new Vector3(0f, verticalVel, 0f) * Time.deltaTime);
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (Vector3.Dot(hit.normal, Vector3.up) < 0.45f)
        {
            lastHitNormal = hit.normal;
            lastHitTime = Time.time;
        }
    }

    // ================== SEPARACIÓN / NUDGE ==================

    Vector3 ComputeSeparationLimited()
    {
        float cell = (map ? map.cellSize : 1f);
        Vector3 origin = transform.position + Vector3.up * 0.2f;
        float sideProbe = Mathf.Max(0.3f, cell * 0.45f);

        bool wallLeft = Physics.Raycast(origin, -transform.right, sideProbe, losObstacles);
        bool wallRight = Physics.Raycast(origin, transform.right, sideProbe, losObstacles);

        float limiter = 1f;
        if (wallLeft && wallRight) limiter = 0.2f;
        else if (wallLeft || wallRight) limiter = 0.5f;

        return ComputeSeparationVector() * limiter;
    }

    Vector3 ComputeSeparationVector()
    {
        Vector3 acc = Vector3.zero;
        int count = 0;

        Collider[] cols = Physics.OverlapSphere(transform.position, separationRadius, enemyLayer);
        for (int i = 0; i < cols.Length; i++)
        {
            var other = cols[i].transform;
            if (!other || other == this.transform) continue;
            if (enemyLayer == ~0 && !other.CompareTag("Enemy")) continue;

            Vector3 toMe = (transform.position - other.position);
            toMe.y = 0f;
            float d = toMe.magnitude;
            if (d > 0.0001f)
            {
                acc += toMe.normalized / Mathf.Max(0.05f, d);
                count++;
            }
        }

        if (count == 0) return Vector3.zero;
        return acc / count;
    }

    Vector3 ComputeCornerNudge(float probe, float push)
    {
        Vector3 origin = transform.position + Vector3.up * 0.2f;
        Vector3[] dirs = { transform.forward, -transform.forward, transform.right, -transform.right };
        int hits = 0;
        Vector3 acc = Vector3.zero;

        for (int i = 0; i < dirs.Length; i++)
        {
            if (Physics.Raycast(origin, dirs[i], out var hit, probe))
            {
                if (Vector3.Dot(hit.normal, Vector3.up) > 0.5f) continue;
                acc += hit.normal;
                hits++;
            }
        }

        if (hits == 0) return Vector3.zero;

        Vector3 nudge = acc.normalized * push;
        nudge.y = 0f;
        return nudge;
    }

    // ================== MAPA / LINTERNAS ==================

    private void OnMapChanged()
    {
        if (onMapChangedCooldown > 0f) return;
        onMapChangedCooldown = onMapChangedMinInterval;

        Vector3 target;
        if (currentState == EnemyState.Chase && (PlayerHasLOS() || lastSeenTimer > 0f))
            target = PlayerHasLOS() ? player.position : lastSeenPos;
        else if ((currentState == EnemyState.Patrol || currentState == EnemyState.Flee) &&
                 currentPath != null && pathIndex < currentPath.Count)
            target = currentPath[pathIndex];
        else
            return;

        RecalcPathTo(target);
    }
    // Intenta recuperarse cuando avanza muy poco durante un tiempo
    void RecoverFromStuck()
    {
        // 1) Si estamos en CHASE y tenemos (LOS o memoria de última vista), intenta recalcular hacia el jugador/última vista
        if (currentState == EnemyState.Chase && (PlayerHasLOS() || lastSeenTimer > 0f))
        {
            Vector3 tgt = PlayerHasLOS() ? player.position : lastSeenPos;
            if (PathfindingAStar.Instance.TryFindPathStrict(floorIndex, transform.position, tgt, out var p1))
            {
                currentPath = p1;
                pathIndex = 0;
                recalcTimer = 0f;
                return;
            }
        }
        // 2) Si estamos en PATROL o FLEE y ya teníamos un path, intenta recalcular al siguiente punto
        else if ((currentState == EnemyState.Patrol || currentState == EnemyState.Flee) &&
                 currentPath != null && pathIndex < currentPath.Count)
        {
            var tgt = currentPath[pathIndex];
            if (PathfindingAStar.Instance.TryFindPathStrict(floorIndex, transform.position, tgt, out var p2))
            {
                currentPath = p2;
                pathIndex = 0;
                recalcTimer = 0f;
                return;
            }
        }

        // 3) Busca un destino de merodeo (wander) relativamente libre para moverse
        Vector3 wander = PathfindingAStar.Instance.FindWanderTarget(
            floorIndex, transform.position, player, minDistCells: 4f
        );
        if (PathfindingAStar.Instance.TryFindPathStrict(floorIndex, transform.position, wander, out var p3))
        {
            currentPath = p3;
            pathIndex = 0;
            recalcTimer = 0f;
            return;
        }

        // 4) Como último recurso, “centra” al enemigo a la celda actual para destrabarlo suavemente
        if (map != null)
        {
            var c = map.WorldToCell(transform.position);
            Vector3 center = map.CellCenterToWorld(c, floorIndex);
            Vector3 delta = center - transform.position;
            delta.y = 0f;

            if (delta.sqrMagnitude > 0.0001f)
            {
                controller.enabled = false;
                transform.position += Vector3.ClampMagnitude(delta, 0.25f);
                controller.enabled = true;
            }
        }

        // Reintenta pronto
        recalcTimer = recalculatePathInterval;
    }


    // ====== LEGACY (compat) ======
    public void OnFlashlightHit()
    {
        OnFlashlightHitDetailed(BatteryType.Green, PlayerLightController.FlashlightUIMode.High, Time.deltaTime, 0.6f);
    }
    public void OnFlashlightHitByBattery(BatteryType type)
    {
        OnFlashlightHitDetailed(type, PlayerLightController.FlashlightUIMode.High, Time.deltaTime, 0.6f);
    }

    // ====== Handler detallado ======
    public void OnFlashlightHitDetailed(BatteryType battery, PlayerLightController.FlashlightUIMode mode, float dt, float intensity01)
    {
        if (Time.time < scareLockUntil) return;

        lastLightHitTime = Time.time;

        if (!IsBatteryAllowed(battery))
        {
            if (mode == PlayerLightController.FlashlightUIMode.Low && lowModeAppliesSlow)
                ApplyLowSlow(dt);
            return;
        }
        if (!IsModeAllowed(mode))
        {
            if (mode == PlayerLightController.FlashlightUIMode.Low && lowModeAppliesSlow)
                ApplyLowSlow(dt);
            return;
        }

        float rate = (mode == PlayerLightController.FlashlightUIMode.High) ? exposurePerSecondHigh : exposurePerSecondLow;
        float req = requiredExposure;

        if (mode == PlayerLightController.FlashlightUIMode.Low)
            req *= requiredExposureLowMultiplier; // LOW tarda más, si aplica

        float centerBonus = 1f + (centerBonusFactor * Mathf.Clamp01(intensity01));

        float batteryMult = 1f;
        switch (battery)
        {
            case BatteryType.Green: batteryMult = greenBatteryMult; break;
            case BatteryType.Red: batteryMult = redBatteryMult; break;
            case BatteryType.Blue: batteryMult = blueBatteryMult; break;
        }

        exposure += rate * dt * batteryMult * centerBonus;

        if (exposure >= req)
        {
            Scare();
            exposure = 0f;
            LockScare();
        }
    }

    private void DecayExposure(float dt)
    {
        if (exposure <= 0f) return;
        exposure = Mathf.Max(0f, exposure - (exposureDecayPerSecond * dt));
    }

    private void ApplyLowSlow(float dt)
    {
        slowTimer = Mathf.Max(slowTimer, lowSlowDuration);
    }

    private float GetCurrentSpeedMultiplier()
    {
        return (slowTimer > 0f) ? lowSlowFactor : 1f;
    }

    private void LockScare()
    {
        scareLockUntil = Time.time + Mathf.Max(scareCooldown, postScareImmunity);
    }

    private void Scare()
    {
        if (currentState != EnemyState.Flee)
        {
            ClearReservedCell();
            currentState = EnemyState.Flee;
            ChooseFleeDestination();
        }
    }

    private void ChooseFleeDestination()
    {
        var free = MultiFloorDynamicMapManager.Instance.GetFreeCells(floorIndex);
        if (free.Count == 0 || player == null) return;

        Vector2Int farthest = free[0];
        float maxDist = -1f;

        foreach (var cell in free)
        {
            Vector3 cellPos = MultiFloorDynamicMapManager.Instance.CellCenterToWorld(cell, floorIndex);
            float dist = Vector3.Distance(cellPos, player.position);
            if (dist > maxDist) { maxDist = dist; farthest = cell; }
        }

        Vector3 fleePos = MultiFloorDynamicMapManager.Instance.CellCenterToWorld(farthest, floorIndex);
        RecalcPathTo(fleePos);
    }

    // ================== SENSING ==================

    bool UpdatePlayerSensing()
    {
        if (!player) return false;

        float dist = Vector3.Distance(transform.position, player.position);
        if (dist > detectionRange) return false;

        if (requireLineOfSightToChase && !HasLineOfSightTo(player.position))
            return false;

        lastSeenPos = player.position;
        lastSeenTimer = lastSeenMemorySeconds;
        return true;
    }

    bool PlayerHasLOS()
    {
        if (!player) return false;
        if (Vector3.Distance(transform.position, player.position) > detectionRange) return false;
        if (!requireLineOfSightToChase) return true;
        return HasLineOfSightTo(player.position);
    }

    bool HasLineOfSightTo(Vector3 worldTarget)
    {
        Vector3 origin = transform.position + Vector3.up * 0.6f;
        Vector3 target = worldTarget + Vector3.up * 0.6f;
        Vector3 dir = (target - origin).normalized;
        float dist = Vector3.Distance(origin, target);

        return !Physics.Raycast(origin, dir, dist, losObstacles);
    }

    void SwitchToChase()
    {
        currentState = EnemyState.Chase;
        recalcTimer = recalculatePathInterval;
        if (player)
        {
            lastChaseTargetWorld = player.position;
            lastSeenPos = player.position;
            lastSeenTimer = lastSeenMemorySeconds;
        }
        FaceTargetIf(player, 1f);
    }

    // ---------- Helpers de mirada/rotación ----------
    void FaceTowardsPathOrPlayer()
    {
        Vector3? look = null;
        if (currentPath != null && pathIndex < currentPath.Count)
            look = currentPath[pathIndex];
        else if (player)
            look = (PlayerHasLOS() ? player.position : (lastSeenTimer > 0f ? lastSeenPos : transform.position));

        if (look.HasValue) FaceTarget(look.Value, 1f);
    }

    void FaceTargetIf(Transform t, float weight01)
    {
        if (!t) return;
        FaceTarget(t.position, weight01);
    }

    void FaceTarget(Vector3 worldPoint, float weight01)
    {
        Vector3 to = (worldPoint - transform.position);
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return;

        Quaternion look = Quaternion.LookRotation(to.normalized, Vector3.up)
                          * Quaternion.Euler(0f, visualYawOffset, 0f);
        RotateVisualTowards(look, weight01);
    }

    void FaceDirection(Vector3 dir, float weight01)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion look = Quaternion.LookRotation(dir.normalized, Vector3.up)
                          * Quaternion.Euler(0f, visualYawOffset, 0f);
        RotateVisualTowards(look, weight01);
    }

    void RotateVisualTowards(Quaternion look, float weight01)
    {
        float deg = turnSpeedDeg * Mathf.Clamp01(weight01) * Time.deltaTime;
        if (visualRoot)
            visualRoot.rotation = Quaternion.RotateTowards(visualRoot.rotation, look, deg);
        else
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, deg);
    }
    // -----------------------------------------------------------

    // ================== AUDIO / ANIM ==================
    void SetAnimation(string state)
    {
        if (!animator || !useCrossfadeFallback) return;

        string stateName =
            state == "Idle" ? idleState :
            state == "Walk" ? walkState :
            state == "Run" ? runState :
            state == "Attack" ? attackState :
                                idleState;

        int hash = Animator.StringToHash(stateName);
        _targetAnimHash = hash;

        var st = animator.GetCurrentAnimatorStateInfo(animLayer);
        var next = animator.GetNextAnimatorStateInfo(animLayer);

        if (st.shortNameHash == hash || next.shortNameHash == hash) return;
        if (_lastAnimHash == hash && animator.IsInTransition(animLayer)) return;

        animator.CrossFade(hash, animFade, animLayer, 0f);
        _lastAnimHash = hash;

        if (logAnimDebug) Debug.Log($"[EnemyFSM] CrossFade -> {stateName}");
        UpdateLoopByLogical(state);
    }

    void UpdateLoopByLogical(string logical)
    {
        if (!enableAudio || loopSrc == null || monsterLoop == null) return;

        float targetVol = 0f;
        switch (logical)
        {
            case "Walk":
            case "Run":
            case "Attack":
            case "Flee":
                targetVol = loopVolumeActive; break;
            case "Idle":
            default:
                targetVol = loopVolumeIdle; break;
        }

        if (loopSrc.clip != monsterLoop)
        {
            loopSrc.clip = monsterLoop;
            loopSrc.pitch = Mathf.Clamp(1f + Random.Range(-pitchJitter, pitchJitter), 0.1f, 3f) * loopPitch;
            loopSrc.Play();
        }

        if (loopFadeCo != null) StopCoroutine(loopFadeCo);
        loopFadeCo = StartCoroutine(FadeLoopTo(targetVol, loopFade));
    }

    IEnumerator FadeLoopTo(float target, float dur)
    {
        float start = loopSrc.volume;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            loopSrc.volume = Mathf.Lerp(start, target, t / Mathf.Max(dur, 0.0001f));
            yield return null;
        }
        loopSrc.volume = target;
        if (Mathf.Approximately(target, 0f)) loopSrc.Stop();
        loopFadeCo = null;
    }

    void OnDestroy()
    {
        ClearReservedCell();
        if (MultiFloorDynamicMapManager.Instance != null)
            MultiFloorDynamicMapManager.Instance.OnMapUpdated -= OnMapChanged;
    }

    void OnDrawGizmos()
    {
        if (!drawPathGizmos || currentPath == null || currentPath.Count == 0) return;
        Gizmos.color = pathColor;
        for (int i = 0; i < currentPath.Count; i++)
        {
            Gizmos.DrawSphere(currentPath[i], 0.08f);
            if (i + 1 < currentPath.Count) Gizmos.DrawLine(currentPath[i + 1], currentPath[i]);
        }
    }
}