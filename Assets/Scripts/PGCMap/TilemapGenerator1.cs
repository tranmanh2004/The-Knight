using UnityEngine;
using UnityEngine.Tilemaps;
using MoreMountains.TopDownEngine;
using System.Collections.Generic;

public class TilemapGenerator1 : MonoBehaviour
{
    [Header("Tilemap")]
    public Tilemap tilemap;
    public TileBase floorTile;
    public TileBase wallTile;

    [Header("Text Map")]
    public TextAsset mapText;
    public Vector2Int mapOffset = Vector2Int.zero;

    [Header("Player Spawn")]
    public bool spawnPlayerFromText = true;
    public Character targetPlayer;
    public Character.FacingDirections playerFacingDirection = Character.FacingDirections.East;

    [Header("Enemy Spawn")]
    public bool spawnEnemiesFromText = true;
    public Character enemyPrefab;
    public Transform enemyContainer;
    public Character.FacingDirections enemyFacingDirection = Character.FacingDirections.West;

    private bool _hasPlayerSpawn;
    private Vector3Int _playerSpawnCell;
    private readonly List<Vector3Int> _enemySpawnCells = new List<Vector3Int>();
    private readonly List<Character> _spawnedEnemies = new List<Character>();

    void Start()
    {
        GenerateTilemap();
    }

    [ContextMenu("Generate Tilemap")]
    public void GenerateTilemap()
    {
        if (tilemap == null || floorTile == null || wallTile == null)
        {
            Debug.LogWarning("TilemapGenerator1 thiếu tham chiếu Tilemap/FloorTile/WallTile.", this);
            return;
        }

        int[,] grid = BuildGridFromText();
        if (grid == null)
        {
            Debug.LogWarning("Không đọc được map text.", this);
            return;
        }

        tilemap.ClearAllTiles();
        Render(grid);
        SpawnPlayerIfNeeded();
        SpawnEnemiesIfNeeded();
    }

    private int[,] BuildGridFromText()
    {
        if (mapText == null)
        {
            return null;
        }

        _hasPlayerSpawn = false;
        _enemySpawnCells.Clear();

        string text = mapText.text.Replace("\r\n", "\n").TrimEnd('\n');
        string[] lines = text.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return null;
        }

        int height = lines.Length;
        int width = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length > width)
            {
                width = lines[i].Length;
            }
        }

        int[,] grid = new int[width, height];

        for (int row = 0; row < height; row++)
        {
            string line = lines[row];
            for (int col = 0; col < width; col++)
            {
                char c = col < line.Length ? line[col] : '.';
                int x = col;
                int y = height - 1 - row;
                grid[x, y] = CharToTileType(c);

                if (c == 'P')
                {
                    _hasPlayerSpawn = true;
                    _playerSpawnCell = new Vector3Int(mapOffset.x + x, mapOffset.y + y, 0);
                }

                if (c == 'E')
                {
                    _enemySpawnCells.Add(new Vector3Int(mapOffset.x + x, mapOffset.y + y, 0));
                }
            }
        }

        return grid;
    }

    private void Render(int[,] grid)
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector3Int cell = new Vector3Int(mapOffset.x + x, mapOffset.y + y, 0);
                tilemap.SetTile(cell, grid[x, y] == 1 ? wallTile : floorTile);
            }
        }
    }

    private int CharToTileType(char c)
    {
        return c == '#' ? 1 : 0;
    }

    private void SpawnEnemiesIfNeeded()
    {
        if (!spawnEnemiesFromText)
        {
            return;
        }

        ClearGeneratedEnemies();

        if (enemyPrefab == null)
        {
            if (_enemySpawnCells.Count > 0)
            {
                Debug.LogWarning("Có ký tự E trong map nhưng chưa gán enemyPrefab.", this);
            }
            return;
        }

        for (int i = 0; i < _enemySpawnCells.Count; i++)
        {
            Vector3 worldSpawnPosition = tilemap.GetCellCenterWorld(_enemySpawnCells[i]);
            Character enemy = Instantiate(enemyPrefab, worldSpawnPosition, Quaternion.identity, enemyContainer);
            enemy.RespawnAt(worldSpawnPosition, enemyFacingDirection);
            _spawnedEnemies.Add(enemy);
        }
    }

    private void ClearGeneratedEnemies()
    {
        for (int i = _spawnedEnemies.Count - 1; i >= 0; i--)
        {
            if (_spawnedEnemies[i] == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(_spawnedEnemies[i].gameObject);
            }
            else
            {
                DestroyImmediate(_spawnedEnemies[i].gameObject);
            }
        }

        _spawnedEnemies.Clear();
    }

    private void SpawnPlayerIfNeeded()
    {
        if (!spawnPlayerFromText || !_hasPlayerSpawn)
        {
            return;
        }

        Character player = targetPlayer != null ? targetPlayer : FindPlayerCharacter();
        if (player == null)
        {
            Debug.LogWarning("Không tìm thấy player để spawn theo ký tự P.", this);
            return;
        }

        Vector3 worldSpawnPosition = tilemap.GetCellCenterWorld(_playerSpawnCell);
        player.RespawnAt(worldSpawnPosition, playerFacingDirection);
    }

    private Character FindPlayerCharacter()
    {
        GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
        if (taggedPlayer != null)
        {
            Character taggedCharacter = taggedPlayer.GetComponent<Character>();
            if (taggedCharacter != null)
            {
                return taggedCharacter;
            }
        }

        Character[] allCharacters = FindObjectsByType<Character>(FindObjectsSortMode.None);
        for (int i = 0; i < allCharacters.Length; i++)
        {
            if (allCharacters[i].CharacterType == Character.CharacterTypes.Player)
            {
                return allCharacters[i];
            }
        }

        return null;
    }
}
