using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
public class EnemyFSM : MonoBehaviour
{
    public enum EnemyState { Idle, Patrol, Chase, Attack, Flee }
    public enum EnemyKind { Basic, Heavy, Runner }

    public EnemyState CurrentState => currentState;

    [Header("Tipo de enemigo")]
    public EnemyKind kind = EnemyKind.Basic;

    [Header("Referencias")]
    public CharacterController controller;
    public Animator animator;

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

    [Header("Hitbox simple (sin layers / sin animator)")]
    public bool useSimpleDamageBox = true;
    public SimpleDamageBox simpleHitbox;

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
    public bool debugScareLogs = false;

    [Header("Giro visual")]
    public Transform visualRoot;
    public float visualYawOffset = 0f;
    public bool faceByVelocity = true;
    public float faceVelThreshold = 0.05f;

    [Header("Audio (único loop)")]
    public bool enableAudio = true;
    public AudioClip monsterLoop;
    [Range(0f, 1f)] public float loopVolumeIdle = 0f;
    [Range(0f, 1f)] public float loopVolumeActive = 0.6f;
    public float loopPitch = 1.0f;
    public float pitchJitter = 0.03f;
    public float loopFade = 0.08f;

    private AudioSource loopSrc;
    private Coroutine loopFadeCo;

    // ---- estado interno
    private EnemyState currentState;
    private EnemyState _prevState;
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

    private int myId;

    // ===== Huida por impacto de luz =====
    [Header("Huida por impacto de luz")]
    [Tooltip("Tiempo que se suprime el ataque tras recibir luz.")]
    public float suppressAttackSeconds = 1.0f;
    [Tooltip("Duración del desvío si no entra en Flee.")]
    public float deflectDuration = 0.8f;
    [Tooltip("Distancia del punto de huida inmediato (opuesto a la luz).")]
    public float immediateRetreatDistance = 4.0f;
    [Tooltip("Knockback inicial al espantarse.")]
    public float scareKnockback = 1.2f;

    // NUEVO: duración de huida por tipo
    [Header("Duración de Flee por tipo")]
    public float fleeSecondsBasic = 2.5f;
    public float fleeSecondsHeavy = 2.0f;
    public float fleeSecondsRunner = 3.0f;

    private float attackSuppressedUntil = -999f;
    private float deflectUntil = -999f;
    private float scareLockUntil = -999f;
    private float fleeUntil = -999f;                  // NUEVO
    private Vector3 lastAwayFromLight = Vector3.zero; // NUEVO

    // ===== Feedback visual (flash de color al espantarse) =====
    [Header("Feedback visual (scare flash)")]
    public bool scareFlashEnabled = true;
    [Range(0.05f, 2f)] public float scareFlashDuration = 0.5f;
    [Range(0f, 8f)] public float scareEmissionIntensity = 2.5f;
    public bool scareAffectBaseColor = true;
    public bool scareAffectEmission = true;
    public Renderer[] scareRenderers;

    private struct MatCache
    {
        public Material mat;
        public bool hasColor; public Color baseColor;
        public bool hasEmission; public Color baseEmission;
    }
    private readonly List<MatCache> _scareMatCache = new List<MatCache>(8);
    private Coroutine _scareFlashCo;

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
            loopSrc.spatialBlend = 1f;
            loopSrc.minDistance = 3f;
            loopSrc.maxDistance = 25f;
            loopSrc.rolloffMode = AudioRolloffMode.Linear;
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

        if (simpleHitbox)
        {
            if (!simpleHitbox.ownerFSM) simpleHitbox.ownerFSM = this;
            simpleHitbox.damage = Mathf.RoundToInt(attackDamage);
            simpleHitbox.SetActive(false);
        }
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
        _prevState = currentState;
        Invoke(nameof(StartPatrolling), 0.75f);

        lastPos = transform.position;
        lastChaseTargetWorld = transform.position;
        lastSeenPos = transform.position;
        lastSeenTimer = 0f;

        UpdateLoopByLogical("Idle");

