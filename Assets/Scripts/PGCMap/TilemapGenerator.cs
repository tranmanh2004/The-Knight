using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapGenerator : MonoBehaviour
{
    public Tilemap tilemap;  // Tham chiếu tới Tilemap của bạn
    public TileBase groundTile;  // Tile cho nền (ground)
    public RuleTile wallTile;    // Tile cho tường (wall)
    public Vector2Int mapSize = new Vector2Int(20, 20); // Kích thước bản đồ
    public int roomWidth = 8;  // Chiều rộng phòng
    public int roomHeight = 6; // Chiều cao phòng
    public int roomX = 5;     // Vị trí bắt đầu phòng trên trục X
    public int roomY = 5;     // Vị trí bắt đầu phòng trên trục Y

    void Start()
    {
        GenerateTilemap();
    }

    // Hàm vẽ tilemap
    void GenerateTilemap()
    {
        // Vẽ nền (ground) cho toàn bộ Tilemap
        for (int x = 0; x < mapSize.x; x++)
        {
            for (int y = 0; y < mapSize.y; y++)
            {
                tilemap.SetTile(new Vector3Int(x, y, 0), groundTile);  // Gán nền
            }
        }

        // Vẽ tường xung quanh phòng
        for (int x = roomX; x < roomX + roomWidth; x++)
        {
            for (int y = roomY; y < roomY + roomHeight; y++)
            {
                // Vẽ tường xung quanh phòng (phòng được bao quanh bởi tường)
                if (x == roomX || x == roomX + roomWidth - 1 || y == roomY || y == roomY + roomHeight - 1)
                {
                    tilemap.SetTile(new Vector3Int(x, y, 0), wallTile);  // Gán tường
                }
            }
        }
    }
}
