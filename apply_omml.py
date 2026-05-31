"""
apply_omml.py
==============
Hậu xử lý file Slides_KhoaLuanTotNghiep_TranTheManh.pptx:
   thay text công thức bằng OMML thật (Office Math Markup Language) -
   PowerPoint sẽ render bằng equation editor chuẩn, edit được, đẹp hơn nhiều.

Yêu cầu:
    1. Python 3.8+
    2. Pandoc (https://pandoc.org/installing.html)
       - Windows: `winget install --id JohnMacFarlane.Pandoc`  hoặc tải installer
       - Kiểm tra: chạy `pandoc --version` trong PowerShell

Cách dùng:
    Đặt file này CÙNG THƯ MỤC với file pptx (D:\\Project\\The Knight\\), rồi:
        python apply_omml.py

    Script sẽ tạo file mới:
        Slides_KhoaLuanTotNghiep_TranTheManh_with_OMML.pptx
"""

import os
import re
import sys
import shutil
import subprocess
import tempfile
import zipfile
from pathlib import Path

# ----------------------------------------------------------------------
INPUT_PPTX  = "Slides_KhoaLuanTotNghiep_TranTheManh.pptx"
OUTPUT_PPTX = "Slides_KhoaLuanTotNghiep_TranTheManh_with_OMML.pptx"

# ----------------------------------------------------------------------
# Map text trong slide hiện tại  →  LaTeX equation
# ----------------------------------------------------------------------
FORMULAS = [
    # (text trong slide cần thay,  LaTeX)
    (
        "J(πθ) = 𝔼τ∼πθ [ Σₜ γᵗ rₜ ]",
        r"J(\pi_\theta) \;=\; \mathbb{E}_{\tau \sim \pi_\theta} \!\left[ \sum_{t=0}^{T} \gamma^{t}\, r_{t} \right]",
    ),
    (
        "L_CLIP = 𝔼[ min(ρₜÂₜ, clip(ρₜ, 1-ε, 1+ε) Âₜ) ]",
        r"L^{CLIP}(\theta) \;=\; \hat{\mathbb{E}}_{t}\!\left[ \min\!\left( \rho_t(\theta)\hat{A}_t,\; \mathrm{clip}(\rho_t,\, 1-\varepsilon,\, 1+\varepsilon)\,\hat{A}_t \right) \right]",
    ),
    (
        "δₜ = rₜ + γV(sₜ₊₁) – V(sₜ)        Â_t^GAE = Σ(γλ)ˡ δₜ₊ₗ",
        r"\delta_t = r_t + \gamma V(s_{t+1}) - V(s_t) \qquad \hat{A}_t^{GAE} = \sum_{l=0}^{T-t-1}(\gamma\lambda)^{l}\,\delta_{t+l}",
    ),
    (
        "r_ICM = ½ ‖φ̂(sₜ₊₁) – φ(sₜ₊₁)‖²",
        r"r_t^{ICM} \;=\; \tfrac{1}{2}\,\big\| \hat{\phi}(s_{t+1}) - \phi(s_{t+1}) \big\|_{2}^{2}",
    ),
    (
        "r_RND = ‖f̂_θ(sₜ) – f(sₜ)‖²",
        r"r_t^{RND} \;=\; \big\| \hat{f}_\theta(s_t) - f(s_t) \big\|_{2}^{2}",
    ),
    (
        "Ḡₜ = (1-α) Ḡₜ₋₁ + α Gₜ      Khi Ḡₜ ≥ θₗ ⇒ tăng difficulty",
        r"\bar{G}_t = (1-\alpha)\,\bar{G}_{t-1} + \alpha\, G_t \qquad \bar{G}_t \geq \theta_{\ell} \;\Rightarrow\; \text{tăng difficulty}",
    ),
    (
        "t_{i,j} = argmax fθ ( C(i,j), z )",
        r"t_{i,j} \;=\; \arg\max\, f_{\theta}\!\left( C(i,j),\, z \right)",
    ),
    (
        "Hₘₐₓ = ln(3) + ln(3) + ln(3) + ln(9) ≈ 5.49 nat",
        r"H_{\max} \;=\; \ln 3 + \ln 3 + \ln 3 + \ln 9 \;\approx\; 5.49 \text{ nat}",
    ),
    (
        "difficulty_score = 0.90 × wall_ratio + 0.05 × min(path_norm, 1.0) + 0.03 × dead_end_ratio + 0.02 × astar_difficulty",
        r"\text{difficulty\_score} = 0.90 \cdot \text{wall\_ratio} + 0.05 \cdot \min(\text{path\_norm}, 1.0) + 0.03 \cdot \text{dead\_end\_ratio} + 0.02 \cdot \text{astar\_difficulty}",
    ),
]

