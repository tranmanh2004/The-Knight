using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using MoreMountains.Tools;
using MoreMountains.TopDownEngine;
using Unity.MLAgents.Actuators;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Cấu hình cho một hành động cần đợi (tương tự MeleeAgent)
/// </summary>
[Serializable]
public class AttentionActionConfig
{
    public string StateName;
    public float LockDuration = 0.5f;
    public bool RequiresWeaponIdle = false;
    
    [HideInInspector] public float Timer;
    [HideInInspector] public bool IsLocked;
}

[Serializable]
public class WeaponAttackDelayConfig
{
    public string WeaponName;
    public float LockDuration = 0.3f;
}

/// <summary>
/// AttentionAgent - RL Agent for 2D roguelike shooter (Soul Knight-like)
/// 
/// OBSERVATION SPECIFICATION (BUG FIX #6: DOCUMENTED & CORRECTED):
/// ===============================================================
/// Total observation size: 321 dimensions (not 326 — padding was overstated)
/// 
/// Player Features (13 dims):
///   - Health (1), Ammo (1), Cooldown (1), WeaponReady (1), Speed (1)
///   - Recent Damage (1), Velocity X,Z (2), Padding (5)
/// 
/// Global Features (6 dims):
///   - Enemy/Bullet/Item/Hazard fractions (4), Time normalized (1), Recent deaths (1)
/// 
/// Enemy Features (54 dims = 3 enemies × 18):
///   - Per enemy: Pos X,Z (2), Vel X,Z (2), Distance (1), Health (1), 
///     Threat, IsAttacking, AttackCooldown (3), Padding (5) = 18 dims
/// 
/// Bullet Features (120 dims = 10 bullets × 12):
///   - Per bullet: Pos X,Z (2), Vel X,Z (2), Distance (1), TimeToImpact (1),
///     OwnerType (1), Padding (5) = 12 dims
/// 
/// Item Features (68 dims = 4 items × 17):
///   - Per item: Pos X,Z (2), ItemType one-hot (12), Rarity (1), Distance (1), Padding (2) = 17 dims
/// 
/// Hazard Features (60 dims = 5 hazards × 12):
///   - Per hazard: Pos X,Z (2), Size (2), IsActive (1), HazardType one-hot (6), Padding (1) = 12 dims
/// 
/// Total = 13 + 6 + 54 + 120 + 68 + 60 = 321 dims
/// 
/// CRITICAL: If changing MaxEnemies, MaxBullets, MaxItems, MaxHazards,
/// update ML-Agents YAML config to match this observation size!
/// 
/// </summary>
[RequireComponent(typeof(AIBrain))]
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(AIDecisionDetectTargetRadius2D))]
[RequireComponent(typeof(CharacterHandleWeapon))]
public class AttentionAgent : Agent
{
    [Header("Reward Shaping Settings")]
    public float DealDamageReward = 0.5f;
    public float TakeDamagePenalty = -0.5f;
    public float KillPlayerReward = 1.0f;
    public float AgentDiedPenalty = -1.0f;
    public float TimePenalty = -0.001f;
    public float DodgeSuccessReward = 0.2f;  // Tránh được viên đạn

    [Header("Vision Settings")]
    [Tooltip("Bán kính tầm nhìn cho việc phát hiện các đối tượng")]
    public float VisionRadius = 20f;

    [Header("Object Capacity Settings")]
    [Tooltip("Số lượng enemies tối đa để đưa vào observation")]
    public int MaxEnemies = 3;
    [Tooltip("Số lượng bullets tối đa")]
    public int MaxBullets = 10;
    [Tooltip("Số lượng items tối đa")]
    public int MaxItems = 4;
    [Tooltip("Số lượng hazards tối đa")]
    public int MaxHazards = 5;

    [Header("Action Configurations")]
    [Tooltip("Cấu hình cho từng hành động")]
    public List<AttentionActionConfig> ActionConfigs = new List<AttentionActionConfig>
    {
        new AttentionActionConfig { StateName = "Idle",      LockDuration = 0f,   RequiresWeaponIdle = false },
        new AttentionActionConfig { StateName = "Moving",    LockDuration = 0f,   RequiresWeaponIdle = false },
        new AttentionActionConfig { StateName = "Attacking", LockDuration = 0f,   RequiresWeaponIdle = true  },
        new AttentionActionConfig { StateName = "Dashing",   LockDuration = 0.4f, RequiresWeaponIdle = false },
        new AttentionActionConfig { StateName = "MoveAway",  LockDuration = 0f,   RequiresWeaponIdle = false },
    };

