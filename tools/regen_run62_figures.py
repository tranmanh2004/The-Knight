"""Regenerate run62_merged_overview.png + run62_merged_entropy.png
voi data hop nhat run62 (24.25M) + run64 (16.57M) = 40.82M buoc.
"""
import io
import sys
import os
import glob
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from tensorboard.backend.event_processing import event_accumulator

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

RUN62_DIR = r"Assets/results/run62_local_grid_aggressive/CombatAgentConfig"
RUN64_DIR = r"Assets/results/run64_continue_from_run62_24M/CombatAgentConfig"
OUT_DIR = r"Dự_án_công_nghệ (7)/figures/training"

# baseline (run46 = curriculum PPO)
RUN46_CLEAR = 0.267
RUN46_KILLS = 0.994
OLD_PLATEAU = 4.40
H_MAX = 5.49


def load_ea_list(d):
    files = sorted(glob.glob(os.path.join(d, "events.out.tfevents.*")))
    files = [f for f in files if not f.endswith('.meta')]
    eas = []
    for f in files:
        ea = event_accumulator.EventAccumulator(f, size_guidance={'scalars': 0})
        ea.Reload()
        eas.append(ea)
    return eas


def merge_tag(eas, tag, step_offset=0):
    pts = {}
    for ea in eas:
        if tag in ea.Tags()['scalars']:
            for e in ea.Scalars(tag):
                pts[e.step + step_offset] = e.value
    return sorted(pts.items())


def smooth(values, window=15):
    if len(values) < window:
        return np.array(values)
    arr = np.array(values, dtype=float)
    kernel = np.ones(window) / window
    smoothed = np.convolve(arr, kernel, mode='same')
    half = window // 2
    for i in range(half):
        smoothed[i] = arr[:i + half + 1].mean()
        smoothed[-(i + 1)] = arr[-(i + half + 1):].mean()
    return smoothed


def get_series(eas62, eas64, run62_last, tag):
    """Tra ve (steps_in_M, values, smoothed_values)."""
    pts62 = merge_tag(eas62, tag, 0)
    pts64 = merge_tag(eas64, tag, run62_last)
    all_pts = sorted(pts62 + pts64)
    if not all_pts:
        return None, None, None
    steps = np.array([s / 1e6 for s, _ in all_pts])
    vals = np.array([v for _, v in all_pts])
    sm = smooth(vals.tolist(), window=15)
    return steps, vals, sm


