using UnityEngine;
using UnityEngine.Tilemaps;

public class RoomGenerator : MonoBehaviour
{
    [Header("Tilemap References")]
    public Tilemap tilemap;
    public TileBase floorTile;
    public TileBase wallTile;   // Dùng RuleTile để tường tự nối liền nhau đẹp hơn
    public TileBase obstacleTile; // Ví dụ: Hộp gỗ, cột đá

    [Header("Room Settings")]
    public int width = 10;
    public int height = 8;
    public Vector2Int startPosition = new Vector2Int(0, 0);

    // Enum để chọn kiểu phòng muốn test
    public enum RoomStyle { EmptyBox, Pillars, RandomNoise }
    public RoomStyle currentStyle = RoomStyle.EmptyBox;

    // void Start()
    // {
    //     GenerateRoom();
    // }

    // Hàm gọi khi bạn thay đổi thông số trong Inspector (để test nhanh)
    [ContextMenu("Generate Room")] 
    public void GenerateRoom()
    {
        tilemap.ClearAllTiles(); // Xóa cũ để vẽ mới
        
        // 1. Khởi tạo mảng dữ liệu (0: Sàn, 1: Tường, 2: Vật cản)
        int[,] roomGrid = new int[width, height];

        // 2. Xử lý logic điền dữ liệu vào mảng
        switch (currentStyle)
        {
            case RoomStyle.EmptyBox:
                FillEmptyRoom(roomGrid);
                break;
            case RoomStyle.Pillars:
                FillPillarsRoom(roomGrid);
                break;
            case RoomStyle.RandomNoise:
                FillRandomRoom(roomGrid);
                break;
        }

        // 3. Từ mảng dữ liệu -> Vẽ lên Tilemap
        RenderToTilemap(roomGrid);
    }

    // --- CÁC THUẬT TOÁN ĐIỀN DỮ LIỆU (SPACE GENERATION LOGIC) ---

    // Kiểu 1: Phòng hình chữ nhật cơ bản
    void FillEmptyRoom(int[,] grid)
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                {
                    grid[x, y] = 1; // Tường bao quanh
                }
                else
                {
                    grid[x, y] = 0; // Sàn bên trong
                }
            }
        }
    }

    // Kiểu 2: Phòng có các cột trụ xen kẽ (Pattern Based)
    // Đây là ví dụ đơn giản của Shape Grammar: Áp dụng quy tắc lặp lại
    void FillPillarsRoom(int[,] grid)
    {
        FillEmptyRoom(grid); // Tạo khung trước

        for (int x = 2; x < width - 2; x++)
        {
            for (int y = 2; y < height - 2; y++)
            {
                // Quy tắc: Cứ cách 2 ô lại đặt 1 cột
                if (x % 2 == 0 && y % 2 == 0)
                {
                    grid[x, y] = 2; // Vật cản (Cột)
                }
            }
        }
    }

    // Kiểu 3: Phòng có chướng ngại vật ngẫu nhiên (Noise Based)
    void FillRandomRoom(int[,] grid)
    {
        FillEmptyRoom(grid); // Tạo khung trước

        for (int x = 1; x < width - 1; x++)
        {
            for (int y = 1; y < height - 1; y++)
            {
                // Tỉ lệ 10% xuất hiện vật cản ngẫu nhiên
                if (Random.value < 0.1f) 
                {
                    grid[x, y] = 2;
                }
            }
        }
    }

    // --- PHẦN HIỂN THỊ (RENDERER) ---
    void RenderToTilemap(int[,] grid)
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                // Tính tọa độ thực tế trên bản đồ game
                Vector3Int pos = new Vector3Int(startPosition.x + x, startPosition.y + y, 0);
                
                int tileType = grid[x, y];

                if (tileType == 1) // Tường
                {
                    tilemap.SetTile(pos, wallTile);
                }
                else if (tileType == 2) // Vật cản
                {
                    // Vật cản thường nằm trên sàn, nên vẽ sàn trước (nếu game 2D topdown cần layer)
                    tilemap.SetTile(pos, obstacleTile); 
                }
                else // Sàn (0)
                {
                    tilemap.SetTile(pos, floorTile);
                }
            }
        }
    }
}