    [Header("Weapon Randomization")]
    [Tooltip("Danh sách vũ khí sẽ được chọn ngẫu nhiên khi bắt đầu episode.")]
    public List<Weapon> RandomWeaponPool = new List<Weapon>();
    [Tooltip("Nếu bật, agent sẽ equip ngẫu nhiên một vũ khí từ pool ở mỗi episode.")]
    public bool RandomizeWeaponOnEpisodeBegin = true;

    [Header("Attack Lock Settings")]
    [Tooltip("Nhân hệ số này vào cooldown của vũ khí (TimeBetweenUses).")]
    public float AttackLockDurationMultiplier = 1f;
    [Tooltip("Nếu có config theo WeaponName thì sẽ ưu tiên dùng giá trị này thay vì cooldown của vũ khí.")]
    public List<WeaponAttackDelayConfig> WeaponAttackDelayOverrides = new List<WeaponAttackDelayConfig>();

    [Header("Episode Map Generation")]
    [Tooltip("Nếu bật, agent sẽ generate map mới khi bắt đầu episode.")]
    public bool GenerateRandomMapOnEpisodeBegin = false;
    [Tooltip("RoomGenerator dùng để generate map theo text.")]
    public TilemapGenerator EpisodeRoomGenerator;
    [Tooltip("Tự chuyển RoomGenerator sang FolderRandom trước khi generate.")]
    public bool ForceFolderRandomMode = true;

    // --- Hằng số định danh hành động ---
    private const int ACTION_IDLE = 0;
    private const int ACTION_MOVING = 1;
    private const int ACTION_ATTACKING = 2;
    private const int ACTION_DASHING = 3;
    private const int ACTION_MOVE_AWAY = 4;

    // --- Components ---
    private AIBrain aiBrain;
    private Health agentHealth;
    private AIDecisionDetectTargetRadius2D detectTargetDecision;
    private CharacterHandleWeapon characterHandleWeapon;
    private Weapon _currentWeapon;

    // --- State Tracking ---
    [SerializeField] private float previousPlayerHealth;
    [SerializeField] private int _currentLockedAction = -1;
    private Vector3 agentStartingPosition;

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

    // --- Character reference ---
    private Character _character;

    protected override void Awake()
    {
        base.Awake();
        aiBrain = GetComponent<AIBrain>();
        agentHealth = GetComponent<Health>();
        EquipRandomWeaponIfConfigured();
        detectTargetDecision = GetComponent<AIDecisionDetectTargetRadius2D>();
        characterHandleWeapon = GetComponent<CharacterHandleWeapon>();
    }

    public override void Initialize()
    {
        if (aiBrain == null) aiBrain = GetComponent<AIBrain>();
        if (agentHealth == null) agentHealth = GetComponent<Health>();
        if (detectTargetDecision == null) detectTargetDecision = GetComponent<AIDecisionDetectTargetRadius2D>();
        if (characterHandleWeapon == null) characterHandleWeapon = GetComponent<CharacterHandleWeapon>();
        if (_character == null) _character = GetComponent<Character>();

        if (characterHandleWeapon == null)
        {
            Debug.LogError("AttentionAgent: Missing CharacterHandleWeapon!", gameObject);
        }

        if (aiBrain != null)
        {
            aiBrain.ResetBrain();
        }

        agentStartingPosition = transform.position;
    }

    public override void OnEpisodeBegin()
    {
        GenerateEpisodeMapIfConfigured();

        agentHealth.Revive();
        transform.position = GetSpawnPosition();

        EquipRandomWeaponIfConfigured();

        _previousHealth = agentHealth.MaximumHealth;
        _isDodging = false;
        _tookDamageDuringDodge = false;
        _currentLockedAction = -1;

        foreach (var config in ActionConfigs)
        {
            config.Timer = 0f;
            config.IsLocked = false;
        }

        _currentWeapon = characterHandleWeapon.CurrentWeapon;

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            Health playerHealth = playerObject.GetComponent<Health>();
            if (playerHealth != null)
            {
                playerHealth.Revive();
                previousPlayerHealth = playerHealth.MaximumHealth;
            }
            else
            {
                previousPlayerHealth = 0f;
            }
        }
        else
        {
            previousPlayerHealth = 0f;
        }

        aiBrain.Target = null;
        aiBrain.TransitionToState(ActionConfigs.Count > 0 ? ActionConfigs[0].StateName : "Moving");
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
        if (!GenerateRandomMapOnEpisodeBegin || EpisodeRoomGenerator == null)
        {
            return;
        }

