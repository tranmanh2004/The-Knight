"""Rolling-mean reward curve cho 4 cell: run33/34/35/36.

Window 200k step (= 20 entries voi summary_freq=10000).
Trailing rolling mean (gia tri tai step t = mean cua [t-200k, t]).
"""
from tensorboard.backend.event_processing import event_accumulator
import statistics

try:
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    HAS_PLT = True
except ImportError:
    HAS_PLT = False
    print('[warn] matplotlib not installed, output text only')

runs = {
    'run34_PPO_scratch_1M':  ('Assets/results/run34_clean_env_ppo_baseline_v1/AttentionAgentConfig', '#888888', '-'),
    'run35_ICM_scratch_1M':  ('Assets/results/run35_clean_env_ppo_baseline_v1/AttentionAgentConfig', '#1f77b4', '-'),
    'run36_RND_scratch_1M':  ('Assets/results/run36_clean_env_rnd_scratch_v1/AttentionAgentConfig', '#2ca02c', '-'),
    'run33_ICM_init25_3.5M': ('Assets/results/run33_clean_env_icm_v1/AttentionAgentConfig', '#d62728', '--'),
}

WINDOW_STEPS = 200000
TAG = 'Environment/Cumulative Reward'

def rolling_trailing(events, window):
    """For each event, mean of all events with step in [step-window, step]."""
    pts = []
    for i, e in enumerate(events):
        lo = e.step - window
        bucket = [ev.value for ev in events if lo <= ev.step <= e.step]
        pts.append((e.step, statistics.mean(bucket)))
    return pts

# Load
data = {}
for name, (path, color, ls) in runs.items():
    ea = event_accumulator.EventAccumulator(path, size_guidance={'scalars': 0})
    ea.Reload()
    if TAG not in ea.Tags()['scalars']:
        print(f'[skip] {name}: no {TAG}')
        continue
    ev = ea.Scalars(TAG)
    raw = [(e.step, e.value) for e in ev]
    roll = rolling_trailing(ev, WINDOW_STEPS)
    data[name] = {'raw': raw, 'roll': roll, 'color': color, 'ls': ls}

# Print rolling values at key steps
print(f'\n=== Rolling-mean reward (window={WINDOW_STEPS//1000}k step) ===')
key_steps = [100_000, 200_000, 300_000, 500_000, 700_000, 1_000_000, 1_500_000, 2_000_000, 2_500_000, 3_000_000, 3_500_000]
header = f'{"step":>10s}'
for n in data:
    header += f' {n[:18]:>20s}'
print(header)
print('-' * len(header))
for s in key_steps:
    row = f'{s:>10d}'
    for n, d in data.items():
        roll = d['roll']
        last = next((v for st, v in reversed(roll) if st <= s), None)
        if last is not None and roll[-1][0] >= s:
            row += f' {last:>20.3f}'
        else:
            row += f' {"-":>20s}'
    print(row)

# Trend analysis: is curve monotonically declining?
print('\n=== Trend analysis (declining = degrade thuc su, oscillate = noise dau) ===')
for name, d in data.items():
    roll = d['roll']
    if len(roll) < 5:
        continue
    # Take rolling values at 25%, 50%, 75%, 100% of run
    last_step = roll[-1][0]
    samples = []
    for frac in [0.25, 0.50, 0.75, 1.00]:
        target = last_step * frac
        v = next((val for st, val in reversed(roll) if st <= target), roll[0][1])
        samples.append(v)
    # Diff vs final
    diffs = [s - samples[-1] for s in samples]
    monotonic_decline = all(samples[i] >= samples[i+1] - 0.05 for i in range(len(samples)-1))
    print(f'  {name:24s} 25%={samples[0]:.2f}  50%={samples[1]:.2f}  75%={samples[2]:.2f}  final={samples[3]:.2f}  '
          f'monotonic_decline={monotonic_decline}')

if HAS_PLT:
    fig, axes = plt.subplots(2, 1, figsize=(12, 10), sharex=False)

    # Top: full timeline (run33 has 3.5M, others 1M)
    ax = axes[0]
    for name, d in data.items():
        roll = d['roll']
        steps = [s for s, _ in roll]
        vals = [v for _, v in roll]
        ax.plot(steps, vals, label=name, color=d['color'], linestyle=d['ls'], linewidth=1.8)
        # Lighter raw
        rs = [s for s, _ in d['raw']]
        rv = [v for _, v in d['raw']]
        ax.plot(rs, rv, color=d['color'], alpha=0.15, linewidth=0.8)
    ax.set_xlabel('Step')
    ax.set_ylabel('Cumulative Reward')
    ax.set_title(f'Rolling mean reward (window={WINDOW_STEPS//1000}k) — full timeline')
    ax.legend(loc='upper right', fontsize=9)
    ax.grid(alpha=0.3)
    ax.axhline(y=0, color='k', linewidth=0.5)

    # Bottom: zoom 0..1M (apples-to-apples 3 scratch runs + run33 first third)
    ax2 = axes[1]
    for name, d in data.items():
        roll = [(s, v) for s, v in d['roll'] if s <= 1_000_000]
        if not roll:
            continue
        steps = [s for s, _ in roll]
        vals = [v for _, v in roll]
        ax2.plot(steps, vals, label=name, color=d['color'], linestyle=d['ls'], linewidth=1.8)
    ax2.set_xlabel('Step')
    ax2.set_ylabel('Cumulative Reward')
    ax2.set_title('Zoom 0..1M (apples-to-apples)')
    ax2.legend(loc='upper right', fontsize=9)
    ax2.grid(alpha=0.3)

    plt.tight_layout()
    out = 'rolling_mean_reward.png'
    plt.savefig(out, dpi=120)
    print(f'\n[saved] {out}')
