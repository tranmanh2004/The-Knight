"""
restyle_academic.py
====================
Chuyển file pptx hiện có sang phong cách "Academic Paper":
  - Nền trắng (thay vì navy)
  - Chữ navy đậm (thay vì trắng/vàng)
  - Accent burgundy (thay vì Pac-Man yellow)
  - Font Cambria (serif) cho toàn bộ text - đồng bộ với Cambria Math
    của các công thức OMML.

Cách dùng:
    1. Đặt file này CÙNG THƯ MỤC với file pptx ở D:\\Project\\The Knight\\
    2. Mở PowerShell:
           cd "D:\\Project\\The Knight"
           python restyle_academic.py
    3. Output: Slides_Academic.pptx
       (giữ nguyên file gốc và file _with_OMML.pptx)

Nguồn vào (theo thứ tự ưu tiên):
    1) Slides_KhoaLuanTotNghiep_TranTheManh_with_OMML.pptx   (có equation)
    2) Slides_KhoaLuanTotNghiep_TranTheManh.pptx             (chưa có equation)

Edit dictionaries COLOR_MAP / FONT_MAP ở đầu file để chỉnh lại nếu cần.
"""

import os
import re
import sys
import shutil
import zipfile
from pathlib import Path
from collections import Counter

# ----------------------------------------------------------------------
# Source / destination
# ----------------------------------------------------------------------
HERE = Path(__file__).parent
CANDIDATES_IN = [
    "Slides_KhoaLuanTotNghiep_TranTheManh_with_OMML.pptx",
    "Slides_KhoaLuanTotNghiep_TranTheManh.pptx",
]
OUTPUT_NAME = "Slides_Academic.pptx"

# ----------------------------------------------------------------------
# COLOR MAPPING (hex, không có dấu #)
# ----------------------------------------------------------------------
#  Bên trái = màu trong file hiện tại
#  Bên phải = màu Academic Paper tương ứng
#
#  Bạn có thể tự thêm/sửa cặp ánh xạ ở đây — script sẽ tự in ra
#  danh sách màu thực có trong file để bạn biết cần map gì thêm.
# ----------------------------------------------------------------------
COLOR_MAP = {
    # Dark navy / black backgrounds → trắng tinh
    "0F1A2E": "FFFFFF",
    "0F2A4E": "FFFFFF",
    "1A2E4E": "FAFAF7",
    "1B2D4E": "FAFAF7",
    "000000": "FFFFFF",
    "0B0F1A": "FFFFFF",
    "1C1F2A": "FAFAF7",

    # Pac-Man yellow → burgundy (accent màu chính)
    "FFCC00": "8B1A1A",
    "FFD700": "8B1A1A",
    "FFDD33": "8B1A1A",
    "FFE066": "A33333",
    "FFC107": "8B1A1A",

    # White / off-white text → navy đậm
    "FFFFFF": "1B2D4E",
    "F0F4FF": "1B2D4E",
    "F5F5F5": "1B2D4E",
    "F7F9FC": "1B2D4E",
    "FAFAFA": "1B2D4E",
    "E8ECF5": "2D3E5C",

    # Muted gray-blue (sub-headers, captions) → warm gray
    "8FA3C7": "5F5E5A",
    "9BB0D0": "5F5E5A",
    "B0C4DE": "6F6E68",
    "A0B4D4": "5F5E5A",
    "778899": "5F5E5A",

    # ============= 3-COLOR-ONLY ACADEMIC =============
    # Tất cả accent (mọi màu sắc rực) → BURGUNDY 8B1A1A
    # Text / heading                   → NAVY     1B2D4E
    # Muted captions / dividers        → GRAY     5F5E5A
    # Background                       → WHITE    FFFFFF
    # ==================================================

    # Tailwind palette accents — TẤT CẢ → burgundy
    "EC4899": "8B1A1A",   # pink-500
    "DB2777": "8B1A1A",   # pink-600
    "FFB8DE": "8B1A1A",
    "FF6EC7": "8B1A1A",
    "7C3AED": "8B1A1A",   # violet-600
    "6D28D9": "8B1A1A",   # violet-700
    "8B5CF6": "8B1A1A",   # violet-500
    "FACC15": "8B1A1A",   # yellow-400
    "FBBF24": "8B1A1A",   # yellow-500
    "06B6D4": "8B1A1A",   # cyan-500
    "0891B2": "8B1A1A",   # cyan-600
    "5BCEFA": "8B1A1A",
    "00D9FF": "8B1A1A",
    "5B9BD5": "8B1A1A",   # office light blue
    "4472C4": "8B1A1A",   # office accent blue
    "84CC16": "8B1A1A",   # lime-500
    "65A30D": "8B1A1A",   # lime-600
    "70AD47": "8B1A1A",   # office green
    "22C55E": "8B1A1A",   # green-500
    "9DEC9D": "8B1A1A",
    "7CFC00": "8B1A1A",
    "F97316": "8B1A1A",   # orange-500
    "EA580C": "8B1A1A",   # orange-600
    "ED7D31": "8B1A1A",   # office orange
    "FF7F50": "8B1A1A",

    # Slate muted (caption) → warm gray
    "64748B": "5F5E5A",   # slate-500
    "94A3B8": "6F6E68",   # slate-400
    "475569": "5F5E5A",   # slate-600
    "A5A5A5": "5F5E5A",   # office neutral
    "44546A": "1B2D4E",   # office dark blue → navy

    # Dark olive (Cambria Math glyph artifacts) → navy
    "105000": "1B2D4E",
    "103000": "1B2D4E",
    "102000": "1B2D4E",

    # Lines / dividers → light warm gray
    "2D4263": "C9C2B5",
    "33476C": "E8E5DC",
    "445577": "C9C2B5",
}

