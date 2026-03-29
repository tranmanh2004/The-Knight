using UnityEngine;
using UnityEngine.Tilemaps;
using MoreMountains.TopDownEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class RoomGenerator : MonoBehaviour
{
    public enum MapSelectionMode
    {
        SingleTextAsset,
        FolderByIndex,
        FolderRandom
    }

    [Header("Tilemap References")]
    public Tilemap tilemap;
    public Tilemap collisionTilemap;
    public TileBase floorTile;
    public TileBase wallTile;

    [Header("Map Placement")]
    public Vector2Int startPosition = new Vector2Int(0, 0);

    [Header("Text Map Settings")]
    public MapSelectionMode mapSelectionMode = MapSelectionMode.SingleTextAsset;
    public TextAsset roomLayoutText;
    public int selectedFolderMapIndex = 0;
    [SerializeField] private TextAsset[] folderTextMaps;

    #if UNITY_EDITOR
    public DefaultAsset textMapFolder;
    public bool autoRefreshFolderMaps = false;
    #endif

    [Header("Spawn Point Reference")]
    public Transform playerSpawnPointTransform;

    [Header("Enemy Spawn From Text")]
    public bool spawnEnemiesFromText = true;
    public GameObject[] enemyPrefabs;
    public Transform enemyParent;

    private bool _hasPlayerSpawn;
    private Vector3Int _playerSpawnCell;
    private readonly List<Vector3Int> _enemySpawnCells = new List<Vector3Int>();
    private readonly List<GameObject> _spawnedEnemies = new List<GameObject>();

    [ContextMenu("Generate Room")] 
    public void GenerateRoom()
    {
        if (tilemap == null || floorTile == null || wallTile == null)
        {
            Debug.LogWarning("RoomGenerator thiếu tham chiếu Tilemap/FloorTile/WallTile.", this);
            return;
        }

        int[,] roomGrid = BuildGridFromText();
        if (roomGrid == null)
        {
            Debug.LogWarning("Không đọc được dữ liệu map từ file text.", this);
            return;
        }

        tilemap.ClearAllTiles();
        if (collisionTilemap != null)
        {
            collisionTilemap.ClearAllTiles();
        }
        RenderToTilemap(roomGrid);
        UpdateLevelManagerSpawnPointIfNeeded();
        SpawnEnemiesFromTextIfNeeded();
    }

#if UNITY_EDITOR
    [ContextMenu("Refresh Text Maps From Folder")]
    public void RefreshTextMapsFromFolder()
    {
        if (textMapFolder == null)
        {
            folderTextMaps = new TextAsset[0];
            return;
        }

        string folderPath = AssetDatabase.GetAssetPath(textMapFolder);
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            folderTextMaps = new TextAsset[0];
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { folderPath });
        List<TextAsset> found = new List<TextAsset>();

        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!assetPath.EndsWith(".txt", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TextAsset textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (textAsset != null)
            {
                found.Add(textAsset);
            }
        }

        folderTextMaps = found.ToArray();

        if (selectedFolderMapIndex < 0)
        {
            selectedFolderMapIndex = 0;
        }
        if (folderTextMaps.Length > 0 && selectedFolderMapIndex >= folderTextMaps.Length)
        {
            selectedFolderMapIndex = folderTextMaps.Length - 1;
        }
    }

    private void OnValidate()
    {
        if (!autoRefreshFolderMaps)
        {
            return;
        }

        RefreshTextMapsFromFolder();
    }
