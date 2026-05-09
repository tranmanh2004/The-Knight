"""5-way comparison: run33, run34, run35, run36, run37."""
from tensorboard.backend.event_processing import event_accumulator
import statistics

runs = {
    'run37_ICM_Cov_scr':   'Assets/results/run37_clean_env_icm_coverage_v1/AttentionAgentConfig',
    'run36_RND_scratch':   'Assets/results/run36_clean_env_rnd_scratch_v1/AttentionAgentConfig',
    'run35_ICM_scratch':   'Assets/results/run35_clean_env_ppo_baseline_v1/AttentionAgentConfig',
    'run34_PPO_scratch':   'Assets/results/run34_clean_env_ppo_baseline_v1/AttentionAgentConfig',
    'run33_ICM_init25':    'Assets/results/run33_clean_env_icm_v1/AttentionAgentConfig',
}
metrics = [
    'Environment/Cumulative Reward',
    'Policy/Curiosity Reward',
    'Policy/Rnd Reward',
    'Losses/Curiosity Forward Loss',
    'Losses/Curiosity Inverse Loss',
    'Policy/Entropy',
    'Losses/Value Loss',
    'AttentionAgent/EpisodeKills',
    'AttentionAgent/ClearedRoom',
    'AttentionAgent/Died',
    'AttentionAgent/EpisodeDamageDealt',
    'AttentionAgent/EpisodeDamageTaken',
    'AttentionAgent/TargetVisibleRate',
    'AttentionAgent/AlignedAttackRate',
    'AttentionAgent/UsefulDashRate',
    'AttentionAgent/CellsVisited',
    'AttentionAgent/EnemyStuckRecoveries',
    'AttentionAgent/AgentStuckRecoveries',
    'AttentionAgent/BrokenEpisode',
]
eas = {k: event_accumulator.EventAccumulator(p, size_guidance={'scalars': 0}) for k, p in runs.items()}
for ea in eas.values():
    ea.Reload()


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


print('=== Last steps + CellsVisited tag verify ===')
for k in runs:
    has_cv = 'AttentionAgent/CellsVisited' in eas[k].Tags()['scalars']
    print(f'  {k:25s} last_step={last_step(eas[k]):>8d}  has_CellsVisited={has_cv}')
print()

print('=== A. TAIL-50 final (5-way) ===')
hdr = f'{"metric":40s} {"run37_Cov":>10s} {"run36_RND":>10s} {"run35_ICM":>10s} {"run34_PPO":>10s} {"run33_i25":>10s}'
print(hdr)
print('-' * len(hdr))
for m in metrics:
    vals = []
    for k in ['run37_ICM_Cov_scr', 'run36_RND_scratch', 'run35_ICM_scratch', 'run34_PPO_scratch', 'run33_ICM_init25']:
        v = tail(eas[k], m)
        vals.append(f'{v:>10.3f}' if v is not None else f'{"N/A":>10}')
    print(f'{m:40s} {" ".join(vals)}')

print()
print('=== B. KEY: Coverage vs ICM (cung scratch 1M, khac block coverage) ===')
print(f'{"metric":40s} {"run37_Cov":>10s} {"run35_ICM":>10s}  d(37-35)  pct')
print('-' * 82)
for m in ['Environment/Cumulative Reward', 'AttentionAgent/EpisodeKills',
          'AttentionAgent/ClearedRoom', 'AttentionAgent/EpisodeDamageDealt',
          'AttentionAgent/EpisodeDamageTaken', 'AttentionAgent/Died',
          'AttentionAgent/TargetVisibleRate', 'AttentionAgent/AlignedAttackRate',
          'AttentionAgent/UsefulDashRate', 'AttentionAgent/CellsVisited',
          'Policy/Entropy', 'Losses/Value Loss']:
    v37 = tail(eas['run37_ICM_Cov_scr'], m)
    v35 = tail(eas['run35_ICM_scratch'], m)
    if v37 is None or v35 is None:
        continue
    delta = v37 - v35
    pct = (delta / abs(v35) * 100) if abs(v35) > 0.001 else 0
    print(f'{m:40s} {v37:>10.3f} {v35:>10.3f}  {delta:>+8.3f}  {pct:>+5.1f}%')