# ----------------------------------------------------------------------
# FONT MAPPING
# ----------------------------------------------------------------------
FONT_MAP = {
    "Inter": "Cambria",
    "Calibri": "Cambria",
    "Calibri Light": "Cambria",
    "Segoe UI": "Cambria",
    "Segoe UI Semibold": "Cambria",
    "Arial": "Cambria",
    "Helvetica": "Cambria",
    "Roboto": "Cambria",
    "Open Sans": "Cambria",
    "Lato": "Cambria",
    "Source Sans Pro": "Cambria",
    "+mn-lt": "Cambria",
    "+mj-lt": "Cambria",
    # Giữ nguyên các font math / mono
    # "Cambria Math": "Cambria Math",
    # "Consolas":     "Consolas",
}

# Các thư mục trong .pptx mà script sẽ remap màu / font
TARGET_PREFIXES = (
    "ppt/slides/",
    "ppt/slideLayouts/",
    "ppt/slideMasters/",
    "ppt/theme/",
    "ppt/notesSlides/",
    "ppt/notesMasters/",
)


# ----------------------------------------------------------------------
# Heuristic remap cho màu KHÔNG có trong COLOR_MAP
# ----------------------------------------------------------------------
def luminance(hex_rgb: str) -> float:
    r = int(hex_rgb[0:2], 16) / 255
    g = int(hex_rgb[2:4], 16) / 255
    b = int(hex_rgb[4:6], 16) / 255
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def yellow_ish(hex_rgb: str) -> bool:
    r = int(hex_rgb[0:2], 16)
    g = int(hex_rgb[2:4], 16)
    b = int(hex_rgb[4:6], 16)
    return r > 200 and g > 160 and b < 100


def navy_ish(hex_rgb: str) -> bool:
    """Dark cool tone — typical 'navy / slate-dark' surface."""
    r = int(hex_rgb[0:2], 16)
    g = int(hex_rgb[2:4], 16)
    b = int(hex_rgb[4:6], 16)
    return b >= r and b >= g and luminance(hex_rgb) < 0.30


def is_saturated(hex_rgb: str) -> bool:
    """Có màu (không phải gray/black/white) — đủ để coi là 'accent'."""
    r = int(hex_rgb[0:2], 16)
    g = int(hex_rgb[2:4], 16)
    b = int(hex_rgb[4:6], 16)
    mx, mn = max(r, g, b), min(r, g, b)
    return (mx - mn) > 40  # đủ chênh để thấy là "có màu"


def heuristic_remap(hex_rgb: str) -> str:
    """Trả về màu Academic Paper cho hex chưa có trong COLOR_MAP."""
    hex_rgb = hex_rgb.upper()
    L = luminance(hex_rgb)
    # Vàng → burgundy
    if yellow_ish(hex_rgb):
        return "8B1A1A"
    # Rất tối → trắng (background)
    if L < 0.20:
        return "FFFFFF"
    # Navy/slate tối → trắng (card / surface)
    if navy_ish(hex_rgb):
        return "FFFFFF"
    # Rất sáng → navy đậm (text)
    if L > 0.85:
        return "1B2D4E"
    # Mid-tone CÓ MÀU (accent decorative) → burgundy
    if is_saturated(hex_rgb):
        return "8B1A1A"
    # Mid-tone GRAY → warm gray
    return "5F5E5A"


# ----------------------------------------------------------------------
# Force slide / layout / master background về trắng
# ----------------------------------------------------------------------
WHITE_BG_BLOCK = (
    '<p:bg><p:bgPr>'
    '<a:solidFill><a:srgbClr val="FFFFFF"/></a:solidFill>'
    '<a:effectLst/>'
    '</p:bgPr></p:bg>'
)

def force_white_background(xml: str) -> str:
    """Ép <p:bg> trong slide/layout/master về solid white."""
    # Đã có <p:bg>...</p:bg>: replace
    new_xml, n = re.subn(
        r"<p:bg>[\s\S]*?</p:bg>",
        WHITE_BG_BLOCK,
        xml,
        count=1,
    )
    if n:
        return new_xml
    # Chưa có: chèn ngay sau <p:cSld...>
    new_xml, n = re.subn(
        r"(<p:cSld[^>]*>)",
        r"\1" + WHITE_BG_BLOCK,
        xml,
        count=1,
    )
    return new_xml


