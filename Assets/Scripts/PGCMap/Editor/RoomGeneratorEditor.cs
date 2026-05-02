using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TilemapGenerator))]
[CanEditMultipleObjects]
public class TilemapGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        bool hasValidTarget = false;
        foreach (UnityEngine.Object obj in targets)
        {
            if (obj is TilemapGenerator rg && rg.tilemap != null)
            {
                hasValidTarget = true;
                break;
            }
        }

        GUI.enabled = hasValidTarget;
        if (GUILayout.Button("Generate Room"))
        {
            Undo.IncrementCurrentGroup();
            foreach (UnityEngine.Object obj in targets)
            {
                if (obj is not TilemapGenerator generator || generator.tilemap == null)
                {
                    continue;
                }

                Undo.RegisterFullObjectHierarchyUndo(generator.tilemap.gameObject, "Generate Room");
                generator.GenerateRoom();
                EditorUtility.SetDirty(generator.tilemap);
                EditorUtility.SetDirty(generator);
            }
        }
        GUI.enabled = true;

        if (GUILayout.Button("Refresh Text Maps From Folder"))
        {
            foreach (UnityEngine.Object obj in targets)
            {
                if (obj is not TilemapGenerator generator)
                {
                    continue;
                }

                generator.RefreshTextMapsFromFolder();
                EditorUtility.SetDirty(generator);
            }
        }

        if (GUILayout.Button("Edit Map Data In Folder"))
        {
            foreach (UnityEngine.Object obj in targets)
            {
                if (obj is not TilemapGenerator generator)
                {
                    continue;
                }

                generator.EditMapDataInFolder();
                EditorUtility.SetDirty(generator);
            }
        }
    }
}