# ----------------------------------------------------------------------
# Pandoc: LaTeX → docx → trích xuất OMML
# ----------------------------------------------------------------------
def find_pandoc() -> str:
    """Tìm pandoc.exe trong PATH hoặc các vị trí cài chuẩn Windows."""
    # 1. PATH
    found = shutil.which("pandoc")
    if found:
        return found
    # 2. Vị trí cài chuẩn của winget / installer
    candidates = [
        r"C:\Program Files\Pandoc\pandoc.exe",
        r"C:\Program Files (x86)\Pandoc\pandoc.exe",
        os.path.expandvars(r"%LOCALAPPDATA%\Pandoc\pandoc.exe"),
        os.path.expandvars(r"%LOCALAPPDATA%\Microsoft\WinGet\Packages\JohnMacFarlane.Pandoc_Microsoft.Winget.Source_8wekyb3d8bbwe\pandoc.exe"),
    ]
    # 3. Quét toàn bộ thư mục WinGet Packages (version có thể thay đổi)
    winget_root = os.path.expandvars(r"%LOCALAPPDATA%\Microsoft\WinGet\Packages")
    if os.path.isdir(winget_root):
        for sub in os.listdir(winget_root):
            if "Pandoc" in sub:
                # tìm pandoc.exe trong sub-tree
                for root, _, files in os.walk(os.path.join(winget_root, sub)):
                    if "pandoc.exe" in files:
                        candidates.append(os.path.join(root, "pandoc.exe"))
    for c in candidates:
        if c and os.path.isfile(c):
            return c
    return None


_PANDOC_EXE = None
def latex_to_omml(latex: str) -> str:
    """Convert LaTeX → OMML via pandoc."""
    global _PANDOC_EXE
    if _PANDOC_EXE is None:
        _PANDOC_EXE = find_pandoc()
        if not _PANDOC_EXE:
            sys.exit(
                "❌ Không tìm thấy pandoc.exe.\n"
                "   - Cài bằng winget: `winget install --id JohnMacFarlane.Pandoc`\n"
                "   - Hoặc tải installer tại https://pandoc.org/installing.html\n"
                "   - Sau khi cài, ĐÓNG VÀ MỞ LẠI PowerShell, rồi chạy lại."
            )
        print(f"   ↳ Pandoc: {_PANDOC_EXE}")

    with tempfile.TemporaryDirectory() as tmpd:
        md_path  = os.path.join(tmpd, "in.md")
        out_path = os.path.join(tmpd, "out.docx")
        with open(md_path, "w", encoding="utf-8") as f:
            f.write("$$" + latex + "$$\n")
        try:
            subprocess.run(
                [_PANDOC_EXE, "-f", "markdown", "-t", "docx", md_path, "-o", out_path],
                check=True, capture_output=True,
            )
        except subprocess.CalledProcessError as e:
            sys.exit(f"❌ Pandoc fail:\n{e.stderr.decode(errors='ignore')}")

        with zipfile.ZipFile(out_path) as z:
            doc_xml = z.read("word/document.xml").decode("utf-8")

    m = re.search(r"(<m:oMathPara[\s\S]*?</m:oMathPara>)", doc_xml)
    if not m:
        m = re.search(r"(<m:oMath[\s\S]*?</m:oMath>)", doc_xml)
    if not m:
        sys.exit(f"❌ Không trích được OMML từ pandoc cho công thức:\n{latex}")
    return m.group(1)


# ----------------------------------------------------------------------
# Inject OMML vào slide XML
# ----------------------------------------------------------------------
NS_A14 = 'xmlns:a14="http://schemas.microsoft.com/office/drawing/2010/main"'
NS_M   = 'xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math"'

