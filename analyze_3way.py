from tensorboard.backend.event_processing import event_accumulator
import statistics

runs = {
    'run36_RND_scratch':  'Assets/results/run36_clean_env_rnd_scratch_v1/AttentionAgentConfig',
    'run35_ICM_scratch':  'Assets/results/run35_clean_env_ppo_baseline_v1/AttentionAgentConfig',
    'run34_PPO_scratch':  'Assets/results/run34_clean_env_ppo_baseline_v1/AttentionAgentConfig',
    'run33_ICM_init25':   'Assets/results/run33_clean_env_icm_v1/AttentionAgentConfig',
}
metrics = [
    'Environment/Cumulative Reward',
    'Policy/Curiosity Reward',
    'Policy/Rnd Reward',
    'Losses/Curiosity Forward Loss',
    'Losses/Curiosity Inverse Loss',
    'Losses/Rnd Loss',
    'Policy/Entropy',
    'Losses/Policy Loss',
    'Losses/Value Loss',
    'AttentionAgent/EpisodeKills',
    'AttentionAgent/ClearedRoom',
    'AttentionAgent/Died',
    'AttentionAgent/EpisodeDamageDealt',
    'AttentionAgent/EpisodeDamageTaken',
    'AttentionAgent/TargetVisibleRate',
    'AttentionAgent/AlignedAttackRate',
    'AttentionAgent/UsefulDashRate',
    'AttentionAgent/EnemyStuckRecoveries',
    'AttentionAgent/AgentStuckRecoveries',
    'AttentionAgent/BrokenEpisode',
]

eas = {k: event_accumulator.EventAccumulator(p, size_guidance={'scalars': 0}) for k, p in runs.items()}
for ea in eas.values():
    ea.Reload()

print('Tags in run36 (intrinsic-related):')
for t in eas['run36_RND_scratch'].Tags()['scalars']:
    if any(s in t.lower() for s in ['rnd', 'curiosity', 'intrinsic']):
        print(f'  {t}')
print()


def tail(ea, tag, n=50):
    if tag not in ea.Tags()['scalars']:
        return None
    ev = ea.Scalars(tag)
    return statistics.mean(e.value for e in ev[-n:]) if ev else None


def first(ea, tag, n=10):
    if tag not in ea.Tags()['scalars']:
        return None
    ev = ea.Scalars(tag)
    return statistics.mean(e.value for e in ev[:n]) if ev else None


def maxv(ea, tag, max_step=None):
    if tag not in ea.Tags()['scalars']:
        return None, None
    ev = ea.Scalars(tag)
    if max_step:
        ev = [e for e in ev if e.step <= max_step]
    if not ev:
        return None, None
    m = max(ev, key=lambda e: e.value)
    return m.value, m.step


def window(ea, tag, lo, hi):
    if tag not in ea.Tags()['scalars']:
        return None
    ev = [e for e in ea.Scalars(tag) if lo <= e.step <= hi]
    return statistics.mean(e.value for e in ev) if ev else None


def last_step(ea):
    ev = ea.Scalars('Environment/Cumulative Reward')
    return ev[-1].step if ev else None


print('Last step:')
for k in runs:
    print(f'  {k:25s} = {last_step(eas[k])}')
print()

print('=== A. TAIL-50 final ===')
hdr = f'{"metric":40s} {"run36_RND":>10s} {"run35_ICM":>10s} {"run34_PPO":>10s} {"run33_ICM25":>12s}'
print(hdr)
print('-' * len(hdr))
for m in metrics:
    v36 = tail(eas['run36_RND_scratch'], m)
    v35 = tail(eas['run35_ICM_scratch'], m)
    v34 = tail(eas['run34_PPO_scratch'], m)
    v33 = tail(eas['run33_ICM_init25'], m)
    fmt = lambda v, w=10: f'{v:>{w}.3f}' if v is not None else f'{"N/A":>{w}}'
    print(f'{m:40s} {fmt(v36)} {fmt(v35)} {fmt(v34)} {fmt(v33,12)}')

print()
print('=== B. RND vs ICM (cung scratch) — KEY HYPOTHESIS TEST ===')
print(f'{"metric":40s} {"run36_RND":>10s} {"run35_ICM":>10s}  d(36-35)  pct')
print('-' * 82)
for m in ['Environment/Cumulative Reward', 'AttentionAgent/EpisodeKills',
          'AttentionAgent/ClearedRoom', 'AttentionAgent/EpisodeDamageDealt',
          'AttentionAgent/EpisodeDamageTaken', 'AttentionAgent/Died',
          'AttentionAgent/TargetVisibleRate', 'AttentionAgent/AlignedAttackRate',
          'AttentionAgent/UsefulDashRate', 'Policy/Entropy', 'Losses/Value Loss']:
    v36 = tail(eas['run36_RND_scratch'], m)
    v35 = tail(eas['run35_ICM_scratch'], m)
    if v36 is None or v35 is None:
        continue
    delta = v36 - v35
    pct = (delta / v35 * 100) if abs(v35) > 0.001 else 0
    print(f'{m:40s} {v36:>10.3f} {v35:>10.3f}  {delta:>+8.3f}  {pct:>+5.1f}%')

