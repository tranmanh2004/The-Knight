using UnityEngine;
using UnityEngine.Tilemaps;

public class TilemapGenerator1 : MonoBehaviour
{
    public Tilemap tilemap;  // Tham chiếu tới Tilemap của bạn
    public RuleTile[] ruleTiles; // Mảng các RuleTile mà bạn muốn sử dụng
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
                // Chọn tile ngẫu nhiên từ mảng ruleTiles
                RuleTile selectedRuleTile = ruleTiles[Random.Range(0, ruleTiles.Length)];

                // Đặt tile tại vị trí (x, y)
                tilemap.SetTile(new Vector3Int(x, y, 0), selectedRuleTile);
            }
        }
    }
}