def main():
    print("[INFO] Loading run62 tfevents...")
    eas62 = load_ea_list(RUN62_DIR)
    print(f"  {len(eas62)} files")
    print("[INFO] Loading run64 tfevents...")
    eas64 = load_ea_list(RUN64_DIR)
    print(f"  {len(eas64)} files")

    pts62_r = merge_tag(eas62, 'Environment/Cumulative Reward')
    run62_last = pts62_r[-1][0] if pts62_r else 0
    print(f"  run62 last step = {run62_last:,}")

    pts64_r = merge_tag(eas64, 'Environment/Cumulative Reward')
    run64_last = pts64_r[-1][0] if pts64_r else 0
    total = run62_last + run64_last
    print(f"  run64 last step = {run64_last:,}")
    print(f"  combined total = {total:,} ({total/1e6:.2f}M)")

    run64_starts_at = run62_last / 1e6

    fig, axes = plt.subplots(2, 2, figsize=(14, 8))
    fig.suptitle(
        f"run62+64 merged -- {total/1e6:.2f}M steps "
        f"(local grid + aggressive + ICM + 4-tier)",
        fontsize=13
    )

    # Panel 1: Reward
    ax = axes[0, 0]
    s, v, sm = get_series(eas62, eas64, run62_last, 'Environment/Cumulative Reward')
    ax.plot(s, sm, color='#1f77b4', linewidth=1.2)
    ax.axvline(run64_starts_at, color='gray', linestyle=':', linewidth=1, label='run64 starts')
    ax.set_title("Reward")
    ax.set_xlabel("Steps (M)")
    ax.set_ylabel("Reward")
    ax.legend(loc='upper left', fontsize=9)
    ax.grid(alpha=0.3)

    # Panel 2: Clear rate
    ax = axes[0, 1]
    s, v, sm = get_series(eas62, eas64, run62_last, 'CombatAgent/ClearedRoom')
    ax.plot(s, sm, color='#2ca02c', linewidth=1.2)
    ax.axhline(RUN46_CLEAR, color='gray', linestyle='--', linewidth=1, label=f'run46 ({RUN46_CLEAR})')
    ax.axvline(run64_starts_at, color='gray', linestyle=':', linewidth=1, label='run64 starts')
    ax.set_title("Clear rate")
    ax.set_xlabel("Steps (M)")
    ax.set_ylabel("Clear rate")
    ax.legend(loc='upper left', fontsize=9)
    ax.grid(alpha=0.3)

    # Panel 3: Kills/episode
    ax = axes[1, 0]
    s, v, sm = get_series(eas62, eas64, run62_last, 'CombatAgent/EpisodeKills')
    ax.plot(s, sm, color='#9467bd', linewidth=1.2)
    ax.axhline(RUN46_KILLS, color='gray', linestyle='--', linewidth=1, label=f'run46 ({RUN46_KILLS})')
    ax.axvline(run64_starts_at, color='gray', linestyle=':', linewidth=1, label='run64 starts')
    ax.set_title("Kills/episode")
    ax.set_xlabel("Steps (M)")
    ax.set_ylabel("Kills/episode")
    ax.legend(loc='upper left', fontsize=9)
    ax.grid(alpha=0.3)

    # Panel 4: Entropy
    ax = axes[1, 1]
    s, v, sm = get_series(eas62, eas64, run62_last, 'Policy/Entropy')
    ax.plot(s, sm, color='#d62728', linewidth=1.2)
    ax.axhline(OLD_PLATEAU, color='gray', linestyle='--', linewidth=1, label=f'old plateau ({OLD_PLATEAU})')
    ax.axvline(run64_starts_at, color='gray', linestyle=':', linewidth=1, label='run64 starts')
    ax.set_title("Entropy (nat)")
    ax.set_xlabel("Steps (M)")
    ax.set_ylabel("Entropy (nat)")
    ax.legend(loc='lower left', fontsize=9)
    ax.grid(alpha=0.3)

    plt.tight_layout()
    out1 = os.path.join(OUT_DIR, "run62_merged_overview.png")
    plt.savefig(out1, dpi=110, bbox_inches='tight')
    print(f"[SAVED] {out1}")
    plt.close()

    # === Figure 2: Entropy zoom (Fig 4.13) ===
    fig, ax = plt.subplots(figsize=(12, 4.5))
    s, v, sm = get_series(eas62, eas64, run62_last, 'Policy/Entropy')

    ax.plot(s, sm, color='#a02828', linewidth=1.8, label='run62+64 (local grid 15x15)')
    ax.fill_between(s, sm, OLD_PLATEAU, where=(sm < OLD_PLATEAU),
                    color='#d62728', alpha=0.12, interpolate=True,
                    label='below plateau')
    ax.axhline(OLD_PLATEAU, color='orange', linestyle='--', linewidth=1.5,
               label=f'207D plateau = {OLD_PLATEAU:.2f}')
    ax.axvline(run64_starts_at, color='gray', linestyle=':', linewidth=1.2,
               label=f'run64 starts ({run64_starts_at:.1f}M)')

    ax.set_title(f"Entropy chinh sach giam ben vung qua vung bao hoa 4.40 nat "
                 f"(run62+64 -- {total/1e6:.2f}M buoc)", fontsize=12)
    ax.set_xlabel("Steps (M)")
    ax.set_ylabel("Entropy (nat)")
    ax.set_ylim(2.85, 4.55)
    ax.set_xlim(-0.5, total / 1e6 + 1)
    ax.legend(loc='lower left', fontsize=9, framealpha=0.92)
    ax.grid(alpha=0.3)
    plt.tight_layout()
    out2 = os.path.join(OUT_DIR, "run62_merged_entropy.png")
    plt.savefig(out2, dpi=110, bbox_inches='tight')
    print(f"[SAVED] {out2}")
    plt.close()


if __name__ == '__main__':
    main()
