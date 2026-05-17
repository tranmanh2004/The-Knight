"""
Redraw fig:obs_breakdown from Chap2_Design.tex — horizontal stacked bar
showing 207-dim observation vector breakdown.
Output: figures/training/fig_obs_breakdown.{png,pdf}
"""

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import os

OUT = r"d:\Project\The Knight\Dự_án_công_nghệ (5)\figures\training"
os.makedirs(OUT, exist_ok=True)

plt.rcParams.update({
    "font.family": "serif",
    "axes.labelsize": 14,
})

groups = [
    ("Người chơi (13)",     13,  "#90CAF9"),  # blue
    ("Toàn cục (4)",         4,  "#FFF59D"),  # yellow
    ("Kẻ địch (54)",        54,  "#EF9A9A"),  # red
    ("Đạn (120)",          120,  "#FFCC80"),  # orange
    ("Tường (16)",          16,  "#A5D6A7"),  # green
]

fig, ax = plt.subplots(figsize=(12, 2.8))

# small-segment labels need to stagger to avoid overlap; we collect them first
small_idx = 0
left = 0
for label, width, color in groups:
    ax.barh(0, width, left=left, color=color, edgecolor="black", linewidth=0.6, height=0.6)
    # Label inside the bar if wide enough, else above with a leader line
    if width >= 30:
        ax.text(left + width / 2, 0, label, ha="center", va="center",
                fontsize=13, fontweight="bold")
    else:
        # small segments: stagger labels above the bar at two heights with a thin leader line
        y_label = 0.95 if small_idx % 2 == 0 else 0.55
        cx = left + width / 2
        ax.annotate(label,
                    xy=(cx, 0.3), xytext=(cx, y_label),
                    ha="center", va="bottom", fontsize=12,
                    arrowprops=dict(arrowstyle="-", color="gray", lw=0.6))
        small_idx += 1
    left += width

ax.set_xlim(0, 207)
ax.set_ylim(-1.2, 1.4)

# Hide ALL default spines + ticks; we draw a custom baseline below the bar
for spine in ("top", "right", "left", "bottom"):
    ax.spines[spine].set_visible(False)
ax.set_xticks([])
ax.set_yticks([])
ax.tick_params(left=False, bottom=False)

# Custom baseline (horizontal axis line) just below the bar
baseline_y = -0.40
ax.hlines(baseline_y, 0, 207, color="black", lw=0.9, clip_on=False)

# Tick marks at every label position
tick_positions = [0, 13, 17, 71, 191, 207]
for x in tick_positions:
    ax.vlines(x, baseline_y - 0.06, baseline_y, color="black", lw=0.9, clip_on=False)

# Tick labels — "13" and "17" close together so stagger them vertically
for x, lbl, y_off in [(0, "0", -0.16), (13, "13", -0.16), (17, "17", -0.38),
                       (71, "71", -0.16), (191, "191", -0.16), (207, "207", -0.16)]:
    ax.text(x, baseline_y + y_off, lbl, ha="center", va="top", fontsize=11)

ax.set_xlabel("Chỉ số chiều trong vector quan sát 207", labelpad=34)

fig.tight_layout()
for ext in ("png", "pdf"):
    fig.savefig(os.path.join(OUT, f"fig_obs_breakdown.{ext}"),
                dpi=250, bbox_inches="tight")
plt.close(fig)
print("saved fig_obs_breakdown")