#endif

    void RenderToTilemap(int[,] grid)
    {
        int mapWidth = grid.GetLength(0);
        int mapHeight = grid.GetLength(1);

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                Vector3Int pos = new Vector3Int(startPosition.x + x, startPosition.y + y, 0);
                tilemap.SetTile(pos, grid[x, y] == 1 ? null : floorTile);
                collisionTilemap.SetTile(pos, grid[x, y] == 1 ? wallTile : null);
            }
        }
    }

    private int[,] BuildGridFromText()
    {
        TextAsset sourceMap = GetSelectedMapTextAsset();
        if (sourceMap == null)
        {
            return null;
        }

        _hasPlayerSpawn = false;
        _enemySpawnCells.Clear();

        string text = sourceMap.text.Replace("\r\n", "\n").TrimEnd('\n');
        string[] lines = text.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return null;
        }

        int parsedHeight = lines.Length;
        int parsedWidth = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length > parsedWidth)
            {
                parsedWidth = lines[i].Length;
            }
        }

        int[,] grid = new int[parsedWidth, parsedHeight];

        for (int row = 0; row < parsedHeight; row++)
        {
            string line = lines[row];
            for (int col = 0; col < parsedWidth; col++)
            {
                char c = col < line.Length ? line[col] : '.';
                int x = col;
                int y = parsedHeight - 1 - row;
                grid[x, y] = CharToTileType(c);

                if (c == 'P')
                {
                    _hasPlayerSpawn = true;
                    _playerSpawnCell = new Vector3Int(startPosition.x + x, startPosition.y + y, 0);
                }
                else if (c == 'E')
                {
                    _enemySpawnCells.Add(new Vector3Int(startPosition.x + x, startPosition.y + y, 0));
                }
            }
        }

        return grid;
    }

    private TextAsset GetSelectedMapTextAsset()
    {
        switch (mapSelectionMode)
        {
            case MapSelectionMode.SingleTextAsset:
                return roomLayoutText;

            case MapSelectionMode.FolderByIndex:
                if (folderTextMaps == null || folderTextMaps.Length == 0)
                {
                    return null;
                }

                int clampedIndex = Mathf.Clamp(selectedFolderMapIndex, 0, folderTextMaps.Length - 1);
                return folderTextMaps[clampedIndex];

            case MapSelectionMode.FolderRandom:
                if (folderTextMaps == null || folderTextMaps.Length == 0)
                {
                    return null;
                }

                return folderTextMaps[Random.Range(0, folderTextMaps.Length)];

            default:
                return roomLayoutText;
        }
    }

    private void UpdateLevelManagerSpawnPointIfNeeded()
    {
        Vector3 worldSpawnPosition = tilemap.GetCellCenterWorld(_playerSpawnCell);
        playerSpawnPointTransform.position = worldSpawnPosition;
    }

    private void SpawnEnemiesFromTextIfNeeded()
    {
        ClearSpawnedEnemies();

        if (!spawnEnemiesFromText)
        {
            return;
        }

        if (_enemySpawnCells.Count == 0)
        {
            return;
        }

        List<GameObject> validPrefabs = new List<GameObject>();
        if (enemyPrefabs != null)
        {
            for (int i = 0; i < enemyPrefabs.Length; i++)
            {
                if (enemyPrefabs[i] != null)
                {
                    validPrefabs.Add(enemyPrefabs[i]);
                }
            }
        }

        if (validPrefabs.Count == 0)
        {
            Debug.LogWarning("Chưa gán enemyPrefabs để spawn quái từ ký tự E.", this);
            return;
        }

        for (int i = 0; i < _enemySpawnCells.Count; i++)
        {
            Vector3 worldPosition = tilemap.GetCellCenterWorld(_enemySpawnCells[i]);
            GameObject selectedPrefab = validPrefabs[Random.Range(0, validPrefabs.Count)];
            GameObject spawned = Instantiate(selectedPrefab, worldPosition, Quaternion.identity, enemyParent);
            _spawnedEnemies.Add(spawned);
        }
    }

    private void ClearSpawnedEnemies()
    {
        if (enemyParent != null)
        {
            for (int i = enemyParent.childCount - 1; i >= 0; i--)
            {
                Transform child = enemyParent.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }

        for (int i = _spawnedEnemies.Count - 1; i >= 0; i--)
        {
            if (_spawnedEnemies[i] == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(_spawnedEnemies[i]);
            }
            else
            {
                DestroyImmediate(_spawnedEnemies[i]);
            }
        }

        _spawnedEnemies.Clear();
    }

    private int CharToTileType(char c)
    {
        switch (c)
        {
            case '#':
                return 1; // Wall
            case '.':
            case 'P':
            case 'E':
            default:
                return 0; // Floor
        }
    }
}