using UnityEngine;
using UnityEditor;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using MoreMountains.TopDownEngine;
using MoreMountains.Tools;

/// <summary>
/// Editor tool: finds the "Koala" GameObject in the active scene and adds all
/// components needed for ML-Agents training (AttentionAgent, BehaviorParameters,
/// DecisionRequester, AIBrain, AIDecisionDetectTargetRadius2D, Health, and the
/// five AI action states: Moving, Deciding, Attacking, Dashing, MoveAway).
///
/// Menu: Tools → Setup Koala Agent
/// </summary>
public class KoalaAgentSetup : Editor
{
    [MenuItem("Tools/Setup Koala Agent")]
    private static void SetupKoalaAgent()
    {
        // ── 1. Find the "Koala" GameObject ──────────────────────────────────
        GameObject koala = GameObject.Find("Koala");
        if (koala == null)
        {
            EditorUtility.DisplayDialog("Koala Agent Setup",
                "No GameObject named 'Koala' found in the active scene.\n\nPlease add an empty GameObject named 'Koala' first.",
                "OK");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(koala, "Setup Koala Agent");

        // ── 2. Health ────────────────────────────────────────────────────────
        Health health = GetOrAdd<Health>(koala);
        health.MaximumHealth = 100f;
        health.CurrentHealth = 100f;

        // ── 3. Character ─────────────────────────────────────────────────────
        Character character = GetOrAdd<Character>(koala);
        character.CharacterType = Character.CharacterTypes.AI;

        // ── 4. TopDownController2D ───────────────────────────────────────────
        GetOrAdd<TopDownController2D>(koala);

        // ── 5. CharacterMovement ─────────────────────────────────────────────
        GetOrAdd<CharacterMovement>(koala);

        // ── 6. CharacterOrientation2D ────────────────────────────────────────
        GetOrAdd<CharacterOrientation2D>(koala);

        // ── 7. CharacterHandleWeapon ─────────────────────────────────────────
        GetOrAdd<CharacterHandleWeapon>(koala);

        // ── 7b. CharacterDash2D ──────────────────────────────────────────────
        GetOrAdd<CharacterDash2D>(koala);

        // ── 8. Rigidbody2D ───────────────────────────────────────────────────
        Rigidbody2D rb = GetOrAdd<Rigidbody2D>(koala);
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        // ── 9. Collider ──────────────────────────────────────────────────────
        if (koala.GetComponent<Collider2D>() == null)
        {
            CapsuleCollider2D col = koala.AddComponent<CapsuleCollider2D>();
            col.size = new Vector2(0.6f, 0.8f);
            col.offset = new Vector2(0f, 0.1f);
        }

        // ── 10. AIDecisionDetectTargetRadius2D ───────────────────────────────
        AIDecisionDetectTargetRadius2D detect = GetOrAdd<AIDecisionDetectTargetRadius2D>(koala);
        detect.Radius = 10f;
        // Target layer = Player layer (8 = built-in Player layer)
        detect.TargetLayer = LayerMask.GetMask("Player");

        // ── 11. AI Action components ─────────────────────────────────────────
        AIActionMoveTowardsTarget2D moveTowards   = GetOrAdd<AIActionMoveTowardsTarget2D>(koala);
        AIActionMoveAwayFromTarget2D moveAway      = GetOrAdd<AIActionMoveAwayFromTarget2D>(koala);
        AIActionAimWeaponAtTarget2D aimAtTarget    = GetOrAdd<AIActionAimWeaponAtTarget2D>(koala);
        AIActionShoot2D shoot                      = GetOrAdd<AIActionShoot2D>(koala);
        AIActionDash dash                          = GetOrAdd<AIActionDash>(koala);

        // ── 12. AIBrain with 4 states ────────────────────────────────────────
        AIBrain brain = GetOrAdd<AIBrain>(koala);

        AIState stateMoving = new AIState();
        stateMoving.StateName = "Moving";
        stateMoving.Actions = new AIActionsList();
        stateMoving.Actions.Add(moveTowards);
        stateMoving.Transitions = new AITransitionsList();
        AITransition toDeciding = new AITransition();
        toDeciding.Decision = detect;
        toDeciding.TrueState = "Deciding";
        stateMoving.Transitions.Add(toDeciding);

        AIState stateDeciding = new AIState();
        stateDeciding.StateName = "Deciding";
        stateDeciding.Actions = new AIActionsList();
        stateDeciding.Actions.Add(aimAtTarget);
        stateDeciding.Actions.Add(moveTowards);
        stateDeciding.Transitions = new AITransitionsList();
        AITransition toAttacking = new AITransition();
        toAttacking.Decision = detect;
        toAttacking.TrueState = "Attacking";
        stateDeciding.Transitions.Add(toAttacking);

        AIState stateAttacking = new AIState();
        stateAttacking.StateName = "Attacking";
        stateAttacking.Actions = new AIActionsList();
        stateAttacking.Actions.Add(shoot);
        stateAttacking.Transitions = new AITransitionsList();
        AITransition backToMoving = new AITransition();
        backToMoving.Decision = detect;
        backToMoving.FalseState = "Moving";
        stateAttacking.Transitions.Add(backToMoving);

        AIState stateDashing = new AIState();
        stateDashing.StateName = "Dashing";
        stateDashing.Actions = new AIActionsList();
        stateDashing.Actions.Add(dash);
        stateDashing.Transitions = new AITransitionsList();

        AIState stateMoveAway = new AIState();
        stateMoveAway.StateName = "MoveAway";
        stateMoveAway.Actions = new AIActionsList();
        stateMoveAway.Actions.Add(moveAway);
        stateMoveAway.Transitions = new AITransitionsList();

        brain.States = new System.Collections.Generic.List<AIState>
        {
            stateMoving, stateDeciding, stateAttacking, stateDashing, stateMoveAway
        };

        brain.BrainActive = true;

        // ── 13. BehaviorParameters ───────────────────────────────────────────
        BehaviorParameters bp = GetOrAdd<BehaviorParameters>(koala);
        bp.BehaviorName = "AttentionAgentConfig";
        bp.BehaviorType = BehaviorType.Default;

        var actionSpec = Unity.MLAgents.Actuators.ActionSpec.MakeDiscrete(5); // 5 actions
        bp.BrainParameters.ActionSpec = actionSpec;
        bp.BrainParameters.VectorObservationSize = 321;
        bp.BrainParameters.NumStackedVectorObservations = 1;

        // ── 14. DecisionRequester ────────────────────────────────────────────
        Unity.MLAgents.DecisionRequester dr = GetOrAdd<Unity.MLAgents.DecisionRequester>(koala);
        dr.DecisionPeriod = 5;
        dr.TakeActionsBetweenDecisions = true;

        // ── 15. AttentionAgent ───────────────────────────────────────────────
        AttentionAgent agent = GetOrAdd<AttentionAgent>(koala);
        agent.MaxStep = 5000;
        agent.DealDamageReward    =  0.5f;
        agent.TakeDamagePenalty   = -0.5f;
        agent.KillPlayerReward    =  1.0f;
        agent.AgentDiedPenalty    = -1.0f;
        agent.TimePenalty         = -0.001f;
        agent.DodgeSuccessReward  =  0.2f;
        agent.VisionRadius        = 20f;
        agent.MaxEnemies          = 3;
        agent.MaxBullets          = 10;
        agent.MaxItems            = 4;
        agent.MaxHazards          = 5;
        agent.ActionConfigs = new System.Collections.Generic.List<AttentionActionConfig>
        {
            new AttentionActionConfig { StateName = "Moving",   LockDuration = 0f,   RequiresWeaponIdle = false },
            new AttentionActionConfig { StateName = "Deciding", LockDuration = 0f,   RequiresWeaponIdle = false },
            new AttentionActionConfig { StateName = "Attacking",LockDuration = 0.3f, RequiresWeaponIdle = true  },
            new AttentionActionConfig { StateName = "Dashing",  LockDuration = 0.4f, RequiresWeaponIdle = false },
            new AttentionActionConfig { StateName = "MoveAway", LockDuration = 0f,   RequiresWeaponIdle = false },
        };

        // Auto-find TilemapGenerator on "Map" and enable per-episode map generation
        GameObject mapGo = GameObject.Find("Map");
        TilemapGenerator roomGen = mapGo != null ? mapGo.GetComponent<TilemapGenerator>() : null;
        agent.EpisodeRoomGenerator = roomGen;
        agent.GenerateRandomMapOnEpisodeBegin = (roomGen != null);
        agent.ForceFolderRandomMode = true;

        // ── 16. Tag & Layer ──────────────────────────────────────────────────
        koala.tag   = "Enemy";
        koala.layer = LayerMask.NameToLayer("Enemy");

        // ── Mark scene dirty ─────────────────────────────────────────────────
        EditorUtility.SetDirty(koala);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(koala.scene);

        Debug.Log("[KoalaAgentSetup] Done! All components added to 'Koala'. Remember to assign a weapon to CharacterHandleWeapon and save the scene.");
        EditorUtility.DisplayDialog("Koala Agent Setup",
            "Setup complete!\n\n" +
            "Components added:\n" +
            "✓ Health\n" +
            "✓ Character (AI type)\n" +
            "✓ TopDownController2D\n" +
            "✓ CharacterMovement\n" +
            "✓ CharacterOrientation2D\n" +
            "✓ CharacterHandleWeapon\n" +
            "✓ Rigidbody2D\n" +
            "✓ CapsuleCollider2D\n" +
            "✓ AIDecisionDetectTargetRadius2D\n" +
            "✓ CharacterDash2D\n" +
            "✓ AIBrain (Moving / Deciding / Attacking / Dashing / MoveAway)\n" +
            "✓ BehaviorParameters (obs=321, actions=5)\n" +
            "✓ DecisionRequester (period=5)\n" +
            "✓ AttentionAgent\n\n" +
            "✓ Map generation per episode: " + (roomGen != null ? "ENABLED (Map found)" : "DISABLED — 'Map' GameObject not found") + "\n\n" +
            "Next: assign a weapon to CharacterHandleWeapon, then save the scene.",
            "OK");
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T comp = go.GetComponent<T>();
        if (comp == null)
            comp = go.AddComponent<T>();
        return comp;
    }
}
