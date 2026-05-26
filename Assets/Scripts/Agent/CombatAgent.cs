using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using MoreMountains.Tools;
using MoreMountains.TopDownEngine;
using Unity.MLAgents.Actuators;
using System.Collections.Generic;

/// <summary>
/// CombatAgent - RL Agent for 2D roguelike shooter (Soul Knight-like)
/// 
/// OBSERVATION SPECIFICATION:
/// ===========================
/// Total observation size: 432 dimensions
///
/// Player Features (13 dims):
///   - Health (1), Ammo (1), Cooldown (1), WeaponReady (1), Speed (1)
///   - Recent Damage (1), Velocity X,Y (2), Padding (5)
///
/// Global Features (4 dims):
///   - Visible enemy fraction (1), Visible bullet fraction (1), Time normalized (1), Recent deaths (1)
///
/// Enemy Features (54 dims = 3 enemies × 18):
///   - Per enemy: Pos X,Y (2), Vel X,Y (2), Distance (1), Health (1),
///     Threat, IsAttacking, AttackCooldown (3), Padding (5) = 18 dims
///
/// Bullet Features (120 dims = 10 bullets × 12):
///   - Per bullet: Pos X,Y (2), Vel X,Y (2), Distance (1), TimeToImpact (1),
///     OwnerType (1), Padding (5) = 12 dims
///
/// Wall Features (16 dims = WALL_RAY_COUNT rays × 1):
///   - 16 rays every 22.5°: normalized distance to nearest wall (0=touching, 1=nothing in range)
///
/// Local Occupancy Grid (225 dims = 15x15 walkability cells):
///   - Agent-centered floor/wall grid, 1=walkable floor and 0=wall/outside/unknown
///
/// Total = 13 + 4 + 225 + 54 + 120 + 16 = 432 dims
///
/// CRITICAL: If changing MaxEnemies, MaxBullets, or WALL_RAY_COUNT,
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
public class CombatAgent : Agent
{
    // Reward tuning is intentionally code-only. Keeping these non-serialized
    // prevents scenes/prefabs from silently overriding training experiments.
    // --- A. Terminal / Kết quả thật ---
    private const float DealDamageReward = 1.0f;
    private const float DamageRewardPerPoint = 0.04f;
    private const float TakeDamagePenalty = -0.3f;
    private const float DamageTakenPenaltyPerPoint = 0.006f;
    private const float KillEnemyReward = 3.0f;
    private const float AllEnemiesKilledReward = 5.0f;
    private const float AgentDiedPenalty = -2.0f;

    // --- B. Định vị & Tiếp cận ---
    private const float ApproachTargetReward = 0.008f;
    private const float MoveAwayFromTargetPenalty = -0.001f;
    private const float IdleNearEnemyPenalty = -0.0005f;
    private const float AggressiveSeekProgressReward = 0.1f;
    private const float AggressiveSeekProgressRewardCap = 0.12f;
    private const float AggressiveSeekDirectionReward = 0.01f;
    private const float AggressiveSeekMoveAwayPenalty = -0.02f;
    private const float AggressiveSeekIdlePenalty = -0.008f;
    private const float AggressiveSeekTargetAcquiredReward = 0.05f;
    private const float CloseTargetNoAttackPenalty = -0.008f;

    // --- C. Thực thi Tấn công ---
    private const float AimAtTargetReward = 0.001f;
    private const float ReadyAttackIntentWithTargetReward = 0.002f;
    private const float CooldownPatienceReward = 0.0001f;
    private const float AttackAlignedReward = 0.015f;
    private const float InvalidAttackPenalty = -0.003f;
    private const float MissedAttackPenalty = -0.12f;
    private const float RepeatedAttackWhilePendingPenalty = -0.02f;

    // --- D. Né tránh ---
    private const float DodgeSuccessReward = 0.1f;
    private const float UsefulDashReward = 0.04f;
    private const float WastefulDashPenalty = -0.002f;

    // --- E. Điều hòa / Meta ---
    private const float TimePenalty = -0.0005f;
    private const float EpisodicCoverageReward = 0f;

    // --- Tham số phụ trợ (không phải reward) ---
    private const int MissedAttackGraceDecisions = 6;
    private const float AggressiveSeekCloseDistance = 2.5f;
    private const float CoverageGridCellSize = 1f;
    private const bool AutoAimAttackAtCurrentTarget = true;   // nguồn [AUTOAIM]: snap aim vào target khi bắn
    private const bool RequireLineOfSightToTarget = true;

    [Header("Vision Settings")]
    [Tooltip("Bán kính tầm nhìn cho việc phát hiện các đối tượng")]
    public float VisionRadius = 20f;

    [Header("Wall Perception")]
    [Tooltip("Layer(s) chứa wall colliders (collision tilemap layer).")]
    public LayerMask WallLayerMask;
    [Tooltip("Tầm xa của mỗi wall ray. Nên <= chiều rộng phòng.")]
    public float WallRayLength = 15f;

    [Header("Object Capacity Settings")]
    [Tooltip("Số lượng enemies tối đa để đưa vào observation")]
    public int MaxEnemies = 3;
    [Tooltip("Số lượng bullets tối đa")]
    public int MaxBullets = 10;
    [Tooltip("Reusable 2D overlap buffer size. Increase if many colliders can be inside VisionRadius.")]
    public int OverlapBufferSize = 128;

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
    [Tooltip("Ends a broken episode if the agent is pushed far away from the episode spawn.")]
    public bool EndEpisodeWhenOutOfBounds = true;
    [Tooltip("Max world-space distance from spawn before the episode is considered broken.")]
    public float MaxDistanceFromSpawnBeforeReset = 80f;
    [Tooltip("Ends a broken episode if any living enemy is pushed far away from the episode spawn.")]
    public bool EndEpisodeWhenEnemyOutOfBounds = true;
    [Tooltip("Max world-space distance from spawn before an enemy is considered out of the playable room.")]
    public float MaxEnemyDistanceFromSpawnBeforeReset = 80f;
    [Tooltip("Ends episode if agent stays still for too many consecutive decisions (stuck against wall or by knockback).")]
    public bool EndEpisodeWhenStuck = true;
    [Tooltip("How many consecutive still decisions before episode ends. At DecisionPeriod=5, 30 decisions ≈ 7.5 seconds stuck.")]
    public int MaxStillDecisionsBeforeReset = 30;
    [Tooltip("Attempts a small safe reposition before ending a grounded stuck episode.")]
    public bool RecoverGroundedStuckBeforeEnding = true;
    [Tooltip("How many consecutive still decisions before trying grounded stuck recovery.")]
    public int GroundedStuckRecoveryDecisionThreshold = 20;
    [Tooltip("Max grounded stuck recoveries allowed in one episode before ending it as broken.")]
    public int MaxGroundedStuckRecoveriesPerEpisode = 3;
    [Tooltip("Small penalty applied when the agent needs grounded stuck recovery.")]
    public float GroundedStuckRecoveryPenalty = -0.05f;
    [Tooltip("Search radius in world units for a safe floor point during grounded stuck recovery.")]
    public float GroundedStuckRecoverySearchRadius = 4f;
    [Tooltip("Spacing in world units between sampled recovery points.")]
    public float GroundedStuckRecoverySearchStep = 0.5f;
    [Tooltip("Minimum clearance from wall/enemy colliders when choosing a grounded stuck recovery point.")]
    public float GroundedStuckRecoveryClearance = 0.45f;
    [Tooltip("Relocates living enemies that appear stuck near geometry before they keep poisoning the episode.")]
    public bool RecoverEnemyStuckDuringTraining = true;
    [Tooltip("How many decisions an enemy can barely move before being considered stuck.")]
    public int EnemyStuckRecoveryDecisionThreshold = 60;
    [Tooltip("Max enemy stuck recoveries allowed in one episode.")]
    public int MaxEnemyStuckRecoveriesPerEpisode = 6;
    [Tooltip("Max recoveries for the same enemy in one episode.")]
    public int MaxEnemyStuckRecoveriesPerEnemy = 2;
    [Tooltip("Last-chance teleport-to-spawn recoveries for the agent per episode before truncating instead of hard EndEpisode. Set 0 to disable (revert to old hard-broken behaviour).")]
    public int MaxAgentStuckRecoveriesPerEpisode = 3;
    [Tooltip("Soft penalty applied when the agent is teleported to spawn area as stuck recovery. Replaces the hard BrokenEpisodePenalty noise that poisoned ICM forward model in run31.")]
    public float AgentStuckRecoveryPenalty = -0.05f;
    [Tooltip("World-space movement below this value counts as still for enemy stuck detection.")]
    public float EnemyStuckDistanceEpsilon = 0.04f;
    [Tooltip("Minimum distance from the agent when choosing an enemy recovery point.")]
    public float EnemyStuckRecoveryMinDistanceFromAgent = 2f;
    [Tooltip("Search radius in world units for a safe enemy recovery point.")]
    public float EnemyStuckRecoverySearchRadius = 5f;
    [Tooltip("Spacing in world units between sampled enemy recovery points.")]
    public float EnemyStuckRecoverySearchStep = 0.5f;
    [Tooltip("Clearance from wall/player/other enemy colliders when choosing enemy recovery points.")]
    public float EnemyStuckRecoveryClearance = 0.45f;
    [Tooltip("Ends episode if agent makes too many consecutive invalid attacks (weapon not ready or no target).")]
    public bool EndEpisodeOnConsecutiveInvalid = true;
    [Tooltip("How many consecutive invalid attack actions before episode ends.")]
    public int MaxConsecutiveInvalidAttacks = 20;
    [Tooltip("Penalty used when ending a broken episode early.")]
    public float BrokenEpisodePenalty = -1f;
    [Tooltip("Disables damage knockback on the training agent and spawned enemies. This reduces physics-driven broken episodes during RL training.")]
    public bool DisableTrainingKnockback = true;

