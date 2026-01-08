using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapGenerator : MonoBehaviour
{
    public Tilemap tilemap;  // Tham chiếu tới Tilemap của bạn
    public TileBase[] tiles; // Mảng các Tile mà bạn muốn sử dụng (ví dụ: tường, sàn, v.v.)
    public Vector2Int mapSize = new Vector2Int(10, 10); // Kích thước phòng hoặc bản đồ

    void Start()
    {
        GenerateTilemap();
    }

    // Hàm vẽ tilemap
    void GenerateTilemap()
    {
        for (int x = 0; x < mapSize.x; x++)
        {
            for (int y = 0; y < mapSize.y; y++)
            {
                // Chọn tile ngẫu nhiên từ mảng tiles
                TileBase selectedTile = tiles[Random.Range(0, tiles.Length)];

                // Đặt tile tại vị trí (x, y)
                tilemap.SetTile(new Vector3Int(x, y, 0), selectedTile);
            }
        }
    }
}