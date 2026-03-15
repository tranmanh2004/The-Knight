using UnityEngine;
using UnityEngine.Tilemaps;
using MoreMountains.TopDownEngine;
using System.Collections.Generic;

public class RoomGenerator : MonoBehaviour
{
    [Header("Tilemap References")]
    public Tilemap tilemap;
    public Tilemap collisionTilemap;
    public TileBase floorTile;
    public TileBase wallTile;

    [Header("Map Placement")]
    public Vector2Int startPosition = new Vector2Int(0, 0);

    [Header("Text Map Settings")]
    public TextAsset roomLayoutText;

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
        RenderToTilemap(roomGrid);
        UpdateLevelManagerSpawnPointIfNeeded();
        SpawnEnemiesFromTextIfNeeded();
    }

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
        if (roomLayoutText == null)
        {
            return null;
        }

        _hasPlayerSpawn = false;
        _enemySpawnCells.Clear();

        string text = roomLayoutText.text.Replace("\r\n", "\n").TrimEnd('\n');
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

    private void UpdateLevelManagerSpawnPointIfNeeded()
    {
        if (!_hasPlayerSpawn)
        {
            return;
        }

        if (!LevelManager.HasInstance)
        {
            return;
        }

        Vector3 worldSpawnPosition = tilemap.GetCellCenterWorld(_playerSpawnCell);

        Transform targetSpawnTransform = playerSpawnPointTransform;
        if (targetSpawnTransform == null)
        {
            CheckPoint spawnPoint = LevelManager.Instance.InitialSpawnPoint;
            if (spawnPoint != null)
            {
                targetSpawnTransform = spawnPoint.transform;
            }
        }

        if (targetSpawnTransform == null)
        {
            Debug.LogWarning("Chưa gán playerSpawnPointTransform và LevelManager cũng chưa có InitialSpawnPoint.", this);
            return;
        }

        targetSpawnTransform.position = worldSpawnPosition;

        CheckPoint initialSpawnPoint = LevelManager.Instance.InitialSpawnPoint;
        if (initialSpawnPoint != null && LevelManager.Instance.CurrentCheckpoint == null)
        {
            LevelManager.Instance.CurrentCheckpoint = initialSpawnPoint;
        }
    }

    private void SpawnEnemiesFromTextIfNeeded()
    {
        if (!spawnEnemiesFromText)
        {
            return;
        }

        ClearSpawnedEnemies();

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