    [Header("Debug")]
    [Tooltip("Logs chosen action branches and context to Unity Console.")]
    public bool DebugActionLogs = false;
    [Tooltip("Log once every N OnActionReceived calls.")]
    public int DebugLogEveryNSteps = 1;
    [Tooltip("Logs a warning when the agent keeps receiving decisions but barely changes position.")]
    public bool DebugStuckLogs = true;
    [Tooltip("How many logged decisions with almost no position change before a stuck warning is printed.")]
    public int DebugStuckDecisionThreshold = 10;
    [Tooltip("Max world-space movement between logged decisions that counts as standing still.")]
    public float DebugStuckDistanceEpsilon = 0.05f;

    [Header("Aggressive Seek Training")]
    [Tooltip("Adds extra reward for moving toward the nearest living enemy even before it is a valid line-of-sight target. Can also be enabled by trainer env parameter 'aggressive_seek'=1.")]
    public bool UseAggressiveSeekReward = false;
    [Tooltip("Only allows attack actions when the current target is inside this distance. Set <= 0 to disable range gating.")]
    public float TrainingAttackMaxDistance = 2.8f;

    // --- Hằng số định danh hành động ---
    public const int VectorObservationSize = 432;
    private const int LOCAL_GRID_SIZE = 15;
    private const int LOCAL_GRID_RADIUS = LOCAL_GRID_SIZE / 2;
    private const int WALL_RAY_COUNT = 16;   // 16 rays × 22.5° — adds 16 dims to observation
    private const int BRANCH_MOVE_X = 0;
    private const int BRANCH_MOVE_Y = 1;
    private const int BRANCH_COMBAT = 2;
    private const int BRANCH_AIM = 3;
    private const int MOVE_X_NONE = 0;
    private const int MOVE_X_LEFT = 1;
    private const int MOVE_X_RIGHT = 2;
    private const int MOVE_Y_NONE = 0;
    private const int MOVE_Y_DOWN = 1;
    private const int MOVE_Y_UP = 2;
    private const int COMBAT_NONE = 0;
    private const int COMBAT_ATTACK = 1;
    private const int COMBAT_DASH = 2;
    private const int AIM_KEEP = 0;
    private const int AIM_RIGHT = 1;
    private const int AIM_UP_RIGHT = 2;
    private const int AIM_UP = 3;
    private const int AIM_UP_LEFT = 4;
    private const int AIM_LEFT = 5;
    private const int AIM_DOWN_LEFT = 6;
    private const int AIM_DOWN = 7;
    private const int AIM_DOWN_RIGHT = 8;

    // --- Components ---
    private AIBrain aiBrain;
    private Health agentHealth;
    private AIDecisionDetectTargetRadius2D detectTargetDecision;
    private CharacterHandleWeapon characterHandleWeapon;
    private CharacterMovement characterMovement;
    private CharacterDash2D characterDash2D;
    private TopDownController2D topDownController2D;
    private Weapon _currentWeapon;

    // --- State Tracking ---
    [SerializeField] private Transform _currentTarget;
    private float _previousTotalEnemyHealth = 0f;
    private int _previousEnemyCount = 0;
    private float _previousTargetDistance = -1f;
    private Vector2 _lastMoveDirection = Vector2.right;
    private Vector2 _lastAimDirection = Vector2.right;
    private Vector3 agentStartingPosition;
    private int _decisionLogCounter = 0;
    private Vector3 _lastDebugPosition;
    private int _stillDecisionCount = 0;
    private int _consecutiveInvalidAttacks = 0;
    private int _groundedStuckRecoveries = 0;
    private int _agentStuckRecoveries = 0;
    private float _previousSeekEnemyDistance = -1f;
    private bool _hadTargetLastDecision = false;
    private int _pendingAttackDecision = -1;
    private float _pendingAttackEnemyHealth = -1f;

    // --- Object Lists (cached to avoid GC allocation) ---
    private List<GameObject> _enemies = new List<GameObject>();
    private List<Projectile> _bullets = new List<Projectile>();
    private readonly List<GameObject> _trackedEnemies = new List<GameObject>();
    private readonly Dictionary<GameObject, Vector3> _lastEnemyPositions = new Dictionary<GameObject, Vector3>();
    private readonly Dictionary<GameObject, int> _enemyStillDecisionCounts = new Dictionary<GameObject, int>();
    private readonly Dictionary<GameObject, int> _enemyRecoveryCounts = new Dictionary<GameObject, int>();
    private Collider2D[] _overlapResults;
    private ContactFilter2D _overlapFilter;

    // --- Health tracking (single source of truth) ---
    [SerializeField] private float _previousHealth;

    // --- Dodge tracking ---
    [SerializeField] private bool _isDodging;
    [SerializeField] private float _dodgeStartHealth;
    [SerializeField] private float _dodgeStartTime;
    private bool _tookDamageDuringDodge = false;

    // --- Episodic coverage tracking ---
    private readonly HashSet<Vector2Int> _visitedCells = new HashSet<Vector2Int>();

    // --- Training diagnostics ---
    private float _episodeDamageDealt;
    private float _episodeDamageTaken;
    private int _episodeKills;
    private int _episodeAttackActions;
    private int _episodeAlignedAttacks;
    private int _episodeInvalidAttacks;
    private int _episodeDashActions;
    private int _episodeUsefulDashes;
    private int _episodeWastefulDashes;
    private int _episodeDecisionCount;
    private int _episodeDecisionsWithTarget;
    private int _episodeAttacksWithTarget;
    private int _episodeAttacksWithoutTarget;
    private int _episodeAttacksWhileWeaponReady;
    private int _episodeAttacksWhileWeaponNotReady;
    private int _episodeCooldownPatienceDecisions;
    private int _episodeBlockedAttackActions;
    private int _episodeBlockedDashActions;
    private int _episodeEnemyStuckRecoveries;
    private int _episodeCellsVisited;
    private BrokenEpisodeReason _episodeBrokenReason = BrokenEpisodeReason.None;
    private bool _episodeStatsRecorded;

    private enum BrokenEpisodeReason
    {
        None,
        Stuck,
        ConsecutiveInvalidAttacks,
        AgentOutOfBounds,
        EnemyOutOfBounds
    }

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
        topDownController2D = GetComponent<TopDownController2D>();
        _character = GetComponent<Character>();
        InitializeReusableBuffers();
        ConfigureWeaponAimForTraining();
        if (_character != null && _character.Orientation2D != null)
        {
            _character.Orientation2D.FacingMode = CharacterOrientation2D.FacingModes.None;
        }
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
        if (topDownController2D == null) topDownController2D = GetComponent<TopDownController2D>();
        if (_character == null) _character = GetComponent<Character>();
        InitializeReusableBuffers();

