using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.Serialization;
using MoreMountains.TopDownEngine;
using System.Collections.Generic;
using MoreMountains.Tools;
using Unity.MLAgents;
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
        FolderRandom,
        Curriculum
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

    [Header("Curriculum (4-tier difficulty)")]
    [Tooltip("Maps for difficulty=0 (Easy + fixed spawn). Filled by 'Refresh Curriculum Maps' or manually.")]
    [SerializeField] private TextAsset[] easyMaps;
    [Tooltip("Maps for difficulty=1 (Medium + fixed spawn).")]
    [SerializeField] private TextAsset[] mediumMaps;
    [Tooltip("Maps for difficulty=2 (Hard + fixed spawn) and difficulty=3 (Hard + random spawn).")]
    [SerializeField] private TextAsset[] hardMaps;
    [Tooltip("Fallback difficulty when Academy not running (Editor edit mode). 0=Easy, 1=Medium, 2=Hard-Fixed, 3=Hard-Random.")]
    [Range(0, 3)] public int defaultDifficulty = 3;
    [Tooltip("Log mỗi lần GenerateRoom chọn difficulty + map. Tắt khi train production.")]
    public bool logCurriculumChoice = false;
    [Tooltip("Min ratio of reachable floor cells from chosen fixed spawn (0.5 = 50% of total floor cells).")]
    [Range(0.1f, 1.0f)] public float fixedSpawnMinReachableRatio = 0.5f;
    [Tooltip("Log warning khi map fail validation cho fixed spawn.")]
    public bool logFixedSpawnValidation = true;

    #if UNITY_EDITOR
    public DefaultAsset textMapFolder;
    public bool autoRefreshFolderMaps = false;
    public DefaultAsset easyMapFolder;
    public DefaultAsset mediumMapFolder;
    public DefaultAsset hardMapFolder;
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

    // --- Fixed-spawn caches keyed by map name (deterministic per map) ---
    private readonly Dictionary<string, Vector2Int> _fixedPlayerSpawnCache = new Dictionary<string, Vector2Int>();
    private readonly Dictionary<string, List<Vector2Int>> _fixedEnemySpawnCache = new Dictionary<string, List<Vector2Int>>();
    private string _currentMapKey;

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
        _currentMapKey = sourceMap.name;

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

        bool useFixed = UseFixedSpawn();

        if (useFixed)
        {
            if (!TrySelectFixedPlayerSpawnCell(grid, _currentMapKey))
            {
                if (logFixedSpawnValidation)
                {
                    Debug.LogWarning($"[TilemapGenerator] Fixed spawn validation FAILED for map={_currentMapKey} → fallback to random.", this);
                }
                SelectRandomPlayerSpawnCell(grid);
            }
        }
        else if (randomizePlayerSpawnFromFloor)
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

    // Pick deterministic player spawn: floor cell with MAX BFS reachability among floor cells
    // having at least the required clearance. Falls back to clearance=0 if needed. Cached per map.
    private bool TrySelectFixedPlayerSpawnCell(int[,] grid, string mapKey)
    {
        if (grid == null || string.IsNullOrEmpty(mapKey)) return false;

        if (_fixedPlayerSpawnCache.TryGetValue(mapKey, out Vector2Int cached))
        {
            _playerSpawnCell = new Vector3Int(startPosition.x + cached.x, startPosition.y + cached.y, 0);
            _hasPlayerSpawn = true;
            return true;
        }

        int totalFloor = CountFloorCells(grid);
        if (totalFloor == 0) return false;
        int requiredReachable = Mathf.Max(1, Mathf.CeilToInt(totalFloor * fixedSpawnMinReachableRatio));

        // Try with full clearance first; fallback to clearance=0 if no candidate found.
        Vector2Int? best = FindMaxReachCell(grid, playerSpawnWallClearanceCells, requiredReachable);
        if (!best.HasValue && playerSpawnWallClearanceCells > 0)
        {
            best = FindMaxReachCell(grid, 0, requiredReachable);
        }

        if (!best.HasValue) return false;

        Vector2Int cell = best.Value;
        _fixedPlayerSpawnCache[mapKey] = cell;
        _playerSpawnCell = new Vector3Int(startPosition.x + cell.x, startPosition.y + cell.y, 0);
        _hasPlayerSpawn = true;
        return true;
    }

    // Scan all candidate cells with given clearance, pick the one with MAX reachable count.
    // Returns null if no cell meets requiredReachable, or no cell satisfies clearance at all.
    private Vector2Int? FindMaxReachCell(int[,] grid, int clearance, int requiredReachable)
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);
        Vector2 center = new Vector2((width - 1) * 0.5f, (height - 1) * 0.5f);

        int bestReach = -1;
        Vector2Int bestCell = default;
        float bestCenterDist = float.MaxValue;
        bool foundAny = false;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y] == 1) continue;
                if (!HasFloorClearance(grid, x, y, clearance)) continue;

                int reach = CountReachableFromCell(grid, x, y);
                if (reach < requiredReachable) continue;

                float centerDist = ((new Vector2(x, y)) - center).sqrMagnitude;
                bool better = reach > bestReach
                              || (reach == bestReach && centerDist < bestCenterDist);
                if (better)
                {
                    bestReach = reach;
                    bestCell = new Vector2Int(x, y);
                    bestCenterDist = centerDist;
                    foundAny = true;
                }
            }
        }

        return foundAny ? (Vector2Int?)bestCell : null;
    }

    private int CountFloorCells(int[,] grid)
    {
        int n = 0;
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                if (grid[x, y] != 1) n++;
        return n;
    }

    private int CountReachableFromCell(int[,] grid, int sx, int sy)
    {
        if (!IsFloorCell(grid, sx, sy)) return 0;
        int w = grid.GetLength(0);
        int h = grid.GetLength(1);
        bool[,] vis = new bool[w, h];
        Queue<Vector2Int> q = new Queue<Vector2Int>();
        q.Enqueue(new Vector2Int(sx, sy));
        vis[sx, sy] = true;
        int count = 1;
        while (q.Count > 0)
        {
            Vector2Int c = q.Dequeue();
            int[] dx = { 1, -1, 0, 0 };
            int[] dy = { 0, 0, 1, -1 };
            for (int k = 0; k < 4; k++)
            {
                int nx = c.x + dx[k], ny = c.y + dy[k];
                if (IsFloorCell(grid, nx, ny) && !vis[nx, ny])
                {
                    vis[nx, ny] = true;
                    count++;
                    q.Enqueue(new Vector2Int(nx, ny));
                }
            }
        }
        return count;
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

        bool useFixed = UseFixedSpawn();
        if (useFixed && TrySelectFixedEnemySpawnCells(_lastRoomGrid, _currentMapKey))
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

    // Fixed enemy spawn: spread N enemies across the MIDDLE 50% of the distance distribution
    // (percentiles 25%-75%), deterministic per map+player. Avoids enemies-in-corner problem
    // where farthest spawn made Easy lessons unreasonably hard.
    private bool TrySelectFixedEnemySpawnCells(int[,] grid, string mapKey)
    {
        if (grid == null || string.IsNullOrEmpty(mapKey) || !_hasPlayerSpawn) return false;

        if (_fixedEnemySpawnCache.TryGetValue(mapKey, out List<Vector2Int> cached))
        {
            for (int i = 0; i < cached.Count; i++)
            {
                Vector2Int c = cached[i];
                _enemySpawnCells.Add(new Vector3Int(startPosition.x + c.x, startPosition.y + c.y, 0));
            }
            return _enemySpawnCells.Count > 0;
        }

        int desiredCount = limitRandomEnemySpawnCount ? randomEnemySpawnCount : 9999;
        if (desiredCount <= 0) return false;

        List<KeyValuePair<Vector2Int, int>> ranked = new List<KeyValuePair<Vector2Int, int>>();
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);
        int playerX = _playerSpawnCell.x - startPosition.x;
        int playerY = _playerSpawnCell.y - startPosition.y;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y] == 1) continue;
                if (x == playerX && y == playerY) continue;
                if (!IsReachableFromPlayerSpawn(grid, x, y)) continue;
                int dist = Mathf.Abs(x - playerX) + Mathf.Abs(y - playerY);
                ranked.Add(new KeyValuePair<Vector2Int, int>(new Vector2Int(x, y), dist));
            }
        }
        if (ranked.Count == 0) return false;

        // Sort ASCENDING by distance, deterministic tie-break by x*1000+y
        ranked.Sort((a, b) =>
        {
            int cmp = a.Value.CompareTo(b.Value);
            if (cmp != 0) return cmp;
            int aKey = a.Key.x * 1000 + a.Key.y;
            int bKey = b.Key.x * 1000 + b.Key.y;
            return aKey.CompareTo(bKey);
        });

        int n = ranked.Count;
        int take = Mathf.Min(desiredCount, n);
        List<Vector2Int> chosen = new List<Vector2Int>(take);
        HashSet<int> usedIndices = new HashSet<int>();

        // Pick at percentiles 0.25, 0.50, 0.75 (or evenly spread within [0.25, 0.75] for take>3).
        for (int i = 0; i < take; i++)
        {
            float pct = take == 1 ? 0.5f : 0.25f + 0.5f * i / (take - 1);
            int idx = Mathf.Clamp(Mathf.FloorToInt(pct * n), 0, n - 1);
            int probe = idx;
            while (usedIndices.Contains(probe))
            {
                probe = (probe + 1) % n;
                if (probe == idx) break;
            }
            usedIndices.Add(probe);
            chosen.Add(ranked[probe].Key);
            _enemySpawnCells.Add(new Vector3Int(startPosition.x + ranked[probe].Key.x, startPosition.y + ranked[probe].Key.y, 0));
        }

        _fixedEnemySpawnCache[mapKey] = chosen;
        return _enemySpawnCells.Count > 0;
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

            case MapSelectionMode.Curriculum:
                return PickCurriculumMap();

            default:
                return roomLayoutText;
        }
    }

    private int CurrentDifficulty()
    {
        try
        {
            if (Academy.IsInitialized)
            {
                float v = Academy.Instance.EnvironmentParameters.GetWithDefault("difficulty", (float)defaultDifficulty);
                return Mathf.Clamp(Mathf.RoundToInt(v), 0, 3);
            }
        }
        catch
        {
            // Editor edit-mode: Academy not initialized. Fall through.
        }
        return Mathf.Clamp(defaultDifficulty, 0, 3);
    }

    // 0=Easy+Fixed, 1=Medium+Fixed, 2=Hard+Fixed, 3=Hard+Random.
    // Difficulties 0-2 use fixed (deterministic) spawn; 3 uses random spawn.
    private bool UseFixedSpawn()
    {
        return CurrentDifficulty() <= 2;
    }

    private TextAsset PickCurriculumMap()
    {
        int diff = CurrentDifficulty();
        TextAsset[] pool;
        string label;
        switch (diff)
        {
            case 0: pool = easyMaps;   label = "Easy+Fixed";   break;
            case 1: pool = mediumMaps; label = "Medium+Fixed"; break;
            case 2: pool = hardMaps;   label = "Hard+Fixed";   break;
            default: pool = hardMaps;  label = "Hard+Random";  break;
        }

        if (pool == null || pool.Length == 0)
        {
            Debug.LogWarning(
                $"[TilemapGenerator] Curriculum pool for difficulty={diff} ({label}) is empty. Fallback to roomLayoutText.",
                this
            );
            return roomLayoutText;
        }

        TextAsset chosen = pool[Random.Range(0, pool.Length)];
        if (logCurriculumChoice)
        {
            Debug.Log($"[TilemapGenerator] Curriculum picked difficulty={diff} ({label}) map={chosen.name}", this);
        }
        return chosen;
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
    [ContextMenu("Refresh Curriculum Maps")]
    public void RefreshCurriculumMaps()
    {
        easyMaps = LoadFolderTextAssets(easyMapFolder);
        mediumMaps = LoadFolderTextAssets(mediumMapFolder);
        hardMaps = LoadFolderTextAssets(hardMapFolder);
        Debug.Log(
            $"[TilemapGenerator] Curriculum maps loaded — easy={easyMaps.Length}  medium={mediumMaps.Length}  hard={hardMaps.Length}",
            this
        );
    }

    [ContextMenu("Validate Maps For Fixed Spawn")]
    public void ValidateMapsForFixedSpawn()
    {
        int e = ValidatePool(easyMaps, "Easy");
        int m = ValidatePool(mediumMaps, "Medium");
        int h = ValidatePool(hardMaps, "Hard");
        Debug.Log($"[ValidateFixedSpawn] PASS Easy={e}/{(easyMaps==null?0:easyMaps.Length)}  Medium={m}/{(mediumMaps==null?0:mediumMaps.Length)}  Hard={h}/{(hardMaps==null?0:hardMaps.Length)}", this);
    }

    private int ValidatePool(TextAsset[] pool, string label)
    {
        if (pool == null) return 0;
        int pass = 0;
        for (int i = 0; i < pool.Length; i++)
        {
            TextAsset map = pool[i];
            if (map == null) continue;
            int[,] grid = ParseGridForValidation(map);
            if (grid == null)
            {
                Debug.LogWarning($"[ValidateFixedSpawn] {label}/{map.name}: FAIL (could not parse grid)", this);
                continue;
            }

            int totalFloor = CountFloorCells(grid);
            int required = Mathf.Max(1, Mathf.CeilToInt(totalFloor * fixedSpawnMinReachableRatio));

            Vector2Int? best = FindMaxReachCell(grid, playerSpawnWallClearanceCells, required);
            string clearanceTag = $"clearance={playerSpawnWallClearanceCells}";
            if (!best.HasValue && playerSpawnWallClearanceCells > 0)
            {
                best = FindMaxReachCell(grid, 0, required);
                clearanceTag = "clearance=0 (fallback)";
            }

            if (best.HasValue)
            {
                Vector2Int cell = best.Value;
                int reach = CountReachableFromCell(grid, cell.x, cell.y);
                pass++;
                Debug.Log($"[ValidateFixedSpawn] {label}/{map.name}: PASS  spawn=({cell.x},{cell.y})  reach={reach}/{totalFloor} ({100f*reach/totalFloor:F0}%)  {clearanceTag}", map);
            }
            else
            {
                int absoluteMax = ComputeAbsoluteMaxReach(grid);
                Debug.LogWarning($"[ValidateFixedSpawn] {label}/{map.name}: FAIL  totalFloor={totalFloor}  required≥{required}  bestPossibleReach={absoluteMax} ({100f*absoluteMax/totalFloor:F0}%)", map);
            }
        }
        return pass;
    }

    private int ComputeAbsoluteMaxReach(int[,] grid)
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);
        int max = 0;
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (grid[x, y] != 1)
                {
                    int r = CountReachableFromCell(grid, x, y);
                    if (r > max) max = r;
                }
        return max;
    }

    private int[,] ParseGridForValidation(TextAsset asset)
    {
        if (asset == null) return null;
        string text = asset.text.Replace("\r\n", "\n").TrimEnd('\n');
        string[] lines = text.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return null;
        int parsedHeight = lines.Length;
        int parsedWidth = 0;
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Length > parsedWidth) parsedWidth = lines[i].Length;
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
            }
        }
        return grid;
    }

    private TextAsset[] LoadFolderTextAssets(DefaultAsset folder)
    {
        if (folder == null) return new TextAsset[0];
        string folderPath = AssetDatabase.GetAssetPath(folder);
        if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath)) return new TextAsset[0];
        string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { folderPath });
        List<TextAsset> found = new List<TextAsset>();
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!assetPath.EndsWith(".txt", System.StringComparison.OrdinalIgnoreCase)) continue;
            TextAsset textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (textAsset != null) found.Add(textAsset);
        }
        return found.ToArray();
    }

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
