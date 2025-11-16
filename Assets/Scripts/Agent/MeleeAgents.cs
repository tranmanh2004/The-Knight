using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using MoreMountains.Tools; // Cần thiết để truy cập AIBrain
using MoreMountains.TopDownEngine; // Cần thiết để truy cập Health

[RequireComponent(typeof(AIBrain), typeof(Health))]
public class MeleeAgent : Agent
{
    private AIBrain aiBrain;
    private Transform player;
    private Health playerHealth; // Biến để theo dõi máu người chơi
    private Health agentHealth; // Biến để theo dõi máu của chính AI

    private Vector3 startingPosition; // Vị trí ban đầu của AI

    private const int STATE_DETECTING = 0;
    private const int STATE_MOVING = 1;
    private const int STATE_ATTACKING = 2;

    public override void Initialize()
    {
        aiBrain = GetComponent<AIBrain>();
        agentHealth = GetComponent<Health>();
        aiBrain.BrainActive = false;
        
        startingPosition = transform.position; // Lưu vị trí ban đầu
    }

    public override void OnEpisodeBegin()
    {
        // --- PHẦN RESET MÔI TRƯỜNG ---

        // Reset lại máu và vị trí của AI
        agentHealth.Revive(); 
        transform.position = startingPosition;

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
            playerHealth = playerObject.GetComponent<Health>();
            aiBrain.Target = player;

            // Reset lại máu và vị trí của người chơi
            playerHealth.Revive();
            // TODO: Bạn nên có một script quản lý để đặt lại vị trí người chơi về điểm xuất phát của họ
            // Ví dụ: player.position = new Vector3(0, 1, 0); 
        }
        else
        {
            Debug.LogError("Không tìm thấy đối tượng có tag 'Player'.");
        }

        aiBrain.TransitionToState("Detecting");
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (player == null || playerHealth == null || playerHealth.CurrentHealth <= 0)
        {
            // Nếu không có người chơi, gửi dữ liệu rỗng
            sensor.AddObservation(0f); // Khoảng cách
            sensor.AddObservation(Vector3.zero); // Hướng
            sensor.AddObservation(-1); // Trạng thái AI
            sensor.AddObservation(0f); // Máu của AI
            return;
        }

        // 1. Khoảng cách tới người chơi
        sensor.AddObservation(Vector3.Distance(transform.position, player.position));
        // 2. Hướng tới người chơi
        sensor.AddObservation((player.position - transform.position).normalized);
        // 3. Trạng thái hiện tại của AIBrain
        int currentStateIndex = -1;
        if (aiBrain.CurrentState.StateName == "Detecting") currentStateIndex = STATE_DETECTING;
        else if (aiBrain.CurrentState.StateName == "Moving") currentStateIndex = STATE_MOVING;
        else if (aiBrain.CurrentState.StateName == "Attacking") currentStateIndex = STATE_ATTACKING;
        sensor.AddObservation(currentStateIndex);
        // 4. Máu hiện tại của AI (đã chuẩn hóa)
        sensor.AddObservation(agentHealth.CurrentHealth / agentHealth.MaximumHealth);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // --- KIỂM TRA ĐIỀU KIỆN KẾT THÚC EPISODE ---

        // THẤT BẠI: Nếu AI chết
        if (agentHealth.CurrentHealth <= 0)
        {
            AddReward(-1.0f); // Phạt nặng vì đã chết
            EndEpisode();
            return; // Dừng ngay lập tức
        }

        // THÀNH CÔNG: Nếu người chơi chết
        if (playerHealth != null && playerHealth.CurrentHealth <= 0)
        {
            AddReward(1.0f); // Thưởng lớn vì đã tiêu diệt được mục tiêu
            EndEpisode();
            return; // Dừng ngay lập tức
        }

        // --- PHẦN LOGIC HÀNH ĐỘNG VÀ THƯỞNG/PHẠT (như cũ) ---
        int chosenState = actions.DiscreteActions[0];
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        float attackRange = 2.0f;
        float detectionRange = 10.0f;

        if (chosenState == STATE_ATTACKING)
        {
            if (distanceToPlayer <= attackRange)
            {
                // Thưởng nhẹ vì chọn tấn công đúng lúc, phần thưởng chính sẽ là khi giết được player
                AddReward(0.1f); 
                aiBrain.TransitionToState("Attacking");
                // Không nên EndEpisode() ở đây nữa, chỉ kết thúc khi player chết
            }
            else
            {
                AddReward(-0.5f);
            }
        }
        else if (chosenState == STATE_MOVING)
        {
            if (distanceToPlayer > attackRange && distanceToPlayer <= detectionRange)
            {
                AddReward(0.05f);
                aiBrain.TransitionToState("Moving");
            }
            else
            {
                AddReward(-0.1f);
            }
        }
        else if (chosenState == STATE_DETECTING)
        {
            if (distanceToPlayer > detectionRange)
            {
                AddReward(0.01f);
                aiBrain.TransitionToState("Detecting");
            }
            else
            {
                AddReward(-1.0f);
            }
        }
        AddReward(-0.001f);
    }

    // Dùng để test bằng cách điều khiển thủ công
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActionsOut = actionsOut.DiscreteActions;
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            discreteActionsOut[0] = STATE_DETECTING;
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            discreteActionsOut[0] = STATE_MOVING;
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            discreteActionsOut[0] = STATE_ATTACKING;
        }
    }
}