print()
print('=== C. Coverage vs PPO tran ===')
print(f'{"metric":40s} {"run37_Cov":>10s} {"run34_PPO":>10s}  d(37-34)  pct')
print('-' * 82)
for m in ['Environment/Cumulative Reward', 'AttentionAgent/EpisodeKills',
          'AttentionAgent/ClearedRoom', 'AttentionAgent/EpisodeDamageDealt',
          'AttentionAgent/EpisodeDamageTaken', 'AttentionAgent/Died',
          'AttentionAgent/TargetVisibleRate']:
    v37 = tail(eas['run37_ICM_Cov_scr'], m)
    v34 = tail(eas['run34_PPO_scratch'], m)
    if v37 is None or v34 is None:
        continue
    delta = v37 - v34
    pct = (delta / abs(v34) * 100) if abs(v34) > 0.001 else 0
    print(f'{m:40s} {v37:>10.3f} {v34:>10.3f}  {delta:>+8.3f}  {pct:>+5.1f}%')

print()
print('=== D. PEAK 0..1M (5-way) ===')
print(f'{"metric":40s} {"run37":>14s}        {"run36":>14s}        {"run35":>14s}        {"run34":>14s}')
for m in ['Environment/Cumulative Reward', 'AttentionAgent/EpisodeKills', 'AttentionAgent/ClearedRoom']:
    p37, s37 = maxv(eas['run37_ICM_Cov_scr'], m)
    p36, s36 = maxv(eas['run36_RND_scratch'], m)
    p35, s35 = maxv(eas['run35_ICM_scratch'], m)
    p34, s34 = maxv(eas['run34_PPO_scratch'], m)
    fmt = lambda p, s: f'{p:>8.3f} ({s:>6d})' if p is not None else '             N/A'
    print(f'{m:40s} {fmt(p37,s37)}  {fmt(p36,s36)}  {fmt(p35,s35)}  {fmt(p34,s34)}')

print()
print('=== E. WINDOW 0..1M mean (apples-to-apples 5-way) ===')
hdr = f'{"metric":40s} {"run37_Cov":>10s} {"run36_RND":>10s} {"run35_ICM":>10s} {"run34_PPO":>10s} {"run33_i25":>10s}'
print(hdr)
print('-' * len(hdr))
for m in ['Environment/Cumulative Reward', 'AttentionAgent/EpisodeKills',
          'AttentionAgent/ClearedRoom', 'AttentionAgent/EpisodeDamageDealt',
          'AttentionAgent/EpisodeDamageTaken', 'AttentionAgent/CellsVisited',
          'Policy/Entropy']:
    vals = []
    for k in ['run37_ICM_Cov_scr', 'run36_RND_scratch', 'run35_ICM_scratch', 'run34_PPO_scratch', 'run33_ICM_init25']:
        v = window(eas[k], m, 0, 1000000)
        vals.append(f'{v:>10.3f}' if v is not None else f'{"N/A":>10}')
    print(f'{m:40s} {" ".join(vals)}')

print()
print('=== F. run37 trend first10 vs tail50 ===')
for m in ['Environment/Cumulative Reward', 'AttentionAgent/EpisodeKills',
          'AttentionAgent/ClearedRoom', 'AttentionAgent/CellsVisited',
          'Policy/Entropy', 'Policy/Curiosity Reward', 'Losses/Curiosity Inverse Loss']:
    f10 = first(eas['run37_ICM_Cov_scr'], m, 10)
    t50 = tail(eas['run37_ICM_Cov_scr'], m, 50)
    if f10 is None or t50 is None:
        continue
    print(f'  {m:42s} first10={f10:7.3f}  tail50={t50:7.3f}  delta={t50-f10:+7.3f}')

print()
print('=== G. Coverage reward effective contribution ===')
# CellsVisited × 0.005 = coverage bonus per episode
cv37_tail = tail(eas['run37_ICM_Cov_scr'], 'AttentionAgent/CellsVisited')
ext37_tail = tail(eas['run37_ICM_Cov_scr'], 'Environment/Cumulative Reward')
cur37_tail = tail(eas['run37_ICM_Cov_scr'], 'Policy/Curiosity Reward')
if cv37_tail is not None and ext37_tail is not None:
    coverage_bonus = cv37_tail * 0.005
    coverage_ratio = coverage_bonus / max(0.001, ext37_tail) * 100
    print(f'  CellsVisited tail50:        {cv37_tail:.1f} cells/episode')
    print(f'  Coverage bonus per episode: {coverage_bonus:.3f} (= cells x 0.005)')
    print(f'  Extrinsic reward tail50:    {ext37_tail:.3f}')
    print(f'  Coverage ratio:             {coverage_ratio:.1f}%')
    if cur37_tail is not None:
        cur_ratio = cur37_tail / max(0.001, ext37_tail) * 100
        print(f'  Curiosity reward tail50:    {cur37_tail:.3f}  ratio={cur_ratio:.1f}%')
