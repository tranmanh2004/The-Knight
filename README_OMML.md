# Hướng dẫn áp dụng OMML cho công thức trong slide

## Bối cảnh

File `Slides_KhoaLuanTotNghiep_TranTheManh.pptx` hiện đang dùng **text Unicode** cho các công thức (Cambria italic). Cách này render được nhưng không phải equation thật của PowerPoint.

Script `apply_omml.py` sẽ chuyển toàn bộ công thức sang **OMML (Office Math Markup Language)** – định dạng equation chuẩn của Word/PowerPoint, render đẹp hơn và edit được bằng Equation Editor.

## Cài đặt 1 lần

### 1. Cài Python (nếu chưa có)
- Tải từ https://www.python.org/downloads/ — phiên bản 3.8+
- Khi cài, **tick "Add Python to PATH"**
- Kiểm tra: mở PowerShell, gõ `python --version`

### 2. Cài Pandoc
**Cách dễ nhất (PowerShell với admin):**
```powershell
winget install --id JohnMacFarlane.Pandoc
```

**Hoặc tải installer trực tiếp:**
- https://pandoc.org/installing.html
- Tải file `.msi` cho Windows, double-click cài

**Kiểm tra:**
```powershell
pandoc --version
```
Phải in ra `pandoc 3.x.x` hoặc tương tự.

## Chạy script

1. Mở PowerShell, `cd` vào thư mục chứa file:
   ```powershell
   cd "D:\Project\The Knight"
   ```

2. Chạy:
   ```powershell
   python apply_omml.py
   ```

3. Output sẽ là file mới `Slides_KhoaLuanTotNghiep_TranTheManh_with_OMML.pptx` (file gốc giữ nguyên).

4. Mở file mới bằng PowerPoint → các công thức sẽ render bằng equation editor (Cambria Math), nhìn vào sẽ thấy: click vào công thức → tab "Equation" hiện ra.

## Các công thức được chuyển

| Slide | Công thức | LaTeX tương đương |
|---|---|---|
| 10 (RL & PPO) | J(πθ) = 𝔼…[Σγᵗrₜ] | `J(\pi_\theta) = \mathbb{E}_{\tau\sim\pi_\theta}[\sum_t \gamma^t r_t]` |
| 10 (RL & PPO) | L_CLIP = 𝔼[min(...)] | PPO clip objective |
| 10 (GAE) | δₜ = rₜ + γV(sₜ₊₁) – V(sₜ);  Âₜ^GAE = Σ(γλ)ˡδₜ₊ₗ | TD error + GAE |
| 11 (ICM) | r_ICM = ½‖φ̂(sₜ₊₁) – φ(sₜ₊₁)‖² | ICM intrinsic reward |
| 11 (RND) | r_RND = ‖f̂_θ(sₜ) – f(sₜ)‖² | RND intrinsic reward |
| 12 (CL) | Ḡₜ = (1-α)Ḡₜ₋₁ + αGₜ;  Ḡₜ ≥ θₗ ⇒ tăng difficulty | EMA + điều kiện chuyển mức |
| 13 (PCGNN) | t_{i,j} = argmax fθ(C(i,j), z) | Tile decision function |
| 17 (Action) | Hₘₐₓ = ln(3)+ln(3)+ln(3)+ln(9) ≈ 5.49 nat | Entropy cực đại lý thuyết |
| 19 (PCGNN) | difficulty_score = 0.90×wall_ratio + ... | Công thức điểm độ khó |

## Khắc phục sự cố

**Lỗi `pandoc: command not found`**
→ Pandoc chưa nằm trong PATH. Restart PowerShell sau khi cài xong, hoặc tự thêm `C:\Program Files\Pandoc` vào PATH.

**Công thức không render đúng khi mở file**
→ Kiểm tra PowerPoint của bạn ít nhất là 2010+. Office 365 / 2019 / 2021 đều OK.

**Script chạy xong nhưng không có công thức nào được thay**
→ Có thể file `.pptx` đã bị edit thủ công và text formula khác mã hóa. Mở `apply_omml.py`, chỉnh lại các string trong `FORMULAS` cho khớp.

## Edit công thức sau khi áp dụng

Sau khi mở file `_with_OMML.pptx` trong PowerPoint:
- Click vào công thức → tab "Equation" hiện ra trên ribbon
- Có thể edit bằng symbol palette hoặc gõ trực tiếp dạng LaTeX (PowerPoint 365 hỗ trợ)
