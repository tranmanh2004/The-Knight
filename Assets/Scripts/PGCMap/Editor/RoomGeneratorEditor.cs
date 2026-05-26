using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TilemapGenerator))]
[CanEditMultipleObjects]
public class TilemapGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        TilemapGenerator gen = (TilemapGenerator)target;

        // --- Tilemap References ---
        Header("Tilemap References");
        Draw("tilemap"); Draw("collisionTilemap"); Draw("floorTile"); Draw("wallTile");
        Draw("ensureFloorGroundCollider");

        // --- Map Placement ---
        Header("Map Placement");
        Draw("startPosition");

        // --- Generation Mode ---
        Header("Generation Mode");
        Draw("generationMode");

        EditorGUILayout.Space(4);

        switch (gen.generationMode)
        {
            case TilemapGenerator.MapGenerationMode.TextAsset:
                DrawTextAssetSection(gen);
                break;

            case TilemapGenerator.MapGenerationMode.BSP:
                Header("BSP Settings");
                Draw("bspMapSize"); Draw("bspDepth"); Draw("bspMinRoomSize");
                break;

            case TilemapGenerator.MapGenerationMode.CellularAutomata:
                Header("Cellular Automata Settings");
                Draw("caMapSize"); Draw("caFillRatio"); Draw("caIterations");
                Draw("caBirthThreshold"); Draw("caDeathThreshold");
                break;

            case TilemapGenerator.MapGenerationMode.RandomWalk:
                Header("Random Walk Settings");
                Draw("rwMapSize"); Draw("rwSteps");
                break;
        }

        // --- Spawn Point ---
        Header("Spawn Point Reference");
        Draw("playerSpawnPointTransform");
        Draw("randomizePlayerSpawnFromFloor");

        // --- Enemy Spawn ---
        Header("Random Enemy Spawn");
        Draw("spawnEnemiesFromText");
        Draw("limitRandomEnemySpawnCount");
        Draw("randomEnemySpawnCount");
        Draw("playerSpawnWallClearanceCells");
        Draw("enemyPrefabs");
        Draw("enemyParent");

        // --- Spawn Cleanup ---
        Header("Spawn Area Cleanup");
        Draw("regenerateRoomOnRespawn");
        Draw("clearPlayerSpawnArea");
        Draw("spawnAreaClearanceCells");
        Draw("preserveOuterWallBorder");
        Draw("logSpawnAreaCleanup");

        // --- Pool ---
        Header("Enemy Object Pool");
        Draw("poolSizePerPrefab");
        Draw("enforceContinuousCollisionForEnemies");

        serializedObject.ApplyModifiedProperties();

        // --- Buttons ---
        EditorGUILayout.Space(6);
        DrawButtons(gen);
    }

    // -------------------------------------------------------------------------

    private void DrawTextAssetSection(TilemapGenerator gen)
    {
        Header("Text Map Settings");
        Draw("mapSelectionMode");

        switch (gen.mapSelectionMode)
        {
            case TilemapGenerator.MapSelectionMode.SingleTextAsset:
                Draw("roomLayoutText");
                break;

            case TilemapGenerator.MapSelectionMode.FolderByIndex:
                Draw("textMapFolder");
                Draw("autoRefreshFolderMaps");
                Draw("folderTextMaps");
                Draw("selectedFolderMapIndex");
                break;

            case TilemapGenerator.MapSelectionMode.FolderRandom:
                Draw("textMapFolder");
                Draw("autoRefreshFolderMaps");
                Draw("folderTextMaps");
                break;

            case TilemapGenerator.MapSelectionMode.Curriculum:
                Header("Curriculum (4-tier difficulty)");
                Draw("easyMapFolder"); Draw("easyMaps");
                Draw("mediumMapFolder"); Draw("mediumMaps");
                Draw("hardMapFolder"); Draw("hardMaps");
                Draw("defaultDifficulty");
                Draw("logCurriculumChoice");
                Draw("fixedSpawnMinReachableRatio");
                Draw("logFixedSpawnValidation");
                break;
        }
    }

    private void DrawButtons(TilemapGenerator gen)
    {
        bool canGenerate = gen.tilemap != null;
        GUI.enabled = canGenerate;
        if (GUILayout.Button("Generate Room"))
        {
            Undo.IncrementCurrentGroup();
            foreach (UnityEngine.Object obj in targets)
            {
                if (obj is not TilemapGenerator g || g.tilemap == null) continue;
                Undo.RegisterFullObjectHierarchyUndo(g.tilemap.gameObject, "Generate Room");
                g.GenerateRoom();
                EditorUtility.SetDirty(g.tilemap);
                EditorUtility.SetDirty(g);
            }
        }
        GUI.enabled = true;

        if (gen.generationMode == TilemapGenerator.MapGenerationMode.TextAsset)
        {
            if (GUILayout.Button("Refresh Text Maps From Folder"))
            {
                foreach (UnityEngine.Object obj in targets)
                {
                    if (obj is not TilemapGenerator g) continue;
                    g.RefreshTextMapsFromFolder();
                    EditorUtility.SetDirty(g);
                }
            }

            if (GUILayout.Button("Edit Map Data In Folder"))
            {
                foreach (UnityEngine.Object obj in targets)
                {
                    if (obj is not TilemapGenerator g) continue;
                    g.EditMapDataInFolder();
                    EditorUtility.SetDirty(g);
                }
            }
        }
    }

    private void Draw(string propName)
    {
        SerializedProperty prop = serializedObject.FindProperty(propName);
        if (prop != null)
            EditorGUILayout.PropertyField(prop, true);
    }

    private static void Header(string label)
    {
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
    }
}
