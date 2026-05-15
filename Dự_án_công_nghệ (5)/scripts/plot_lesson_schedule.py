import matplotlib.pyplot as plt
import matplotlib.ticker as ticker
import numpy as np

plt.rcParams.update({
    'font.family': 'DejaVu Sans',
    'font.size': 11,
    'axes.linewidth': 1.1,
    'axes.spines.top': False,
    'axes.spines.right': False,
    'xtick.direction': 'out',
    'ytick.direction': 'out',
    'xtick.major.size': 4,
    'ytick.major.size': 4,
})

fig, ax = plt.subplots(figsize=(8, 4.0))

# --- Data ---
# run46: Easy(0) -> Medium(1) at ~70k -> Hard(2) at ~140k -> stays at 2
s46 = [0, 0.070, 0.070, 0.140, 0.140, 3.0]
l46 = [0,   0,     1,     1,     2,    2]

# run50: same lesson structure as run46 (also 3 tiers)
s50 = [0, 0.060, 0.060, 0.120, 0.120, 3.0]
l50 = [0,   0,     1,     1,     2,    2]

# run54: 4-tier — EasyF(0)->MediumF(1)->HardF(2)->HardR(3)
s54 = [0, 0.060, 0.060, 0.130, 0.130, 0.280, 0.280, 3.0]
l54 = [0,   0,     1,     1,     2,     2,      3,    3]

C1 = '#2563EB'   # blue  — Curriculum
C2 = '#16A34A'   # green — Curriculum + ICM
C3 = '#0891B2'   # teal  — 4-tier spawn

ax.step(s46, l46, where='post', color=C1, lw=2.2, label='Curriculum (run46)')
ax.step(s50, l50, where='post', color=C2, lw=2.2, label='Curriculum + ICM (run50)', linestyle='--')
ax.step(s54, l54, where='post', color=C3, lw=2.2, label='4-tier spawn (run54)')

# Shade transition zones
for x in [0.070, 0.140, 0.060, 0.120, 0.060, 0.130, 0.280]:
    ax.axvline(x, color='gray', lw=0.5, linestyle=':', alpha=0.4)

# Y-axis labels
ax.set_yticks([0, 1, 2, 3])
ax.set_yticklabels(['0  Easy', '1  Medium', '2  Hard F', '3  Hard R'], fontsize=10.5)
ax.set_ylim(-0.3, 3.5)

# X-axis
ax.set_xlim(0, 3.0)
ax.xaxis.set_major_formatter(ticker.FuncFormatter(lambda x, _: f'{x:.1f}M'))
ax.set_xlabel('Số bước huấn luyện', fontsize=12, labelpad=6)
ax.set_ylabel('Lesson', fontsize=12, labelpad=8)

ax.grid(True, axis='y', linestyle='--', alpha=0.25, color='gray')

leg = ax.legend(loc='lower right', framealpha=0.92, edgecolor='#ccc',
                fontsize=10.5, handlelength=2.2)

plt.tight_layout(pad=1.2)

out_png = r"c:\Riot Games\The-Knight\Dự_án_công_nghệ (5)\figures\training\curriculum_lesson_schedule.png"
out_pdf = r"c:\Riot Games\The-Knight\Dự_án_công_nghệ (5)\figures\training\curriculum_lesson_schedule.pdf"

plt.savefig(out_png, dpi=250, bbox_inches='tight')
plt.savefig(out_pdf, bbox_inches='tight')
print(f"Saved: {out_png}")
print(f"Saved: {out_pdf}")
