"""Tim checkpoint tot nhat run64 theo cac metric tong hop."""
import io
import sys
import os
import glob
import statistics
from tensorboard.backend.event_processing import event_accumulator

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

RUN_DIR = r"Assets/results/run64_continue_from_run62_24M/CombatAgentConfig"

def load_all():
    files = sorted(glob.glob(os.path.join(RUN_DIR, "events.out.tfevents.*")))
    files = [f for f in files if not f.endswith('.meta')]
    eas = []
    for f in files:
        ea = event_accumulator.EventAccumulator(f, size_guidance={'scalars': 0})
        ea.Reload()
        eas.append(ea)
    return eas

def merge(eas, tag):
    pts = {}
    for ea in eas:
        if tag in ea.Tags()['scalars']:
            for e in ea.Scalars(tag):
                pts[e.step] = e.value
    return sorted(pts.items())

def list_checkpoints():
    ckpts = []
    for fn in os.listdir(RUN_DIR):
        if fn.startswith("CombatAgentConfig-") and fn.endswith(".onnx") and ".meta" not in fn:
            try:
                step = int(fn.replace("CombatAgentConfig-", "").replace(".onnx", ""))
                ckpts.append(step)
            except ValueError:
                pass
    return sorted(ckpts)

def smoothed_window(items, step, window=5):
    """Smooth +/- window entries quanh step."""
    if not items:
        return None
    idx = min(range(len(items)), key=lambda i: abs(items[i][0] - step))
    lo = max(0, idx - window)
    hi = min(len(items), idx + window + 1)
    return statistics.mean(v for _, v in items[lo:hi])

def main():
    eas = load_all()
    ckpts = list_checkpoints()
    print(f"[INFO] {len(ckpts)} checkpoints, range [{ckpts[0]:,} .. {ckpts[-1]:,}]")
    print()

    metrics = {
        'kills':      ('CombatAgent/EpisodeKills',         1.0,  1),  # tag, weight, higher_is_better
        'cleared':    ('CombatAgent/ClearedRoom',          2.0,  1),
        'died':       ('CombatAgent/Died',                 1.5, -1),
        'dmg_dealt':  ('CombatAgent/EpisodeDamageDealt',   0.05, 1),
        'dmg_taken':  ('CombatAgent/EpisodeDamageTaken',   0.05,-1),
        'reward':     ('Environment/Cumulative Reward',    0.1,  1),
        'invalid':    ('CombatAgent/InvalidAttackRate',    1.0, -1),
        'aligned':    ('CombatAgent/AlignedAttackRate',    1.0,  1),
        'useful_dsh': ('CombatAgent/UsefulDashRate',       0.5,  1),
    }

    series = {k: merge(eas, tag) for k, (tag, _, _) in metrics.items()}

    # in cac metric tai moi checkpoint
    print(f"{'step':>10s} | {'kill':>5s} {'clr':>5s} {'died':>5s} {'dmg+':>5s} {'dmg-':>5s} {'rew':>6s} {'inv':>5s} {'aln':>5s} {'usD':>5s} | {'score':>7s}")
    print("-" * 95)

    scored = []
    for step in ckpts:
        vals = {}
        for k, (_, _, _) in metrics.items():
            vals[k] = smoothed_window(series[k], step, window=5)
        # normalize per metric across all ckpts -> later
        scored.append((step, vals))

    # min/max per metric (chi tren cac ckpt)
    mm = {}
    for k in metrics:
        col = [v[1][k] for v in scored if v[1][k] is not None]
        if col:
            mm[k] = (min(col), max(col))

    # diem tong hop = weighted sum (normalize 0..1 voi huong)
    def score_of(vals):
        s = 0.0
        for k, (_, w, hib) in metrics.items():
            v = vals[k]
            if v is None or k not in mm:
                continue
            lo, hi = mm[k]
            if hi - lo < 1e-9:
                continue
            n = (v - lo) / (hi - lo)
            if hib < 0:
                n = 1.0 - n
            s += w * n
        return s

    rows = []
    for step, vals in scored:
        sc = score_of(vals)
        rows.append((step, vals, sc))

    for step, vals, sc in rows:
        line = (f"{step:>10,d} | "
                f"{vals['kills']:.2f}  "
                f"{vals['cleared']:.2f}  "
                f"{vals['died']:.2f}  "
                f"{vals['dmg_dealt']:5.1f}  "
                f"{vals['dmg_taken']:5.1f}  "
                f"{vals['reward']:6.2f}  "
                f"{vals['invalid']:.2f}  "
                f"{vals['aligned']:.2f}  "
                f"{vals['useful_dsh']:.2f}  | {sc:>7.3f}")
        print(line)

    rows_sorted = sorted(rows, key=lambda r: r[2], reverse=True)
    print()
    print("=" * 95)
    print("TOP-10 checkpoint theo diem tong hop:")
    print("=" * 95)
    for step, vals, sc in rows_sorted[:10]:
        print(f"  step {step:>10,d}  score={sc:.3f}  | "
              f"kills={vals['kills']:.2f} cleared={vals['cleared']:.2f} "
              f"died={vals['died']:.2f} invalid={vals['invalid']:.2f} "
              f"reward={vals['reward']:.2f}")

    print()
    print("BEST theo tung tieu chi rieng le:")
    print("=" * 95)
    for k, (_, _, hib) in metrics.items():
        best = max(rows, key=lambda r: (r[1][k] or -1e9) * hib)
        print(f"  {k:>10s}: step {best[0]:>10,d} = {best[1][k]:.3f}")

if __name__ == '__main__':
    main()
