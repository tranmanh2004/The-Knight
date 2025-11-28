using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using MoreMountains.Tools;
using MoreMountains.TopDownEngine;
using Unity.MLAgents.Actuators;
using System;
using System.Collections.Generic;


/// <summary>
/// Cấu hình cho một hành động có thể đợi
/// </summary>
[Serializable]
public class ActionConfig
{
    public string StateName;           // Tên state trong AIBrain (ví dụ: "Attacking", "Dashing")
    public float LockDuration = 0.5f;  // Thời gian khóa tối thiểu
    public bool RequiresWeaponIdle = true; // Có cần weapon idle không?
    
    [HideInInspector] public float Timer;
    [HideInInspector] public bool IsLocked;
}

[RequireComponent(typeof(AIBrain))]
[RequireComponent(typeof(Health))]
[RequireComponent(typeof(AIDecisionDetectTargetRadius2D))]
[RequireComponent(typeof(CharacterHandleWeapon))]
public class MeleeAgent : Agent
{
    [Header("Reward Shaping Settings")]
    public float DealDamageReward = 0.5f;
    public float TakeDamagePenalty = -0.5f;
    public float KillPlayerReward = 1.0f;
    public float AgentDiedPenalty = -1.0f;
    public float TimePenalty = -0.001f;

    [Header("Action Configurations")]
    [Tooltip("Cấu hình cho từng hành động cần đợi. Index tương ứng với action ID (0=Detecting, 1=Moving, 2=Attacking, 3=Dashing...)")]
    public List<ActionConfig> ActionConfigs = new List<ActionConfig>
    {
        new ActionConfig { StateName = "Detecting", LockDuration = 0f, RequiresWeaponIdle = false },
        new ActionConfig { StateName = "Moving", LockDuration = 0f, RequiresWeaponIdle = false },
        new ActionConfig { StateName = "Attacking", LockDuration = 0.5f, RequiresWeaponIdle = true }
        // Thêm các action khác ở đây: Dashing, Skill1, Skill2...
    };

    // --- CÁC THÀNH PHẦN CỐT LÕI ---
    private AIBrain aiBrain;
    private Health agentHealth;
    private AIDecisionDetectTargetRadius2D detectTargetDecision;
    private CharacterHandleWeapon characterHandleWeapon;
    private Weapon _currentWeapon;

    // --- BIẾN THEO DÕI TRẠNG THÁI ---
    private float previousPlayerHealth;
    private float previousAgentHealth;
    private int _currentLockedAction = -1; // -1 = không có action nào đang lock
    
    // --- BIẾN ĐỂ RESET ---
    private Vector3 agentStartingPosition;

    // --- HẰNG SỐ ĐỊNH DANH HÀNH ĐỘNG (phải khớp với thứ tự trong ActionConfigs) ---
    private const int ACTION_DETECTING = 0;
    private const int ACTION_MOVING = 1;
    private const int ACTION_ATTACKING = 2;
    // Thêm các hằng số khác nếu cần: ACTION_DASHING = 3, ACTION_SKILL1 = 4...

    private bool _brainInitialized = false;

    /// <summary>
    /// Awake chạy trước Update của các component khác, đảm bảo AIBrain được init sớm
    /// </summary>
    private void Awake()
    {
        aiBrain = GetComponent<AIBrain>();
        agentHealth = GetComponent<Health>();
        detectTargetDecision = GetComponent<AIDecisionDetectTargetRadius2D>();
        characterHandleWeapon = GetComponent<CharacterHandleWeapon>();

        if (aiBrain != null && !_brainInitialized)
        {
            // Gọi ResetBrain sớm để các AIAction/AIDecision được Initialization()
            aiBrain.ResetBrain();
            _brainInitialized = true;
        }
    }

    public override void Initialize()
    {
        // Đảm bảo components đã được cache (phòng trường hợp Awake chưa chạy)
        if (aiBrain == null) aiBrain = GetComponent<AIBrain>();
        if (agentHealth == null) agentHealth = GetComponent<Health>();
        if (detectTargetDecision == null) detectTargetDecision = GetComponent<AIDecisionDetectTargetRadius2D>();
        if (characterHandleWeapon == null) characterHandleWeapon = GetComponent<CharacterHandleWeapon>();

        if (characterHandleWeapon == null)
        {
            Debug.LogError("Lỗi nghiêm trọng: MeleeAgent không tìm thấy component CharacterHandleWeapon!", this.gameObject);
        }

        // Khởi tạo AIBrain nếu chưa được init trong Awake
        if (!_brainInitialized && aiBrain != null)
        {
            aiBrain.ResetBrain();
            _brainInitialized = true;
        }
        
        agentStartingPosition = transform.position;
    }

