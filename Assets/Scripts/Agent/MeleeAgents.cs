using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using MoreMountains.Tools;
using MoreMountains.TopDownEngine;
using Unity.MLAgents.Actuators;


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

    // --- CÁC THÀNH PHẦN CỐT LÕI ---
    private AIBrain aiBrain;
    private Health agentHealth;
    private AIDecisionDetectTargetRadius2D detectTargetDecision;
    private CharacterHandleWeapon characterHandleWeapon; // --- MỚI --- Tham chiếu đến "đôi tay" cầm vũ khí
    private Weapon _currentWeapon; // --- MỚI --- Vũ khí hiện tại

    // --- BIẾN THEO DÕI TRẠNG THÁI ---
    private float previousPlayerHealth;
    private float previousAgentHealth;
    
    // --- BIẾN ĐỂ RESET ---
    private Vector3 agentStartingPosition;

    // --- HẰNG SỐ ĐỊNH DANH HÀNH ĐỘNG ---
    private const int STATE_DETECTING = 0;
    private const int STATE_MOVING = 1;
    private const int STATE_ATTACKING = 2;

    public override void Initialize()
    {
        aiBrain = GetComponent<AIBrain>();
        agentHealth = GetComponent<Health>();
        detectTargetDecision = GetComponent<AIDecisionDetectTargetRadius2D>();
        characterHandleWeapon = GetComponent<CharacterHandleWeapon>(); // --- MỚI --- Lấy component HandleWeapon

        if (characterHandleWeapon == null)
        {
            Debug.LogError("Lỗi nghiêm trọng: MeleeAgent không tìm thấy component CharacterHandleWeapon!", this.gameObject);
        }

        aiBrain.BrainActive = false;
        agentStartingPosition = transform.position;
    }

    public override void OnEpisodeBegin()
    {
        agentHealth.Revive(); 
        transform.position = agentStartingPosition;
        
        previousAgentHealth = agentHealth.MaximumHealth;

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
    
    // --- MỚI --- Thêm logic Action Masking
    public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
    {
        // Cập nhật tham chiếu vũ khí trước khi kiểm tra (phòng trường hợp vũ khí thay đổi)
        if (characterHandleWeapon != null)
        {
            _currentWeapon = characterHandleWeapon.CurrentWeapon;
        }
        
        if (_currentWeapon == null || _currentWeapon.WeaponState.CurrentState != Weapon.WeaponStates.WeaponIdle)
        {
            // Che hành động Attack nếu không có vũ khí hoặc vũ khí đang không rảnh rỗi (đang tấn công, đang cooldown)
            actionMask.SetActionEnabled(0, STATE_ATTACKING, false);
        }
        
        // (Tùy chọn) Bạn cũng có thể che Attack nếu ở ngoài tầm, nhưng để AI tự học cách bị phạt cũng là một cách hay.
        // float attackRange = 2.0f;
        // if (aiBrain.Target == null || Vector3.Distance(transform.position, aiBrain.Target.position) > attackRange)
        // {
        //     actionMasker.SetActionEnabled(0, STATE_ATTACKING, false);
        // }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // ... (phần code này giữ nguyên) ...
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
        ExecuteAction(actions.DiscreteActions[0]);
    }

    private void ExecuteAction(int chosenAction)
    {
        // Vì đã dùng Action Masking, chúng ta có thể tin tưởng hơn vào quyết định của AI
        // Logic kiểm tra ở đây chỉ để dự phòng
        if (chosenAction == STATE_ATTACKING)
        {
            if (_currentWeapon != null && _currentWeapon.WeaponState.CurrentState == Weapon.WeaponStates.WeaponIdle)
            {
                 aiBrain.TransitionToState("Attacking");
            }
        }
        else if (chosenAction == STATE_MOVING)
        {
            aiBrain.TransitionToState("Moving");
        }
        else if (chosenAction == STATE_DETECTING)
        {
            aiBrain.TransitionToState("Detecting");
        }
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        // Heuristic để test agent bằng cách điều khiển thủ công
        int action = STATE_DETECTING;
        
        // Kiểm tra input từ bàn phím
        if (Input.GetKey(KeyCode.Alpha1))
        {
            action = STATE_DETECTING;
        }
        else if (Input.GetKey(KeyCode.Alpha2))
        {
            action = STATE_MOVING;
        }
        else if (Input.GetKey(KeyCode.Alpha3))
        {
            action = STATE_ATTACKING;
        }
        
        actionsOut.DiscreteActions.Array[0] = action;
    }
}