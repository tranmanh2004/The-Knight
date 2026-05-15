"""
Redraw fig:obs_breakdown from Chap2_Design.tex — horizontal stacked bar
showing 207-dim observation vector breakdown.
Output: figures/training/fig_obs_breakdown.{png,pdf}
"""

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import os

OUT = r"c:\Riot Games\The-Knight\Dự_án_công_nghệ (5)\figures\training"
os.makedirs(OUT, exist_ok=True)

plt.rcParams.update({
    "font.family": "serif",
    "axes.labelsize": 10,
})

groups = [
    ("Người chơi (13)",     13,  "#90CAF9"),  # blue
    ("Toàn cục (4)",         4,  "#FFF59D"),  # yellow
    ("Kẻ địch (54)",        54,  "#EF9A9A"),  # red
    ("Đạn (120)",          120,  "#FFCC80"),  # orange
    ("Tường (16)",          16,  "#A5D6A7"),  # green
]

fig, ax = plt.subplots(figsize=(10, 1.6))

left = 0
for label, width, color in groups:
    ax.barh(0, width, left=left, color=color, edgecolor="black", linewidth=0.6, height=0.6)
    # Label inside the bar if wide enough, else above
    if width >= 30:
        ax.text(left + width / 2, 0, label, ha="center", va="center",
                fontsize=9, fontweight="bold")
    else:
        # small segments: label above the bar
        ax.text(left + width / 2, 0.45, label, ha="center", va="bottom",
                fontsize=8)
    left += width

ax.set_xlim(0, 207)
ax.set_ylim(-0.6, 1.0)
ax.set_xticks([0, 13, 17, 71, 191, 207])
ax.set_xticklabels(["0", "13", "17", "71", "191", "207"], fontsize=8)
ax.set_yticks([])
ax.set_xlabel("Chỉ số chiều trong vector quan sát 207")

# Remove top/right/left spines for cleaner look
for spine in ("top", "right", "left"):
    ax.spines[spine].set_visible(False)
ax.tick_params(left=False)

fig.tight_layout()
for ext in ("png", "pdf"):
    fig.savefig(os.path.join(OUT, f"fig_obs_breakdown.{ext}"),
                dpi=250, bbox_inches="tight")
plt.close(fig)
print("saved fig_obs_breakdown")