def build_omml_paragraph(omml_xml: str, color_hex: str = None) -> str:
    """Bọc OMML trong <a:p> + <a14:m> để PowerPoint hiểu."""
    color = ""
    if color_hex:
        color = f'<a:defRPr><a:solidFill><a:srgbClr val="{color_hex}"/></a:solidFill></a:defRPr>'

    return (
        f'<a:p><a:pPr algn="ctr">{color}</a:pPr>'
        f'<a14:m {NS_A14}>'
        f'{omml_xml.replace("<m:oMathPara>", f"<m:oMathPara {NS_M}>")}'
        f'</a14:m></a:p>'
    )


def replace_in_slide(xml: str, plain_text: str, omml_paragraph: str) -> str:
    """Tìm <a:p>...<a:t>plain_text</a:t>...</a:p>  →  thay bằng omml_paragraph."""
    # Pattern: tìm <a:p>...</a:p> bao quanh <a:t>plain_text</a:t>
    # (greedy không an toàn, dùng lookahead non-greedy)
    escaped = re.escape(plain_text)
    pattern = re.compile(
        r"<a:p>(?:(?!</a:p>).)*?<a:t>" + escaped + r"</a:t>(?:(?!</a:p>).)*?</a:p>",
        re.DOTALL,
    )
    new_xml, n = pattern.subn(omml_paragraph, xml, count=1)
    return new_xml, n


# ----------------------------------------------------------------------
# Main
# ----------------------------------------------------------------------
def main():
    here = Path(__file__).parent
    src  = here / INPUT_PPTX
    dst  = here / OUTPUT_PPTX

    if not src.exists():
        sys.exit(f"❌ Không thấy file {INPUT_PPTX} ở {here}")

    print(f"📂 Input : {src}")
    print(f"📂 Output: {dst}\n")

    # Pre-compute tất cả OMML từ LaTeX qua pandoc
    print("⚙️  Generating OMML qua pandoc...")
    omml_map = {}
    for txt, latex in FORMULAS:
        print(f"   • {txt[:60]}{'...' if len(txt)>60 else ''}")
        omml_map[txt] = latex_to_omml(latex)
    print()

    # Đọc pptx, sửa các slide XMLs, ghi pptx mới
    shutil.copy(src, dst)
    counts = {txt: 0 for txt, _ in FORMULAS}

    with zipfile.ZipFile(dst, "r") as zin:
        names = zin.namelist()
        data  = {n: zin.read(n) for n in names}

    for name in list(data.keys()):
        if not (name.startswith("ppt/slides/slide") and name.endswith(".xml")):
            continue
        xml = data[name].decode("utf-8")
        changed = False

        for txt, _latex in FORMULAS:
            if txt not in xml:
                continue
            # Lấy color từ run hiện có (heuristic): tìm srgbClr gần text
            color_match = re.search(
                r"<a:srgbClr val=\"([0-9A-Fa-f]{6})\"[^>]*/>\s*</a:solidFill>[\s\S]{0,400}?<a:t>" + re.escape(txt),
                xml,
            )
            color_hex = color_match.group(1) if color_match else None

            para = build_omml_paragraph(omml_map[txt], color_hex)
            xml, n = replace_in_slide(xml, txt, para)
            counts[txt] += n
            if n:
                changed = True

        if changed:
            data[name] = xml.encode("utf-8")
            print(f"✅ Cập nhật: {name}")

    # Cần khai báo a14 namespace ở root presentation hoặc trong slide
    # Pandoc OMML đã chứa elements với prefix m: nhưng namespace declared trên a14:m wrapper
    # Kiểm tra slide có namespace a14 chưa, nếu chưa thì thêm vào <p:sld>
    for name in list(data.keys()):
        if not (name.startswith("ppt/slides/slide") and name.endswith(".xml")):
            continue
        xml = data[name].decode("utf-8")
        if "xmlns:a14=" not in xml and "<a14:m " in xml:
            xml = xml.replace(
                "<p:sld ",
                f'<p:sld {NS_A14} ',
                1,
            )
            data[name] = xml.encode("utf-8")

    # Ghi pptx mới
    with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as zout:
        for name in names:
            zout.writestr(name, data[name])

    print("\n📊 Thống kê thay thế:")
    for txt, n in counts.items():
        status = "✅" if n > 0 else "⚠️ "
        print(f"   {status} {n}× : {txt[:70]}")

    total = sum(counts.values())
    print(f"\n🎯 Tổng cộng {total} công thức đã chuyển sang OMML.")
    print(f"📄 File đã ghi: {dst}")


if __name__ == "__main__":
    main()