# ----------------------------------------------------------------------
# Main rewrite logic
# ----------------------------------------------------------------------
SRGB_RE = re.compile(r'val="([0-9A-Fa-f]{6})"')
TYPEFACE_RE = re.compile(r'typeface="([^"]+)"')


def remap_xml(xml: str, color_stats: Counter, font_stats: Counter) -> str:
    """Áp dụng COLOR_MAP, FONT_MAP và heuristic cho 1 XML string."""

    def color_sub(m):
        old = m.group(1).upper()
        color_stats[old] += 1
        if old in COLOR_MAP:
            return f'val="{COLOR_MAP[old]}"'
        new = heuristic_remap(old)
        if new != old:
            color_stats[f"~{old}->{new}"] += 1
        return f'val="{new}"'

    xml = re.sub(r'val="([0-9A-Fa-f]{6})"', color_sub, xml)

    def font_sub(m):
        old = m.group(1)
        font_stats[old] += 1
        return f'typeface="{FONT_MAP.get(old, old)}"'

    xml = TYPEFACE_RE.sub(font_sub, xml)

    # ----- Decorative fills có alpha (vòng tròn nền v.v.) -----
    # Bất kỳ srgbClr nào có <a:alpha> kèm  → chuyển sang gray nhạt
    # để chỉ còn là texture mờ chứ không "tranh" với nội dung
    xml = re.sub(
        r'<a:srgbClr val="[0-9A-Fa-f]{6}">(\s*<a:alpha val="\d+"\s*/>\s*)</a:srgbClr>',
        r'<a:srgbClr val="C9C2B5">\1</a:srgbClr>',
        xml,
    )
    return xml


def main():
    src = None
    for name in CANDIDATES_IN:
        p = HERE / name
        if p.exists():
            src = p
            break
    if src is None:
        sys.exit(
            "❌ Không tìm thấy file pptx. Đặt 1 trong 2 file sau cùng thư mục:\n"
            + "\n".join("   - " + n for n in CANDIDATES_IN)
        )

    dst = HERE / OUTPUT_NAME
    print(f"📂 Input : {src.name}")
    print(f"📂 Output: {dst.name}\n")

    shutil.copy(src, dst)
    color_stats = Counter()
    font_stats = Counter()

    with zipfile.ZipFile(dst, "r") as zin:
        names = zin.namelist()
        data = {n: zin.read(n) for n in names}

    bg_forced = 0
    for name in list(data.keys()):
        if not name.endswith(".xml"):
            continue
        if not name.startswith(TARGET_PREFIXES):
            continue
        try:
            xml = data[name].decode("utf-8")
        except UnicodeDecodeError:
            continue
        new_xml = remap_xml(xml, color_stats, font_stats)
        # Force white bg cho slides/layouts/masters
        if name.startswith(("ppt/slides/", "ppt/slideLayouts/", "ppt/slideMasters/")):
            forced = force_white_background(new_xml)
            if forced != new_xml:
                bg_forced += 1
                new_xml = forced
        if new_xml != xml:
            data[name] = new_xml.encode("utf-8")
    print(f"🏳️  Ép {bg_forced} slide/layout/master về nền trắng.\n")

    with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as zout:
        for n in names:
            zout.writestr(n, data[n])

    # ------------------------------------------------------------------
    # Báo cáo
    # ------------------------------------------------------------------
    print("🎨 Màu sắc đã thấy trong file (top 20 theo số lần xuất hiện):")
    mapped, heuristic, unchanged = [], [], []
    for color, count in color_stats.most_common():
        if color.startswith("~"):
            heuristic.append((color, count))
            continue
        if color in COLOR_MAP:
            mapped.append((color, count))
        else:
            new = heuristic_remap(color)
            if new == color:
                unchanged.append((color, count))

    print("\n   ✅ Đã map theo COLOR_MAP:")
    for c, n in mapped[:15]:
        print(f"      {c}  →  {COLOR_MAP[c]}   ({n} lần)")

    if heuristic:
        print("\n   🔧 Đã map theo heuristic (sáng→navy / tối→trắng / vàng→burgundy):")
        for c, n in heuristic[:15]:
            print(f"      {c}   ({n} lần)")

    if unchanged:
        print("\n   ⚠️  Không thay đổi (nằm trong khoảng giữa, có thể cần map tay):")
        for c, n in unchanged[:15]:
            print(f"      {c}   ({n} lần) — luminance={luminance(c):.2f}")
        print("      → Thêm cặp vào COLOR_MAP ở đầu file rồi chạy lại nếu muốn đổi.")

    print("\n🔤 Font đã thấy:")
    for f, n in font_stats.most_common():
        new = FONT_MAP.get(f, f)
        mark = "→ " + new if new != f else "(giữ nguyên)"
        print(f"      {f:25}  {mark}   ({n} lần)")

    print(f"\n📄 Đã ghi file: {dst}")
    print("   Mở bằng PowerPoint để kiểm tra. Nếu màu nào lạ, sửa COLOR_MAP và chạy lại.")


if __name__ == "__main__":
    main()
