using UnityEngine;
using UnityEditor;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using MoreMountains.TopDownEngine;
using MoreMountains.Tools;

/// <summary>
/// Editor tool: finds the "Koala" GameObject in the active scene and adds all
/// components needed for ML-Agents training (CombatAgent, BehaviorParameters,
/// DecisionRequester, AIBrain compatibility target cache, AIDecisionDetectTargetRadius2D,
/// Health, movement, weapon, and dash abilities).
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
        CharacterDash2D dashAbility = GetOrAdd<CharacterDash2D>(koala);
        dashAbility.DashMode = CharacterDash2D.DashModes.Script;

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
        detect.TargetLayer = LayerMask.GetMask("Enemies");

        // ── 11. AIBrain compatibility only ───────────────────────────────────
        AIBrain brain = GetOrAdd<AIBrain>(koala);
        brain.States = new System.Collections.Generic.List<AIState>();
        brain.BrainActive = true;

        // ── 12. BehaviorParameters ───────────────────────────────────────────
        BehaviorParameters bp = GetOrAdd<BehaviorParameters>(koala);
        bp.BehaviorName = "CombatAgentConfig";
        bp.BehaviorType = BehaviorType.Default;

        var actionSpec = Unity.MLAgents.Actuators.ActionSpec.MakeDiscrete(3, 3, 3, 9);
        bp.BrainParameters.ActionSpec = actionSpec;
        bp.BrainParameters.VectorObservationSize = CombatAgent.VectorObservationSize;
        bp.BrainParameters.NumStackedVectorObservations = 1;

        // ── 14. DecisionRequester ────────────────────────────────────────────
        Unity.MLAgents.DecisionRequester dr = GetOrAdd<Unity.MLAgents.DecisionRequester>(koala);
        dr.DecisionPeriod = 5;
        dr.TakeActionsBetweenDecisions = true;

        // ── 15. CombatAgent ───────────────────────────────────────────────
        CombatAgent agent = GetOrAdd<CombatAgent>(koala);
        agent.MaxStep = 5000;
        agent.VisionRadius        = 20f;
        agent.MaxEnemies          = 3;
        agent.MaxBullets          = 10;
        agent.OverlapBufferSize   = 128;

        // Auto-find TilemapGenerator on "Map" and enable per-episode map generation
        GameObject mapGo = GameObject.Find("Map");
        TilemapGenerator roomGen = mapGo != null ? mapGo.GetComponent<TilemapGenerator>() : null;
        agent.EpisodeRoomGenerator = roomGen;
        agent.GenerateRandomMapOnEpisodeBegin = (roomGen != null);
        agent.ForceFolderRandomMode = true;

        // ── 16. Tag & Layer ──────────────────────────────────────────────────
        koala.tag   = "Player";
        koala.layer = LayerMask.NameToLayer("Player");

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
            "✓ AIBrain compatibility target only (no policy states)\n" +
            "✓ BehaviorParameters (obs=" + CombatAgent.VectorObservationSize + ", branches=[3,3,3,9])\n" +
            "✓ DecisionRequester (period=5)\n" +
            "✓ CombatAgent\n\n" +
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
