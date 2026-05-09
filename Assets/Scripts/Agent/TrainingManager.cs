using UnityEngine;
using MoreMountains.TopDownEngine;
using MoreMountains.Tools;

public class TrainingManager : MonoBehaviour, MMEventListener<TopDownEngineEvent>
{
    [SerializeField] private CombatAgent combatAgent;
    [SerializeField] private TilemapGenerator tilemapGenerator;

    void OnEnable()
    {
        this.MMEventStartListening<TopDownEngineEvent>();
    }

    void OnDisable()
    {
        this.MMEventStopListening<TopDownEngineEvent>();
    }

    public void OnMMEvent(TopDownEngineEvent engineEvent)
    {
        if (engineEvent.EventType == TopDownEngineEventTypes.PlayerDeath)
        {
            if (tilemapGenerator != null)
                tilemapGenerator.RespawnEnemies();

            if (combatAgent != null)
                combatAgent.EndEpisode();
            else
                TopDownEngineEvent.Trigger(TopDownEngineEventTypes.RespawnStarted, null);
        }
    }
}