using UnityEngine;
using UnityEngine.Tilemaps;
using MoreMountains.TopDownEngine;
using System.Collections.Generic;
using MoreMountains.Tools;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class TilemapGenerator : MonoBehaviour
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

    [Header("Enemy Object Pool")]
    [Tooltip("Instances pre-created per prefab at startup to avoid Instantiate during training.")]
    public int poolSizePerPrefab = 5;

    // --- Map parsing state ---
    private bool _hasPlayerSpawn;
    private Vector3Int _playerSpawnCell;
    private readonly List<Vector3Int> _enemySpawnCells = new List<Vector3Int>();

    // --- Active enemies for the current episode ---
    private readonly List<GameObject> _spawnedEnemies = new List<GameObject>();

    // --- Pool: prefab.GetInstanceID() → all instances ever created from that prefab ---
    private readonly Dictionary<int, List<GameObject>> _pool = new Dictionary<int, List<GameObject>>();

    // -------------------------------------------------------------------------
    //  Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        WarmUpPool();
    }

    // -------------------------------------------------------------------------
    //  Public API
    // -------------------------------------------------------------------------

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

    /// <summary>
    /// Returns all current enemies to the pool and re-spawns them at the same
    /// positions from the last parsed map.  Use this at episode begin when you
    /// want fresh enemies without changing the map layout.
    /// </summary>
    public void RespawnEnemies()
    {
        SpawnEnemiesFromTextIfNeeded();
    }

    // -------------------------------------------------------------------------
    //  Pool
    // -------------------------------------------------------------------------

    private void WarmUpPool()
    {
        if (enemyPrefabs == null) return;
        foreach (GameObject prefab in enemyPrefabs)
        {
            if (prefab == null) continue;
            int id = prefab.GetInstanceID();
            if (!_pool.ContainsKey(id))
                _pool[id] = new List<GameObject>();

            for (int i = 0; i < poolSizePerPrefab; i++)
            {
                GameObject go = CreatePoolInstance(prefab);
                go.SetActive(false);
                _pool[id].Add(go);
            }
        }
    }

    private GameObject CreatePoolInstance(GameObject prefab)
    {
        Transform parent = enemyParent != null ? enemyParent : transform;
        GameObject go = Instantiate(prefab, Vector3.zero, Quaternion.identity, parent);
        go.tag = "Enemy";
        return go;
    }

    /// <summary>
    /// Returns an idle instance from the pool, growing it if needed.
    /// The instance is moved to <paramref name="position"/> and fully revived.
    /// </summary>
    private GameObject GetFromPool(GameObject prefab, Vector3 position)
    {
        int id = prefab.GetInstanceID();
        if (!_pool.ContainsKey(id))
            _pool[id] = new List<GameObject>();

        List<GameObject> instances = _pool[id];

        // Reuse the first inactive instance
        for (int i = 0; i < instances.Count; i++)
        {
            if (instances[i] != null && !instances[i].activeInHierarchy)
            {
                ActivateInstance(instances[i], position);
                return instances[i];
            }
        }

        // Pool exhausted — grow it dynamically
        GameObject newGo = CreatePoolInstance(prefab);
        instances.Add(newGo);
        ActivateInstance(newGo, position);
        return newGo;
    }

    private void ActivateInstance(GameObject go, Vector3 position)
    {
        go.transform.SetParent(enemyParent != null ? enemyParent : transform);
        go.transform.position = position;
        go.transform.rotation = Quaternion.identity;
        go.SetActive(true);

        // RespawnAt resets ConditionState → Normal, re-enables TopDownController,
        // re-enables colliders, resets velocity, then calls health.Revive() internally.
        // Calling health.Revive() directly skips all of this and leaves the character
        // stuck in Dead ConditionState → unable to move.
        Character character = go.GetComponent<Character>();
        if (character != null)
        {
            character.RespawnAt(position, character.transform.localScale.x > 0
                ? Character.FacingDirections.East
                : Character.FacingDirections.West);
        }
        else
        {
            // Fallback for non-Character enemies
            Health health = go.GetComponent<Health>();
            if (health != null)
                health.Revive();
        }

        // RespawnAt re-enables the brain via OnRevive callback, but ResetBrain()
        // ensures state machine restarts cleanly from the initial state.
        AIBrain brain = go.GetComponent<AIBrain>();
        if (brain != null)
        {
            brain.BrainActive = true;
            brain.enabled = true;
            brain.ResetBrain();
        }

        // If killed mid-attack the weapon state machine stays stuck (WeaponUse,
        // WeaponDelayBeforeUse, etc.) and the enemy can never fire again.
        CharacterHandleWeapon handleWeapon = go.GetComponent<CharacterHandleWeapon>();
        if (handleWeapon != null && handleWeapon.CurrentWeapon != null)
        {
            handleWeapon.CurrentWeapon.WeaponState.ChangeState(Weapon.WeaponStates.WeaponIdle);
        }
    }

    // -------------------------------------------------------------------------
    //  Spawn / clear helpers
    // -------------------------------------------------------------------------

    private void SpawnEnemiesFromTextIfNeeded()
    {
        ReturnActiveEnemiesToPool();

        if (!spawnEnemiesFromText || _enemySpawnCells.Count == 0) return;

        List<GameObject> validPrefabs = new List<GameObject>();
        if (enemyPrefabs != null)
        {
            foreach (GameObject p in enemyPrefabs)
            {
                if (p != null) validPrefabs.Add(p);
            }
        }

        if (validPrefabs.Count == 0)
        {
            Debug.LogWarning("Chưa gán enemyPrefabs để spawn quái từ ký tự E.", this);
            return;
        }

        foreach (Vector3Int cell in _enemySpawnCells)
        {
            Vector3 worldPosition = tilemap.GetCellCenterWorld(cell);
            GameObject prefab = validPrefabs[Random.Range(0, validPrefabs.Count)];

            GameObject spawned;
            if (Application.isPlaying)
            {
                spawned = GetFromPool(prefab, worldPosition);
            }
            else
            {
                // Editor mode: pool is not available, instantiate directly
                Transform parent = enemyParent != null ? enemyParent : transform;
                spawned = Instantiate(prefab, worldPosition, Quaternion.identity, parent);
                spawned.tag = "Enemy";
            }

            _spawnedEnemies.Add(spawned);
        }
    }

    /// <summary>
    /// In play mode: disables all active enemies from the current episode (returns them to pool).
    /// In editor mode: destroys them immediately.
    /// </summary>
    private void ReturnActiveEnemiesToPool()
    {
        if (Application.isPlaying)
        {
            foreach (GameObject go in _spawnedEnemies)
            {
                if (go != null && go.activeInHierarchy)
                {
                    CleanupDetachedUiForEnemy(go.transform);
                    go.SetActive(false);
                }
            }
        }
        else
        {
            // Editor: also sweep the parent for any orphaned instances
            if (enemyParent != null)
            {
                for (int i = enemyParent.childCount - 1; i >= 0; i--)
                {
                    Transform child = enemyParent.GetChild(i);
                    if (child != null) DestroyImmediate(child.gameObject);
                }
            }

            foreach (GameObject go in _spawnedEnemies)
            {
                if (go != null) DestroyImmediate(go);
            }
        }

        _spawnedEnemies.Clear();
    }

    private void CleanupDetachedUiForEnemy(Transform enemyTransform)
    {
        if (enemyTransform == null)
        {
            return;
        }

        MMFollowTarget[] followers = Object.FindObjectsByType<MMFollowTarget>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < followers.Length; i++)
        {
            MMFollowTarget follow = followers[i];
            if (follow == null || follow.Target != enemyTransform)
            {
                continue;
            }

            if (follow.gameObject != null)
            {
                Destroy(follow.gameObject);
            }
        }
    }

    // -------------------------------------------------------------------------
    //  Map building helpers
    // -------------------------------------------------------------------------

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
        if (sourceMap == null) return null;

        _hasPlayerSpawn = false;
        _enemySpawnCells.Clear();

        string text = sourceMap.text.Replace("\r\n", "\n").TrimEnd('\n');
        string[] lines = text.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return null;

        int parsedHeight = lines.Length;
        int parsedWidth = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length > parsedWidth)
                parsedWidth = lines[i].Length;
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
                if (folderTextMaps == null || folderTextMaps.Length == 0) return null;
                return folderTextMaps[Mathf.Clamp(selectedFolderMapIndex, 0, folderTextMaps.Length - 1)];

            case MapSelectionMode.FolderRandom:
                if (folderTextMaps == null || folderTextMaps.Length == 0) return null;
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

    private int CharToTileType(char c)
    {
        switch (c)
        {
            case '#': return 1;   // Wall
            default:  return 0;   // Floor (includes '.', 'P', 'E')
        }
    }

    // -------------------------------------------------------------------------
    //  Editor utilities
    // -------------------------------------------------------------------------

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
            if (!assetPath.EndsWith(".txt", System.StringComparison.OrdinalIgnoreCase)) continue;
            TextAsset textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (textAsset != null) found.Add(textAsset);
        }

        folderTextMaps = found.ToArray();

        if (selectedFolderMapIndex < 0) selectedFolderMapIndex = 0;
        if (folderTextMaps.Length > 0 && selectedFolderMapIndex >= folderTextMaps.Length)
            selectedFolderMapIndex = folderTextMaps.Length - 1;
    }

    private void OnValidate()
    {
        if (autoRefreshFolderMaps)
            RefreshTextMapsFromFolder();
    }
#endif
}
