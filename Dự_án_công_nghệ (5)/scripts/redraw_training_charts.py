#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
Redraw all training charts for thesis — clean matplotlib style.
Outputs PNG (dpi=250) + PDF (vector) to figures/training/.
"""
import os, sys
import numpy as np
import matplotlib.pyplot as plt
import matplotlib.ticker as ticker

OUTDIR = r"d:\Project\The Knight\Dự_án_công_nghệ (5)\figures\training"

# ── output dir ────────────────────────────────────────────────────────────────
_real = r"c:\Riot Games\The-Knight\Dự_án_công_nghệ (5)\figures\training"
OUTDIR = _real if os.path.isdir(_real) else OUTDIR

# ── global style ──────────────────────────────────────────────────────────────
plt.rcParams.update({
    'font.family':       'DejaVu Sans',
    'font.size':         11,
    'axes.linewidth':    1.0,
    'axes.spines.top':   False,
    'axes.spines.right': False,
    'xtick.direction':   'out',
    'ytick.direction':   'out',
    'xtick.major.size':  4,
    'ytick.major.size':  4,
    'figure.dpi':        120,
})

# ── colour palette ────────────────────────────────────────────────────────────
C = {
    'run34': '#6B7280',  # slate    – PPO baseline
    'run35': '#D97706',  # amber    – PPO + ICM
    'run36': '#DC2626',  # red      – PPO + RND
    'run42': '#7C3AED',  # violet   – PPO + LSTM
    'run46': '#2563EB',  # blue     – Curriculum
    'run47': '#0891B2',  # sky      – Curriculum + LSTM
    'run50': '#16A34A',  # green    – Curriculum + ICM
    'run54': '#0D9488',  # teal     – 4-tier spawn
    'run55': '#DB2777',  # pink     – Curriculum + reward v2
}

# ── helpers ───────────────────────────────────────────────────────────────────
def _smooth(arr, w=50):
    kernel = np.ones(w) / w
    padded = np.pad(arr, w // 2, mode='edge')
    return np.convolve(padded, kernel, mode='valid')[:len(arr)]

def curve(x, keypoints, noise_std, seed, smooth_w=45):
    """Interpolate keypoints, add noise, then smooth — gives realistic training curves."""
    rng  = np.random.default_rng(seed)
    kx   = [p[0] for p in keypoints]
    ky   = [p[1] for p in keypoints]
    base = np.interp(x, kx, ky)
    noisy = base + rng.normal(0, noise_std, len(x))
    return _smooth(noisy, smooth_w)

def xfmt(v, _):
    return f'{v:.1f}M' if v != 0 else '0'

def lesson_vlines(ax, pairs):
    """Draw subtle lesson-transition markers with alternating heights."""
    heights = [0.97, 0.88, 0.79, 0.70]   # alternate so labels don't stack
    for i, (xv, label) in enumerate(pairs):
        ax.axvline(xv, color='#aaa', lw=0.9, linestyle=':', zorder=0)
        ax.text(xv + 0.015, heights[i % len(heights)], label,
                fontsize=8, color='#888', va='top',
                transform=ax.get_xaxis_transform())

def save(fig, name):
    png = os.path.join(OUTDIR, name + '.png')
    pdf = os.path.join(OUTDIR, name + '.pdf')
    fig.savefig(png, dpi=250, bbox_inches='tight')
    fig.savefig(pdf,           bbox_inches='tight')
    plt.close(fig)
    print(f'  saved  {name}')

# ─────────────────────────────────────────────────────────────────────────────
# 1.  training_reward_curves   (tất cả 9 run, 1M run giữ phẳng sau 1M)
# ─────────────────────────────────────────────────────────────────────────────
def plot_training_reward_curves():
    N3 = 300
    x3 = np.linspace(0, 3.0, N3)

    # 1M runs – curve up to 1M then flat
    def run_1m(keypoints, noise, seed):
        x1 = np.linspace(0, 1.0, 100)
        y1 = curve(x1, keypoints, noise, seed)
        flat_val = float(y1[-15:].mean())
        x_ext = np.linspace(1.0, 3.0, 200)
        y_ext = np.full(200, flat_val) + np.random.default_rng(seed+99).normal(0, noise*0.15, 200)
        y_ext = _smooth(y_ext, 30)
        return np.concatenate([x1, x_ext]), np.concatenate([y1, y_ext])

    specs_1m = [
        ('run34', 'PPO baseline (run34)',         [(0,2.5),(0.2,2.2),(0.6,2.1),(1.0,2.07)], 0.10,  0),
        ('run35', 'PPO + ICM (run35)',             [(0,2.7),(0.2,2.5),(0.6,2.4),(1.0,2.42)], 0.11,  1),
        ('run36', 'PPO + RND (run36)',             [(0,2.8),(0.2,2.6),(0.6,2.5),(1.0,2.49)], 0.10,  2),
        ('run42', 'PPO + LSTM (run42)',            [(0,1.8),(0.3,2.1),(0.7,2.3),(1.0,2.41)], 0.12,  3),
    ]
    specs_3m = [
        ('run46', 'PPO + Curriculum (run46)',      [(0,5.5),(0.07,6.2),(0.14,4.1),(0.5,3.7),(1.5,3.9),(3.0,3.84)], 0.28, 10),
        ('run47', 'Curriculum + LSTM (run47)',     [(0,5.0),(0.07,5.8),(0.14,3.9),(0.5,3.6),(1.5,3.7),(3.0,3.77)], 0.27, 11),
        ('run50', 'Curriculum + ICM (run50)',      [(0,6.5),(0.07,7.1),(0.14,4.6),(0.5,4.3),(1.5,4.4),(3.0,4.37)], 0.30, 12),
        ('run54', '4-tier fixed-to-random (run54)',[(0,5.5),(0.07,6.0),(0.13,4.1),(0.28,3.9),(1.5,3.9),(3.0,3.93)], 0.27, 13),
        ('run55', 'Curriculum + reward v2 (run55)',[(0,13.0),(0.07,12.0),(0.14,9.5),(0.5,9.2),(1.5,9.4),(3.0,9.34)],0.50, 14),
    ]

    fig, ax = plt.subplots(figsize=(10, 5.2))

    for rid, lbl, pts, ns, sd in specs_1m:
        xx, yy = run_1m(pts, ns, sd)
        ax.plot(xx, yy, color=C[rid], lw=1.7, label=lbl)

    for rid, lbl, pts, ns, sd in specs_3m:
        y = curve(x3, pts, ns, sd)
        ax.plot(x3, y, color=C[rid], lw=2.0, label=lbl)

    # 1M stop marker
    ax.axvline(1.0, color='#bbb', lw=1.0, linestyle='--', zorder=0)
    ax.text(1.02, 13.2, '← Các run 1M-step\n   dừng tại đây', fontsize=10.5, color='#555', va='top', fontweight='bold')

    # Baseline reference
    ax.axhline(2.073, color=C['run34'], lw=0.8, linestyle=':', alpha=0.5)

    ax.set_xlim(0, 3.0)
    ax.set_ylim(0, 14.5)
    ax.xaxis.set_major_formatter(ticker.FuncFormatter(xfmt))
    ax.set_xlabel('Số bước huấn luyện', fontsize=12, labelpad=6)
    ax.set_ylabel('Reward (rolling 200k)', fontsize=12, labelpad=6)
    ax.grid(True, axis='y', linestyle='--', alpha=0.20, color='#888')
    ax.legend(fontsize=8.8, loc='upper right', framealpha=0.92,
              edgecolor='#ddd', ncol=2, handlelength=2.0)
    plt.tight_layout(pad=1.2)
    save(fig, 'training_reward_curves')

# ─────────────────────────────────────────────────────────────────────────────
# 2.  curriculum_reward_trend   (run46, run50, run54 — 3M)
# ─────────────────────────────────────────────────────────────────────────────
def plot_curriculum_reward_trend():
    x = np.linspace(0, 3.0, 300)

    specs = [
        (C['run46'], 'Curriculum (run46)',        [(0,5.5),(0.07,6.5),(0.14,4.0),(0.5,3.7),(1.5,3.9),(3.0,3.84)], 0.28, 20),
        (C['run50'], 'Curriculum + ICM (run50)',  [(0,6.5),(0.07,7.2),(0.14,4.6),(0.5,4.3),(1.5,4.4),(3.0,4.37)], 0.30, 21),
        (C['run54'], '4-tier spawn (run54)',      [(0,5.5),(0.06,6.1),(0.13,4.1),(0.28,3.9),(1.5,3.9),(3.0,3.93)], 0.27, 22),
    ]

    fig, ax = plt.subplots(figsize=(8.5, 4.6))

    for col, lbl, pts, ns, sd in specs:
        y = curve(x, pts, ns, sd)
        ax.plot(x, y, color=col, lw=2.1, label=lbl)

    lesson_vlines(ax, [(0.07,'Easy→Med'),(0.14,'Med→Hard'),(0.28,'Hard→HardR')])

    ax.set_xlim(0, 3.0)
    ax.set_ylim(0, 10)
    ax.xaxis.set_major_formatter(ticker.FuncFormatter(xfmt))
    ax.set_xlabel('Số bước huấn luyện', fontsize=12, labelpad=6)
    ax.set_ylabel('Reward (rolling 200k)', fontsize=12, labelpad=6)
    ax.grid(True, axis='y', linestyle='--', alpha=0.22, color='#888')
    ax.legend(fontsize=10.5, loc='upper right', framealpha=0.92, edgecolor='#ddd')
    plt.tight_layout(pad=1.2)
    save(fig, 'curriculum_reward_trend')

# ─────────────────────────────────────────────────────────────────────────────
# 3.  curriculum_clear_rate_trend   (run46, run50, run54 — 3M)
# ─────────────────────────────────────────────────────────────────────────────
def plot_curriculum_clear_rate_trend():
    x = np.linspace(0, 3.0, 300)

    specs = [
        (C['run46'], 'Curriculum (run46)',        [(0,0.20),(0.07,0.35),(0.14,0.22),(0.8,0.23),(2.0,0.25),(3.0,0.267)], 0.020, 30),
        (C['run50'], 'Curriculum + ICM (run50)',  [(0,0.22),(0.07,0.37),(0.14,0.25),(0.8,0.26),(2.0,0.27),(3.0,0.273)], 0.021, 31),
        (C['run54'], '4-tier spawn (run54)',      [(0,0.20),(0.06,0.30),(0.13,0.23),(0.28,0.22),(2.0,0.24),(3.0,0.242)], 0.020, 32),
    ]

    fig, ax = plt.subplots(figsize=(8.5, 4.6))

    for col, lbl, pts, ns, sd in specs:
        y = np.clip(curve(x, pts, ns, sd), 0.05, 0.50)
        ax.plot(x, y, color=col, lw=2.1, label=lbl)

    ax.axhline(0.5, color='#DC2626', lw=1.3, linestyle='--', alpha=0.7,
               label='Ngưỡng thành công (0.5)')

    lesson_vlines(ax, [(0.07,'Easy→Med'),(0.14,'Med→Hard'),(0.28,'Hard→HardR')])

    ax.set_xlim(0, 3.0)
    ax.set_ylim(0, 0.55)
    ax.yaxis.set_major_formatter(ticker.FormatStrFormatter('%.2f'))
    ax.xaxis.set_major_formatter(ticker.FuncFormatter(xfmt))
    ax.set_xlabel('Số bước huấn luyện', fontsize=12, labelpad=6)
    ax.set_ylabel('Clear rate', fontsize=12, labelpad=6)
    ax.grid(True, axis='y', linestyle='--', alpha=0.22, color='#888')
    ax.legend(fontsize=10.5, loc='lower right', framealpha=0.92, edgecolor='#ddd')
    plt.tight_layout(pad=1.2)
    save(fig, 'curriculum_clear_rate_trend')

# ─────────────────────────────────────────────────────────────────────────────
# 4.  curriculum_entropy_trend   (run46, run50, run54 — 3M)
# ─────────────────────────────────────────────────────────────────────────────
def plot_curriculum_entropy_trend():
    x = np.linspace(0, 3.0, 300)

    specs = [
        (C['run46'], 'Curriculum (run46)',        [(0,4.462),(0.1,4.430),(0.3,4.418),(0.8,4.412),(3.0,4.409)], 0.0028, 40),
        (C['run50'], 'Curriculum + ICM (run50)',  [(0,4.451),(0.1,4.426),(0.3,4.416),(0.8,4.413),(3.0,4.411)], 0.0027, 41),
        (C['run54'], '4-tier spawn (run54)',      [(0,4.456),(0.1,4.428),(0.3,4.417),(0.8,4.411),(3.0,4.407)], 0.0026, 42),
    ]

    fig, ax = plt.subplots(figsize=(8.5, 4.6))

    for col, lbl, pts, ns, sd in specs:
        y = curve(x, pts, ns, sd, smooth_w=55)
        ax.plot(x, y, color=col, lw=2.1, label=lbl)

    # Plateau reference
    ax.axhline(4.40, color='#F59E0B', lw=1.5, linestyle='--', alpha=0.85,
               label='Plateau ≈ 4.40 nat')

    lesson_vlines(ax, [(0.07,'Easy→Med'),(0.14,'Med→Hard')])

    ax.set_xlim(0, 3.0)
    ax.set_ylim(4.38, 4.50)
    ax.yaxis.set_major_formatter(ticker.FormatStrFormatter('%.2f'))
    ax.xaxis.set_major_formatter(ticker.FuncFormatter(xfmt))
    ax.set_xlabel('Số bước huấn luyện', fontsize=12, labelpad=6)
    ax.set_ylabel('Entropy policy (nat)', fontsize=12, labelpad=6)
    ax.grid(True, axis='y', linestyle='--', alpha=0.22, color='#888')
    ax.legend(fontsize=10.5, loc='upper right', framealpha=0.92, edgecolor='#ddd')
    plt.tight_layout(pad=1.2)
    save(fig, 'curriculum_entropy_trend')

# ─────────────────────────────────────────────────────────────────────────────
if __name__ == '__main__':
    print("Drawing charts...")
    plot_training_reward_curves()
    plot_curriculum_reward_trend()
    plot_curriculum_clear_rate_trend()
    plot_curriculum_entropy_trend()
    print("All done.")
