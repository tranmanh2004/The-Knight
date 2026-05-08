using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.Serialization;
using MoreMountains.TopDownEngine;
using System.Collections.Generic;
using MoreMountains.Tools;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
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
    [Tooltip("Ensures the floor tilemap has a trigger collider so TopDownController2D grounded checks can detect floor tiles during training.")]
    public bool ensureFloorGroundCollider = true;

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
    [Tooltip("Nếu bật, ký tự P trong map chỉ là fallback. Player spawn sẽ được chọn ngẫu nhiên từ ô sàn hợp lệ.")]
    public bool randomizePlayerSpawnFromFloor = true;

    [Header("Random Enemy Spawn")]
    public bool spawnEnemiesFromText = true;
    [FormerlySerializedAs("spawnOnlyOneEnemyFromText")]
    [Tooltip("Nếu bật, mỗi map spawn số enemy giới hạn ở ô sàn ngẫu nhiên reachable từ player.")]
    public bool limitRandomEnemySpawnCount = true;
    [Min(1)]
    [Tooltip("Số enemy random spawn khi limitRandomEnemySpawnCount bật.")]
    public int randomEnemySpawnCount = 3;
    [Min(0)]
    [Tooltip("Số ô sàn trống cần có quanh điểm spawn player khi randomizePlayerSpawnFromFloor bật. 0 chỉ yêu cầu tâm player nằm trên ô sàn.")]
    public int playerSpawnWallClearanceCells = 1;
    public GameObject[] enemyPrefabs;
    public Transform enemyParent;

    [Header("Spawn Area Cleanup")]
    [Tooltip("Regenerates the room on each runtime respawn so spawn cleanup does not accumulate across episodes.")]
    public bool regenerateRoomOnRespawn = true;
    [Tooltip("Clears nearby wall tiles around the selected player spawn.")]
    public bool clearPlayerSpawnArea = true;
    [Min(0)]
    [Tooltip("Radius in cells to clear around the player spawn point. 1 means a 3x3 area.")]
    public int spawnAreaClearanceCells = 1;
    [Tooltip("Keeps the outer map border intact while clearing spawn areas.")]
    public bool preserveOuterWallBorder = true;
    public bool logSpawnAreaCleanup = false;

    [Header("Enemy Object Pool")]
    [Tooltip("Instances pre-created per prefab at startup to avoid Instantiate during training.")]
    public int poolSizePerPrefab = 5;
    [Tooltip("Use continuous collision detection on spawned enemy rigidbodies to reduce wall tunneling during training.")]
    public bool enforceContinuousCollisionForEnemies = true;

    // --- Map parsing state ---
    private bool _hasPlayerSpawn;
    private Vector3Int _playerSpawnCell;
    private readonly List<Vector3Int> _enemySpawnCells = new List<Vector3Int>();
    private int[,] _lastRoomGrid;
    private bool[,] _reachableFromPlayerSpawnCache;
    private bool _hasGeneratedRuntimeRoom;

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
        EnsureFloorGroundCollider();
        RenderToTilemap(roomGrid);
        UpdateLevelManagerSpawnPointIfNeeded();
        _hasGeneratedRuntimeRoom = true;
        SpawnEnemiesFromTextIfNeeded();
    }

    /// <summary>
    /// Returns all current enemies to the pool and re-spawns them at the same
    /// positions from the last parsed map.  Use this at episode begin when you
    /// want fresh enemies without changing the map layout.
    /// </summary>
    public void RespawnEnemies()
    {
        if (Application.isPlaying && regenerateRoomOnRespawn)
        {
            GenerateRoom();
            return;
        }

        // During training, the first episode can start before the room has ever been
        // rendered from the selected text map. Generate once so spawn candidates and
        // actual wall/floor tiles stay in sync and enemies never spawn into scene walls.
        if (Application.isPlaying && !_hasGeneratedRuntimeRoom)
        {
            GenerateRoom();
            return;
        }

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
            TopDownController controller = go.GetComponent<TopDownController>();
            if (controller != null)
            {
                controller.Reset();
                controller.SetMovement(Vector2.zero);
            }
        }
        else
        {
            // Fallback for non-Character enemies
            Health health = go.GetComponent<Health>();
            if (health != null)
                health.Revive();
        }
        ResetEnemyPhysics(go);

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

        ForceEnemyCollidersNonTrigger(go);
    }

    private void ResetEnemyPhysics(GameObject enemy)
    {
        if (enemy == null)
        {
            return;
        }

        Rigidbody2D rigidbody2D = enemy.GetComponent<Rigidbody2D>();
        if (rigidbody2D != null)
        {
            rigidbody2D.linearVelocity = Vector2.zero;
            rigidbody2D.angularVelocity = 0f;
            rigidbody2D.gravityScale = 0f;
            if (enforceContinuousCollisionForEnemies)
            {
                rigidbody2D.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            }
        }
    }

    private void ForceEnemyCollidersNonTrigger(GameObject enemy)
    {
        if (enemy == null)
        {
            return;
        }

        Collider2D[] colliders = enemy.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
            {
                colliders[i].isTrigger = false;
            }
        }

    }

    // -------------------------------------------------------------------------
    //  Spawn / clear helpers
    // -------------------------------------------------------------------------

    private void SpawnEnemiesFromTextIfNeeded()
    {
        ReturnActiveEnemiesToPool();

        if (!spawnEnemiesFromText) return;
        EnsureRoomGridForSpawning();

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
            Debug.LogWarning("Chưa gán enemyPrefabs để spawn quái.", this);
            return;
        }

        SelectRandomEnemySpawnCells();
        if (_enemySpawnCells.Count == 0)
        {
            Debug.LogWarning("Không tìm thấy ô sàn hợp lệ để random spawn enemy.", this);
            return;
        }

        int enemyCount = _enemySpawnCells.Count;
        for (int i = 0; i < enemyCount; i++)
        {
            Vector3Int cell = _enemySpawnCells[i];
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

    private void EnsureRoomGridForSpawning()
    {
        if (_lastRoomGrid != null)
        {
            return;
        }

        _lastRoomGrid = BuildGridFromText();
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

    private void EnsureFloorGroundCollider()
    {
        if (!ensureFloorGroundCollider || tilemap == null)
        {
            return;
        }

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer >= 0)
        {
            tilemap.gameObject.layer = groundLayer;
        }

        TilemapCollider2D floorCollider = tilemap.GetComponent<TilemapCollider2D>();
        if (floorCollider == null)
        {
            floorCollider = tilemap.gameObject.AddComponent<TilemapCollider2D>();
        }

        // TopDownController2D uses Physics2D.OverlapPoint to decide if the
        // character is grounded. The floor needs a Collider2D for that test,
        // but it must stay trigger-only so it never blocks top-down movement.
        floorCollider.isTrigger = true;
        floorCollider.enabled = true;
    }

    private int[,] BuildGridFromText()
    {
        TextAsset sourceMap = GetSelectedMapTextAsset();
        if (sourceMap == null) return null;

        _hasPlayerSpawn = false;
        _enemySpawnCells.Clear();
        _reachableFromPlayerSpawnCache = null;

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
            }
        }

        if (randomizePlayerSpawnFromFloor)
        {
            SelectRandomPlayerSpawnCell(grid);
        }

        if (clearPlayerSpawnArea && _hasPlayerSpawn)
        {
            ClearSpawnArea(grid, _playerSpawnCell, spawnAreaClearanceCells, false, "player");
        }

        _lastRoomGrid = grid;
        _reachableFromPlayerSpawnCache = BuildReachableFromPlayerSpawnCache(grid);
        return grid;
    }

    private void SelectRandomPlayerSpawnCell(int[,] grid)
    {
        List<Vector3Int> candidates = new List<Vector3Int>();
        CollectPlayerSpawnCandidates(grid, candidates, playerSpawnWallClearanceCells);

        if (candidates.Count == 0 && playerSpawnWallClearanceCells > 0)
        {
            CollectPlayerSpawnCandidates(grid, candidates, 0);
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning("Không tìm thấy ô sàn hợp lệ để random spawn P.", this);
            return;
        }

        _playerSpawnCell = candidates[Random.Range(0, candidates.Count)];
        _hasPlayerSpawn = true;
    }

    private void CollectPlayerSpawnCandidates(int[,] grid, List<Vector3Int> candidates, int clearance)
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y] == 1)
                {
                    continue;
                }

                if (!HasFloorClearance(grid, x, y, clearance))
                {
                    continue;
                }

                candidates.Add(new Vector3Int(startPosition.x + x, startPosition.y + y, 0));
            }
        }
    }

    private void SelectRandomEnemySpawnCells()
    {
        _enemySpawnCells.Clear();
        if (_lastRoomGrid == null)
        {
            return;
        }

        List<Vector3Int> candidates = new List<Vector3Int>();
        CollectEnemySpawnCandidates(_lastRoomGrid, candidates);
        if (candidates.Count == 0 && _hasPlayerSpawn)
        {
            Debug.LogWarning("Không tìm thấy ô spawn enemy reachable từ player spawn.", this);
        }

        int desiredCount = limitRandomEnemySpawnCount ? randomEnemySpawnCount : candidates.Count;
        desiredCount = Mathf.Min(desiredCount, candidates.Count);
        for (int i = 0; i < desiredCount; i++)
        {
            int selectedIndex = Random.Range(0, candidates.Count);
            _enemySpawnCells.Add(candidates[selectedIndex]);
            candidates.RemoveAt(selectedIndex);
        }

    }

    private int ClearSpawnArea(int[,] grid, Vector3Int centerCell, int clearance, bool updateTilemaps, string label)
    {
        if (grid == null)
        {
            return 0;
        }

        int centerX = centerCell.x - startPosition.x;
        int centerY = centerCell.y - startPosition.y;
        int safeClearance = Mathf.Max(0, clearance);
        int cleared = 0;

        for (int x = centerX - safeClearance; x <= centerX + safeClearance; x++)
        {
            for (int y = centerY - safeClearance; y <= centerY + safeClearance; y++)
            {
                if (!IsInsideGrid(grid, x, y))
                {
                    continue;
                }

                if (preserveOuterWallBorder && IsOuterBorderCell(grid, x, y))
                {
                    continue;
                }

                if (grid[x, y] != 1)
                {
                    continue;
                }

                grid[x, y] = 0;
                cleared++;

                if (updateTilemaps)
                {
                    ApplyGridCellToTilemaps(x, y, grid[x, y]);
                }
            }
        }

        if (logSpawnAreaCleanup && cleared > 0)
        {
            Debug.Log($"[SpawnAreaClear] label={label} center={centerCell} radius={safeClearance} clearedTiles={cleared}", this);
        }

        return cleared;
    }

    private bool IsInsideGrid(int[,] grid, int x, int y)
    {
        return x >= 0
            && y >= 0
            && x < grid.GetLength(0)
            && y < grid.GetLength(1);
    }

    private bool IsOuterBorderCell(int[,] grid, int x, int y)
    {
        return x == 0
            || y == 0
            || x == grid.GetLength(0) - 1
            || y == grid.GetLength(1) - 1;
    }

    private void ApplyGridCellToTilemaps(int gridX, int gridY, int tileType)
    {
        if (tilemap == null)
        {
            return;
        }

        Vector3Int pos = new Vector3Int(startPosition.x + gridX, startPosition.y + gridY, 0);
        tilemap.SetTile(pos, tileType == 1 ? null : floorTile);

        if (collisionTilemap != null)
        {
            collisionTilemap.SetTile(pos, tileType == 1 ? wallTile : null);
        }
    }

    private void CollectEnemySpawnCandidates(int[,] grid, List<Vector3Int> candidates)
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y] == 1)
                {
                    continue;
                }

                Vector3Int cell = new Vector3Int(startPosition.x + x, startPosition.y + y, 0);
                if (_hasPlayerSpawn && cell == _playerSpawnCell)
                {
                    continue;
                }

                if (_hasPlayerSpawn && !IsReachableFromPlayerSpawn(grid, x, y))
                {
                    continue;
                }

                candidates.Add(cell);
            }
        }
    }

    private bool HasFloorClearance(int[,] grid, int centerX, int centerY, int clearance)
    {
        int safeClearance = Mathf.Max(0, clearance);
        for (int x = centerX - safeClearance; x <= centerX + safeClearance; x++)
        {
            for (int y = centerY - safeClearance; y <= centerY + safeClearance; y++)
            {
                if (!IsFloorCell(grid, x, y))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool IsFloorCell(int[,] grid, int x, int y)
    {
        return x >= 0
            && y >= 0
            && x < grid.GetLength(0)
            && y < grid.GetLength(1)
            && grid[x, y] != 1;
    }

    private bool[,] BuildReachableFromPlayerSpawnCache(int[,] grid)
    {
        if (grid == null || !_hasPlayerSpawn)
        {
            return null;
        }

        int width = grid.GetLength(0);
        int height = grid.GetLength(1);
        int startX = _playerSpawnCell.x - startPosition.x;
        int startY = _playerSpawnCell.y - startPosition.y;

        if (!IsFloorCell(grid, startX, startY))
        {
            return null;
        }

        bool[,] reachable = new bool[width, height];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(new Vector2Int(startX, startY));
        reachable[startX, startY] = true;

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            EnqueueReachableCell(grid, reachable, queue, current.x + 1, current.y);
            EnqueueReachableCell(grid, reachable, queue, current.x - 1, current.y);
            EnqueueReachableCell(grid, reachable, queue, current.x, current.y + 1);
            EnqueueReachableCell(grid, reachable, queue, current.x, current.y - 1);
        }

        return reachable;
    }

    private void EnqueueReachableCell(int[,] grid, bool[,] reachable, Queue<Vector2Int> queue, int x, int y)
    {
        if (!IsFloorCell(grid, x, y) || reachable[x, y])
        {
            return;
        }

        reachable[x, y] = true;
        queue.Enqueue(new Vector2Int(x, y));
    }

    private bool IsReachableFromPlayerSpawn(int[,] grid, int x, int y)
    {
        if (_reachableFromPlayerSpawnCache == null)
        {
            _reachableFromPlayerSpawnCache = BuildReachableFromPlayerSpawnCache(grid);
        }

        return _reachableFromPlayerSpawnCache != null
            && x >= 0
            && y >= 0
            && x < _reachableFromPlayerSpawnCache.GetLength(0)
            && y < _reachableFromPlayerSpawnCache.GetLength(1)
            && _reachableFromPlayerSpawnCache[x, y];
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
        if (!_hasPlayerSpawn || playerSpawnPointTransform == null)
        {
            return;
        }

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

    [ContextMenu("Edit Map Data In Folder")]
    public void EditMapDataInFolder()
    {
        if (textMapFolder == null)
        {
            Debug.LogWarning("Chưa gán textMapFolder để edit map data.", this);
            return;
        }

        string folderPath = AssetDatabase.GetAssetPath(textMapFolder);
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
        {
            Debug.LogWarning("textMapFolder không hợp lệ.", this);
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { folderPath });
        int changedCount = 0;

        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!assetPath.EndsWith(".txt", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string fullPath = Path.GetFullPath(assetPath);
            string original = File.ReadAllText(fullPath);
            string edited = EditMapData(original);
            if (edited == original)
            {
                continue;
            }

            File.WriteAllText(fullPath, edited);
            changedCount++;
        }

        AssetDatabase.Refresh();
        RefreshTextMapsFromFolder();
        Debug.Log($"Edited {changedCount} text map data file(s). Removed fixed P/E markers and ensured a wall border.", this);
    }

    private string EditMapData(string text)
    {
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        string[] lines = normalized.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return text;
        }

        int width = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length > width)
            {
                width = lines[i].Length;
            }
        }

        List<char[]> editedRows = new List<char[]>();

        for (int row = 0; row < lines.Length; row++)
        {
            char[] chars = new char[width];
            for (int col = 0; col < width; col++)
            {
                char c = col < lines[row].Length ? lines[row][col] : '.';
                if (c == 'P')
                {
                    c = '.';
                }
                else if (c == 'E')
                {
                    c = '.';
                }
                else if (c != '#')
                {
                    c = '.';
                }

                chars[col] = c;
            }

            editedRows.Add(chars);
        }

        EnsureMapDataBorder(editedRows);

        List<string> editedLines = new List<string>();
        for (int i = 0; i < editedRows.Count; i++)
        {
            editedLines.Add(new string(editedRows[i]));
        }

        return string.Join("\n", editedLines) + "\n";
    }

    private void EnsureMapDataBorder(List<char[]> rows)
    {
        if (rows.Count == 0 || HasMapDataBorder(rows))
        {
            return;
        }

        int innerWidth = rows[0].Length;
        int borderedWidth = innerWidth + 2;
        char[] topBorder = MakeBorderRow(borderedWidth);
        char[] bottomBorder = MakeBorderRow(borderedWidth);

        for (int row = 0; row < rows.Count; row++)
        {
            char[] borderedRow = new char[borderedWidth];
            borderedRow[0] = '#';
            for (int col = 0; col < innerWidth; col++)
            {
                borderedRow[col + 1] = rows[row][col];
            }
            borderedRow[borderedWidth - 1] = '#';
            rows[row] = borderedRow;
        }

        rows.Insert(0, topBorder);
        rows.Add(bottomBorder);
    }

    private bool HasMapDataBorder(List<char[]> rows)
    {
        if (rows.Count < 3 || rows[0].Length < 3)
        {
            return false;
        }

        int width = rows[0].Length;
        for (int row = 0; row < rows.Count; row++)
        {
            if (rows[row].Length != width)
            {
                return false;
            }
        }

        for (int col = 0; col < width; col++)
        {
            if (rows[0][col] != '#' || rows[rows.Count - 1][col] != '#')
            {
                return false;
            }
        }

        for (int row = 1; row < rows.Count - 1; row++)
        {
            if (rows[row][0] != '#' || rows[row][width - 1] != '#')
            {
                return false;
            }
        }

        return true;
    }

    private char[] MakeBorderRow(int width)
    {
        char[] row = new char[width];
        for (int i = 0; i < width; i++)
        {
            row[i] = '#';
        }

        return row;
    }

    private void OnValidate()
    {
        if (autoRefreshFolderMaps)
            RefreshTextMapsFromFolder();
    }
#endif
}
