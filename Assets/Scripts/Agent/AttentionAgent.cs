using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using MoreMountains.Tools;
using MoreMountains.TopDownEngine;
using Unity.MLAgents.Actuators;
using System.Collections.Generic;

/// <summary>
/// AttentionAgent - RL Agent for 2D roguelike shooter (Soul Knight-like)
/// 
/// OBSERVATION SPECIFICATION:
/// ===========================
/// Total observation size: 329 dimensions
///
/// Player Features (13 dims):
///   - Health (1), Ammo (1), Cooldown (1), WeaponReady (1), Speed (1)
///   - Recent Damage (1), Velocity X,Y (2), Padding (5)
///
/// Global Features (6 dims):
///   - Enemy/Bullet/Item/Hazard fractions (4), Time normalized (1), Recent deaths (1)
///
/// Enemy Features (54 dims = 3 enemies × 18):
///   - Per enemy: Pos X,Y (2), Vel X,Y (2), Distance (1), Health (1),
///     Threat, IsAttacking, AttackCooldown (3), Padding (5) = 18 dims
///
/// Bullet Features (120 dims = 10 bullets × 12):
///   - Per bullet: Pos X,Y (2), Vel X,Y (2), Distance (1), TimeToImpact (1),
///     OwnerType (1), Padding (5) = 12 dims
///
/// Item Features (68 dims = 4 items × 17):
///   - Per item: Pos X,Y (2), ItemType one-hot (12), Rarity (1), Distance (1), Padding (2) = 17 dims
///
/// Hazard Features (60 dims = 5 hazards × 12):
///   - Per hazard: Pos X,Y (2), Size (2), IsActive (1), HazardType one-hot (6), Padding (1) = 12 dims
///
/// Wall Features (8 dims = WALL_RAY_COUNT rays × 1):
///   - 8 rays every 45°: normalized distance to nearest wall (0=touching, 1=nothing in range)
///
/// Total = 13 + 6 + 54 + 120 + 68 + 60 + 8 = 329 dims
///
/// CRITICAL: If changing MaxEnemies, MaxBullets, MaxItems, MaxHazards, or WALL_RAY_COUNT,
/// update BehaviorParameters.VectorObservationSize in the Unity Inspector to match!
/// 
/// </summary>
[RequireComponent(typeof(Character))]
[RequireComponent(typeof(AIBrain))]
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(AIDecisionDetectTargetRadius2D))]
[RequireComponent(typeof(CharacterHandleWeapon))]
[RequireComponent(typeof(CharacterMovement))]
[RequireComponent(typeof(CharacterDash2D))]
public class AttentionAgent : Agent
{
    [Header("Reward Shaping Settings")]
    public float DealDamageReward = 0.5f;
    public float TakeDamagePenalty = -0.5f;
    public float KillPlayerReward = 1.0f;
    public float AllEnemiesKilledReward = 1.0f;
    public float AgentDiedPenalty = -1.0f;
    public float TimePenalty = -0.001f;
    public float DodgeSuccessReward = 0.2f;  // Tránh được viên đạn

    [Header("Vision Settings")]
    [Tooltip("Bán kính tầm nhìn cho việc phát hiện các đối tượng")]
    public float VisionRadius = 20f;

    [Header("Wall Perception")]
    [Tooltip("Layer(s) chứa wall colliders (collision tilemap layer).")]
    public LayerMask WallLayerMask;
    [Tooltip("Tầm xa của mỗi wall ray. Nên <= chiều rộng phòng.")]
    public float WallRayLength = 15f;

    [Header("Episodic Coverage Bonus")]
    [Tooltip("Reward khi agent đến grid cell mới trong episode. Set 0 để tắt (dùng cho baseline/icm-only runs).")]
    public float EpisodicCoverageReward = 0.01f;
    [Tooltip("Kích thước mỗi grid cell (world units). 1f = mỗi tile là 1 cell.")]
    public float CoverageGridCellSize = 1f;

    [Header("Object Capacity Settings")]
    [Tooltip("Số lượng enemies tối đa để đưa vào observation")]
    public int MaxEnemies = 3;
    [Tooltip("Số lượng bullets tối đa")]
    public int MaxBullets = 10;
    [Tooltip("Số lượng items tối đa")]
    public int MaxItems = 4;
    [Tooltip("Số lượng hazards tối đa")]
    public int MaxHazards = 5;

    [Header("Weapon Randomization")]
    [Tooltip("Danh sách vũ khí sẽ được chọn ngẫu nhiên khi bắt đầu episode.")]
    public List<Weapon> RandomWeaponPool = new List<Weapon>();
    [Tooltip("Nếu bật, agent sẽ equip ngẫu nhiên một vũ khí từ pool ở mỗi episode.")]
    public bool RandomizeWeaponOnEpisodeBegin = true;

    [Header("Episode Map Generation")]
    [Tooltip("Nếu bật, agent sẽ generate map mới khi bắt đầu episode.")]
    public bool GenerateRandomMapOnEpisodeBegin = false;
    [Tooltip("RoomGenerator dùng để generate map theo text.")]
    public TilemapGenerator EpisodeRoomGenerator;
    [Tooltip("Tự chuyển RoomGenerator sang FolderRandom trước khi generate.")]
    public bool ForceFolderRandomMode = true;

    [Header("Debug")]
    [Tooltip("Logs chosen action branches and context to Unity Console.")]
    public bool DebugActionLogs = false;
    [Tooltip("Log once every N OnActionReceived calls.")]
    public int DebugLogEveryNSteps = 1;

    // --- Hằng số định danh hành động ---
    private const int WALL_RAY_COUNT = 8;   // 8 rays × 45° — adds 8 dims to observation
    private const int BRANCH_MOVE_X = 0;
    private const int BRANCH_MOVE_Y = 1;
    private const int BRANCH_COMBAT = 2;
    private const int MOVE_X_NONE = 0;
    private const int MOVE_X_LEFT = 1;
    private const int MOVE_X_RIGHT = 2;
    private const int MOVE_Y_NONE = 0;
    private const int MOVE_Y_DOWN = 1;
    private const int MOVE_Y_UP = 2;
    private const int COMBAT_NONE = 0;
    private const int COMBAT_ATTACK = 1;
    private const int COMBAT_DASH = 2;

