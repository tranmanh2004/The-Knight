"""ICM analysis for run64_continue_from_run62_24M."""

import io
import sys
import os

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import glob
import statistics
from tensorboard.backend.event_processing import event_accumulator

RUN_DIR = r"Assets/results/run64_continue_from_run62_24M/CombatAgentConfig"

def load_scalars(path):
    ea = event_accumulator.EventAccumulator(path, size_guidance={'scalars': 0})
    ea.Reload()
    return ea

def merge_scalars(eas, tag):
    """Gộp 1 tag từ nhiều EventAccumulator theo step (sort tăng)."""
    pts = []
    for ea in eas:
        if tag in ea.Tags()['scalars']:
            pts.extend((e.step, e.value) for e in ea.Scalars(tag))
    pts.sort(key=lambda p: p[0])
    # dedup theo step (giữ entry cuối)
    out = {}
    for s, v in pts:
        out[s] = v
    items = sorted(out.items())
    return items

def stats(items, head=50, tail=50):
    if not items:
        return None
    vals = [v for _, v in items]
    return {
        'n': len(items),
        'step_min': items[0][0],
        'step_max': items[-1][0],
        'head_mean': statistics.mean(vals[:head]) if len(vals) >= head else statistics.mean(vals),
        'tail_mean': statistics.mean(vals[-tail:]) if len(vals) >= tail else statistics.mean(vals),
        'overall_mean': statistics.mean(vals),
        'min': min(vals),
        'max': max(vals),
    }

def fmt(s):
    if s is None:
        return "  (no data)"
    return (f"  n={s['n']:4d}  step=[{s['step_min']:>10}..{s['step_max']:>10}]  "
            f"head50={s['head_mean']:+.4f}  tail50={s['tail_mean']:+.4f}  "
            f"min={s['min']:+.4f}  max={s['max']:+.4f}")

def main():
    files = sorted(glob.glob(os.path.join(RUN_DIR, "events.out.tfevents.*")))
    files = [f for f in files if not f.endswith('.meta')]
    print(f"[INFO] Tfevents files: {len(files)}")
    for f in files:
        sz = os.path.getsize(f)
        print(f"   - {os.path.basename(f)}  ({sz:,} bytes)")
    eas = [load_scalars(f) for f in files]

    all_tags = set()
    for ea in eas:
        all_tags.update(ea.Tags()['scalars'])
    print(f"\n[INFO] Total scalar tags: {len(all_tags)}")
    for t in sorted(all_tags):
        print(f"   {t}")

    icm_groups = {
        "REWARD / RETURN": [
            'Environment/Cumulative Reward',
            'Environment/Episode Length',
            'Policy/Extrinsic Reward',
            'Policy/Extrinsic Value Estimate',
            'Policy/Curiosity Reward',
            'Policy/Curiosity Value Estimate',
        ],
        "LOSSES": [
            'Losses/Policy Loss',
            'Losses/Value Loss',
            'Losses/Curiosity Forward Loss',
            'Losses/Curiosity Inverse Loss',
        ],
        "POLICY HEALTH": [
            'Policy/Entropy',
            'Policy/Learning Rate',
            'Policy/Beta',
            'Policy/Epsilon',
        ],
    }

    print("\n" + "=" * 90)
    print("  ICM ANALYSIS — run64_continue_from_run62_24M")
    print("=" * 90)
    for group, tags in icm_groups.items():
        print(f"\n[{group}]")
        for tag in tags:
            items = merge_scalars(eas, tag)
            s = stats(items)
            print(f" {tag}")
            print(fmt(s))

    print("\n[ICM HEALTH SUMMARY]")
    cur = merge_scalars(eas, 'Policy/Curiosity Reward')
    ext = merge_scalars(eas, 'Policy/Extrinsic Reward')
    fwd = merge_scalars(eas, 'Losses/Curiosity Forward Loss')
    inv = merge_scalars(eas, 'Losses/Curiosity Inverse Loss')
    ent = merge_scalars(eas, 'Policy/Entropy')

    def tail_mean(items, n=50):
        if not items:
            return None
        return statistics.mean(v for _, v in items[-n:])

    def head_mean(items, n=50):
        if not items:
            return None
        return statistics.mean(v for _, v in items[:n])

    tc, hc = tail_mean(cur), head_mean(cur)
    te, he = tail_mean(ext), head_mean(ext)
    tf, hf = tail_mean(fwd), head_mean(fwd)
    ti, hi = tail_mean(inv), head_mean(inv)
    tn, hn = tail_mean(ent), head_mean(ent)

    def show(name, h, t):
        if h is None or t is None:
            print(f"  {name}: missing")
            return
        delta = t - h
        pct = (delta / abs(h) * 100) if abs(h) > 1e-9 else float('nan')
        print(f"  {name}: head={h:+.4f}  tail={t:+.4f}  delta={delta:+.4f}  ({pct:+.1f}%)")

    show("Curiosity reward    ", hc, tc)
    show("Extrinsic reward    ", he, te)
    show("Forward loss        ", hf, tf)
    show("Inverse loss        ", hi, ti)
    show("Entropy             ", hn, tn)

    if tc is not None and te is not None:
        ratio_h = (hc / he) if he and abs(he) > 1e-9 else float('inf')
        ratio_t = (tc / te) if te and abs(te) > 1e-9 else float('inf')
        print(f"\n  Curiosity / Extrinsic ratio:  head={ratio_h:+.5f}  tail={ratio_t:+.5f}")

    print("\n[EPISODE QUALITY]")
    quality_tags = [
        'CombatAgent/EpisodeKills',
        'CombatAgent/ClearedRoom',
        'CombatAgent/Died',
        'CombatAgent/EpisodeDamageDealt',
        'CombatAgent/EpisodeDamageTaken',
        'CombatAgent/BrokenEpisode',
        'CombatAgent/BrokenStuck',
        'CombatAgent/BrokenEnemyOutOfBounds',
        'CombatAgent/BrokenAgentOutOfBounds',
        'CombatAgent/BrokenInvalidAttack',
        'CombatAgent/AlignedAttackRate',
        'CombatAgent/InvalidAttackRate',
        'CombatAgent/TargetVisibleRate',
        'CombatAgent/UsefulDashRate',
        'CombatAgent/WastefulDashRate',
        'CombatAgent/CellsVisited',
        'CombatAgent/AgentStuckRecoveries',
        'CombatAgent/EnemyStuckRecoveries',
    ]
    for tag in quality_tags:
        items = merge_scalars(eas, tag)
        s = stats(items)
        print(f" {tag}")
        print(fmt(s))

if __name__ == '__main__':
    main()
