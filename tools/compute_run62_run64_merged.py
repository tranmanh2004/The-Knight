"""Tinh tail-50 + max cho run62 + run64 ghep, tra so lieu cap nhat cho bao cao."""
import io
import sys
import os
import glob
import statistics
from tensorboard.backend.event_processing import event_accumulator

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

# Tim ca 2 thu muc run62 va run64
RUN62_DIR = r"Assets/results/run62_local_grid_aggressive"
RUN64_DIR = r"Assets/results/run64_continue_from_run62_24M/CombatAgentConfig"

print(f"[INFO] run62 dir = {RUN62_DIR}")
print(f"[INFO] run64 dir = {RUN64_DIR}")

def find_tfevents_dir(run_dir):
    for sub in os.listdir(run_dir):
        full = os.path.join(run_dir, sub)
        if os.path.isdir(full):
            files = glob.glob(os.path.join(full, "events.out.tfevents.*"))
            files = [f for f in files if not f.endswith('.meta')]
            if files:
                return full
    return run_dir

def load_ea_list(run_dir):
    sub = find_tfevents_dir(run_dir)
    files = sorted(glob.glob(os.path.join(sub, "events.out.tfevents.*")))
    files = [f for f in files if not f.endswith('.meta')]
    eas = []
    for f in files:
        ea = event_accumulator.EventAccumulator(f, size_guidance={'scalars': 0})
        ea.Reload()
        eas.append(ea)
    return eas

def merge_scalar(eas, tag, step_offset=0):
    pts = []
    for ea in eas:
        if tag in ea.Tags()['scalars']:
            for e in ea.Scalars(tag):
                pts.append((e.step + step_offset, e.value))
    pts.sort(key=lambda p: p[0])
    return pts

def stats(pts, tail=50):
    if not pts:
        return None
    vals = [v for _, v in pts]
    return {
        'n': len(pts),
        'step_min': pts[0][0],
        'step_max': pts[-1][0],
        'tail_mean': statistics.mean(vals[-tail:]) if len(vals) >= tail else statistics.mean(vals),
        'max': max(vals),
        'min': min(vals),
    }

if RUN62_DIR:
    print(f"\n[INFO] Loading run62 tfevents...")
    eas62 = load_ea_list(RUN62_DIR)
    print(f"  {len(eas62)} files loaded")

print(f"\n[INFO] Loading run64 tfevents...")
eas64 = load_ea_list(RUN64_DIR)
print(f"  {len(eas64)} files loaded")

# Run62 last step (de offset run64)
run62_last = 0
if RUN62_DIR:
    s_r62 = merge_scalar(eas62, 'Environment/Cumulative Reward')
    if s_r62:
        run62_last = s_r62[-1][0]
        print(f"  run62 last step = {run62_last:,}")

# Run64 internal range
s_r64 = merge_scalar(eas64, 'Environment/Cumulative Reward')
print(f"  run64 internal step range = [{s_r64[0][0]:,} .. {s_r64[-1][0]:,}]")
print(f"  run64 total steps = {s_r64[-1][0]:,}")

print(f"\n[INFO] Combined total steps = run62({run62_last:,}) + run64({s_r64[-1][0]:,}) = {run62_last + s_r64[-1][0]:,}")

# Bay gio gop run62 + run64 (offset run64 step + run62_last)
tags = [
    'Environment/Cumulative Reward',
    'CombatAgent/EpisodeKills',
    'CombatAgent/ClearedRoom',
    'CombatAgent/Died',
    'CombatAgent/EpisodeDamageDealt',
    'CombatAgent/EpisodeDamageTaken',
    'CombatAgent/UsefulDashRate',
    'CombatAgent/InvalidAttackRate',
    'CombatAgent/AlignedAttackRate',
    'CombatAgent/TargetVisibleRate',
    'Policy/Entropy',
    'Policy/Extrinsic Reward',
    'Policy/Curiosity Reward',
    'Losses/Curiosity Forward Loss',
    'Losses/Curiosity Inverse Loss',
    'Losses/Policy Loss',
    'Losses/Value Loss',
    'Policy/Extrinsic Value Estimate',
    'Environment/Episode Length',
]

print(f"\n{'='*100}")
print(f" RUN62 + RUN64 MERGED (chuoi lien tuc) - tail-50 + max")
print(f"{'='*100}")
print(f"{'tag':<45s} {'tail50':>10s} {'max':>10s} {'min':>10s} {'n':>5s}")
print("-" * 100)

merged_results = {}
for tag in tags:
    pts_combined = []
    if RUN62_DIR:
        pts_combined.extend(merge_scalar(eas62, tag, 0))
    pts_combined.extend(merge_scalar(eas64, tag, run62_last))
    pts_combined.sort(key=lambda p: p[0])
    s = stats(pts_combined)
    if s:
        merged_results[tag] = s
        print(f"{tag:<45s} {s['tail_mean']:>10.4f} {s['max']:>10.4f} {s['min']:>10.4f} {s['n']:>5d}")

print(f"\n[CHO BAO CAO] So sanh voi so cu trong Chap4 (33.45M, reward 14.105, cleared 0.626, kills 1.530, entropy 3.099):")
print("-" * 100)
def show(name, tag, fmt=".3f"):
    s = merged_results.get(tag)
    if s:
        print(f"  {name:<35s} CU: ?  -> MOI: tail50={s['tail_mean']:{fmt}}  max={s['max']:{fmt}}")

show("Total steps",                'Environment/Cumulative Reward')
show("Reward (cum)",               'Environment/Cumulative Reward')
show("Cleared room",               'CombatAgent/ClearedRoom')
show("Kills",                      'CombatAgent/EpisodeKills')
show("Damage dealt",               'CombatAgent/EpisodeDamageDealt')
show("Damage taken",               'CombatAgent/EpisodeDamageTaken')
show("Useful dash rate",           'CombatAgent/UsefulDashRate')
show("Entropy",                    'Policy/Entropy')
show("Curiosity inverse loss",     'Losses/Curiosity Inverse Loss')
show("Policy loss",                'Losses/Policy Loss')
show("Value loss",                 'Losses/Value Loss')
show("Extrinsic value estimate",   'Policy/Extrinsic Value Estimate')
