"""
Redraw all 8 inline TikZ charts from Chap4_Results.tex as clean matplotlib figures.
Output: figures/training/ as PNG (dpi=250) + PDF vector.
"""

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import matplotlib.patches as mpatches
import numpy as np
import os

OUT = r"c:\Riot Games\The-Knight\Dự_án_công_nghệ (5)\figures\training"
os.makedirs(OUT, exist_ok=True)

STYLE = {
    "font.family": "serif",
    "axes.spines.top": False,
    "axes.spines.right": False,
    "axes.grid": True,
    "grid.alpha": 0.35,
    "grid.linestyle": "--",
    "axes.labelsize": 10,
    "xtick.labelsize": 9,
    "ytick.labelsize": 9,
    "legend.fontsize": 9,
}
plt.rcParams.update(STYLE)

def save(fig, name):
    for ext in ("png", "pdf"):
        fig.savefig(os.path.join(OUT, f"{name}.{ext}"),
                    dpi=250, bbox_inches="tight")
    plt.close(fig)
    print(f"  saved {name}")


# ── Figure 4.2 : Rolling mean reward – PPO / ICM / RND ───────────────────────
steps = [100e3, 300e3, 500e3, 700e3, 1e6]
xlabels = ["100k", "300k", "500k", "700k", "1M"]

ppo_y = [2.82, 3.17, 2.14, 1.84, 2.11]
icm_y = [3.29, 1.81, 2.43, 2.33, 2.39]
rnd_y = [2.90, 2.60, 2.62, 2.42, 2.62]

fig, ax = plt.subplots(figsize=(5.5, 3.2))
ax.plot(steps, ppo_y, color="gray",          lw=1.8, marker="o", ms=4, label="PPO")
ax.plot(steps, icm_y, color="steelblue",     lw=1.8, marker="s", ms=4, label="ICM")
ax.plot(steps, rnd_y, color="forestgreen",   lw=1.8, marker="^", ms=4, label="RND")
ax.set_xticks(steps); ax.set_xticklabels(xlabels)
ax.set_xlim(0, 1.12e6)
ax.set_ylim(1.0, 4.2)
ax.set_xlabel("Bước huấn luyện")
ax.set_ylabel("Rolling reward (200k)")
ax.legend(loc="upper right")
fig.tight_layout()
save(fig, "fig42_reward_ppo_icm_rnd")


# ── Figure 4.3 : ICM inverse loss plateau ────────────────────────────────────
train_pts = [0.2e6, 1.5e6, 3.2e6, 5.0e6]
inv_loss  = [4.39,  4.36,  4.35,  4.36]

fig, ax = plt.subplots(figsize=(5.5, 3.0))
ax.axhline(5.49, color="crimson", lw=1.5, ls="--", label=r"$H_{\mathrm{rand}}=5.49$")
ax.plot(train_pts, inv_loss, color="steelblue", lw=2.0, marker="o", ms=4,
        label="ICM inverse loss")
ax.set_xlim(0, 5.5e6)
ax.set_ylim(0, 6.2)
ax.set_xlabel("Bước huấn luyện")
ax.set_ylabel("Loss")
ax.set_xticks([1e6, 2e6, 3e6, 4e6, 5e6])
ax.set_xticklabels(["1M", "2M", "3M", "4M", "5M"])
ax.legend()
fig.tight_layout()
save(fig, "fig43_icm_inverse_loss")


# ── Figure 4.4 : run42 LSTM reward trend ─────────────────────────────────────
lstm_y = [1.64, 2.09, 2.19, 2.21, 2.71]

fig, ax = plt.subplots(figsize=(5.5, 3.0))
ax.plot(steps, lstm_y, color="purple", lw=2.0, marker="o", ms=4, label="run42 (LSTM)")
ax.set_xticks(steps); ax.set_xticklabels(xlabels)
ax.set_xlim(0, 1.12e6)
ax.set_ylim(0.8, 3.2)
ax.set_xlabel("Bước huấn luyện")
ax.set_ylabel("Rolling reward (200k)")
ax.legend()
fig.tight_layout()
save(fig, "fig44_lstm_trend")


# ── Figure 4.5 : Lesson transition run46 (schematic step-function) ───────────
fig, ax = plt.subplots(figsize=(6.2, 2.6))