print()
print('=== C. RND vs PPO tran ===')
print(f'{"metric":40s} {"run36_RND":>10s} {"run34_PPO":>10s}  d(36-34)  pct')
print('-' * 82)
for m in ['Environment/Cumulative Reward', 'AttentionAgent/EpisodeKills',
          'AttentionAgent/ClearedRoom', 'AttentionAgent/EpisodeDamageDealt',
          'AttentionAgent/EpisodeDamageTaken', 'AttentionAgent/Died',
          'AttentionAgent/TargetVisibleRate']:
    v36 = tail(eas['run36_RND_scratch'], m)
    v34 = tail(eas['run34_PPO_scratch'], m)
    if v36 is None or v34 is None:
        continue
    delta = v36 - v34
    pct = (delta / abs(v34) * 100) if abs(v34) > 0.001 else 0
    print(f'{m:40s} {v36:>10.3f} {v34:>10.3f}  {delta:>+8.3f}  {pct:>+5.1f}%')

print()
print('=== D. PEAK 0..1M ===')
print(f'{"metric":40s} {"run36_peak":>14s} (step) {"run35_peak":>14s} (step) {"run34_peak":>14s} (step)')
print('-' * 120)
for m in ['Environment/Cumulative Reward', 'AttentionAgent/EpisodeKills',
          'AttentionAgent/ClearedRoom']:
    p36, s36 = maxv(eas['run36_RND_scratch'], m)
    p35, s35 = maxv(eas['run35_ICM_scratch'], m)
    p34, s34 = maxv(eas['run34_PPO_scratch'], m)
    fmt = lambda p, s: f'{p:>14.3f} ({s:>9d})' if p is not None else '             N/A'
    print(f'{m:40s} {fmt(p36,s36)} {fmt(p35,s35)} {fmt(p34,s34)}')

print()
print('=== E. WINDOW 0..1M mean ===')
hdr = f'{"metric":40s} {"run36_RND":>10s} {"run35_ICM":>10s} {"run34_PPO":>10s} {"run33_ICM25":>12s}'
print(hdr)
print('-' * len(hdr))
for m in ['Environment/Cumulative Reward', 'AttentionAgent/EpisodeKills',
          'AttentionAgent/ClearedRoom', 'AttentionAgent/EpisodeDamageDealt',
          'AttentionAgent/EpisodeDamageTaken', 'Policy/Entropy']:
    v36 = window(eas['run36_RND_scratch'], m, 0, 1000000)
    v35 = window(eas['run35_ICM_scratch'], m, 0, 1000000)
    v34 = window(eas['run34_PPO_scratch'], m, 0, 1000000)
    v33 = window(eas['run33_ICM_init25'], m, 0, 1000000)
    fmt = lambda v, w=10: f'{v:>{w}.3f}' if v is not None else f'{"N/A":>{w}}'
    print(f'{m:40s} {fmt(v36)} {fmt(v35)} {fmt(v34)} {fmt(v33,12)}')

print()
print('=== F. run36 trend first10 vs tail50 ===')
for m in ['Environment/Cumulative Reward', 'AttentionAgent/EpisodeKills',
          'AttentionAgent/ClearedRoom', 'Policy/Entropy',
          'Policy/Rnd Reward', 'Losses/Rnd Loss', 'Losses/Value Loss']:
    f10 = first(eas['run36_RND_scratch'], m, 10)
    t50 = tail(eas['run36_RND_scratch'], m, 50)
    if f10 is None or t50 is None:
        continue
    print(f'  {m:42s} first10={f10:7.3f}  tail50={t50:7.3f}  delta={t50-f10:+7.3f}')

print()
print('=== G. Intrinsic reward magnitude (cung strength 0.01) ===')
for tag, k in [('Policy/Rnd Reward', 'run36_RND_scratch'),
               ('Policy/Curiosity Reward', 'run35_ICM_scratch'),
               ('Policy/Curiosity Reward', 'run33_ICM_init25')]:
    v = tail(eas[k], tag)
    e_tail = tail(eas[k], 'Environment/Cumulative Reward')
    if v is not None and e_tail is not None:
        ratio = v / max(0.001, e_tail) * 100
        print(f'  {k:25s} {tag:25s} tail={v:.3f}  extrinsic={e_tail:.3f}  ratio={ratio:.1f}%')