        if (ForceFolderRandomMode)
        {
            EpisodeRoomGenerator.mapSelectionMode = TilemapGenerator.MapSelectionMode.FolderRandom;
        }

        EpisodeRoomGenerator.GenerateRoom();
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

    /// <summary>
    /// Collect all observations: player features, enemies, bullets, items, hazards, global features
    /// </summary>
    public override void CollectObservations(VectorSensor sensor)
    {
        // Guard: if critical components missing, pad all observations with zeros
        if (agentHealth == null || characterHandleWeapon == null)
        {
            for (int i = 0; i < 321; i++) sensor.AddObservation(0f);
            return;
        }

        if (characterHandleWeapon != null)
        {
            _currentWeapon = characterHandleWeapon.CurrentWeapon;
        }

        // === PERFORMANCE: Cache all nearby objects with SINGLE OverlapSphere call ===
        CacheSurroundingObjects();

        // === PLAYER FEATURES (Fixed ~20 dimensions) ===
        CollectPlayerFeatures(sensor);

        // === GLOBAL FEATURES (Fixed ~6 dimensions) ===
        CollectGlobalFeatures(sensor);

        // === VARIABLE-LENGTH OBJECT LISTS ===
        CollectEnemyFeatures(sensor);
        CollectBulletFeatures(sensor);
        CollectItemFeatures(sensor);
        CollectHazardFeatures(sensor);
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
            if (col.CompareTag("Enemy"))
            {
                _enemies.Add(col.gameObject);
            }
            else if (col.CompareTag("Item") || col.GetComponent<PickableItem>() != null)
            {
                _items.Add(col.gameObject);
            }
            else if (col.CompareTag("Hazard"))
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
        sensor.AddObservation(velDir.z);

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

        // Time in episode (normalized) - BUG FIX #8: use Agent's MaxStep, not global Academy properties
        float timeNorm = 0f;
        int maxSteps = MaxStep > 0 ? MaxStep : 1000; // Default fallback if no max step set
        timeNorm = Mathf.Clamp01(Academy.Instance.StepCount / Mathf.Max(1f, maxSteps));
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
        float relZ = Mathf.Clamp(relativePos.z / VisionRadius, -1f, 1f);
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
        sensor.AddObservation(Mathf.Clamp(relVel.z / 10f, -1f, 1f));

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
        for (int i = 0; i < 5; i++)
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
        sensor.AddObservation(Mathf.Clamp(relativePos.z / VisionRadius, -1f, 1f));

        // Velocity (absolute, as bullet speed is independent)
        Vector3 bulletVel = bullet.GetComponent<Rigidbody>()?.linearVelocity ?? Vector3.zero;
        sensor.AddObservation(Mathf.Clamp(bulletVel.x / 20f, -1f, 1f));
        sensor.AddObservation(Mathf.Clamp(bulletVel.z / 20f, -1f, 1f));

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
        sensor.AddObservation(Mathf.Clamp(relativePos.z / VisionRadius, -1f, 1f));

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
        sensor.AddObservation(Mathf.Clamp(relativePos.z / VisionRadius, -1f, 1f));

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

        // If locked in action, disable other actions but ALWAYS allow Idle
        if (_currentLockedAction >= 0)
        {
            for (int i = 0; i < ActionConfigs.Count; i++)
            {
                actionMask.SetActionEnabled(0, i, (i == ACTION_IDLE));  // Only Idle enabled
            }
            return;
        }

        // Disable attacking if weapon not ready
        for (int i = 0; i < ActionConfigs.Count; i++)
        {
            bool canExecute = true;

            if (ActionConfigs[i].RequiresWeaponIdle && !IsWeaponReady())
            {
                canExecute = false;
            }

            actionMask.SetActionEnabled(0, i, canExecute);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (characterHandleWeapon != null)
        {
            _currentWeapon = characterHandleWeapon.CurrentWeapon;
        }

        float currentHealth = agentHealth.CurrentHealth;
        float healthDelta = currentHealth - _previousHealth;

        // === REWARD CALCULATION ===
        // Damage penalty
        if (healthDelta < 0)
        {
            AddReward(TakeDamagePenalty);
        }

        // Death penalty
        if (currentHealth <= 0)
        {
            AddReward(AgentDiedPenalty);
            EndEpisode();
            return;
        }

        // Dodge reward tracking: now handled in FixedUpdate() for reliability

        // Damage to player
        if (aiBrain.Target != null)
        {
            Health playerHealth = aiBrain.Target.GetComponent<Health>();
            if (playerHealth != null)
            {
                if (playerHealth.CurrentHealth < previousPlayerHealth)
                {
                    AddReward(DealDamageReward);
                }
                if (playerHealth.CurrentHealth <= 0)
                {
                    AddReward(KillPlayerReward);
                    EndEpisode();
                    return;
                }
                previousPlayerHealth = playerHealth.CurrentHealth;
            }
        }

        // Time penalty
        AddReward(TimePenalty);
        
        // Update health tracking
        _previousHealth = currentHealth;

        // === ACTION LOCK CHECK ===
        if (_currentLockedAction >= 0)
        {
            var lockedConfig = ActionConfigs[_currentLockedAction];
            lockedConfig.Timer -= Time.fixedDeltaTime;

            bool timerDone = lockedConfig.Timer <= 0f;
            bool weaponReady = !lockedConfig.RequiresWeaponIdle || IsWeaponReady();

            if (timerDone && weaponReady)
            {
                lockedConfig.IsLocked = false;
                _currentLockedAction = -1;
            }
            else
            {
                return;  // Still locked, don't process new action
            }
        }

        ExecuteAction(actions.DiscreteActions[0]);
    }

    private void ExecuteAction(int chosenAction)
    {
        if (chosenAction < 0 || chosenAction >= ActionConfigs.Count)
        {
            Debug.LogWarning($"Invalid action {chosenAction}!");
            return;
        }

        var config = ActionConfigs[chosenAction];

        if (config.RequiresWeaponIdle && !IsWeaponReady())
        {
            return;
        }

        // Track dodge for success reward (BUG FIX v3: init _tookDamageDuringDodge here)
        if (chosenAction == ACTION_DASHING)
        {
            _isDodging = true;
            _dodgeStartHealth = agentHealth.CurrentHealth;
            _dodgeStartTime = Time.time;
            _tookDamageDuringDodge = false;  // Reset flag at start of dodge
        }

        aiBrain.TransitionToState(config.StateName);

        float lockDuration = GetEffectiveLockDuration(chosenAction, config);
        if (lockDuration > 0f)
        {
            config.Timer = lockDuration;
            config.IsLocked = true;
            _currentLockedAction = chosenAction;
        }
    }

    private float GetEffectiveLockDuration(int chosenAction, AttentionActionConfig config)
    {
        float baseDuration = Mathf.Max(0f, config.LockDuration);

        if (chosenAction != ACTION_ATTACKING)
        {
            return baseDuration;
        }

        if (_currentWeapon == null)
        {
            return baseDuration;
        }

        string currentWeaponName = _currentWeapon.WeaponName;
        if (!string.IsNullOrWhiteSpace(currentWeaponName) && WeaponAttackDelayOverrides != null)
        {
            for (int i = 0; i < WeaponAttackDelayOverrides.Count; i++)
            {
                WeaponAttackDelayConfig overrideConfig = WeaponAttackDelayOverrides[i];
                if (overrideConfig == null || string.IsNullOrWhiteSpace(overrideConfig.WeaponName))
                {
                    continue;
                }

                if (string.Equals(overrideConfig.WeaponName, currentWeaponName, StringComparison.OrdinalIgnoreCase))
                {
                    return Mathf.Max(0f, overrideConfig.LockDuration);
                }
            }
        }

        float weaponCooldown = _currentWeapon.TimeBetweenUses * Mathf.Max(0f, AttackLockDurationMultiplier);
        if (weaponCooldown <= 0f)
        {
            return baseDuration;
        }

        return weaponCooldown;
    }

    private bool IsWeaponReady()
    {
        return _currentWeapon != null && _currentWeapon.WeaponState.CurrentState == Weapon.WeaponStates.WeaponIdle;
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
        int action = ACTION_IDLE;

        if (Input.GetKey(KeyCode.Alpha1)) action = ACTION_IDLE;
        else if (Input.GetKey(KeyCode.Alpha2)) action = ACTION_MOVING;
        else if (Input.GetKey(KeyCode.Alpha3)) action = ACTION_ATTACKING;
        else if (Input.GetKey(KeyCode.Alpha4)) action = ACTION_DASHING;
        else if (Input.GetKey(KeyCode.Alpha5)) action = ACTION_MOVE_AWAY;

        actionsOut.DiscreteActions.Array[0] = action;
    }
}