        if (characterHandleWeapon == null)
        {
            Debug.LogError("CombatAgent: Missing CharacterHandleWeapon!", gameObject);
        }
        ConfigureWeaponAimForTraining();

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
        RecordEpisodeStats(0f, 1f);
        EndEpisode();
    }

    public override void OnEpisodeBegin()
    {
        if (!_episodeStatsRecorded && _episodeDecisionCount > 0)
        {
            RecordEpisodeStats(0f, 0f);
        }
        ResetEpisodeStats();
        GenerateEpisodeMapIfConfigured();
        ConfigureWeaponAimForTraining();
        EnsureCurrentWeaponStateInitialized();

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
        StabilizeTrainingCharacter(gameObject);

        EquipRandomWeaponIfConfigured();
        EnsureWeaponEquipped();
        EnsureCurrentWeaponStateInitialized();

        // Reset weapon state in case agent died mid-attack (WeaponUse/WeaponDelayBeforeUse stuck).
        if (characterHandleWeapon != null && characterHandleWeapon.CurrentWeapon != null && characterHandleWeapon.CurrentWeapon.WeaponState != null)
        {
            characterHandleWeapon.CurrentWeapon.WeaponState.ChangeState(Weapon.WeaponStates.WeaponIdle);
        }

        _previousHealth = agentHealth.MaximumHealth;
        _isDodging = false;
        _tookDamageDuringDodge = false;
        _visitedCells.Clear();
        _lastMoveDirection = Vector2.right;
        _lastAimDirection = Vector2.right;

        _currentWeapon = characterHandleWeapon.CurrentWeapon;
        _currentTarget = null;
        _previousTotalEnemyHealth = 0f;
        _previousEnemyCount = 0;
        _previousTargetDistance = -1f;
        _decisionLogCounter = 0;
        _lastDebugPosition = transform.position;
        _stillDecisionCount = 0;
        _consecutiveInvalidAttacks = 0;
        _groundedStuckRecoveries = 0;
        _agentStuckRecoveries = 0;

        RefreshTrackedEnemies();
        StabilizeTrackedEnemies();
        ResetEnemyStuckTracking();
        CacheSurroundingObjects();
        UpdateStickyTarget();
        _previousTotalEnemyHealth = GetTotalEnemyHealth();
        _previousEnemyCount = GetLivingEnemyCount();
        _previousTargetDistance = GetCurrentTargetDistance();
        _previousSeekEnemyDistance = -1f;
        _hadTargetLastDecision = IsTargetValid(_currentTarget);
        ClearPendingAttack();
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
        _previousTargetDistance = GetCurrentTargetDistance();
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

        return Vector2.Distance(transform.position, target.position) <= VisionRadius
            && HasLineOfSightTo(target);
    }

    private bool HasLineOfSightTo(Transform target)
    {
        if (!RequireLineOfSightToTarget || target == null || WallLayerMask.value == 0)
        {
            return true;
        }

        Vector2 origin = transform.position;
        Vector2 destination = target.position;
        Vector2 direction = destination - origin;
        float distance = direction.magnitude;
        if (distance <= 0.0001f)
        {
            return true;
        }

        RaycastHit2D wallHit = Physics2D.Raycast(origin, direction / distance, distance, WallLayerMask);
        return wallHit.collider == null;
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
        ConfigureWeaponAimForTraining();
        EnsureCurrentWeaponStateInitialized();
        characterHandleWeapon.ChangeWeapon(selectedWeapon, selectedWeapon.WeaponName, false);
        _currentWeapon = characterHandleWeapon.CurrentWeapon;
        ForceScriptAimControl();
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
            ForceScriptAimControl();
            return;
        }

        if (characterHandleWeapon.InitialWeapon != null)
        {
            ConfigureWeaponAimForTraining();
            EnsureCurrentWeaponStateInitialized();
            characterHandleWeapon.ChangeWeapon(
                characterHandleWeapon.InitialWeapon,
                characterHandleWeapon.InitialWeapon.WeaponName,
                false
            );
            _currentWeapon = characterHandleWeapon.CurrentWeapon;
            ForceScriptAimControl();
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

                ConfigureWeaponAimForTraining();
                EnsureCurrentWeaponStateInitialized();
                characterHandleWeapon.ChangeWeapon(fallback, fallback.WeaponName, false);
                _currentWeapon = characterHandleWeapon.CurrentWeapon;
                ForceScriptAimControl();
                return;
            }
        }

        _currentWeapon = null;
        if (Time.frameCount % 120 == 0)
        {
            Debug.LogWarning("CombatAgent: CurrentWeapon is null. Set CharacterHandleWeapon.InitialWeapon or assign RandomWeaponPool.", gameObject);
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
            for (int i = 0; i < VectorObservationSize; i++) sensor.AddObservation(0f);
            return;
        }

        if (characterHandleWeapon != null)
        {
            _currentWeapon = characterHandleWeapon.CurrentWeapon;
        }
        EnsureWeaponEquipped();

        // === PERFORMANCE: Cache nearby combat objects with a non-alloc overlap query ===
        try
        {
            CacheSurroundingObjects();
            UpdateStickyTarget();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[CombatAgent] CollectObservations setup threw: {e}", gameObject);
            for (int i = 0; i < VectorObservationSize; i++) sensor.AddObservation(0f);
            return;
        }

        // === PLAYER FEATURES (13 dims) ===
        CollectPlayerFeatures(sensor);

        // === GLOBAL FEATURES (4 dims) ===
        CollectGlobalFeatures(sensor);

        // === LOCAL OCCUPANCY GRID (225 dims) ===
        CollectLocalOccupancyGrid(sensor);

        // === VARIABLE-LENGTH OBJECT LISTS ===
        CollectEnemyFeatures(sensor);
        CollectBulletFeatures(sensor);

        // === WALL FEATURES (16 dims) ===
        CollectWallFeatures(sensor);
    }

    /// <summary>
    /// Single non-alloc Physics2D overlap query — gathers nearby combat objects.
    /// PERFORMANCE FIX: prevents per-decision collider array allocation.
    /// BUG FIX (v3): Cache GetComponent<Projectile> instead of calling twice.
    /// </summary>
    private void CacheSurroundingObjects()
    {
        InitializeReusableBuffers();

        _enemies.Clear();
        _bullets.Clear();

        int hitCount = Physics2D.OverlapCircle(transform.position, VisionRadius, _overlapFilter, _overlapResults);
        for (int i = 0; i < hitCount; i++)
        {
            Collider2D col = _overlapResults[i];
            if (col == null)
            {
                continue;
            }

            if (col.CompareTag("Enemy"))
            {
                if (IsTargetValid(col.transform))
                {
                    _enemies.Add(col.gameObject);
                }
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

        System.Array.Clear(_overlapResults, 0, hitCount);
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
            bool isWeaponReady = weapon.WeaponState != null
                && weapon.WeaponState.CurrentState == Weapon.WeaponStates.WeaponIdle;
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

        float enemyFraction = Mathf.Clamp01((float)enemyCount / Mathf.Max(1, MaxEnemies));
        sensor.AddObservation(enemyFraction);

        float bulletFraction = Mathf.Clamp01((float)bulletCount / Mathf.Max(1, MaxBullets));
        sensor.AddObservation(bulletFraction);

        // Time in episode (normalized) — use Agent's per-episode StepCount, not global Academy counter
        int maxSteps = MaxStep > 0 ? MaxStep : 1000;
        float timeNorm = Mathf.Clamp01((float)StepCount / Mathf.Max(1f, maxSteps));
        sensor.AddObservation(timeNorm);

        // Recent deaths nearby (placeholder)
        sensor.AddObservation(0.0f);
    }

    private void CollectLocalOccupancyGrid(VectorSensor sensor)
    {
        if (EpisodeRoomGenerator == null)
        {
            AddEmptyLocalOccupancyGrid(sensor);
            return;
        }

        for (int y = LOCAL_GRID_RADIUS; y >= -LOCAL_GRID_RADIUS; y--)
        {
            for (int x = -LOCAL_GRID_RADIUS; x <= LOCAL_GRID_RADIUS; x++)
            {
                if (EpisodeRoomGenerator.TryGetWalkableAtCellOffset(transform.position, x, y, out bool walkable))
                {
                    sensor.AddObservation(walkable ? 1.0f : 0.0f);
                }
                else
                {
                    sensor.AddObservation(0.0f);
                }
            }
        }
    }

    private void AddEmptyLocalOccupancyGrid(VectorSensor sensor)
    {
        for (int i = 0; i < LOCAL_GRID_SIZE * LOCAL_GRID_SIZE; i++)
        {
            sensor.AddObservation(0.0f);
        }
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
    /// Casts WALL_RAY_COUNT rays evenly around the agent against WallLayerMask.
    /// Each ray returns normalized distance to nearest wall (0=touching, 1=nothing in range).
    /// First ray points right (+X), rotates CCW every 22.5°. Adds 16 observations.
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

    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        if (characterHandleWeapon != null)
        {
            _currentWeapon = characterHandleWeapon.CurrentWeapon;
        }
        EnsureWeaponEquipped();

        try
        {
            CacheSurroundingObjects();
            UpdateStickyTarget();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CombatAgent] Action mask target refresh failed: {e.Message}", gameObject);
        }

        if (!CanRequestAttack())
        {
            actionMask.SetActionEnabled(BRANCH_COMBAT, COMBAT_ATTACK, false);
        }

        if (!HasNearbyBulletThreat())
        {
            actionMask.SetActionEnabled(BRANCH_COMBAT, COMBAT_DASH, false);
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
        RecoverStuckEnemiesIfNeeded();

        if (TryEndBrokenEpisodeIfNeeded())
        {
            return;
        }

        int moveX = actions.DiscreteActions[BRANCH_MOVE_X];
        int moveY = actions.DiscreteActions[BRANCH_MOVE_Y];
        int requestedCombat = actions.DiscreteActions[BRANCH_COMBAT];
        int combat = SanitizeCombatAction(requestedCombat);
        int aim = actions.DiscreteActions[BRANCH_AIM];
        Vector2 movement = DecodeMovement(actions.DiscreteActions);
        Vector2 aimDirection = DecodeAim(aim);

        float currentHealth = agentHealth.CurrentHealth;
        float healthDelta = currentHealth - _previousHealth;

        // === REWARD CALCULATION ===
        // Damage penalty
        if (healthDelta < 0)
        {
            float damageTaken = -healthDelta;
            _episodeDamageTaken += damageTaken;
            float cappedPenalty = -Mathf.Min(Mathf.Abs(TakeDamagePenalty), damageTaken * DamageTakenPenaltyPerPoint);
            AddReward(cappedPenalty);
        }

        // Dodge reward tracking: now handled in FixedUpdate() for reliability

        // Reward cho damage/kill với BẤT KỲ enemy nào
        float prevHealth = _previousTotalEnemyHealth;
        int prevCount = _previousEnemyCount;

        float currHealth = GetTotalEnemyHealth();
        int currCount = GetLivingEnemyCount();

        float enemyDamageDealt = Mathf.Max(0f, prevHealth - currHealth);
        if (enemyDamageDealt > 0f)
        {
            _episodeDamageDealt += enemyDamageDealt;
            ClearPendingAttack();
            AddReward(Mathf.Min(DealDamageReward, enemyDamageDealt * DamageRewardPerPoint));
        }
        else
        {
            PenalizeMissedAttackIfExpired(currHealth);
        }

        int killed = prevCount - currCount;
        if (killed > 0)
        {
            _episodeKills += killed;
            AddReward(KillEnemyReward * killed);
        }

        _previousTotalEnemyHealth = currHealth;
        _previousEnemyCount = currCount;

        if (!HasAnyLivingEnemy())
        {
            bool roomWasActuallyCleared = _previousEnemyCount > 0 || _episodeKills > 0;
            AddReward(roomWasActuallyCleared ? AllEnemiesKilledReward : BrokenEpisodePenalty);
            RecordEpisodeStats(roomWasActuallyCleared ? 1f : 0f, 0f);
            EndEpisode();
            return;
        }

        // Time penalty
        AddReward(TimePenalty);
        _episodeDecisionCount++;

        if (EpisodicCoverageReward > 0f)
        {
            Vector2Int currentCell = WorldToCell(transform.position);
            if (_visitedCells.Add(currentCell))
            {
                AddReward(EpisodicCoverageReward);
                _episodeCellsVisited++;
            }
        }

        AddCombatShapingReward(movement, aimDirection, combat);

        // Update health tracking
        _previousHealth = currentHealth;

        ApplyMovement(movement);
        ApplyAim(aimDirection);
        ExecuteCombat(combat, movement, aimDirection);
        LogActionDecision(moveX, moveY, requestedCombat, combat, aim, movement, aimDirection);
    }

    private void AddCombatShapingReward(Vector2 movement, Vector2 aimDirection, int combat)
    {
        bool hasTarget = IsTargetValid(_currentTarget);
        bool hasThreat = HasNearbyBulletThreat();
        bool isAttacking = combat == COMBAT_ATTACK;
        bool weaponReady = IsWeaponReady();

        if (!isAttacking)
            _consecutiveInvalidAttacks = 0;

        if (hasTarget)
        {
            _episodeDecisionsWithTarget++;
        }

        if (isAttacking)
        {
            if (hasTarget)
            {
                _episodeAttacksWithTarget++;
            }
            else
            {
                _episodeAttacksWithoutTarget++;
            }

            if (weaponReady)
            {
                _episodeAttacksWhileWeaponReady++;
            }
            else
            {
                _episodeAttacksWhileWeaponNotReady++;
            }
        }
        if (combat == COMBAT_DASH)
        {
            _episodeDashActions++;
            if (hasThreat)
            {
                _episodeUsefulDashes++;
            }
            else
            {
                _episodeWastefulDashes++;
            }
            AddReward(hasThreat ? UsefulDashReward : WastefulDashPenalty);
        }

        if (!hasTarget)
        {
            _previousTargetDistance = -1f;
            AddAggressiveSeekReward(movement, hasTarget, isAttacking);
            if (isAttacking)
            {
                _consecutiveInvalidAttacks++;
                _episodeAttackActions++;
                _episodeInvalidAttacks++;
                AddReward(InvalidAttackPenalty);
            }
            return;
        }

        AddAggressiveSeekReward(movement, hasTarget, isAttacking);

        float currentDistance = GetCurrentTargetDistance();
        if (_previousTargetDistance >= 0f)
        {
            float progress = _previousTargetDistance - currentDistance;
            if (progress > 0.01f)
            {
                AddReward(Mathf.Min(0.03f, progress * ApproachTargetReward));
            }
            else if (progress < -0.01f)
            {
                AddReward(Mathf.Max(-0.01f, -progress * MoveAwayFromTargetPenalty));
            }
        }
        _previousTargetDistance = currentDistance;

        if (movement.sqrMagnitude <= 0.0001f && currentDistance <= VisionRadius * 0.5f)
        {
            AddReward(IdleNearEnemyPenalty);
        }

        Vector2 effectiveAimDirection = GetCombatAimDirection(aimDirection, combat);
        float aimDot = GetAimDotToTarget(effectiveAimDirection);
        if (aimDot > 0.75f)
        {
            AddReward(AimAtTargetReward * aimDot);
        }

        if (!isAttacking && !weaponReady && _currentWeapon != null && aimDot > 0.75f)
        {
            _episodeCooldownPatienceDecisions++;
            AddReward(CooldownPatienceReward);
        }

        if (isAttacking)
        {
            _episodeAttackActions++;

            if (!weaponReady || !IsCurrentTargetInAttackRange())
            {
                return;
            }

            AddReward(ReadyAttackIntentWithTargetReward);

            if (aimDot > 0.75f)
            {
                _consecutiveInvalidAttacks = 0;
                _episodeAlignedAttacks++;
                RegisterPendingAttack();
                AddReward(AttackAlignedReward * aimDot);
            }
            else
            {
                _consecutiveInvalidAttacks++;
                _episodeInvalidAttacks++;
                AddReward(InvalidAttackPenalty * 0.5f);
            }
        }
    }

    private void AddAggressiveSeekReward(Vector2 movement, bool hasTarget, bool isAttacking)
    {
        if (!IsAggressiveSeekRewardEnabled())
        {
            _previousSeekEnemyDistance = -1f;
            _hadTargetLastDecision = hasTarget;
            return;
        }

        Transform nearestEnemy;
        if (!TryGetNearestLivingEnemy(out nearestEnemy))
        {
            _previousSeekEnemyDistance = -1f;
            _hadTargetLastDecision = false;
            return;
        }

        Vector2 toEnemy = (Vector2)nearestEnemy.position - (Vector2)transform.position;
        float distance = toEnemy.magnitude;
        float seekDistance = distance;
        Vector2 seekDirection = toEnemy.sqrMagnitude > 0.0001f ? toEnemy.normalized : Vector2.zero;
        bool hasPathDistance = TryGetPathDistanceToEnemy(nearestEnemy, out int pathDistance);
        if (hasPathDistance)
        {
            seekDistance = pathDistance;
        }

        if (distance <= 0.0001f)
        {
            _previousSeekEnemyDistance = seekDistance;
            _hadTargetLastDecision = hasTarget;
            return;
        }

        if (hasTarget && !_hadTargetLastDecision)
        {
            AddReward(AggressiveSeekTargetAcquiredReward);
        }

        bool madeProgress = false;
        if (_previousSeekEnemyDistance >= 0f)
        {
            float progress = _previousSeekEnemyDistance - seekDistance;
            if (progress > 0.01f)
            {
                madeProgress = true;
                AddReward(Mathf.Min(AggressiveSeekProgressRewardCap, progress * AggressiveSeekProgressReward));
            }
            else if (progress < -0.01f)
            {
                AddReward(Mathf.Max(-0.02f, progress * Mathf.Abs(AggressiveSeekMoveAwayPenalty)));
            }
        }

        bool closeEnoughToFight = hasPathDistance
            ? pathDistance <= AggressiveSeekCloseDistance
            : distance <= AggressiveSeekCloseDistance;
        bool weaponReady = IsWeaponReady();
        if (hasTarget && closeEnoughToFight && weaponReady && !isAttacking)
        {
            AddReward(CloseTargetNoAttackPenalty);
            _previousSeekEnemyDistance = seekDistance;
            _hadTargetLastDecision = hasTarget;
            return;
        }

        if (movement.sqrMagnitude <= 0.0001f)
        {
            AddReward(AggressiveSeekIdlePenalty);
        }
        else if (madeProgress || !closeEnoughToFight)
        {
            float moveDot = seekDirection.sqrMagnitude > 0.0001f
                ? Vector2.Dot(movement.normalized, seekDirection.normalized)
                : 0f;
            if (moveDot > 0.25f)
            {
                float directionScale = madeProgress ? 1f : 0.5f;
                AddReward(AggressiveSeekDirectionReward * directionScale * moveDot);
            }
            else if (moveDot < -0.25f)
            {
                AddReward(AggressiveSeekMoveAwayPenalty * -moveDot);
            }
        }

        _previousSeekEnemyDistance = seekDistance;
        _hadTargetLastDecision = hasTarget;
    }

    private bool IsAggressiveSeekRewardEnabled()
    {
        if (UseAggressiveSeekReward)
        {
            return true;
        }

        return Academy.Instance != null
            && Academy.Instance.EnvironmentParameters.GetWithDefault("aggressive_seek", 0f) >= 0.5f;
    }

    private bool TryGetPathDistanceToEnemy(Transform enemy, out int pathDistance)
    {
        pathDistance = -1;

        if (enemy == null || EpisodeRoomGenerator == null)
        {
            return false;
        }

        return EpisodeRoomGenerator.TryGetShortestPathInfo(
            transform.position,
            enemy.position,
            out pathDistance,
            out _);
    }

    private void RegisterPendingAttack()
    {
        if (_pendingAttackDecision >= 0)
        {
            _episodeInvalidAttacks++;
            AddReward(RepeatedAttackWhilePendingPenalty);
            return;
        }

        _pendingAttackDecision = _episodeDecisionCount;
        _pendingAttackEnemyHealth = _previousTotalEnemyHealth;
    }

    private void ClearPendingAttack()
    {
        _pendingAttackDecision = -1;
        _pendingAttackEnemyHealth = -1f;
    }

    private void PenalizeMissedAttackIfExpired(float currentEnemyHealth)
    {
        if (_pendingAttackDecision < 0)
        {
            return;
        }

        if (_pendingAttackEnemyHealth >= 0f && currentEnemyHealth < _pendingAttackEnemyHealth - 0.001f)
        {
            ClearPendingAttack();
            return;
        }

        if (_episodeDecisionCount - _pendingAttackDecision < MissedAttackGraceDecisions)
        {
            return;
        }

        _episodeInvalidAttacks++;
        AddReward(MissedAttackPenalty);
        ClearPendingAttack();
    }

    private bool TryEndBrokenEpisodeIfNeeded()
    {
        if (TryRecoverGroundedStuckIfNeeded())
        {
            return false;
        }

        if (EndEpisodeWhenStuck && _stillDecisionCount >= MaxStillDecisionsBeforeReset)
        {
            if (TryRecoverAgentStuckToSpawn())
            {
                return false;
            }

            if (DebugStuckLogs)
                Debug.LogWarning("[AgentBrokenEpisode] reason=Stuck(truncated) step=" + StepCount + " stillCount=" + _stillDecisionCount, gameObject);
            _episodeBrokenReason = BrokenEpisodeReason.Stuck;
            RecordEpisodeStats(0f, 0f);
            EpisodeInterrupted();
            return true;
        }

        if (EndEpisodeOnConsecutiveInvalid && _consecutiveInvalidAttacks >= MaxConsecutiveInvalidAttacks)
        {
            if (DebugStuckLogs)
                Debug.LogWarning("[AgentBrokenEpisode] reason=ConsecutiveInvalidAttacks step=" + StepCount + " count=" + _consecutiveInvalidAttacks, gameObject);
            AddReward(BrokenEpisodePenalty);
            _episodeBrokenReason = BrokenEpisodeReason.ConsecutiveInvalidAttacks;
            RecordEpisodeStats(0f, 0f);
            EndEpisode();
            return true;
        }

        if (TryEndEnemyOutOfBoundsEpisode())
        {
            return true;
        }

        if (!EndEpisodeWhenOutOfBounds || MaxDistanceFromSpawnBeforeReset <= 0f)
        {
            return false;
        }

        Vector3 spawnPosition = GetSpawnPosition();
        float distanceFromSpawn = Vector2.Distance(transform.position, spawnPosition);
        if (distanceFromSpawn <= MaxDistanceFromSpawnBeforeReset)
        {
            return false;
        }

        AddReward(BrokenEpisodePenalty);
        _episodeBrokenReason = BrokenEpisodeReason.AgentOutOfBounds;
        RecordEpisodeStats(0f, 0f);

        if (DebugStuckLogs)
        {
            Debug.LogWarning(
                "[AgentBrokenEpisode] " +
                "reason=OutOfBounds" +
                " step=" + StepCount +
                " pos=(" + transform.position.x.ToString("F2") + "," + transform.position.y.ToString("F2") + ")" +
                " spawn=(" + spawnPosition.x.ToString("F2") + "," + spawnPosition.y.ToString("F2") + ")" +
                " distance=" + distanceFromSpawn.ToString("F2") +
                " maxDistance=" + MaxDistanceFromSpawnBeforeReset.ToString("F2"),
                gameObject
            );
        }

        EndEpisode();
        return true;
    }

    private bool TryRecoverGroundedStuckIfNeeded()
    {
        if (!RecoverGroundedStuckBeforeEnding)
        {
            return false;
        }

        int threshold = Mathf.Clamp(
            GroundedStuckRecoveryDecisionThreshold,
            1,
            Mathf.Max(1, MaxStillDecisionsBeforeReset)
        );
        if (_stillDecisionCount < threshold)
        {
            return false;
        }

        if (topDownController2D == null || !topDownController2D.Grounded || topDownController2D.OverHole)
        {
            return false;
        }

        int maxRecoveries = Mathf.Max(0, MaxGroundedStuckRecoveriesPerEpisode);
        if (_groundedStuckRecoveries >= maxRecoveries)
        {
            return false;
        }

        Vector2 recoveryPosition;
        if (!TryFindGroundedStuckRecoveryPosition(out recoveryPosition))
        {
            return false;
        }

        Vector3 oldPosition = transform.position;
        transform.position = new Vector3(recoveryPosition.x, recoveryPosition.y, oldPosition.z);
        StabilizeTrainingCharacter(gameObject);
        _lastDebugPosition = transform.position;
        _stillDecisionCount = 0;
        _groundedStuckRecoveries++;
        AddReward(GroundedStuckRecoveryPenalty);

        if (DebugStuckLogs)
        {
            Debug.LogWarning(
                "[AgentUnstuck] " +
                "step=" + StepCount +
                " count=" + _groundedStuckRecoveries +
                " from=(" + oldPosition.x.ToString("F2") + "," + oldPosition.y.ToString("F2") + ")" +
                " to=(" + recoveryPosition.x.ToString("F2") + "," + recoveryPosition.y.ToString("F2") + ")" +
                " grounded=" + topDownController2D.Grounded +
                " overHole=" + topDownController2D.OverHole,
                gameObject
            );
        }

        return true;
    }

    private bool TryFindGroundedStuckRecoveryPosition(out Vector2 recoveryPosition)
    {
        return TryFindGroundedStuckRecoveryPositionFrom(transform.position, out recoveryPosition);
    }

    private bool TryFindGroundedStuckRecoveryPositionFrom(Vector2 origin, out Vector2 recoveryPosition)
    {
        float step = Mathf.Max(0.1f, GroundedStuckRecoverySearchStep);
        float radius = Mathf.Max(step, GroundedStuckRecoverySearchRadius);
        float bestScore = float.MaxValue;
        recoveryPosition = origin;
        bool found = false;

        for (float r = step; r <= radius + 0.001f; r += step)
        {
            int samples = Mathf.Max(8, Mathf.CeilToInt((2f * Mathf.PI * r) / step));
            for (int i = 0; i < samples; i++)
            {
                float angle = (2f * Mathf.PI * i) / samples;
                Vector2 candidate = origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
                if (!IsSafeGroundedRecoveryPosition(candidate))
                {
                    continue;
                }

                float score = (candidate - origin).sqrMagnitude;
                if (IsTargetValid(_currentTarget))
                {
                    score += 0.05f * Vector2.Distance(candidate, _currentTarget.position);
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    recoveryPosition = candidate;
                    found = true;
                }
            }

            if (found)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsSafeGroundedRecoveryPosition(Vector2 candidate)
    {
        LayerMask groundMask = topDownController2D != null
            ? topDownController2D.GroundLayerMask
            : LayerMask.GetMask("Ground");
        if (groundMask.value != 0 && Physics2D.OverlapPoint(candidate, groundMask) == null)
        {
            return false;
        }

        float clearance = Mathf.Max(0.1f, GroundedStuckRecoveryClearance);
        LayerMask wallMask = WallLayerMask.value != 0
            ? WallLayerMask
            : LayerMask.GetMask("Obstacles", "ObstaclesDoors");
        if (wallMask.value != 0 && Physics2D.OverlapCircle(candidate, clearance, wallMask) != null)
        {
            return false;
        }

        LayerMask enemyMask = LayerMask.GetMask("Enemies");
        if (enemyMask.value != 0 && Physics2D.OverlapCircle(candidate, clearance, enemyMask) != null)
        {
            return false;
        }

        return true;
    }

    private void RecoverStuckEnemiesIfNeeded()
    {
        if (!RecoverEnemyStuckDuringTraining)
        {
            return;
        }

        if (_episodeEnemyStuckRecoveries >= Mathf.Max(0, MaxEnemyStuckRecoveriesPerEpisode))
        {
            return;
        }

        for (int i = 0; i < _trackedEnemies.Count; i++)
        {
            GameObject enemy = _trackedEnemies[i];
            if (!IsLivingEnemy(enemy))
            {
                continue;
            }

            if (!ShouldRecoverEnemyStuck(enemy))
            {
                continue;
            }

            Vector2 recoveryPosition;
            if (!TryFindEnemyRecoveryPosition(enemy, out recoveryPosition))
            {
                ResetEnemyStuckCounter(enemy);
                continue;
            }

            RecoverEnemyToPosition(enemy, recoveryPosition);
            if (_episodeEnemyStuckRecoveries >= Mathf.Max(0, MaxEnemyStuckRecoveriesPerEpisode))
            {
                return;
            }
        }
    }

    private bool IsLivingEnemy(GameObject enemy)
    {
        if (enemy == null || !enemy.activeInHierarchy)
        {
            return false;
        }

        Health health = enemy.GetComponent<Health>();
        return health == null || health.CurrentHealth > 0f;
    }

    private bool ShouldRecoverEnemyStuck(GameObject enemy)
    {
        Vector3 currentPosition = enemy.transform.position;
        Vector3 lastPosition;
        if (!_lastEnemyPositions.TryGetValue(enemy, out lastPosition))
        {
            _lastEnemyPositions[enemy] = currentPosition;
            _enemyStillDecisionCounts[enemy] = 0;
            return false;
        }

        float moved = Vector2.Distance(currentPosition, lastPosition);
        _lastEnemyPositions[enemy] = currentPosition;

        int stillCount = moved <= Mathf.Max(0.001f, EnemyStuckDistanceEpsilon)
            ? GetDictionaryCount(_enemyStillDecisionCounts, enemy) + 1
            : 0;
        _enemyStillDecisionCounts[enemy] = stillCount;

        int perEnemyRecoveries = GetDictionaryCount(_enemyRecoveryCounts, enemy);
        if (perEnemyRecoveries >= Mathf.Max(0, MaxEnemyStuckRecoveriesPerEnemy))
        {
            return false;
        }

        if (stillCount < Mathf.Max(1, EnemyStuckRecoveryDecisionThreshold))
        {
            return false;
        }

        TopDownController2D enemyController = enemy.GetComponent<TopDownController2D>();
        bool enemyGrounded = enemyController == null || enemyController.Grounded;
        bool enemyOverHole = enemyController != null && enemyController.OverHole;
        bool nearWall = IsNearWall(enemy.transform.position, EnemyStuckRecoveryClearance);
        bool nearAgent = Vector2.Distance(enemy.transform.position, transform.position) <= EnemyStuckRecoveryMinDistanceFromAgent;
        bool likelyBlockingAgent = nearAgent && _stillDecisionCount >= Mathf.Max(1, GroundedStuckRecoveryDecisionThreshold / 2);

        return !enemyGrounded || enemyOverHole || nearWall || likelyBlockingAgent;
    }

    private int GetDictionaryCount(Dictionary<GameObject, int> dictionary, GameObject key)
    {
        int value;
        return dictionary.TryGetValue(key, out value) ? value : 0;
    }

    private bool IsNearWall(Vector2 position, float clearance)
    {
        LayerMask wallMask = WallLayerMask.value != 0
            ? WallLayerMask
            : LayerMask.GetMask("Obstacles", "ObstaclesDoors");
        return wallMask.value != 0
            && Physics2D.OverlapCircle(position, Mathf.Max(0.1f, clearance), wallMask) != null;
    }

    private bool TryFindEnemyRecoveryPosition(GameObject enemy, out Vector2 recoveryPosition)
    {
        return TryFindEnemyRecoveryPositionFrom(enemy, enemy.transform.position, out recoveryPosition);
    }

    private bool TryFindEnemyRecoveryPositionFrom(GameObject enemy, Vector2 origin, out Vector2 recoveryPosition)
    {
        float step = Mathf.Max(0.1f, EnemyStuckRecoverySearchStep);
        float radius = Mathf.Max(step, EnemyStuckRecoverySearchRadius);
        float minAgentDistance = Mathf.Max(0f, EnemyStuckRecoveryMinDistanceFromAgent);
        float bestScore = float.MaxValue;
        recoveryPosition = origin;
        bool found = false;

        for (float r = step; r <= radius + 0.001f; r += step)
        {
            int samples = Mathf.Max(8, Mathf.CeilToInt((2f * Mathf.PI * r) / step));
            for (int i = 0; i < samples; i++)
            {
                float angle = (2f * Mathf.PI * i) / samples;
                Vector2 candidate = origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
                if (Vector2.Distance(candidate, transform.position) < minAgentDistance)
                {
                    continue;
                }

                if (!IsSafeEnemyRecoveryPosition(enemy, candidate))
                {
                    continue;
                }

                float score = (candidate - origin).sqrMagnitude + 0.1f * Vector2.Distance(candidate, transform.position);
                if (score < bestScore)
                {
                    bestScore = score;
                    recoveryPosition = candidate;
                    found = true;
                }
            }

            if (found)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsSafeEnemyRecoveryPosition(GameObject enemy, Vector2 candidate)
    {
        TopDownController2D enemyController = enemy != null ? enemy.GetComponent<TopDownController2D>() : null;
        LayerMask groundMask = enemyController != null
            ? enemyController.GroundLayerMask
            : LayerMask.GetMask("Ground");
        if (groundMask.value != 0 && Physics2D.OverlapPoint(candidate, groundMask) == null)
        {
            return false;
        }

        float clearance = Mathf.Max(0.1f, EnemyStuckRecoveryClearance);
        if (IsNearWall(candidate, clearance))
        {
            return false;
        }

        LayerMask dynamicMask = LayerMask.GetMask("Player", "Enemies");
        if (dynamicMask.value == 0)
        {
            return true;
        }

        Collider2D[] hits = Physics2D.OverlapCircleAll(candidate, clearance, dynamicMask);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] == null)
            {
                continue;
            }

            if (enemy != null && hits[i].transform.IsChildOf(enemy.transform))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private void RecoverEnemyToPosition(GameObject enemy, Vector2 recoveryPosition)
    {
        Vector3 oldPosition = enemy.transform.position;
        enemy.transform.position = new Vector3(recoveryPosition.x, recoveryPosition.y, oldPosition.z);
        StabilizeTrainingCharacter(enemy);

        AIBrain brain = enemy.GetComponent<AIBrain>();
        if (brain != null)
        {
            brain.BrainActive = true;
            brain.enabled = true;
            brain.ResetBrain();
        }

        _lastEnemyPositions[enemy] = enemy.transform.position;
        _enemyStillDecisionCounts[enemy] = 0;
        _enemyRecoveryCounts[enemy] = GetDictionaryCount(_enemyRecoveryCounts, enemy) + 1;
        _episodeEnemyStuckRecoveries++;

        if (DebugStuckLogs)
        {
            Debug.LogWarning(
                "[EnemyUnstuck] " +
                "step=" + StepCount +
                " enemy=" + enemy.name +
                " count=" + _episodeEnemyStuckRecoveries +
                " enemyCount=" + _enemyRecoveryCounts[enemy] +
                " from=(" + oldPosition.x.ToString("F2") + "," + oldPosition.y.ToString("F2") + ")" +
                " to=(" + recoveryPosition.x.ToString("F2") + "," + recoveryPosition.y.ToString("F2") + ")",
                enemy
            );
        }
    }

    private void ResetEnemyStuckCounter(GameObject enemy)
    {
        if (enemy == null)
        {
            return;
        }

        _lastEnemyPositions[enemy] = enemy.transform.position;
        _enemyStillDecisionCounts[enemy] = 0;
    }

    private bool TryRecoverOutOfBoundsEnemyToSpawn(GameObject enemy, Vector2 spawnPosition, float distanceFromSpawn)
    {
        if (!RecoverEnemyStuckDuringTraining)
        {
            return false;
        }

        if (_episodeEnemyStuckRecoveries >= Mathf.Max(0, MaxEnemyStuckRecoveriesPerEpisode))
        {
            return false;
        }

        int perEnemyRecoveries = GetDictionaryCount(_enemyRecoveryCounts, enemy);
        if (perEnemyRecoveries >= Mathf.Max(0, MaxEnemyStuckRecoveriesPerEnemy))
        {
            return false;
        }

        Vector2 recoveryPosition;
        if (!TryFindEnemyRecoveryPositionFrom(enemy, spawnPosition, out recoveryPosition))
        {
            return false;
        }

        if (DebugStuckLogs)
        {
            Debug.LogWarning(
                "[EnemyOutOfBoundsRecover] " +
                "step=" + StepCount +
                " enemy=" + enemy.name +
                " distance=" + distanceFromSpawn.ToString("F2") +
                " spawn=(" + spawnPosition.x.ToString("F2") + "," + spawnPosition.y.ToString("F2") + ")",
                enemy
            );
        }

        RecoverEnemyToPosition(enemy, recoveryPosition);
        return true;
    }

    private bool TryRecoverAgentStuckToSpawn()
    {
        if (_agentStuckRecoveries >= Mathf.Max(0, MaxAgentStuckRecoveriesPerEpisode))
        {
            return false;
        }

        Vector3 spawnPosition = GetSpawnPosition();
        Vector2 recoveryPosition;
        if (!TryFindGroundedStuckRecoveryPositionFrom(spawnPosition, out recoveryPosition))
        {
            recoveryPosition = spawnPosition;
        }

        Vector3 oldPosition = transform.position;
        transform.position = new Vector3(recoveryPosition.x, recoveryPosition.y, oldPosition.z);
        StabilizeTrainingCharacter(gameObject);
        _lastDebugPosition = transform.position;
        _stillDecisionCount = 0;
        _agentStuckRecoveries++;
        AddReward(AgentStuckRecoveryPenalty);

        if (DebugStuckLogs)
        {
            Debug.LogWarning(
                "[AgentStuckRecover] " +
                "step=" + StepCount +
                " count=" + _agentStuckRecoveries +
                " from=(" + oldPosition.x.ToString("F2") + "," + oldPosition.y.ToString("F2") + ")" +
                " to=(" + recoveryPosition.x.ToString("F2") + "," + recoveryPosition.y.ToString("F2") + ")",
                gameObject
            );
        }

        return true;
    }

    private bool TryEndEnemyOutOfBoundsEpisode()
    {
        if (!EndEpisodeWhenEnemyOutOfBounds || MaxEnemyDistanceFromSpawnBeforeReset <= 0f)
        {
            return false;
        }

        Vector3 spawnPosition = GetSpawnPosition();
        for (int i = 0; i < _trackedEnemies.Count; i++)
        {
            GameObject enemy = _trackedEnemies[i];
            if (enemy == null || !enemy.activeInHierarchy)
            {
                continue;
            }

            Health health = enemy.GetComponent<Health>();
            if (health != null && health.CurrentHealth <= 0f)
            {
                continue;
            }

            float distanceFromSpawn = Vector2.Distance(enemy.transform.position, spawnPosition);
            if (distanceFromSpawn <= MaxEnemyDistanceFromSpawnBeforeReset)
            {
                continue;
            }

            if (TryRecoverOutOfBoundsEnemyToSpawn(enemy, spawnPosition, distanceFromSpawn))
            {
                return false;
            }

            _episodeBrokenReason = BrokenEpisodeReason.EnemyOutOfBounds;
            RecordEpisodeStats(0f, 0f);

            if (DebugStuckLogs)
            {
                Debug.LogWarning(
                    "[AgentBrokenEpisode] " +
                    "reason=EnemyOutOfBounds(truncated)" +
                    " step=" + StepCount +
                    " enemy=" + enemy.name +
                    " enemyPos=(" + enemy.transform.position.x.ToString("F2") + "," + enemy.transform.position.y.ToString("F2") + ")" +
                    " spawn=(" + spawnPosition.x.ToString("F2") + "," + spawnPosition.y.ToString("F2") + ")" +
                    " distance=" + distanceFromSpawn.ToString("F2") +
                    " maxDistance=" + MaxEnemyDistanceFromSpawnBeforeReset.ToString("F2"),
                    enemy
                );
            }

            EpisodeInterrupted();
            return true;
        }

        return false;
    }

    private void StabilizeTrackedEnemies()
    {
        for (int i = 0; i < _trackedEnemies.Count; i++)
        {
            StabilizeTrainingCharacter(_trackedEnemies[i]);
        }
    }

    private void StabilizeTrainingCharacter(GameObject characterObject)
    {
        if (characterObject == null)
        {
            return;
        }

        TopDownController controller = characterObject.GetComponent<TopDownController>();
        if (controller != null)
        {
            controller.Reset();
            controller.SetMovement(Vector2.zero);
        }

        Character character = characterObject.GetComponent<Character>();
        if (character != null && character.MovementState != null)
        {
            CharacterStates.MovementStates movementState = character.MovementState.CurrentState;
            if (movementState == CharacterStates.MovementStates.Falling ||
                movementState == CharacterStates.MovementStates.FallingDownHole)
            {
                character.MovementState.ChangeState(CharacterStates.MovementStates.Idle);
            }
        }

        Rigidbody2D rigidbody2D = characterObject.GetComponent<Rigidbody2D>();
        if (rigidbody2D != null)
        {
            rigidbody2D.linearVelocity = Vector2.zero;
            rigidbody2D.angularVelocity = 0f;
        }

        if (!DisableTrainingKnockback)
        {
            return;
        }

        Health health = characterObject.GetComponent<Health>();
        if (health != null)
        {
            health.ImmuneToKnockback = true;
            health.KnockbackForceMultiplier = 0f;
        }
    }

    private void ResetEpisodeStats()
    {
        _episodeDamageDealt = 0f;
        _episodeDamageTaken = 0f;
        _episodeKills = 0;
        _episodeAttackActions = 0;
        _episodeAlignedAttacks = 0;
        _episodeInvalidAttacks = 0;
        _episodeDashActions = 0;
        _episodeUsefulDashes = 0;
        _episodeWastefulDashes = 0;
        _episodeDecisionCount = 0;
        _episodeDecisionsWithTarget = 0;
        _episodeAttacksWithTarget = 0;
        _episodeAttacksWithoutTarget = 0;
        _episodeAttacksWhileWeaponReady = 0;
        _episodeAttacksWhileWeaponNotReady = 0;
        _episodeCooldownPatienceDecisions = 0;
        _episodeBlockedAttackActions = 0;
        _episodeBlockedDashActions = 0;
        _episodeEnemyStuckRecoveries = 0;
        _episodeCellsVisited = 0;
        _episodeBrokenReason = BrokenEpisodeReason.None;
        _episodeStatsRecorded = false;
    }

    private void RecordEpisodeStats(float clearedRoom, float died)
    {
        if (_episodeStatsRecorded)
        {
            return;
        }

        _episodeStatsRecorded = true;
        float attacks = Mathf.Max(1, _episodeAttackActions);
        float dashes = Mathf.Max(1, _episodeDashActions);
        float decisions = Mathf.Max(1, _episodeDecisionCount);
        StatsRecorder stats = Academy.Instance.StatsRecorder;

        stats.Add("CombatAgent/EpisodeDamageDealt", _episodeDamageDealt);
        stats.Add("CombatAgent/EpisodeDamageTaken", _episodeDamageTaken);
        stats.Add("CombatAgent/EpisodeKills", _episodeKills);
        stats.Add("CombatAgent/ClearedRoom", clearedRoom);
        stats.Add("CombatAgent/Died", died);
        stats.Add("CombatAgent/AlignedAttackRate", _episodeAlignedAttacks / attacks);
        stats.Add("CombatAgent/InvalidAttackRate", _episodeInvalidAttacks / attacks);
        stats.Add("CombatAgent/UsefulDashRate", _episodeUsefulDashes / dashes);
        stats.Add("CombatAgent/WastefulDashRate", _episodeWastefulDashes / dashes);
        stats.Add("CombatAgent/AttackActionsPerDecision", _episodeAttackActions / decisions);
        stats.Add("CombatAgent/DashActionsPerDecision", _episodeDashActions / decisions);
        stats.Add("CombatAgent/TargetVisibleRate", _episodeDecisionsWithTarget / decisions);
        stats.Add("CombatAgent/AttackWithTargetRate", _episodeAttacksWithTarget / attacks);
        stats.Add("CombatAgent/AttackNoTargetRate", _episodeAttacksWithoutTarget / attacks);
        stats.Add("CombatAgent/AttackWeaponReadyRate", _episodeAttacksWhileWeaponReady / attacks);
        stats.Add("CombatAgent/AttackWeaponNotReadyRate", _episodeAttacksWhileWeaponNotReady / attacks);
        stats.Add("CombatAgent/CooldownPatiencePerDecision", _episodeCooldownPatienceDecisions / decisions);
        stats.Add("CombatAgent/BlockedAttackActionsPerDecision", _episodeBlockedAttackActions / decisions);
        stats.Add("CombatAgent/BlockedDashActionsPerDecision", _episodeBlockedDashActions / decisions);
        stats.Add("CombatAgent/EnemyStuckRecoveries", _episodeEnemyStuckRecoveries);
        stats.Add("CombatAgent/AgentStuckRecoveries", _agentStuckRecoveries);
        stats.Add("CombatAgent/CellsVisited", _episodeCellsVisited);
        stats.Add("CombatAgent/BrokenEpisode", _episodeBrokenReason == BrokenEpisodeReason.None ? 0f : 1f);
        stats.Add("CombatAgent/BrokenStuck", _episodeBrokenReason == BrokenEpisodeReason.Stuck ? 1f : 0f);
        stats.Add("CombatAgent/BrokenInvalidAttack", _episodeBrokenReason == BrokenEpisodeReason.ConsecutiveInvalidAttacks ? 1f : 0f);
        stats.Add("CombatAgent/BrokenAgentOutOfBounds", _episodeBrokenReason == BrokenEpisodeReason.AgentOutOfBounds ? 1f : 0f);
        stats.Add("CombatAgent/BrokenEnemyOutOfBounds", _episodeBrokenReason == BrokenEpisodeReason.EnemyOutOfBounds ? 1f : 0f);
    }

    private int SanitizeCombatAction(int requestedCombat)
    {
        if (requestedCombat == COMBAT_ATTACK)
        {
            if (!CanRequestAttack())
            {
                _episodeBlockedAttackActions++;
                return COMBAT_NONE;
            }
        }
        else if (requestedCombat == COMBAT_DASH && !HasNearbyBulletThreat())
        {
            _episodeBlockedDashActions++;
            return COMBAT_NONE;
        }

        return requestedCombat;
    }

    private bool CanRequestAttack()
    {
        return IsTargetValid(_currentTarget)
            && IsWeaponReady()
            && _pendingAttackDecision < 0
            && IsCurrentTargetInAttackRange();
    }

    private bool IsCurrentTargetInAttackRange()
    {
        if (TrainingAttackMaxDistance <= 0f)
        {
            return true;
        }

        if (!IsTargetValid(_currentTarget))
        {
            return false;
        }

        return Vector2.Distance(transform.position, _currentTarget.position) <= TrainingAttackMaxDistance;
    }

    private float GetCurrentTargetDistance()
    {
        if (!IsTargetValid(_currentTarget))
        {
            return -1f;
        }
        return Vector2.Distance(transform.position, _currentTarget.position);
    }

    private Vector2 GetEffectiveAimDirection(Vector2 aimDirection)
    {
        if (aimDirection.sqrMagnitude > 0.0001f)
        {
            return aimDirection.normalized;
        }

        return _lastAimDirection.sqrMagnitude > 0.0001f
            ? _lastAimDirection.normalized
            : Vector2.right;
    }

    private Vector2 GetCombatAimDirection(Vector2 aimDirection, int combat)
    {
        if (AutoAimAttackAtCurrentTarget && combat == COMBAT_ATTACK && IsTargetValid(_currentTarget))
        {
            Vector2 toTarget = (Vector2)_currentTarget.position - (Vector2)transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                return toTarget.normalized;
            }
        }

        return GetEffectiveAimDirection(aimDirection);
    }

    private float GetAimDotToTarget(Vector2 aimDirection)
    {
        if (!IsTargetValid(_currentTarget) || aimDirection.sqrMagnitude <= 0.0001f)
        {
            return 0f;
        }

        Vector2 toTarget = ((Vector2)_currentTarget.position - (Vector2)transform.position).normalized;
        return Vector2.Dot(aimDirection.normalized, toTarget);
    }

    private float GetTotalEnemyHealth()
    {
        float total = 0f;

        for (int i = 0; i < _trackedEnemies.Count; i++)
        {
            GameObject enemy = _trackedEnemies[i];
            if (enemy == null) continue;
            if (!enemy.activeInHierarchy) continue;

            Health h = enemy.GetComponent<Health>();
            if (h != null && h.CurrentHealth > 0f) total += h.CurrentHealth;
        }

        return total;
    }

    private void InitializeReusableBuffers()
    {
        int bufferSize = Mathf.Max(16, OverlapBufferSize);
        if (_overlapResults == null || _overlapResults.Length != bufferSize)
        {
            _overlapResults = new Collider2D[bufferSize];
        }

        _overlapFilter = new ContactFilter2D
        {
            useTriggers = true
        };
    }

    private int GetLivingEnemyCount()
    {
        int count = 0;
        for (int i = 0; i < _trackedEnemies.Count; i++)
        {
            GameObject enemy = _trackedEnemies[i];
            if (enemy == null || !enemy.activeInHierarchy)
            {
                continue;
            }

            Health health = enemy.GetComponent<Health>();
            if (health == null || health.CurrentHealth > 0f)
            {
                count++;
            }
        }

        return count;
    }

    private bool TryGetNearestLivingEnemy(out Transform nearestEnemy)
    {
        nearestEnemy = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < _trackedEnemies.Count; i++)
        {
            GameObject enemy = _trackedEnemies[i];
            if (!IsLivingEnemy(enemy))
            {
                continue;
            }

            float distance = Vector2.Distance(transform.position, enemy.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestEnemy = enemy.transform;
            }
        }

        return nearestEnemy != null;
    }

    private void RefreshTrackedEnemies()
    {
        _trackedEnemies.Clear();

        GameObject[] enemies = GameObject.FindGameObjectsWithTag("Enemy");
        for (int i = 0; i < enemies.Length; i++)
        {
            GameObject enemy = enemies[i];
            if (enemy != null)
            {
                _trackedEnemies.Add(enemy);
            }
        }
    }

    private void ResetEnemyStuckTracking()
    {
        _lastEnemyPositions.Clear();
        _enemyStillDecisionCounts.Clear();
        _enemyRecoveryCounts.Clear();

        for (int i = 0; i < _trackedEnemies.Count; i++)
        {
            GameObject enemy = _trackedEnemies[i];
            if (!IsLivingEnemy(enemy))
            {
                continue;
            }

            _lastEnemyPositions[enemy] = enemy.transform.position;
            _enemyStillDecisionCounts[enemy] = 0;
            _enemyRecoveryCounts[enemy] = 0;
        }
    }

    private bool HasAnyLivingEnemy()
    {
        return GetLivingEnemyCount() > 0;
    }

    private void LogActionDecision(int moveX, int moveY, int requestedCombat, int combat, int aim, Vector2 movement, Vector2 aimDirection)
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
        bool combatSanitized = requestedCombat != combat;
        string moveXLabel = MoveXToLabel(moveX);
        string moveYLabel = MoveYToLabel(moveY);
        string requestedCombatLabel = CombatToLabel(requestedCombat);
        string combatLabel = CombatToLabel(combat);
        string aimLabel = AimToLabel(aim);
        Vector3 pos = transform.position;
        float movedSinceLastLog = Vector2.Distance(pos, _lastDebugPosition);
        _stillDecisionCount = movedSinceLastLog <= DebugStuckDistanceEpsilon ? _stillDecisionCount + 1 : 0;
        _lastDebugPosition = pos;

        float hp = agentHealth != null ? agentHealth.CurrentHealth : -1f;
        float maxHp = agentHealth != null ? agentHealth.MaximumHealth : -1f;
        float targetDistance = hasTarget ? Vector2.Distance(pos, _currentTarget.position) : -1f;
        float cooldownLeft = _currentWeapon != null ? _currentWeapon.CooldownTimeLeft : -1f;
        string movementState = _character != null && _character.MovementState != null
            ? _character.MovementState.CurrentState.ToString()
            : "Unknown";
        string conditionState = _character != null && _character.ConditionState != null
            ? _character.ConditionState.CurrentState.ToString()
            : "Unknown";
        string groundedState = topDownController2D != null ? topDownController2D.Grounded.ToString() : "Unknown";
        string overHoleState = topDownController2D != null ? topDownController2D.OverHole.ToString() : "Unknown";

        Debug.Log(
            "[AgentDecision] " +
            "step=" + StepCount +
            " moveX=" + moveX +
            " moveY=" + moveY +
            " requestedCombat=" + requestedCombat +
            " combat=" + combat +
            " aim=" + aim +
            " action=(" + moveXLabel + "," + moveYLabel + "," + requestedCombatLabel + "->" + combatLabel + "," + aimLabel + ")" +
            " movement=(" + movement.x.ToString("F2") + "," + movement.y.ToString("F2") + ")" +
            " aimDir=(" + aimDirection.x.ToString("F2") + "," + aimDirection.y.ToString("F2") + ")" +
            " pos=(" + pos.x.ToString("F2") + "," + pos.y.ToString("F2") + ")" +
            " moved=" + movedSinceLastLog.ToString("F3") +
            " stillCount=" + _stillDecisionCount +
            " hp=" + hp.ToString("F1") + "/" + maxHp.ToString("F1") +
            " reward=" + GetCumulativeReward().ToString("F3") +
            " target=" + hasTarget +
            " targetDist=" + targetDistance.ToString("F2") +
            " weapon=" + hasWeapon +
            " ready=" + weaponReady +
            " cooldown=" + cooldownLeft.ToString("F2") +
            " sanitized=" + combatSanitized +
            " moveState=" + movementState +
            " condition=" + conditionState +
            " grounded=" + groundedState +
            " overHole=" + overHoleState,
            gameObject
        );

        if (DebugStuckLogs && _stillDecisionCount >= Mathf.Max(1, DebugStuckDecisionThreshold))
        {
            Debug.LogWarning(
                "[AgentStuck] " +
                "step=" + StepCount +
                " stillCount=" + _stillDecisionCount +
                " moved=" + movedSinceLastLog.ToString("F3") +
                " action=(" + moveXLabel + "," + moveYLabel + "," + requestedCombatLabel + "->" + combatLabel + "," + aimLabel + ")" +
                " movement=(" + movement.x.ToString("F2") + "," + movement.y.ToString("F2") + ")" +
                " target=" + hasTarget +
                " targetDist=" + targetDistance.ToString("F2") +
                " weaponReady=" + weaponReady +
                " cooldown=" + cooldownLeft.ToString("F2") +
                " sanitized=" + combatSanitized +
                " moveState=" + movementState +
                " condition=" + conditionState +
                " grounded=" + groundedState +
                " overHole=" + overHoleState,
                gameObject
            );
        }
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

    private string AimToLabel(int aim)
    {
        switch (aim)
        {
            case AIM_RIGHT: return "AimRight";
            case AIM_UP_RIGHT: return "AimUpRight";
            case AIM_UP: return "AimUp";
            case AIM_UP_LEFT: return "AimUpLeft";
            case AIM_LEFT: return "AimLeft";
            case AIM_DOWN_LEFT: return "AimDownLeft";
            case AIM_DOWN: return "AimDown";
            case AIM_DOWN_RIGHT: return "AimDownRight";
            default: return "AimKeep";
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

    private Vector2 DecodeAim(int aim)
    {
        switch (aim)
        {
            case AIM_RIGHT: return Vector2.right;
            case AIM_UP_RIGHT: return new Vector2(1f, 1f).normalized;
            case AIM_UP: return Vector2.up;
            case AIM_UP_LEFT: return new Vector2(-1f, 1f).normalized;
            case AIM_LEFT: return Vector2.left;
            case AIM_DOWN_LEFT: return new Vector2(-1f, -1f).normalized;
            case AIM_DOWN: return Vector2.down;
            case AIM_DOWN_RIGHT: return new Vector2(1f, -1f).normalized;
            default: return Vector2.zero;
        }
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

            if (Mathf.Abs(movement.x) > 0.0001f)
            {
                FaceAimDirection(movement);
            }
        }
    }

    private void ApplyAim(Vector2 aimDirection)
    {
        if (aimDirection.sqrMagnitude <= 0.0001f || _currentWeapon == null)
        {
            return;
        }

        Vector2 normalizedAim = aimDirection.normalized;
        _lastAimDirection = normalizedAim;
        FaceAimDirection(normalizedAim);

        WeaponAim weaponAim = _currentWeapon.GetComponent<WeaponAim>();
        if (weaponAim != null)
        {
            weaponAim.SetCurrentAim(GetWeaponAimInput(normalizedAim, weaponAim), true);
        }
    }

    private void ExecuteCombat(int combatAction, Vector2 movement, Vector2 aimDirection)
    {
        switch (combatAction)
        {
            case COMBAT_ATTACK:
                AttackInAimDirection(GetCombatAimDirection(aimDirection, combatAction));
                break;
            case COMBAT_DASH:
                DashInBestDirection(movement);
                break;
        }
    }

    private void AttackInAimDirection(Vector2 aimDirection)
    {
        if (!IsWeaponReady() || characterHandleWeapon == null)
        {
            return;
        }

        Vector2 attackDirection = aimDirection.sqrMagnitude > 0.0001f
            ? aimDirection.normalized
            : _lastAimDirection;

        if (attackDirection.sqrMagnitude <= 0.0001f)
        {
            attackDirection = Vector2.right;
        }

        _lastAimDirection = attackDirection.normalized;
        FaceAimDirection(_lastAimDirection);

        if (_currentWeapon != null)
        {
            WeaponAim weaponAim = _currentWeapon.GetComponent<WeaponAim>();
            if (weaponAim != null)
            {
                weaponAim.SetCurrentAim(GetWeaponAimInput(_lastAimDirection, weaponAim), true);
            }
        }

        characterHandleWeapon.ShootStart();
    }

    private void ForceScriptAimControl()
    {
        if (_currentWeapon == null) return;
        WeaponAim weaponAim = _currentWeapon.GetComponent<WeaponAim>();
        if (weaponAim != null)
        {
            weaponAim.AimControl = WeaponAim.AimControls.Script;
            weaponAim.AimControlActive = true;
            weaponAim.ReticleType = WeaponAim.ReticleTypes.None;
            weaponAim.Reticle = null;
            weaponAim.DisplayReticle = false;
            weaponAim.MoveCameraTargetTowardsReticle = false;
            weaponAim.RemoveReticle();
        }
    }

    private void ConfigureWeaponAimForTraining()
    {
        if (characterHandleWeapon == null)
        {
            return;
        }

        characterHandleWeapon.ForceWeaponAimControl = true;
        characterHandleWeapon.ForcedWeaponAimControl = WeaponAim.AimControls.Script;
    }

    private void EnsureCurrentWeaponStateInitialized()
    {
        if (characterHandleWeapon == null || characterHandleWeapon.CurrentWeapon == null)
        {
            return;
        }

        Weapon weapon = characterHandleWeapon.CurrentWeapon;
        if (weapon.WeaponState == null)
        {
            _currentWeapon = weapon;
            ForceScriptAimControl();
            weapon.Initialization();
        }
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

        _isDodging = HasNearbyBulletThreat();
        _dodgeStartHealth = agentHealth.CurrentHealth;
        _dodgeStartTime = Time.time;
        _tookDamageDuringDodge = false;

        characterDash2D.DashStart();
    }

    private bool HasNearbyBulletThreat(float threatRadius = 6f)
    {
        for (int i = 0; i < _bullets.Count; i++)
        {
            Projectile bullet = _bullets[i];
            if (bullet == null || !bullet.gameObject.activeInHierarchy) continue;
            if (Vector2.Distance(transform.position, bullet.transform.position) <= threatRadius)
                return true;
        }
        return false;
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
        return _currentWeapon != null
            && _currentWeapon.WeaponState != null
            && _currentWeapon.WeaponState.CurrentState == Weapon.WeaponStates.WeaponIdle;
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
        discreteActions[BRANCH_AIM] = AIM_KEEP;

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

        bool aimLeft = Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A);
        bool aimRight = Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D);
        bool aimDown = Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S);
        bool aimUp = Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W);

        if (aimRight && aimUp) discreteActions[BRANCH_AIM] = AIM_UP_RIGHT;
        else if (aimLeft && aimUp) discreteActions[BRANCH_AIM] = AIM_UP_LEFT;
        else if (aimLeft && aimDown) discreteActions[BRANCH_AIM] = AIM_DOWN_LEFT;
        else if (aimRight && aimDown) discreteActions[BRANCH_AIM] = AIM_DOWN_RIGHT;
        else if (aimRight) discreteActions[BRANCH_AIM] = AIM_RIGHT;
        else if (aimUp) discreteActions[BRANCH_AIM] = AIM_UP;
        else if (aimLeft) discreteActions[BRANCH_AIM] = AIM_LEFT;
        else if (aimDown) discreteActions[BRANCH_AIM] = AIM_DOWN;
    }
}