        if (scareFlashEnabled)
        {
            if (scareRenderers == null || scareRenderers.Length == 0)
                scareRenderers = GetComponentsInChildren<Renderer>(true);

            _scareMatCache.Clear();
            foreach (var r in scareRenderers)
            {
                if (!r) continue;
                foreach (var m in r.materials)
                {
                    if (!m) continue;
                    var cache = new MatCache { mat = m };

                    if (m.HasProperty("_Color"))
                    {
                        cache.hasColor = true;
                        cache.baseColor = m.GetColor("_Color");
                    }
                    if (m.HasProperty("_EmissionColor"))
                    {
                        cache.hasEmission = true;
                        cache.baseEmission = m.IsKeywordEnabled("_EMISSION")
                            ? m.GetColor("_EmissionColor")
                            : Color.black;
                    }
                    _scareMatCache.Add(cache);
                }
            }
        }
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

        // Asegura hitbox off si ataque está suprimido
        if (useSimpleDamageBox && simpleHitbox)
        {
            bool shouldBeOn = (currentState == EnemyState.Attack) && (Time.time >= attackSuppressedUntil);
            if (simpleHitbox.active != shouldBeOn)
            {
                simpleHitbox.damage = Mathf.RoundToInt(attackDamage);
                simpleHitbox.SetActive(shouldBeOn);
            }
        }
    }

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
        MoveAlongPath(patrolSpeed);

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

        // Si estamos en desvío por luz, no recalculamos hacia el jugador aún
        if (Time.time >= deflectUntil)
        {
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
        }

        MoveAlongPath(chaseSpeed);
        FaceTowardsPathOrPlayer();

        if (player && Vector3.Distance(transform.position, player.position) <= attackRange)
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

        // Ataque suprimido -> retroceso leve
        if (Time.time < attackSuppressedUntil)
        {
            if (useSimpleDamageBox && simpleHitbox && simpleHitbox.active) simpleHitbox.SetActive(false);

            Vector3 away = (transform.position - player.position);
            away.y = 0f;
            if (away.sqrMagnitude > 0.0001f)
            {
                away = away.normalized * (chaseSpeed * 0.9f);
                SlideMove(away);
            }
            ApplyGravity();
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
            SlideMove(dir * (chaseSpeed * 0.5f));
        }

        FaceTarget(player.position, 1f);

        if (!useSimpleDamageBox)
        {
            if (Time.time >= lastAttackTime + attackCooldown && Time.time >= attackSuppressedUntil)
            {
                var hp = player.GetComponent<PlayerHealth>();
                if (hp != null) hp.TakeDamage(attackDamage);
                lastAttackTime = Time.time;
            }
        }

        if (Vector3.Distance(transform.position, player.position) > attackRange)
            currentState = EnemyState.Chase;

        ApplyGravity();
    }

    void StateFlee()
    {
        SetAnimation("Run");

        // Salir de Flee por tiempo
        if (Time.time >= fleeUntil)
        {
            currentState = EnemyState.Patrol;
            GoToRandomPatrolPoint();
            return;
        }

        // Si llegamos al destino actual de huida, elegir otro en la misma dirección base
        if (ReachedPathEnd())
        {
            ChooseFleeDestination(lastAwayFromLight);
        }

        MoveAlongPath(chaseSpeed * 1.2f);
        FaceTowardsPathOrPlayer();
    }

    // ===== Navegación / Movimiento =====
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

        float reachDist = MultiFloorDynamicMapManager.Instance ? MultiFloorDynamicMapManager.Instance.cellSize * 0.45f : 0.45f;
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
                probe: (MultiFloorDynamicMapManager.Instance ? MultiFloorDynamicMapManager.Instance.cellSize : 1f) * nudgeProbeFactor,
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
        var free = MultiFloorDynamicMapManager.Instance ? MultiFloorDynamicMapManager.Instance.GetFreeCells(floorIndex) : new List<Vector2Int>();
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

    void SlideMove(Vector3 horizVelocity)
    {
        if ((Time.time - lastHitTime) < hitMemory && lastHitNormal != Vector3.zero)
            horizVelocity = Vector3.ProjectOnPlane(horizVelocity, lastHitNormal);

        controller.Move(horizVelocity * Time.deltaTime);
    }

    void ApplyGravity()
    {
        if (controller.isGrounded)
            verticalVel = -groundSnap;
        else
            verticalVel -= gravity * Time.deltaTime;

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

    void RecoverFromStuck()
    {
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

        recalcTimer = recalculatePathInterval;
    }

    // ========= IMPACTO DE LUZ (desde PlayerLightController) =========
    public void OnLightImpact(Vector3 lightOrigin, Vector3 lightForward, BatteryType battery, PlayerLightController.FlashlightUIMode mode)
    {
        // 0) Desactiva hitbox de ataque inmediato
        if (useSimpleDamageBox && simpleHitbox && simpleHitbox.active) simpleHitbox.SetActive(false);

        // 1) Suprimir ataque
        attackSuppressedUntil = Time.time + Mathf.Max(0.05f, suppressAttackSeconds);

        // 2) Dirección opuesta a la luz
        Vector3 away = (transform.position - lightOrigin);
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = -lightForward;
        away = away.normalized;
        lastAwayFromLight = away; // guardar para Flee dirigido

        // 3) Retiro inmediato
        Vector3 immediateTarget = transform.position + away * Mathf.Max(1f, immediateRetreatDistance);
        if (map)
        {
            var c = map.WorldToCell(immediateTarget);
            immediateTarget = map.CellCenterToWorld(c, floorIndex);
        }
        RecalcPathTo(immediateTarget);
        deflectUntil = Time.time + Mathf.Max(0.1f, deflectDuration);

        // 4) ¿Se debe espantar según la regla?
        if (ShouldScareNow(battery, mode) && Time.time >= scareLockUntil)
        {
            // Knockback
            if (scareKnockback > 0f)
                controller.Move(away * scareKnockback * Time.deltaTime);

            // Cambiar a Flee con duración por tipo
            ClearReservedCell();
            currentState = EnemyState.Flee;
            fleeUntil = Time.time + GetFleeDurationByKind();
            ChooseFleeDestination(away); // Flee dirigido

            // Flash de color
            if (scareFlashEnabled)
            {
                Color flash = ColorForBattery(battery);
                if (_scareFlashCo != null) StopCoroutine(_scareFlashCo);
                _scareFlashCo = StartCoroutine(CoScareFlash(flash));
            }

            scareLockUntil = Time.time + 0.6f; // pequeño cooldown anti-spam
            lastAttackTime = Time.time + Mathf.Max(0.2f, attackCooldown * 0.5f);

            if (debugScareLogs) Debug.Log($"[{name}] FLEE by light (battery={battery}, mode={mode}) for {GetFleeDurationByKind():0.00}s");
        }
        else
        {
            // Si no califica para Flee, al menos saca del Attack
            if (currentState == EnemyState.Attack)
                currentState = EnemyState.Chase;
        }
    }

    private float GetFleeDurationByKind()
    {
        switch (kind)
        {
            case EnemyKind.Basic: return Mathf.Max(0.3f, fleeSecondsBasic);
            case EnemyKind.Heavy: return Mathf.Max(0.3f, fleeSecondsHeavy);
            case EnemyKind.Runner: return Mathf.Max(0.3f, fleeSecondsRunner);
        }
        return 2.0f;
    }

    private bool ShouldScareNow(BatteryType battery, PlayerLightController.FlashlightUIMode mode)
    {
        // Reglas acordadas:
        // Basic -> Verde con Low o High
        // Heavy -> Rojo con High
        // Runner -> Azul con Low
        switch (kind)
        {
            case EnemyKind.Basic:
                return battery == BatteryType.Green &&
                       (mode == PlayerLightController.FlashlightUIMode.Low ||
                        mode == PlayerLightController.FlashlightUIMode.High);

            case EnemyKind.Heavy:
                return battery == BatteryType.Red &&
                       mode == PlayerLightController.FlashlightUIMode.High;

            case EnemyKind.Runner:
                return battery == BatteryType.Blue &&
                       mode == PlayerLightController.FlashlightUIMode.Low;
        }
        return false;
    }

    // ====== Flee dirigido por vector "away" ======
    private void ChooseFleeDestination(Vector3 awayDir)
    {
        if (!player || MultiFloorDynamicMapManager.Instance == null) return;

        var free = MultiFloorDynamicMapManager.Instance.GetFreeCells(floorIndex);
        if (free.Count == 0) return;

        awayDir.y = 0f;
        if (awayDir.sqrMagnitude < 0.0001f)
            awayDir = (transform.position - (player ? player.position : transform.position)).normalized;
        awayDir = awayDir.sqrMagnitude > 0f ? awayDir.normalized : Vector3.forward;

        Vector2Int bestCell = free[0];
        float bestScore = float.NegativeInfinity;

        foreach (var cell in free)
        {
            Vector3 pos = MultiFloorDynamicMapManager.Instance.CellCenterToWorld(cell, floorIndex);
            Vector3 myToCell = (pos - transform.position); myToCell.y = 0f;

            float distScore = myToCell.magnitude;                    // preferir lejos
            float dirScore = Vector3.Dot(myToCell.normalized, awayDir) * 3f; // preferir dirección opuesta a luz

            float score = distScore + dirScore;

            if (score > bestScore)
            {
                bestScore = score;
                bestCell = cell;
            }
        }

        Vector3 fleePos = MultiFloorDynamicMapManager.Instance.CellCenterToWorld(bestCell, floorIndex);
        RecalcPathTo(fleePos);
    }

    // Versión legacy (si no tienes vector): conserva por compat
    private void ChooseFleeDestination()
    {
        Vector3 awayDir = lastAwayFromLight.sqrMagnitude > 0.0001f
            ? lastAwayFromLight
            : (player ? (transform.position - player.position).normalized : Vector3.forward);
        ChooseFleeDestination(awayDir);
    }

    // ===== Utilidad de color por batería =====
    private static Color ColorForBattery(BatteryType battery)
    {
        switch (battery)
        {
            case BatteryType.Green: return new Color(0.2f, 1f, 0.2f);
            case BatteryType.Red: return new Color(1f, 0.2f, 0.2f);
            case BatteryType.Blue: return new Color(0.3f, 0.5f, 1.2f);
            default: return Color.white;
        }
    }

    // ===== Sensado de jugador =====
    bool UpdatePlayerSensing()
    {
        if (!player) return false;
        float dist = Vector3.Distance(transform.position, player.position);
        if (dist > detectionRange) return false;
        if (requireLineOfSightToChase && !HasLineOfSightTo(player.position)) return false;
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
            loopSrc.pitch = Mathf.Clamp(1f + Random.Range(-pitchJitter, pitchJitter), 0.1f, 3f);
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

    // ===== Flash visual de susto =====
    private IEnumerator CoScareFlash(Color flashColor)
    {
        if (_scareMatCache.Count == 0) yield break;

        float t = 0f;
        while (t < scareFlashDuration)
        {
            float x = t / Mathf.Max(0.0001f, scareFlashDuration);
            float k = Mathf.Sin(x * Mathf.PI); // 0→1→0

            for (int i = 0; i < _scareMatCache.Count; i++)
            {
                var c = _scareMatCache[i];
                if (!c.mat) continue;

                if (scareAffectBaseColor && c.hasColor)
                {
                    Color target = Color.Lerp(c.baseColor, flashColor, k);
                    c.mat.SetColor("_Color", target);
                }

                if (scareAffectEmission && c.hasEmission && c.mat.HasProperty("_EmissionColor"))
                {
                    c.mat.EnableKeyword("_EMISSION");
                    Color emissionTarget = flashColor * (k * scareEmissionIntensity);
                    c.mat.SetColor("_EmissionColor", emissionTarget);
                }
            }

            t += Time.deltaTime;
            yield return null;
        }

        // restaurar
        for (int i = 0; i < _scareMatCache.Count; i++)
        {
            var c = _scareMatCache[i];
            if (!c.mat) continue;

            if (scareAffectBaseColor && c.hasColor)
                c.mat.SetColor("_Color", c.baseColor);

            if (scareAffectEmission && c.hasEmission && c.mat.HasProperty("_EmissionColor"))
            {
                c.mat.SetColor("_EmissionColor", c.baseEmission);
                if (c.baseEmission.maxColorComponent <= 0.0001f)
                    c.mat.DisableKeyword("_EMISSION");
                else
                    c.mat.EnableKeyword("_EMISSION");
            }
        }

        _scareFlashCo = null;
    }
}