    public override void OnEpisodeBegin()
    {
        agentHealth.Revive(); 
        transform.position = agentStartingPosition;
        
        previousAgentHealth = agentHealth.MaximumHealth;
        _currentLockedAction = -1;
        
        // Reset tất cả action locks
        foreach (var config in ActionConfigs)
        {
            config.Timer = 0f;
            config.IsLocked = false;
        }

        // --- MỚI --- Cập nhật tham chiếu đến vũ khí khi bắt đầu episode
        _currentWeapon = characterHandleWeapon.CurrentWeapon;
        
        // Reset người chơi và lấy previousPlayerHealth đúng cách
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            Health playerHealth = playerObject.GetComponent<Health>();
            if (playerHealth != null)
            {
                playerHealth.Revive();
                previousPlayerHealth = playerHealth.MaximumHealth;
                // TODO: Đặt người chơi về vị trí ban đầu của họ
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
        aiBrain.TransitionToState("Detecting");
    }

    // === PHẦN QUAN SÁT ĐÃ ĐƯỢC NÂNG CẤP ===
    public override void CollectObservations(VectorSensor sensor)
    {
        // Cập nhật tham chiếu vũ khí trước khi quan sát (phòng trường hợp vũ khí thay đổi)
        if (characterHandleWeapon != null)
        {
            _currentWeapon = characterHandleWeapon.CurrentWeapon;
        }
        
        // 1. Quan sát mục tiêu (nếu có)
        bool isTargetDetected = detectTargetDecision.Decide();
        if (isTargetDetected && aiBrain.Target != null)
        {
            Health targetHealth = aiBrain.Target.GetComponent<Health>();
            if (targetHealth != null && targetHealth.CurrentHealth > 0)
            {
                sensor.AddObservation(Vector3.Distance(transform.position, aiBrain.Target.position));
                sensor.AddObservation((aiBrain.Target.position - transform.position).normalized);
                sensor.AddObservation(targetHealth.CurrentHealth / targetHealth.MaximumHealth);
            }
            else { AddObservation_TargetNotAvailable(sensor); }
        }
        else { AddObservation_TargetNotAvailable(sensor); }

        // 2. Quan sát bản thân
        sensor.AddObservation(agentHealth.CurrentHealth / agentHealth.MaximumHealth);
        
        // --- MỚI --- 3. Quan sát trạng thái cooldown của vũ khí
        if (_currentWeapon != null)
        {
            // Trạng thái sẵn sàng (1.0f nếu sẵn sàng, 0.0f nếu không)
            bool isWeaponReady = _currentWeapon.WeaponState.CurrentState == Weapon.WeaponStates.WeaponIdle;
            sensor.AddObservation(isWeaponReady ? 1.0f : 0.0f);

            // Thời gian cooldown còn lại (đã chuẩn hóa về 0-1)
            float cooldownLeft = _currentWeapon.CooldownTimeLeft;
            float totalCooldown = _currentWeapon.TimeBetweenUses;
            float normalizedCooldown = (totalCooldown > 0) ? (cooldownLeft / totalCooldown) : 0f;
            sensor.AddObservation(normalizedCooldown);
        }
        else
        {
            // Nếu không có vũ khí
            sensor.AddObservation(0.0f); // Không sẵn sàng
            sensor.AddObservation(0.0f); // Không có cooldown
        }
    }

    private void AddObservation_TargetNotAvailable(VectorSensor sensor)
    {
        sensor.AddObservation(-1.0f);
        sensor.AddObservation(Vector3.zero);
        sensor.AddObservation(-1.0f);
    }

    private bool IsWeaponReady()
    {
        return _currentWeapon != null && _currentWeapon.WeaponState.CurrentState == Weapon.WeaponStates.WeaponIdle;
    }
    
    // --- MỚI --- Thêm logic Action Masking
    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        // Cập nhật tham chiếu vũ khí
        if (characterHandleWeapon != null)
        {
            _currentWeapon = characterHandleWeapon.CurrentWeapon;
        }
        
        // Nếu đang có action bị lock, che tất cả action khác
        if (_currentLockedAction >= 0)
        {
            for (int i = 0; i < ActionConfigs.Count; i++)
            {
                if (i != ACTION_DETECTING) // Giữ ít nhất 1 action enabled
                {
                    actionMask.SetActionEnabled(0, i, false);
                }
            }
            return;
        }
        
        // Che các action cần weapon idle nhưng weapon chưa sẵn sàng
        for (int i = 0; i < ActionConfigs.Count; i++)
        {
            if (ActionConfigs[i].RequiresWeaponIdle && !IsWeaponReady())
            {
                actionMask.SetActionEnabled(0, i, false);
            }
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (characterHandleWeapon != null)
        {
            _currentWeapon = characterHandleWeapon.CurrentWeapon;
        }

        // === LUÔN TÍNH REWARD TRƯỚC (dù đang đợi action hay không) ===
        if (agentHealth.CurrentHealth < previousAgentHealth) { AddReward(TakeDamagePenalty); }
        if (agentHealth.CurrentHealth <= 0) { AddReward(AgentDiedPenalty); EndEpisode(); return; }

        if (aiBrain.Target != null)
        {
            Health playerHealth = aiBrain.Target.GetComponent<Health>();
            if (playerHealth != null)
            {
                if (playerHealth.CurrentHealth < previousPlayerHealth) { AddReward(DealDamageReward); }
                if (playerHealth.CurrentHealth <= 0) { AddReward(KillPlayerReward); EndEpisode(); return; }
                previousPlayerHealth = playerHealth.CurrentHealth;
            }
        }
        AddReward(TimePenalty);
        previousAgentHealth = agentHealth.CurrentHealth;

        // === KIỂM TRA ACTION LOCK ===
        if (_currentLockedAction >= 0)
        {
            var lockedConfig = ActionConfigs[_currentLockedAction];
            lockedConfig.Timer -= Time.fixedDeltaTime;
            
            bool timerDone = lockedConfig.Timer <= 0f;
            bool weaponReady = !lockedConfig.RequiresWeaponIdle || IsWeaponReady();
            
            if (timerDone && weaponReady)
            {
                // Action đã hoàn tất
                lockedConfig.IsLocked = false;
                _currentLockedAction = -1;
            }
            else
            {
                // Vẫn đang trong quá trình thực hiện action, bỏ qua quyết định mới
                return;
            }
        }

        ExecuteAction(actions.DiscreteActions[0]);
    }

    private void ExecuteAction(int chosenAction)
    {
        if (chosenAction < 0 || chosenAction >= ActionConfigs.Count)
        {
            Debug.LogWarning($"Action {chosenAction} không hợp lệ!");
            return;
        }
        
        var config = ActionConfigs[chosenAction];
        
        // Kiểm tra điều kiện thực hiện action
        if (config.RequiresWeaponIdle && !IsWeaponReady())
        {
            return; // Weapon chưa sẵn sàng
        }
        
        // Chuyển state
        aiBrain.TransitionToState(config.StateName);
        
        // Nếu action có lock duration > 0, bật lock
        if (config.LockDuration > 0f)
        {
            config.Timer = config.LockDuration;
            config.IsLocked = true;
            _currentLockedAction = chosenAction;
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // Heuristic để test agent bằng cách điều khiển thủ công
        int action = ACTION_DETECTING;
        
        // Kiểm tra input từ bàn phím (phím số tương ứng với action index)
        if (Input.GetKey(KeyCode.Alpha1)) action = ACTION_DETECTING;
        else if (Input.GetKey(KeyCode.Alpha2)) action = ACTION_MOVING;
        else if (Input.GetKey(KeyCode.Alpha3)) action = ACTION_ATTACKING;
        // Thêm các phím khác nếu cần:
        // else if (Input.GetKey(KeyCode.Alpha4)) action = ACTION_DASHING;
        // else if (Input.GetKey(KeyCode.Alpha5)) action = ACTION_SKILL1;
        
        actionsOut.DiscreteActions.Array[0] = action;
    }
}