    // --- Components ---
    private AIBrain aiBrain;
    private Health agentHealth;
    private AIDecisionDetectTargetRadius2D detectTargetDecision;
    private CharacterHandleWeapon characterHandleWeapon;
    private CharacterMovement characterMovement;
    private CharacterDash2D characterDash2D;
    private Weapon _currentWeapon;

    // --- State Tracking ---
    [SerializeField] private Transform _currentTarget;
    [SerializeField] private float _previousTargetHealth;
    private Health _currentTargetHealth;
    private Vector2 _lastMoveDirection = Vector2.right;
    private Vector3 agentStartingPosition;
    private int _decisionLogCounter = 0;

    // --- Object Lists (cached to avoid GC allocation) ---
    private List<GameObject> _enemies = new List<GameObject>();
    private List<Projectile> _bullets = new List<Projectile>();
    private List<GameObject> _items = new List<GameObject>();
    private List<GameObject> _hazards = new List<GameObject>();

    // --- Health tracking (single source of truth) ---
    [SerializeField] private float _previousHealth;

    // --- Dodge tracking ---
    [SerializeField] private bool _isDodging;
    [SerializeField] private float _dodgeStartHealth;
    [SerializeField] private float _dodgeStartTime;
    private bool _tookDamageDuringDodge = false;

    // --- Episodic coverage tracking ---
    private readonly HashSet<Vector2Int> _visitedCells = new HashSet<Vector2Int>();

    // --- Character reference ---
    private Character _character;

    protected override void Awake()
    {
        base.Awake();
        aiBrain = GetComponent<AIBrain>();
        agentHealth = GetComponent<Health>();
        detectTargetDecision = GetComponent<AIDecisionDetectTargetRadius2D>();
        characterHandleWeapon = GetComponent<CharacterHandleWeapon>();
        characterMovement = GetComponent<CharacterMovement>();
        characterDash2D = GetComponent<CharacterDash2D>();
        _character = GetComponent<Character>();
        EquipRandomWeaponIfConfigured(); // Must be after characterHandleWeapon is assigned
    }

    public override void Initialize()
    {
        if (aiBrain == null) aiBrain = GetComponent<AIBrain>();
        if (agentHealth == null) agentHealth = GetComponent<Health>();
        if (detectTargetDecision == null) detectTargetDecision = GetComponent<AIDecisionDetectTargetRadius2D>();
        if (characterHandleWeapon == null) characterHandleWeapon = GetComponent<CharacterHandleWeapon>();
        if (characterMovement == null) characterMovement = GetComponent<CharacterMovement>();
        if (characterDash2D == null) characterDash2D = GetComponent<CharacterDash2D>();
        if (_character == null) _character = GetComponent<Character>();

        if (characterHandleWeapon == null)
        {
            Debug.LogError("AttentionAgent: Missing CharacterHandleWeapon!", gameObject);
        }

        if (aiBrain != null)
        {
            aiBrain.ResetBrain();
        }

        if (characterDash2D != null)
        {
            characterDash2D.DashMode = CharacterDash2D.DashModes.Script;
        }

        agentStartingPosition = transform.position;

        // Subscribe to OnDeath so EndEpisode() fires immediately when TopDownEngine
        // kills the agent — before the GameObject is disabled, which would prevent
        // OnActionReceived from ever detecting death.
        if (agentHealth != null)
        {
            agentHealth.OnDeath += HandleAgentDeath;
        }
    }

    private void OnDestroy()
    {
        if (agentHealth != null)
        {
            agentHealth.OnDeath -= HandleAgentDeath;
        }
    }

    private void HandleAgentDeath()
    {
        AddReward(AgentDiedPenalty);
        EndEpisode();
    }

    public override void OnEpisodeBegin()
    {
        GenerateEpisodeMapIfConfigured();

        // RespawnAt resets ConditionState → Normal, re-enables TopDownController,
        // re-enables colliders, resets velocity, and fires OnRevive → re-enables AIBrain.
        Vector3 spawnPos = GetSpawnPosition();
        if (_character != null)
        {
            _character.RespawnAt(spawnPos,
                _lastMoveDirection.x >= 0 ? Character.FacingDirections.East : Character.FacingDirections.West);
        }
        else
        {
            agentHealth.Revive();
            transform.position = spawnPos;
        }

        EquipRandomWeaponIfConfigured();
        EnsureWeaponEquipped();

        // Reset weapon state in case agent died mid-attack (WeaponUse/WeaponDelayBeforeUse stuck).
        if (characterHandleWeapon != null && characterHandleWeapon.CurrentWeapon != null)
        {
            characterHandleWeapon.CurrentWeapon.WeaponState.ChangeState(Weapon.WeaponStates.WeaponIdle);
        }

        _previousHealth = agentHealth.MaximumHealth;
        _isDodging = false;
        _tookDamageDuringDodge = false;
        _visitedCells.Clear();
        _lastMoveDirection = Vector2.right;

        _currentWeapon = characterHandleWeapon.CurrentWeapon;
        _currentTarget = null;
        _currentTargetHealth = null;
        _previousTargetHealth = 0f;
        _decisionLogCounter = 0;

        CacheSurroundingObjects();
        UpdateStickyTarget();
    }

    /// <summary>
    /// Finds the nearest active Enemy and assigns it to aiBrain.Target.
    /// Called at episode begin and whenever the target is lost mid-episode.
    /// </summary>
    private void RefreshTarget()
    {
        CacheSurroundingObjects();
        UpdateStickyTarget();
    }

    private void UpdateStickyTarget()
    {
        if (IsTargetValid(_currentTarget))
        {
            SyncBrainTarget();
            return;
        }

        Transform nearest = null;
        float nearestDist = float.MaxValue;

        for (int i = 0; i < _enemies.Count; i++)
        {
            GameObject enemy = _enemies[i];
            Transform enemyTransform = enemy != null ? enemy.transform : null;
            if (!IsTargetValid(enemyTransform))
            {
                continue;
            }

            float d = Vector2.Distance(transform.position, enemyTransform.position);
            if (d < nearestDist)
            {
                nearestDist = d;
                nearest = enemyTransform;
            }
        }

        SetCurrentTarget(nearest);
        SyncBrainTarget();
    }

