"""5-way final analysis: run33/34/35/36/38."""
from tensorboard.backend.event_processing import event_accumulator
import statistics

runs = {
    'run38_ICM_Cov_scratch': 'Assets/results/run38_clean_env_icm_coverage_v1/AttentionAgentConfig',
    'run36_RND_scratch':     'Assets/results/run36_clean_env_rnd_scratch_v1/AttentionAgentConfig',
    'run35_ICM_scratch':     'Assets/results/run35_clean_env_ppo_baseline_v1/AttentionAgentConfig',
    'run34_PPO_scratch':     'Assets/results/run34_clean_env_ppo_baseline_v1/AttentionAgentConfig',
    'run33_ICM_init25':      'Assets/results/run33_clean_env_icm_v1/AttentionAgentConfig',
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
    if tag not in ea.Tags()['scalars']: return None
    ev = ea.Scalars(tag)
    return statistics.mean(e.value for e in ev[-n:]) if ev else None
def first(ea, tag, n=10):
    if tag not in ea.Tags()['scalars']: return None
    ev = ea.Scalars(tag)
    return statistics.mean(e.value for e in ev[:n]) if ev else None
def maxv(ea, tag, max_step=None):
    if tag not in ea.Tags()['scalars']: return None,None
    ev = ea.Scalars(tag)
    if max_step: ev = [e for e in ev if e.step <= max_step]
    if not ev: return None,None
    m = max(ev, key=lambda e: e.value)
    return m.value, m.step
def window(ea, tag, lo, hi):
    if tag not in ea.Tags()['scalars']: return None
    ev = [e for e in ea.Scalars(tag) if lo <= e.step <= hi]
    return statistics.mean(e.value for e in ev) if ev else None
def last_step(ea):
    ev = ea.Scalars('Environment/Cumulative Reward')
    return ev[-1].step if ev else None


print('=== Last steps + verify CellsVisited ===')
for k in runs:
    has_cv = 'AttentionAgent/CellsVisited' in eas[k].Tags()['scalars']
    cv_tail = tail(eas[k], 'AttentionAgent/CellsVisited')
    cv_str = f'{cv_tail:.1f}' if cv_tail is not None else 'N/A'
    print(f'  {k:25s} last_step={last_step(eas[k]):>8d}  CellsVisited tail50={cv_str}')
print()

print('=== A. TAIL-50 final (5-way) ===')
hdr = f'{"metric":40s} {"run38_Cov":>10s} {"run36_RND":>10s} {"run35_ICM":>10s} {"run34_PPO":>10s} {"run33_i25":>10s}'
print(hdr); print('-'*len(hdr))
for m in metrics:
    vals=[]
    for k in ['run38_ICM_Cov_scratch','run36_RND_scratch','run35_ICM_scratch','run34_PPO_scratch','run33_ICM_init25']:
        v = tail(eas[k], m)
        vals.append(f'{v:>10.3f}' if v is not None else f'{"N/A":>10}')
    print(f'{m:40s} {" ".join(vals)}')

print()
print('=== B. KEY: Coverage vs ICM (cung scratch 1M) — KEY HYPOTHESIS ===')
print(f'{"metric":40s} {"run38_Cov":>10s} {"run35_ICM":>10s}  d(38-35)  pct')
print('-'*82)
for m in ['Environment/Cumulative Reward','AttentionAgent/EpisodeKills','AttentionAgent/ClearedRoom','AttentionAgent/EpisodeDamageDealt','AttentionAgent/EpisodeDamageTaken','AttentionAgent/Died','AttentionAgent/TargetVisibleRate','AttentionAgent/AlignedAttackRate','AttentionAgent/UsefulDashRate','AttentionAgent/CellsVisited','Policy/Entropy','Losses/Value Loss','Losses/Curiosity Inverse Loss']:
    v38 = tail(eas['run38_ICM_Cov_scratch'], m)
    v35 = tail(eas['run35_ICM_scratch'], m)
    if v38 is None or v35 is None: continue
    delta = v38 - v35
    pct = (delta/abs(v35)*100) if abs(v35) > 0.001 else 0
    print(f'{m:40s} {v38:>10.3f} {v35:>10.3f}  {delta:>+8.3f}  {pct:>+5.1f}%')

print()
print('=== C. Coverage vs RND ===')
print(f'{"metric":40s} {"run38_Cov":>10s} {"run36_RND":>10s}  d(38-36)  pct')
print('-'*82)
for m in ['Environment/Cumulative Reward','AttentionAgent/EpisodeKills','AttentionAgent/ClearedRoom','AttentionAgent/EpisodeDamageDealt','AttentionAgent/EpisodeDamageTaken','AttentionAgent/Died']:
    v38 = tail(eas['run38_ICM_Cov_scratch'], m)
    v36 = tail(eas['run36_RND_scratch'], m)
    if v38 is None or v36 is None: continue
    delta = v38 - v36
    pct = (delta/abs(v36)*100) if abs(v36) > 0.001 else 0
    print(f'{m:40s} {v38:>10.3f} {v36:>10.3f}  {delta:>+8.3f}  {pct:>+5.1f}%')

print()
print('=== D. Coverage vs PPO tran ===')
print(f'{"metric":40s} {"run38_Cov":>10s} {"run34_PPO":>10s}  d(38-34)  pct')
print('-'*82)
for m in ['Environment/Cumulative Reward','AttentionAgent/EpisodeKills','AttentionAgent/ClearedRoom','AttentionAgent/EpisodeDamageDealt','AttentionAgent/EpisodeDamageTaken','AttentionAgent/Died']:
    v38 = tail(eas['run38_ICM_Cov_scratch'], m)
    v34 = tail(eas['run34_PPO_scratch'], m)
    if v38 is None or v34 is None: continue
    delta = v38 - v34
    pct = (delta/abs(v34)*100) if abs(v34) > 0.001 else 0
    print(f'{m:40s} {v38:>10.3f} {v34:>10.3f}  {delta:>+8.3f}  {pct:>+5.1f}%')

print()
print('=== E. PEAK 0..1M ===')
print(f'{"metric":40s} {"run38":>22s} {"run36":>22s} {"run35":>22s} {"run34":>22s}')
for m in ['Environment/Cumulative Reward','AttentionAgent/EpisodeKills','AttentionAgent/ClearedRoom']:
    p38,s38 = maxv(eas['run38_ICM_Cov_scratch'], m)
    p36,s36 = maxv(eas['run36_RND_scratch'], m)
    p35,s35 = maxv(eas['run35_ICM_scratch'], m)
    p34,s34 = maxv(eas['run34_PPO_scratch'], m)
    fmt = lambda p,s: f'{p:>10.3f} ({s:>9d})' if p is not None else '             N/A'
    print(f'{m:40s} {fmt(p38,s38)} {fmt(p36,s36)} {fmt(p35,s35)} {fmt(p34,s34)}')

print()
print('=== F. WINDOW 0..1M mean ===')
hdr = f'{"metric":40s} {"run38_Cov":>10s} {"run36_RND":>10s} {"run35_ICM":>10s} {"run34_PPO":>10s} {"run33_i25":>10s}'
print(hdr); print('-'*len(hdr))
for m in ['Environment/Cumulative Reward','AttentionAgent/EpisodeKills','AttentionAgent/ClearedRoom','AttentionAgent/EpisodeDamageDealt','AttentionAgent/EpisodeDamageTaken','AttentionAgent/CellsVisited']:
    vals=[]
    for k in ['run38_ICM_Cov_scratch','run36_RND_scratch','run35_ICM_scratch','run34_PPO_scratch','run33_ICM_init25']:
        v = window(eas[k], m, 0, 1000000)
        vals.append(f'{v:>10.3f}' if v is not None else f'{"N/A":>10}')
    print(f'{m:40s} {" ".join(vals)}')

print()
print('=== G. run38 trend first10 vs tail50 ===')
for m in ['Environment/Cumulative Reward','AttentionAgent/EpisodeKills','AttentionAgent/ClearedRoom','AttentionAgent/CellsVisited','Policy/Entropy','Policy/Curiosity Reward','Losses/Curiosity Inverse Loss','Losses/Curiosity Forward Loss']:
    f10 = first(eas['run38_ICM_Cov_scratch'], m, 10)
    t50 = tail(eas['run38_ICM_Cov_scratch'], m, 50)
    if f10 is None or t50 is None: continue
    print(f'  {m:42s} first10={f10:7.3f}  tail50={t50:7.3f}  delta={t50-f10:+7.3f}')

print()
print('=== H. Coverage reward magnitude ===')
cv_tail = tail(eas['run38_ICM_Cov_scratch'], 'AttentionAgent/CellsVisited')
ext_tail = tail(eas['run38_ICM_Cov_scratch'], 'Environment/Cumulative Reward')
cur_tail = tail(eas['run38_ICM_Cov_scratch'], 'Policy/Curiosity Reward')
if cv_tail is not None and ext_tail is not None:
    cov_bonus = cv_tail * 0.005
    cov_ratio = cov_bonus / max(0.001, ext_tail) * 100
    print(f'  CellsVisited tail50:        {cv_tail:.1f} cells/episode')
    print(f'  Coverage bonus per episode: {cov_bonus:.3f} (= cells * 0.005)')
    print(f'  Extrinsic reward tail50:    {ext_tail:.3f}')
    print(f'  Coverage / extrinsic ratio: {cov_ratio:.1f}%')
    if cur_tail is not None:
        cur_ratio = cur_tail / max(0.001, ext_tail) * 100
        print(f'  Curiosity reward tail50:    {cur_tail:.3f}  ratio={cur_ratio:.1f}%')
        total_intrinsic_ratio = (cov_bonus + cur_tail) / max(0.001, ext_tail) * 100
        print(f'  Total intrinsic ratio:      {total_intrinsic_ratio:.1f}%  (Coverage + Curiosity)')

print()
print('=== I. Rolling 200k mean for run38 (compare with run35/36) ===')
def rolling(ea, tag, window=200000):
    ev = ea.Scalars(tag)
    out = []
    for e in ev:
        bucket = [x.value for x in ev if e.step - window <= x.step <= e.step]
        out.append((e.step, statistics.mean(bucket)))
    return out
key_steps = [100_000, 300_000, 500_000, 700_000, 1_000_000]
print(f'{"step":>10s}  {"run38_Cov":>10s} {"run36_RND":>10s} {"run35_ICM":>10s} {"run34_PPO":>10s}')
for s in key_steps:
    row = f'{s:>10d}'
    for k in ['run38_ICM_Cov_scratch','run36_RND_scratch','run35_ICM_scratch','run34_PPO_scratch']:
        roll = rolling(eas[k], 'Environment/Cumulative Reward')
        v = next((val for st,val in reversed(roll) if st <= s), None)
        row += f'  {v:>10.3f}' if v is not None else f'  {"N/A":>10}'
    print(row)
