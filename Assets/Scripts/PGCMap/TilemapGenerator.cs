using UnityEngine;
using UnityEngine.Tilemaps;

public class RoomGenerator : MonoBehaviour
{
    [Header("Tilemap References")]
    public Tilemap tilemap;
    public TileBase floorTile;
    public TileBase wallTile;

    [Header("Map Placement")]
    public Vector2Int startPosition = new Vector2Int(0, 0);

    [Header("Text Map Settings")]
    public TextAsset roomLayoutText;

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
                tilemap.SetTile(pos, grid[x, y] == 1 ? wallTile : floorTile);
            }
        }
    }

    private int[,] BuildGridFromText()
    {
        if (roomLayoutText == null)
        {
            return null;
        }

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
            }
        }

        return grid;
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