    private void SetCurrentTarget(Transform target)
    {
        if (_currentTarget == target)
        {
            return;
        }

        _currentTarget = target;
        _currentTargetHealth = _currentTarget != null ? _currentTarget.GetComponent<Health>() : null;
        _previousTargetHealth = _currentTargetHealth != null ? _currentTargetHealth.CurrentHealth : 0f;
    }

    private bool IsTargetValid(Transform target)
    {
        if (target == null || target == transform || !target.gameObject.activeInHierarchy)
        {
            return false;
        }

        Health health = target.GetComponent<Health>();
        if (health != null && health.CurrentHealth <= 0f)
        {
            return false;
        }

        return Vector2.Distance(transform.position, target.position) <= VisionRadius;
    }

    private void SyncBrainTarget()
    {
        if (aiBrain != null)
        {
            aiBrain.Target = _currentTarget;
        }
    }

    /// <summary>
    /// Returns the spawn position from the RoomGenerator's playerSpawnPointTransform
    /// after map generation, falling back to the original starting position.
    /// </summary>
    private Vector3 GetSpawnPosition()
    {
        if (EpisodeRoomGenerator != null && EpisodeRoomGenerator.playerSpawnPointTransform != null)
            return EpisodeRoomGenerator.playerSpawnPointTransform.position;

        // Fallback: find InitsPoint in scene
        GameObject initsPoint = GameObject.Find("InitsPoint");
        if (initsPoint != null)
            return initsPoint.transform.position;

        return agentStartingPosition;
    }

    private void GenerateEpisodeMapIfConfigured()
    {
        if (EpisodeRoomGenerator == null) return;

        if (GenerateRandomMapOnEpisodeBegin)
        {
            if (ForceFolderRandomMode)
                EpisodeRoomGenerator.mapSelectionMode = TilemapGenerator.MapSelectionMode.FolderRandom;
            EpisodeRoomGenerator.GenerateRoom(); // despawns old enemies + spawns fresh at new map positions
        }
        else
        {
            // Same map, but still need to respawn enemies — handles MaxStep and KillTarget
            // episode ends where TrainingManager's PlayerDeath handler never fires.
            EpisodeRoomGenerator.RespawnEnemies();
        }
    }

    private void EquipRandomWeaponIfConfigured()
    {
        if (!RandomizeWeaponOnEpisodeBegin || characterHandleWeapon == null)
        {
            return;
        }

        if (RandomWeaponPool == null || RandomWeaponPool.Count == 0)
        {
            return;
        }

        List<Weapon> validWeapons = new List<Weapon>();
        for (int i = 0; i < RandomWeaponPool.Count; i++)
        {
            if (RandomWeaponPool[i] != null)
            {
                validWeapons.Add(RandomWeaponPool[i]);
            }
        }

        if (validWeapons.Count == 0)
        {
            return;
        }

        Weapon selectedWeapon = validWeapons[UnityEngine.Random.Range(0, validWeapons.Count)];
        characterHandleWeapon.ChangeWeapon(selectedWeapon, selectedWeapon.WeaponName, false);
        _currentWeapon = characterHandleWeapon.CurrentWeapon;
    }

    private void EnsureWeaponEquipped()
    {
        if (characterHandleWeapon == null)
        {
            return;
        }

        if (characterHandleWeapon.CurrentWeapon != null)
        {
            _currentWeapon = characterHandleWeapon.CurrentWeapon;
            return;
        }

        if (characterHandleWeapon.InitialWeapon != null)
        {
            characterHandleWeapon.ChangeWeapon(
                characterHandleWeapon.InitialWeapon,
                characterHandleWeapon.InitialWeapon.WeaponName,
                false
            );
            _currentWeapon = characterHandleWeapon.CurrentWeapon;
            return;
        }

        if (RandomWeaponPool != null)
        {
            for (int i = 0; i < RandomWeaponPool.Count; i++)
            {
                Weapon fallback = RandomWeaponPool[i];
                if (fallback == null)
                {
                    continue;
                }

                characterHandleWeapon.ChangeWeapon(fallback, fallback.WeaponName, false);
                _currentWeapon = characterHandleWeapon.CurrentWeapon;
                return;
            }
        }

        _currentWeapon = null;
        if (Time.frameCount % 120 == 0)
        {
            Debug.LogWarning("AttentionAgent: CurrentWeapon is null. Set CharacterHandleWeapon.InitialWeapon or assign RandomWeaponPool.", gameObject);
        }
    }

    /// <summary>
    /// Collect all observations: player features, enemies, bullets, items, hazards, global features
    /// </summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        // Guard: if critical components missing, pad all observations with zeros
        if (agentHealth == null || characterHandleWeapon == null)
        {
            for (int i = 0; i < 329; i++) sensor.AddObservation(0f);
            return;
        }

        if (characterHandleWeapon != null)
        {
            _currentWeapon = characterHandleWeapon.CurrentWeapon;
        }
        EnsureWeaponEquipped();