# step-function segments: (x_start, x_end, y_level, label)
segs46 = [
    (10e3,  70e3,  1, "Easy"),
    (70e3,  140e3, 2, "Medium"),
    (140e3, 3e6,   3, "Hard"),
]
colors46 = ["#4CAF50", "#FF9800", "#F44336"]
for (x0, x1, y, lbl), c in zip(segs46, colors46):
    ax.hlines(y, x0, x1, colors=c, lw=3)
    # Label centered above segment, but for short segments offset to the right
    if (x1 - x0) > 5e5:
        ax.text((x0 + x1) / 2, y + 0.13, lbl, ha="center", va="bottom",
                fontsize=10, color=c, fontweight="bold")
    else:
        ax.text(x1 + 3e4, y + 0.05, lbl, ha="left", va="center",
                fontsize=9, color=c, fontweight="bold")

# Transition markers + annotated tick values (rotated to avoid overlap)
for x, label in [(70e3, "70k"), (140e3, "140k")]:
    ax.axvline(x, color="gray", lw=0.8, ls="--", alpha=0.6)
    ax.annotate(label, xy=(x, 0), xytext=(x, -0.35),
                ha="center", va="top", fontsize=8, color="gray",
                annotation_clip=False)

ax.set_xlim(-5e4, 3.1e6)
ax.set_ylim(0.4, 3.8)
ax.set_xlabel("Bước huấn luyện")
ax.set_yticks([1, 2, 3])
ax.set_yticklabels(["Easy", "Medium", "Hard"])
ax.set_xticks([0, 1e6, 2e6, 3e6])
ax.set_xticklabels(["0", "1M", "2M", "3M"])
ax.grid(axis="y", alpha=0.2)
ax.grid(axis="x", alpha=0.2)
fig.tight_layout()
save(fig, "fig45_run46_lesson_transition")


# ── Figure 4.10 : run54 4-tier curriculum schematic ──────────────────────────
fig, ax = plt.subplots(figsize=(6.5, 3.0))

segs54 = [
    (10e3,  60e3,  1, "Easy F"),
    (60e3,  130e3, 2, "Medium F"),
    (130e3, 280e3, 3, "Hard F"),
    (280e3, 3e6,   4, "Hard R"),
]
colors54 = ["#4CAF50", "#FF9800", "#F44336", "#9C27B0"]
for (x0, x1, y, lbl), c in zip(segs54, colors54):
    ax.hlines(y, x0, x1, colors=c, lw=3)
    # Long segments: label inline; short segments: label to the right
    if (x1 - x0) > 4e5:
        ax.text((x0 + x1) / 2, y + 0.16, lbl, ha="center", va="bottom",
                fontsize=10, color=c, fontweight="bold")
    else:
        ax.text(x1 + 5e4, y + 0.05, lbl, ha="left", va="center",
                fontsize=9, color=c, fontweight="bold")

# Transition markers + tick values rotated below the axis
for x, label in [(60e3, "60k"), (130e3, "130k"), (280e3, "280k")]:
    ax.axvline(x, color="gray", lw=0.8, ls="--", alpha=0.6)
    ax.annotate(label, xy=(x, 0), xytext=(x, -0.45),
                ha="center", va="top", fontsize=8, color="gray",
                rotation=30, annotation_clip=False)

ax.set_xlim(-5e4, 3.1e6)
ax.set_ylim(0.4, 4.9)
ax.set_xlabel("Bước huấn luyện")
ax.set_yticks([1, 2, 3, 4])
ax.set_yticklabels(["Easy F", "Med F", "Hard F", "Hard R"])
ax.set_xticks([0, 1e6, 2e6, 3e6])
ax.set_xticklabels(["0", "1M", "2M", "3M"])
ax.grid(axis="y", alpha=0.2)
ax.grid(axis="x", alpha=0.2)
fig.tight_layout()
save(fig, "fig410_run54_four_tier")


# ── Figure 4.11 : run55 cleared peak bar chart ───────────────────────────────
fig, ax = plt.subplots(figsize=(4.0, 3.2))

runs  = ["run46\n(reward v1)", "run55\n(reward v2)"]
vals  = [0.615, 0.800]
cols  = ["steelblue", "darkorange"]
bars  = ax.bar(runs, vals, color=cols, width=0.45, edgecolor="black", linewidth=0.6)
for bar, v in zip(bars, vals):
    ax.text(bar.get_x() + bar.get_width()/2, v + 0.01, f"{v:.3f}",
            ha="center", va="bottom", fontsize=9, fontweight="bold")

ax.set_ylim(0, 0.95)
ax.set_ylabel("Cleared peak rate")
ax.yaxis.grid(True, alpha=0.35, ls="--")
ax.set_axisbelow(True)
fig.tight_layout()
save(fig, "fig411_reward_v2_clear_peak")


