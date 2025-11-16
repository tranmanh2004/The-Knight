using UnityEngine;
using MoreMountains.TopDownEngine;
using MoreMountains.Tools;
using System.Collections.Generic;

// Lớp này lắng nghe sự kiện của game
public class TrainingManager : MonoBehaviour, MMEventListener<MMGameEvent> 
{
    // (Các biến khác của bạn có thể giữ nguyên)

    // Bắt đầu lắng nghe sự kiện khi được bật
    void OnEnable()
    {
        this.MMEventStartListening<MMGameEvent>();
    }

    // Ngừng lắng nghe khi bị tắt
    void OnDisable()
    {
        this.MMEventStopListening<MMGameEvent>();
    }

    // Hàm này sẽ được gọi mỗi khi có một MMGameEvent được bắn ra
    public void OnMMEvent(MMGameEvent gameEvent)
    {
        // Kiểm tra xem có phải là sự kiện "PlayerDeath" không
        if (gameEvent.EventName == "PlayerDeath")
        {
            // --- LOGIC MỚI NẰM Ở ĐÂY ---
            
            Debug.Log("TrainingManager detected PlayerDeath, triggering instant respawn.");

            // Ngay lập tức bắn ra sự kiện yêu cầu respawn
            TopDownEngineEvent.Trigger(TopDownEngineEventTypes.RespawnStarted, null);

            // Nếu bạn đang dùng ML-Agents, bạn vẫn có thể xử lý logic EndEpisode ở đây
            // foreach (var agent in Agents) { agent.OnPlayerDied(); }
        }
    }

    // public void OnMMEvent(MMGameEvent eventType)
    // {
    //     throw new System.NotImplementedException();
    // }
}