        // === PERFORMANCE: Cache all nearby objects with SINGLE OverlapSphere call ===
        try
        {
            CacheSurroundingObjects();
            UpdateStickyTarget();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AttentionAgent] CollectObservations setup threw: {e}", gameObject);
            for (int i = 0; i < 329; i++) sensor.AddObservation(0f);
            return;
        }

        // === PLAYER FEATURES (13 dims) ===
        CollectPlayerFeatures(sensor);

        // === GLOBAL FEATURES (6 dims) ===
        CollectGlobalFeatures(sensor);

        // === VARIABLE-LENGTH OBJECT LISTS ===
        CollectEnemyFeatures(sensor);
        CollectBulletFeatures(sensor);
        CollectItemFeatures(sensor);
        CollectHazardFeatures(sensor);

        // === WALL FEATURES (8 dims) ===
        CollectWallFeatures(sensor);
    }

    /// <summary>
    /// Single Physics.OverlapSphere call — gathers ALL nearby objects then categorizes them.
    /// PERFORMANCE FIX: Prevents 5 redundant sphere casts per frame.
    /// BUG FIX (v3): Cache GetComponent<Projectile> instead of calling twice.
    /// </summary>
    private void CacheSurroundingObjects()
    {
        _enemies.Clear();
        _bullets.Clear();
        _items.Clear();
        _hazards.Clear();

        Collider2D[] allColliders = Physics2D.OverlapCircleAll(transform.position, VisionRadius);

        foreach (Collider2D col in allColliders)
        {
            if (col.tag == "Enemy")
            {
                _enemies.Add(col.gameObject);
            }
            else if (col.tag == "Item" || col.GetComponent<PickableItem>() != null)
            {
                _items.Add(col.gameObject);
            }
            else if (col.tag == "Hazard")
            {
                _hazards.Add(col.gameObject);
            }
            else
            {
                Projectile proj = col.GetComponent<Projectile>();
                if (proj != null)
                {
                    _bullets.Add(proj);
                }
            }
        }
    }

    private void CollectPlayerFeatures(VectorSensor sensor)
    {
        // Health (normalized 0-1)
        float healthNorm = agentHealth.CurrentHealth / Mathf.Max(1f, agentHealth.MaximumHealth);
        sensor.AddObservation(healthNorm);

        // Ammo / Mana (implement ACTUAL ammo tracking)
        if (_currentWeapon != null && _currentWeapon is Weapon weapon)
        {
            // Try to get actual ammo; if not available, use 1.0f (infinite ammo)
            float ammoNorm = 1.0f;
            if (weapon.WeaponAmmo != null)
            {
                int currentAmmo = weapon.WeaponAmmo.CurrentAmmoAvailable;
                int maxAmmo = weapon.WeaponAmmo.MaxAmmo;
                ammoNorm = maxAmmo > 0 ? currentAmmo / (float)maxAmmo : 1.0f;
            }
            sensor.AddObservation(ammoNorm);

            // Weapon cooldown (normalized 0-1)
            float cooldownLeft = weapon.CooldownTimeLeft;
            float totalCooldown = weapon.TimeBetweenUses;
            float cooldownNorm = (totalCooldown > 0) ? (cooldownLeft / totalCooldown) : 0f;
            sensor.AddObservation(cooldownNorm);

            // Is weapon ready
            bool isWeaponReady = weapon.WeaponState.CurrentState == Weapon.WeaponStates.WeaponIdle;
            sensor.AddObservation(isWeaponReady ? 1.0f : 0.0f);
        }
        else
        {
            sensor.AddObservation(1.0f);  // No weapon = infinite ammo
            sensor.AddObservation(0.0f);  // No cooldown
            sensor.AddObservation(0.0f);  // Not weapon ready
        }

        // Movement speed (use Character from TopDown Engine, not CharacterController)
        if (_character != null)
        {
            TopDownController controller = _character.GetComponent<TopDownController>();
            if (controller != null)
            {
                float speedNorm = controller.Velocity.magnitude / Mathf.Max(1f, 10f); // Assuming max speed around 10
                sensor.AddObservation(Mathf.Clamp01(speedNorm));
            }
            else
            {
                sensor.AddObservation(0.0f);
            }
        }
        else
        {
            sensor.AddObservation(0.0f);
        }

        // Recent damage indicator (1.0 if damaged last frame, else 0.0)
        float currentHealth = agentHealth.CurrentHealth;
        float healthDelta = currentHealth - _previousHealth;
        sensor.AddObservation(healthDelta < 0 ? 1.0f : 0.0f);

        // Player velocity direction (normalized, 2D)
        Vector3 velocity = Vector3.zero;
        if (_character != null)
        {
            TopDownController controller = _character.GetComponent<TopDownController>();
            if (controller != null)
            {
                velocity = controller.Velocity;
            }
        }
        Vector3 velDir = velocity.magnitude > 0.1f ? velocity.normalized : Vector3.zero;
        sensor.AddObservation(velDir.x);
        sensor.AddObservation(velDir.y);

        // Add padding to reach ~20 dimensions
        for (int i = 0; i < 5; i++)
        {
            sensor.AddObservation(0.0f);
        }
    }

    private void CollectGlobalFeatures(VectorSensor sensor)
    {
        // Use cached counts (from CacheSurroundingObjects())
        int enemyCount = _enemies.Count;
        int bulletCount = _bullets.Count;
        int itemCount = _items.Count;
        int hazardCount = _hazards.Count;

        float enemyFraction = Mathf.Clamp01((float)enemyCount / Mathf.Max(1, MaxEnemies));
        sensor.AddObservation(enemyFraction);

        float bulletFraction = Mathf.Clamp01((float)bulletCount / Mathf.Max(1, MaxBullets));
        sensor.AddObservation(bulletFraction);

        float itemFraction = Mathf.Clamp01((float)itemCount / Mathf.Max(1, MaxItems));
        sensor.AddObservation(itemFraction);

        float hazardFraction = Mathf.Clamp01((float)hazardCount / Mathf.Max(1, MaxHazards));
        sensor.AddObservation(hazardFraction);

        // Time in episode (normalized) — use Agent's per-episode StepCount, not global Academy counter
        int maxSteps = MaxStep > 0 ? MaxStep : 1000;
        float timeNorm = Mathf.Clamp01((float)StepCount / Mathf.Max(1f, maxSteps));
        sensor.AddObservation(timeNorm);

        // Recent deaths nearby (placeholder)
        sensor.AddObservation(0.0f);
    }

    /// <summary>
    /// BUG FIX #1: Do NOT call GetEnemiesNearby() — data already cached in CacheSurroundingObjects()
    /// Just sort, no additional Physics queries!
    /// </summary>
    private void CollectEnemyFeatures(VectorSensor sensor)
    {
        // Sort by distance for consistency (use cached data only)
        _enemies.Sort((a, b) =>
        {
            float distA = Vector3.Distance(transform.position, a.transform.position);
            float distB = Vector3.Distance(transform.position, b.transform.position);
            return distA.CompareTo(distB);
        });

        // Observations per enemy: ~16-18 dimensions
        int enemyCount = Mathf.Min(_enemies.Count, MaxEnemies);
        for (int i = 0; i < MaxEnemies; i++)
        {
            if (i < enemyCount)
            {
                GameObject enemy = _enemies[i];
                CollectSingleEnemyFeature(sensor, enemy);
            }
            else
            {
                // Padding: zeros
                for (int j = 0; j < 18; j++) sensor.AddObservation(0.0f);
            }
        }
    }

    private void CollectSingleEnemyFeature(VectorSensor sensor, GameObject enemy)
    {
        Vector3 relativePos = enemy.transform.position - transform.position;
        Vector3 relativeDir = relativePos.magnitude > 0.01f ? relativePos.normalized : Vector3.zero;
        float distance = relativePos.magnitude;

        // Relative position (clamped to vision radius, normalized to [-1, 1])
        float relX = Mathf.Clamp(relativePos.x / VisionRadius, -1f, 1f);
        float relZ = Mathf.Clamp(relativePos.y / VisionRadius, -1f, 1f);
        sensor.AddObservation(relX);
        sensor.AddObservation(relZ);

        // Relative velocity (BUG FIX #4: use Character from TopDown Engine, not CharacterController)
        Character enemyCharacter = enemy.GetComponent<Character>();
        Vector3 relVel = Vector3.zero;
        if (enemyCharacter != null)
        {
            TopDownController enemyController = enemyCharacter.GetComponent<TopDownController>();
            if (enemyController != null)
            {
                relVel = enemyController.Velocity;
            }
        }
        sensor.AddObservation(Mathf.Clamp(relVel.x / 10f, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(relVel.y / 10f, -1f, 1f));

        // Distance (normalized)
        sensor.AddObservation(Mathf.Clamp01(distance / VisionRadius));

        // Health percentage
        Health enemyHealth = enemy.GetComponent<Health>();
        float healthPct = (enemyHealth != null) ? (enemyHealth.CurrentHealth / Mathf.Max(1f, enemyHealth.MaximumHealth)) : 1.0f;
        sensor.AddObservation(healthPct);

        // Threat level (engineered feature): damage_per_hit * attack_frequency / player_health
        // Placeholder: approximate as normalized by health
        float threatLevel = Mathf.Clamp01(1f / (healthPct + 0.1f) * 0.5f);
        sensor.AddObservation(threatLevel);

        // Is attacking (dummy: 0)
        sensor.AddObservation(0.0f);

        // Attack cooldown (dummy: 1.0 if can attack, 0.0 otherwise)
        sensor.AddObservation(1.0f);

        // Padding to reach 18 dimensions
        for (int i = 0; i < 9; i++)
        {
            sensor.AddObservation(0.0f);
        }
    }

    /// <summary>
    /// BUG FIX #1: Do NOT call GetBulletsNearby() — data already cached
    /// </summary>
    private void CollectBulletFeatures(VectorSensor sensor)
    {
        // Sort by distance (use cached data only)
        _bullets.Sort((a, b) =>
        {
            float distA = Vector3.Distance(transform.position, a.transform.position);
            float distB = Vector3.Distance(transform.position, b.transform.position);
            return distA.CompareTo(distB);
        });

        // Observations per bullet: ~11-12 dimensions
        int bulletCount = Mathf.Min(_bullets.Count, MaxBullets);
        for (int i = 0; i < MaxBullets; i++)
        {
            if (i < bulletCount)
            {
                Projectile bullet = _bullets[i];
                CollectSingleBulletFeature(sensor, bullet);
            }
            else
            {
                // Padding
                for (int j = 0; j < 12; j++) sensor.AddObservation(0.0f);
            }
        }
    }

    private void CollectSingleBulletFeature(VectorSensor sensor, Projectile bullet)
    {
        Vector3 relativePos = bullet.transform.position - transform.position;
        float distance = relativePos.magnitude;

        // Relative position
        sensor.AddObservation(Mathf.Clamp(relativePos.x / VisionRadius, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(relativePos.y / VisionRadius, -1f, 1f));

        // Velocity (absolute, as bullet speed is independent)
        Vector2 bulletVel = bullet.GetComponent<Rigidbody2D>()?.linearVelocity ?? Vector2.zero;
        sensor.AddObservation(Mathf.Clamp(bulletVel.x / 20f, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(bulletVel.y / 20f, -1f, 1f));

        // Distance
        sensor.AddObservation(Mathf.Clamp01(distance / VisionRadius));

        // Time to impact (engineered feature)
        float bulletSpeed = bulletVel.magnitude;
        float timeToImpact = (bulletSpeed > 0.1f) ? distance / bulletSpeed : 10f;
        sensor.AddObservation(Mathf.Clamp01(timeToImpact / 5f)); // Horizon = 5 seconds

        // Owner type (dummy)
        sensor.AddObservation(1.0f);

        // Padding
        for (int i = 0; i < 5; i++)
        {
            sensor.AddObservation(0.0f);
        }
    }

    /// <summary>
    /// BUG FIX #1: Do NOT call GetItemsNearby() — data already cached
    /// </summary>
    private void CollectItemFeatures(VectorSensor sensor)
    {
        // Sort by distance (use cached data only)
        _items.Sort((a, b) =>
        {
            float distA = Vector3.Distance(transform.position, a.transform.position);
            float distB = Vector3.Distance(transform.position, b.transform.position);
            return distA.CompareTo(distB);
        });

        // Observations per item: ~16-17 dimensions
        int itemCount = Mathf.Min(_items.Count, MaxItems);
        for (int i = 0; i < MaxItems; i++)
        {
            if (i < itemCount)
            {
                GameObject item = _items[i];
                CollectSingleItemFeature(sensor, item);
            }
            else
            {
                // Padding
                for (int j = 0; j < 17; j++) sensor.AddObservation(0.0f);
            }
        }
    }

    private void CollectSingleItemFeature(VectorSensor sensor, GameObject item)
    {
        Vector3 relativePos = item.transform.position - transform.position;
        float distance = relativePos.magnitude;

        // Relative position
        sensor.AddObservation(Mathf.Clamp(relativePos.x / VisionRadius, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(relativePos.y / VisionRadius, -1f, 1f));

        // Item type (one-hot, assuming max 12 types, placeholder using health as proxy)
        PickableItem pickable = item.GetComponent<PickableItem>();
        int itemType = (pickable != null) ? GetItemTypeIndex(pickable) : 0;
        for (int i = 0; i < 12; i++)
        {
            sensor.AddObservation(i == itemType ? 1.0f : 0.0f);
        }

        // Rarity (dummy)
        sensor.AddObservation(0.5f);

        // Distance
        sensor.AddObservation(Mathf.Clamp01(distance / VisionRadius));

        // Padding
        for (int i = 0; i < 2; i++)
        {
            sensor.AddObservation(0.0f);
        }
    }

    /// <summary>
    /// BUG FIX #1: Do NOT call GetHazardsNearby() — data already cached
    /// </summary>
    private void CollectHazardFeatures(VectorSensor sensor)
    {
        // Sort by distance (use cached data only)
        _hazards.Sort((a, b) =>
        {
            float distA = Vector3.Distance(transform.position, a.transform.position);
            float distB = Vector3.Distance(transform.position, b.transform.position);
            return distA.CompareTo(distB);
        });

        // Observations per hazard: ~12 dimensions
        int hazardCount = Mathf.Min(_hazards.Count, MaxHazards);
        for (int i = 0; i < MaxHazards; i++)
        {
            if (i < hazardCount)
            {
                GameObject hazard = _hazards[i];
                CollectSingleHazardFeature(sensor, hazard);
            }
            else
            {
                // Padding
                for (int j = 0; j < 12; j++) sensor.AddObservation(0.0f);
            }
        }
    }

    private void CollectSingleHazardFeature(VectorSensor sensor, GameObject hazard)
    {
        Vector3 relativePos = hazard.transform.position - transform.position;
        float distance = relativePos.magnitude;

        // Relative position
        sensor.AddObservation(Mathf.Clamp(relativePos.x / VisionRadius, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(relativePos.y / VisionRadius, -1f, 1f));

        // Size (dummy)
        sensor.AddObservation(0.5f);
        sensor.AddObservation(0.5f);

        // Is active
        sensor.AddObservation(1.0f);

        // Hazard type (one-hot, 6 types)
        int hazardType = GetHazardTypeIndex(hazard);
        for (int i = 0; i < 6; i++)
        {
            sensor.AddObservation(i == hazardType ? 1.0f : 0.0f);
        }

        // Padding
        for (int i = 0; i < 1; i++)
        {
            sensor.AddObservation(0.0f);
        }
    }

    /// <summary>
    /// Helper: Get item type index
    /// BUG FIX #3: Use tag-based detection instead of fragile name.Contains()
    /// Tag your item gameobjects: "ItemHealth", "ItemAmmo", "ItemShield", "ItemSpeed", "ItemCoin", "ItemKey"
    /// </summary>
    /// <summary>
    /// Casts WALL_RAY_COUNT rays evenly around the agent against WallLayerMask.
    /// Each ray returns normalized distance to nearest wall (0=touching, 1=nothing in range).
    /// First ray points right (+X), rotates CCW every 45°. Adds 8 observations.
    /// </summary>
    private void CollectWallFeatures(VectorSensor sensor)
    {
        float angleStep = 360f / WALL_RAY_COUNT;
        for (int i = 0; i < WALL_RAY_COUNT; i++)
        {
            float angleRad = i * angleStep * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
            RaycastHit2D hit = Physics2D.Raycast(transform.position, dir, WallRayLength, WallLayerMask);
            sensor.AddObservation(hit.collider != null ? hit.distance / WallRayLength : 1f);
        }
    }

    private Vector2Int WorldToCell(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / CoverageGridCellSize),
            Mathf.FloorToInt(worldPos.y / CoverageGridCellSize)
        );
    }

    private int GetItemTypeIndex(PickableItem item)
    {
        if (item == null) return 0;

        // Primary: Tag-based detection (robust)
        if (item.gameObject.CompareTag("ItemHealth")) return 0;
        if (item.gameObject.CompareTag("ItemAmmo")) return 1;
        if (item.gameObject.CompareTag("ItemShield")) return 2;
        if (item.gameObject.CompareTag("ItemSpeed")) return 3;
        if (item.gameObject.CompareTag("ItemCoin")) return 4;
        if (item.gameObject.CompareTag("ItemKey")) return 5;

        // Fallback: Name-based (fragile, last resort)
        string itemName = item.gameObject.name.ToLower();
        if (itemName.Contains("health")) return 0;
        if (itemName.Contains("ammo")) return 1;
        if (itemName.Contains("shield")) return 2;
        if (itemName.Contains("speed")) return 3;
        if (itemName.Contains("coin")) return 4;
        if (itemName.Contains("key")) return 5;

        return 0;  // Default to health
    }

    /// <summary>
    /// Helper: Get hazard type index
    /// BUG FIX (v3): Use tag-based detection instead of fragile name.Contains()
    /// Tag your hazard gameobjects: "HazardSpike", "HazardFire", "HazardIce", "HazardPit", "HazardLava", "HazardLaser"
    /// </summary>
    private int GetHazardTypeIndex(GameObject hazard)
    {
        if (hazard == null) return 0;

        // Primary: Tag-based detection (robust)
        if (hazard.CompareTag("HazardSpike")) return 0;
        if (hazard.CompareTag("HazardFire")) return 1;
        if (hazard.CompareTag("HazardIce")) return 2;
        if (hazard.CompareTag("HazardPit")) return 3;
        if (hazard.CompareTag("HazardLava")) return 4;
        if (hazard.CompareTag("HazardLaser")) return 5;

        // Fallback: Name-based (fragile, last resort)
        string hazardName = hazard.name.ToLower();
        if (hazardName.Contains("spike")) return 0;
        if (hazardName.Contains("fire")) return 1;
        if (hazardName.Contains("ice")) return 2;
        if (hazardName.Contains("pit") || hazardName.Contains("hole")) return 3;
        if (hazardName.Contains("lava")) return 4;
        if (hazardName.Contains("laser")) return 5;

        return 0;  // Default to spike
    }

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (characterHandleWeapon != null)
        {
            _currentWeapon = characterHandleWeapon.CurrentWeapon;
        }
        EnsureWeaponEquipped();

        if (!IsWeaponReady())
        {
            actionMask.SetActionEnabled(BRANCH_COMBAT, COMBAT_ATTACK, false);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (characterHandleWeapon != null)
        {
            _currentWeapon = characterHandleWeapon.CurrentWeapon;
        }

        CacheSurroundingObjects();
        UpdateStickyTarget();

        float currentHealth = agentHealth.CurrentHealth;
        float healthDelta = currentHealth - _previousHealth;

        // === REWARD CALCULATION ===
        // Damage penalty
        if (healthDelta < 0)
        {
            AddReward(TakeDamagePenalty);
        }

        // Dodge reward tracking: now handled in FixedUpdate() for reliability

        // Damage to current target
        if (_currentTargetHealth != null)
        {
            if (_currentTargetHealth.CurrentHealth < _previousTargetHealth)
            {
                AddReward(DealDamageReward);
            }
            if (_currentTargetHealth.CurrentHealth <= 0)
            {
                AddReward(KillPlayerReward);
                EndEpisode();
                return;
            }
            _previousTargetHealth = _currentTargetHealth.CurrentHealth;
        }

        if (!HasAnyLivingEnemy())
        {
            AddReward(AllEnemiesKilledReward);
            EndEpisode();
            return;
        }

        // Time penalty
        AddReward(TimePenalty);

        // Episodic coverage bonus — reward visiting new grid cells this episode
        if (EpisodicCoverageReward > 0f)
        {
            Vector2Int cell = WorldToCell(transform.position);
            if (_visitedCells.Add(cell))
                AddReward(EpisodicCoverageReward);
        }

        // Update health tracking
        _previousHealth = currentHealth;

        int moveX = actions.DiscreteActions[BRANCH_MOVE_X];
        int moveY = actions.DiscreteActions[BRANCH_MOVE_Y];
        int combat = actions.DiscreteActions[BRANCH_COMBAT];
        Vector2 movement = DecodeMovement(actions.DiscreteActions);
        ApplyMovement(movement);
        ExecuteCombat(combat, movement);
        LogActionDecision(moveX, moveY, combat, movement);
    }

    private bool HasAnyLivingEnemy()
    {
        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        for (int i = 0; i < enemies.Length; i++)
        {
            GameObject enemy = enemies[i];
            if (enemy == null || !enemy.activeInHierarchy)
            {
                continue;
            }

            Health health = enemy.GetComponent<Health>();
            if (health == null || health.CurrentHealth > 0f)
            {
                return true;
            }
        }

        return false;
    }

    private void LogActionDecision(int moveX, int moveY, int combat, Vector2 movement)
    {
        if (!DebugActionLogs)
        {
            return;
        }

        _decisionLogCounter++;
        int every = Mathf.Max(1, DebugLogEveryNSteps);
        if ((_decisionLogCounter % every) != 0)
        {
            return;
        }

        bool hasTarget = _currentTarget != null;
        bool hasWeapon = _currentWeapon != null;
        bool weaponReady = IsWeaponReady();
        string moveXLabel = MoveXToLabel(moveX);
        string moveYLabel = MoveYToLabel(moveY);
        string combatLabel = CombatToLabel(combat);
        Vector3 pos = transform.position;
        float hp = agentHealth != null ? agentHealth.CurrentHealth : -1f;
        float maxHp = agentHealth != null ? agentHealth.MaximumHealth : -1f;

        Debug.Log(
            "[AgentDecision] " +
            "step=" + StepCount +
            " moveX=" + moveX +
            " moveY=" + moveY +
            " combat=" + combat +
            " action=(" + moveXLabel + "," + moveYLabel + "," + combatLabel + ")" +
            " movement=(" + movement.x.ToString("F2") + "," + movement.y.ToString("F2") + ")" +
            " pos=(" + pos.x.ToString("F2") + "," + pos.y.ToString("F2") + ")" +
            " hp=" + hp.ToString("F1") + "/" + maxHp.ToString("F1") +
            " reward=" + GetCumulativeReward().ToString("F3") +
            " target=" + hasTarget +
            " weapon=" + hasWeapon +
            " ready=" + weaponReady,
            gameObject
        );
    }

    private string MoveXToLabel(int moveX)
    {
        switch (moveX)
        {
            case MOVE_X_LEFT: return "Left";
            case MOVE_X_RIGHT: return "Right";
            default: return "NoneX";
        }
    }

    private string MoveYToLabel(int moveY)
    {
        switch (moveY)
        {
            case MOVE_Y_DOWN: return "Down";
            case MOVE_Y_UP: return "Up";
            default: return "NoneY";
        }
    }

    private string CombatToLabel(int combat)
    {
        switch (combat)
        {
            case COMBAT_ATTACK: return "Attack";
            case COMBAT_DASH: return "Dash";
            default: return "NoneCombat";
        }
    }

    private Vector2 DecodeMovement(ActionSegment<int> discreteActions)
    {
        Vector2 movement = Vector2.zero;

        switch (discreteActions[BRANCH_MOVE_X])
        {
            case MOVE_X_LEFT:
                movement.x = -1f;
                break;
            case MOVE_X_RIGHT:
                movement.x = 1f;
                break;
        }

        switch (discreteActions[BRANCH_MOVE_Y])
        {
            case MOVE_Y_DOWN:
                movement.y = -1f;
                break;
            case MOVE_Y_UP:
                movement.y = 1f;
                break;
        }

        return movement.sqrMagnitude > 1f ? movement.normalized : movement;
    }

    private void ApplyMovement(Vector2 movement)
    {
        if (characterMovement != null)
        {
            characterMovement.SetMovement(movement);
        }

        if (movement.sqrMagnitude > 0.0001f)
        {
            _lastMoveDirection = movement.normalized;
        }
    }

    private void ExecuteCombat(int combatAction, Vector2 movement)
    {
        switch (combatAction)
        {
            case COMBAT_ATTACK:
                AttackCurrentTarget();
                break;
            case COMBAT_DASH:
                DashInBestDirection(movement);
                break;
        }
    }

    private void AttackCurrentTarget()
    {
        if (_currentTarget == null || !IsWeaponReady() || characterHandleWeapon == null)
        {
            return;
        }

        Vector3 aimDirection = _currentTarget.position - transform.position;
        if (aimDirection.sqrMagnitude > 0.0001f && _currentWeapon != null)
        {
            Vector3 normalizedAim = aimDirection.normalized;
            FaceAimDirection(normalizedAim);

            WeaponAim weaponAim = _currentWeapon.GetComponent<WeaponAim>();
            if (weaponAim != null)
            {
                weaponAim.SetCurrentAim(GetWeaponAimInput(normalizedAim, weaponAim), true);
            }
        }

        characterHandleWeapon.ShootStart();
    }

    private void FaceAimDirection(Vector3 normalizedAim)
    {
        if (_character == null || _character.Orientation2D == null || Mathf.Abs(normalizedAim.x) <= 0.0001f)
        {
            return;
        }

        _character.Orientation2D.FaceDirection(normalizedAim.x < 0f ? -1 : 1);

        if (_currentWeapon != null)
        {
            _currentWeapon.FlipWeapon();
            _currentWeapon.ApplyOffset();
        }
    }

    private Vector3 GetWeaponAimInput(Vector3 normalizedAim, WeaponAim weaponAim)
    {
        if (weaponAim is WeaponAim2D && _character != null && _character.Orientation2D != null && !_character.Orientation2D.IsFacingRight)
        {
            return -normalizedAim;
        }

        return normalizedAim;
    }

    private void DashInBestDirection(Vector2 movement)
    {
        if (characterDash2D == null)
        {
            return;
        }

        Vector2 dashDirection = movement.sqrMagnitude > 0.0001f
            ? movement.normalized
            : GetDodgeDirectionFromNearestBullet();

        if (dashDirection.sqrMagnitude <= 0.0001f)
        {
            dashDirection = _lastMoveDirection.sqrMagnitude > 0.0001f ? _lastMoveDirection.normalized : Vector2.right;
        }

        characterDash2D.DashMode = CharacterDash2D.DashModes.Script;
        characterDash2D.DashDirection = new Vector3(dashDirection.x, dashDirection.y, 0f);

        _isDodging = true;
        _dodgeStartHealth = agentHealth.CurrentHealth;
        _dodgeStartTime = Time.time;
        _tookDamageDuringDodge = false;

        characterDash2D.DashStart();
    }

    private Vector2 GetDodgeDirectionFromNearestBullet()
    {
        Projectile nearest = null;
        float nearestDist = float.MaxValue;

        for (int i = 0; i < _bullets.Count; i++)
        {
            Projectile bullet = _bullets[i];
            if (bullet == null || !bullet.gameObject.activeInHierarchy)
            {
                continue;
            }

            float distance = Vector2.Distance(transform.position, bullet.transform.position);
            if (distance < nearestDist)
            {
                nearestDist = distance;
                nearest = bullet;
            }
        }

        if (nearest == null)
        {
            return Vector2.zero;
        }

        Vector2 away = (Vector2)(transform.position - nearest.transform.position);
        return away.sqrMagnitude > 0.0001f ? away.normalized : Vector2.zero;
    }

    private bool IsWeaponReady()
    {
        return _currentWeapon != null && _currentWeapon.WeaponState.CurrentState == Weapon.WeaponStates.WeaponIdle;
    }

    private void Update()
    {
        if (aiBrain == null) return;

        // Re-enable brain if something turned it off mid-episode
        if (!aiBrain.BrainActive || !aiBrain.enabled)
        {
            aiBrain.BrainActive = true;
            aiBrain.enabled = true;
        }

        // Re-acquire target if it was destroyed or disabled
        if (aiBrain.Target == null || !aiBrain.Target.gameObject.activeInHierarchy)
        {
            RefreshTarget();
        }
    }

    /// <summary>
    /// BUG FIX #2: Track dodge success reward in FixedUpdate (not OnActionReceived)
    /// Ensures dodge duration window is not missed, and heals don't cause false positives.
    /// Uses _tookDamageDuringDodge bool to track if damage was taken at any point.
    /// </summary>
    private void FixedUpdate()
    {
        if (!_isDodging) return;

        float elapsedTime = Time.time - _dodgeStartTime;
        float currentHealth = agentHealth.CurrentHealth;

        // Track if damage was taken during dodge window
        if (currentHealth < _dodgeStartHealth)
        {
            _tookDamageDuringDodge = true;
        }

        // Dodge duration complete (0.4s passed)
        if (elapsedTime >= 0.4f)
        {
            // Reward only if NO damage was taken at any point during dodge window
            if (!_tookDamageDuringDodge)
            {
                AddReward(DodgeSuccessReward);
            }
            _isDodging = false;
            _tookDamageDuringDodge = false;
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        ActionSegment<int> discreteActions = actionsOut.DiscreteActions;

        discreteActions[BRANCH_MOVE_X] = MOVE_X_NONE;
        discreteActions[BRANCH_MOVE_Y] = MOVE_Y_NONE;
        discreteActions[BRANCH_COMBAT] = COMBAT_NONE;

        if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A))
        {
            discreteActions[BRANCH_MOVE_X] = MOVE_X_LEFT;
        }
        else if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D))
        {
            discreteActions[BRANCH_MOVE_X] = MOVE_X_RIGHT;
        }

        if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S))
        {
            discreteActions[BRANCH_MOVE_Y] = MOVE_Y_DOWN;
        }
        else if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W))
        {
            discreteActions[BRANCH_MOVE_Y] = MOVE_Y_UP;
        }

        if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.LeftShift))
        {
            discreteActions[BRANCH_COMBAT] = COMBAT_DASH;
        }
        else if (Input.GetKey(KeyCode.J) || Input.GetMouseButton(0))
        {
            discreteActions[BRANCH_COMBAT] = COMBAT_ATTACK;
        }
    }
}