# ── Figure 4.12 : Entropy plateau bar chart ──────────────────────────────────
run_ids   = ["34","35","36","42","46","47","50","54","55","62"]
entropies = [4.408,4.407,4.406,4.405,4.409,4.405,4.412,4.407,4.408,3.099]
colors12  = ["steelblue"]*9 + ["darkorange"]

fig, ax = plt.subplots(figsize=(7.5, 3.6))
bars = ax.bar(run_ids, entropies, color=colors12, edgecolor="black",
              linewidth=0.5, width=0.65)

# Pad x-axis on the right so reference-line labels sit outside the bars
ax.set_xlim(-0.6, 11.5)

# Reference lines stop before label text (use hlines, not axhline)
ax.hlines(5.49, xmin=-0.6, xmax=9.5, color="crimson", lw=1.4, ls="--", zorder=1)
ax.hlines(4.40, xmin=-0.6, xmax=9.5, color="#E65100", lw=1.2, ls="--", zorder=1)

# Value label above each bar (so reader sees tiny diffs)
for bar, v in zip(bars, entropies):
    color = "#E65100" if v < 4.0 else "black"
    weight = "bold" if v < 4.0 else "normal"
    ax.text(bar.get_x() + bar.get_width() / 2, v + 0.04, f"{v:.3f}",
            ha="center", va="bottom", fontsize=7.5,
            color=color, fontweight=weight)

ax.set_ylim(2.9, 5.95)
ax.set_xlabel("Run")
ax.set_ylabel("Entropy tail-50 (nat)")
ax.yaxis.grid(True, alpha=0.3, ls="--")
ax.set_axisbelow(True)

# Reference-line labels placed in the gap to the right (line already stopped)
ax.text(9.7, 5.49, r"$H_{\max}=5.49$", va="center", ha="left",
        fontsize=8.5, color="crimson")
ax.text(9.7, 4.40, r"plateau $\approx 4.40$", va="center", ha="left",
        fontsize=8.5, color="#E65100")

# Legend top-left (corner is empty there — all bars except run62 are at ~4.4)
blue_patch   = mpatches.Patch(color="steelblue",  label="Ablation 207 chiều")
orange_patch = mpatches.Patch(color="darkorange", label="run62 (432 chiều)")
ax.legend(handles=[blue_patch, orange_patch],
          loc="upper left", fontsize=8.5, framealpha=0.95)

fig.tight_layout()
save(fig, "fig412_entropy_plateau")


# ── Figure 4.13 : Reward mean comparison bar chart ───────────────────────────
run_ids13  = ["34","35","36","42","46","47","50","54"]
rewards13  = [2.073,2.416,2.487,2.411,3.843,3.765,4.369,3.925]
# no-curriculum: blue; curriculum: green
colors13 = ["steelblue"]*4 + ["#2e7d32","#388e3c","#1b5e20","#43a047"]

fig, ax = plt.subplots(figsize=(7.0, 3.6))
bars13 = ax.bar(run_ids13, rewards13, color=colors13,
                edgecolor="black", linewidth=0.5, width=0.55)

# Baseline reference line + label outside bars on the right
ax.axhline(2.073, color="gray", lw=1.2, ls="--", alpha=0.8, zorder=1)
# Pad x-axis so label fits to the right without overlapping bars
ax.set_xlim(-0.6, 8.3)
ax.text(8.05, 2.073, "mốc 2.073", ha="left", va="center",
        fontsize=8.5, color="gray",
        bbox=dict(facecolor="white", edgecolor="none", pad=1.2))

for bar, v in zip(bars13, rewards13):
    ax.text(bar.get_x()+bar.get_width()/2, v+0.06, f"{v:.2f}",
            ha="center", va="bottom", fontsize=8)

ax.set_ylim(0, 5.2)
ax.set_xlabel("Run")
ax.set_ylabel("Phần thưởng trung bình")
ax.yaxis.grid(True, alpha=0.3, ls="--")
ax.set_axisbelow(True)

blue_p  = mpatches.Patch(color="steelblue", label="Không curriculum (1M bước)")
green_p = mpatches.Patch(color="#2e7d32",   label="Curriculum (3M bước)")
ax.legend(handles=[blue_p, green_p], fontsize=9, loc="upper left")
fig.tight_layout()
save(fig, "fig413_reward_comparison")


print("\nAll 8 figures generated successfully.")
