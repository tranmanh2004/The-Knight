using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using MoreMountains.Tools;
using MoreMountains.TopDownEngine;

[RequireComponent(typeof(AIBrain), typeof(Health), typeof(AIDecisionDetectTargetRadius2D))]
public class MeleeAgent : Agent
{
    [Header("Reward Shaping Settings")]
    [Tooltip("Phần thưởng khi gây được sát thương lên người chơi.")]
    public float DealDamageReward = 0.5f;
    [Tooltip("Hình phạt khi nhận sát thương từ người chơi.")]
    public float TakeDamagePenalty = -0.5f;
    [Tooltip("Phần thưởng khi tiêu diệt được người chơi.")]
    public float KillPlayerReward = 1.0f;
    [Tooltip("Hình phạt khi bị người chơi tiêu diệt.")]
    public float AgentDiedPenalty = -1.0f;
    [Tooltip("Hình phạt nhỏ mỗi bước để khuyến khích AI hành động nhanh.")]
    public float TimePenalty = -0.001f;

    // --- CÁC THÀNH PHẦN CỐT LÕI ---
    private AIBrain aiBrain;
    private Health agentHealth;
    private AIDecisionDetectTargetRadius2D detectTargetDecision; // "Đôi mắt" của AI

    // --- BIẾN THEO DÕI TRẠNG THÁI ---
    private float previousPlayerHealth;
    private float previousAgentHealth;
    
    // --- BIẾN ĐỂ RESET ---
    private Vector3 agentStartingPosition;
    // Chúng ta không cần lưu vị trí player nữa, vì sẽ dùng Checkpoint hoặc TrainingManager

    // --- HẰNG SỐ ĐỊNH DANH HÀNH ĐỘNG ---
    private const int STATE_DETECTING = 0;
    private const int STATE_MOVING = 1;
    private const int STATE_ATTACKING = 2;

    public override void Initialize()
    {
        aiBrain = GetComponent<AIBrain>();
        agentHealth = GetComponent<Health>();
        detectTargetDecision = GetComponent<AIDecisionDetectTargetRadius2D>();

        if (detectTargetDecision == null)
        {
            Debug.LogError("Lỗi nghiêm trọng: MeleeAgent không tìm thấy component AIDecisionDetectTargetRadius2D! Hãy thêm component này vào agent.", this.gameObject);
        }

        aiBrain.BrainActive = false; // Giao quyền điều khiển cho ML-Agents
        agentStartingPosition = transform.position;
    }

    public override void OnEpisodeBegin()
    {
        // Yêu cầu này nên được xử lý bởi một Manager riêng để có thể reset cả Player và quái vật khác
        // Ở đây, chúng ta sẽ tự reset bản thân AI trước
        agentHealth.Revive(); 
        transform.position = agentStartingPosition;

        // Reset các biến theo dõi
        previousAgentHealth = agentHealth.MaximumHealth;
        previousPlayerHealth = 0f; // Sẽ được cập nhật khi thấy player

        // Reset "não" AI
        aiBrain.Target = null;
        aiBrain.TransitionToState("Detecting");
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. Dùng "giác quan" để kiểm tra xem có phát hiện được mục tiêu không
        bool isTargetDetected = detectTargetDecision.Decide();

        if (isTargetDetected && aiBrain.Target != null)
        {
            // Nếu CÓ, cung cấp thông tin thực tế
            Health targetHealth = aiBrain.Target.GetComponent<Health>();
            if (targetHealth != null && targetHealth.CurrentHealth > 0)
            {
                sensor.AddObservation(Vector3.Distance(transform.position, aiBrain.Target.position));
                sensor.AddObservation((aiBrain.Target.position - transform.position).normalized);
                sensor.AddObservation(targetHealth.CurrentHealth / targetHealth.MaximumHealth); // Quan sát máu của mục tiêu
            }
            else
            {
                AddObservation_TargetNotAvailable(sensor); // Mục tiêu đã chết hoặc không hợp lệ
            }
        }
        else
        {
            // Nếu KHÔNG, cung cấp "thông tin rỗng"
            AddObservation_TargetNotAvailable(sensor);
        }

        // 2. Luôn cung cấp thông tin về bản thân
        sensor.AddObservation(agentHealth.CurrentHealth / agentHealth.MaximumHealth);
    }

    private void AddObservation_TargetNotAvailable(VectorSensor sensor)
    {
        sensor.AddObservation(-1.0f); // Khoảng cách không hợp lệ
        sensor.AddObservation(Vector3.zero); // Hướng không xác định
        sensor.AddObservation(-1.0f); // Máu mục tiêu không xác định
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // --- KIỂM TRA ĐIỀU KIỆN KẾT THÚC EPISODE ---
        if (agentHealth.CurrentHealth < previousAgentHealth)
        {
            AddReward(TakeDamagePenalty);
        }
        if (agentHealth.CurrentHealth <= 0)
        {
            AddReward(AgentDiedPenalty);
            EndEpisode();
            return;
        }

        // --- LOGIC THƯỞNG/PHẠT CHỈ HOẠT ĐỘNG KHI CÓ MỤC TIÊU ---
        if (aiBrain.Target != null)
        {
            Health playerHealth = aiBrain.Target.GetComponent<Health>();
            if (playerHealth != null)
            {
                // Thưởng khi gây sát thương
                if (playerHealth.CurrentHealth < previousPlayerHealth)
                {
                    AddReward(DealDamageReward);
                }
                
                // Thưởng lớn khi tiêu diệt được mục tiêu
                if (playerHealth.CurrentHealth <= 0)
                {
                    AddReward(KillPlayerReward);
                    EndEpisode();
                    return;
                }
                
                // Cập nhật máu của player cho lượt sau
                previousPlayerHealth = playerHealth.CurrentHealth;
            }
        }
        
        // Phạt theo thời gian để khuyến khích hành động
        AddReward(TimePenalty);
        
        // Cập nhật máu của agent cho lượt sau
        previousAgentHealth = agentHealth.CurrentHealth;

        // --- THỰC THI HÀNH ĐỘNG ---
        ExecuteAction(actions.DiscreteActions[0]);
    }

    private void ExecuteAction(int chosenAction)
    {
        float attackRange = 2.0f; // Ngưỡng này nên khớp với AIDecisionDistanceToTarget của bạn

        if (chosenAction == STATE_ATTACKING)
        {
            // Chỉ tấn công khi có mục tiêu và ở trong tầm
            if (aiBrain.Target != null && Vector3.Distance(transform.position, aiBrain.Target.position) <= attackRange)
            {
                aiBrain.TransitionToState("Attacking");
            }
            // Nếu không, AI đã ra quyết định sai, và hàm phần thưởng sẽ tự xử lý việc trừng phạt.
            // Chúng ta không cần làm gì thêm.
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
        var discreteActionsOut = actionsOut.DiscreteActions;
        if (Input.GetKeyDown(KeyCode.Alpha1)) { discreteActionsOut[0] = STATE_DETECTING; }
        else if (Input.GetKeyDown(KeyCode.Alpha2)) { discreteActionsOut[0] = STATE_MOVING; }
        else if (Input.GetKeyDown(KeyCode.Alpha3)) { discreteActionsOut[0] = STATE_ATTACKING; }
    }
}