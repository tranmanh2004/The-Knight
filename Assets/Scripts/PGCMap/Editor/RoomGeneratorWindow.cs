using UnityEditor;
using UnityEngine;

public class RoomGeneratorWindow : EditorWindow
{
    private RoomGenerator _generator;

    [MenuItem("Tools/PGC/Room Generator")] // Adds entry in the Unity menu bar
    public static void ShowWindow()
    {
        RoomGeneratorWindow window = GetWindow<RoomGeneratorWindow>(false, "Room Generator", true);
        window.minSize = new Vector2(300f, 120f);
    }

    private void OnEnable()
    {
        Selection.selectionChanged += HandleSelectionChanged;
        if (_generator == null)
        {
            _generator = FindObjectOfType<RoomGenerator>();
        }
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= HandleSelectionChanged;
    }

    private void HandleSelectionChanged()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            return;
        }

        RoomGenerator selectedGenerator = selected.GetComponent<RoomGenerator>();
        if (selectedGenerator != null)
        {
            _generator = selectedGenerator;
            Repaint();
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Room Generator Controls", EditorStyles.boldLabel);
        EditorGUILayout.Space(5f);

        EditorGUI.BeginChangeCheck();
        _generator = (RoomGenerator)EditorGUILayout.ObjectField("Target", _generator, typeof(RoomGenerator), true);
        if (EditorGUI.EndChangeCheck())
        {
            Repaint();
        }

        bool canGenerate = _generator != null && _generator.tilemap != null;

        EditorGUI.BeginDisabledGroup(!canGenerate);
        if (GUILayout.Button("Generate Room", GUILayout.Height(35f)))
        {
            GenerateFromEditor(_generator);
        }
        EditorGUI.EndDisabledGroup();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Use Selected", GUILayout.Height(22f)))
            {
                GameObject selected = Selection.activeGameObject;
                if (selected != null)
                {
                    RoomGenerator selectedGenerator = selected.GetComponent<RoomGenerator>();
                    if (selectedGenerator != null)
                    {
                        _generator = selectedGenerator;
                    }
                }
            }

            if (GUILayout.Button("Ping", GUILayout.Height(22f)) && _generator != null)
            {
                EditorGUIUtility.PingObject(_generator);
            }
        }

        if (_generator == null)
        {
            EditorGUILayout.HelpBox("Assign hoặc chọn 1 GameObject chứa RoomGenerator để dùng nút Generate.", MessageType.Info);
        }
        else if (_generator.tilemap == null)
        {
            EditorGUILayout.HelpBox("RoomGenerator đang thiếu tham chiếu Tilemap. Hãy gán trước khi generate.", MessageType.Warning);
        }
    }

    private static void GenerateFromEditor(RoomGenerator generator)
    {
        if (generator == null)
        {
            return;
        }

        Undo.IncrementCurrentGroup();

        if (generator.tilemap != null)
        {
            Undo.RegisterFullObjectHierarchyUndo(generator.tilemap.gameObject, "Generate Room");
        }

        generator.GenerateRoom();

        if (generator.tilemap != null)
        {
            EditorUtility.SetDirty(generator.tilemap);
        }
        EditorUtility.SetDirty(generator);
    }
}
