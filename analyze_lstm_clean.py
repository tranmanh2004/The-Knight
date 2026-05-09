"""Run42 LSTM clean (no Coverage) — final analysis vs all baselines."""
from tensorboard.backend.event_processing import event_accumulator
import statistics

runs = {
    'run42_LSTM_clean':   'Assets/results/run42_clean_env_lstm_only_v2/AttentionAgentConfig',
    'run41_LSTM_Cov':     'Assets/results/run41_clean_env_lstm_only_v1/AttentionAgentConfig',
    'run40_Cov_only':     'Assets/results/run40_clean_env_coverage_only_v1/AttentionAgentConfig',
    'run38_ICM_Cov':      'Assets/results/run38_clean_env_icm_coverage_v1/AttentionAgentConfig',
    'run36_RND_scratch':  'Assets/results/run36_clean_env_rnd_scratch_v1/AttentionAgentConfig',
    'run35_ICM_scratch':  'Assets/results/run35_clean_env_ppo_baseline_v1/AttentionAgentConfig',
    'run34_PPO_scratch':  'Assets/results/run34_clean_env_ppo_baseline_v1/AttentionAgentConfig',
}
eas = {k: event_accumulator.EventAccumulator(p, size_guidance={'scalars':0}) for k,p in runs.items()}
for ea in eas.values(): ea.Reload()

def tail(ea, tag, n=50):
    if tag not in ea.Tags()['scalars']: return None
    ev = ea.Scalars(tag)
    return statistics.mean(e.value for e in ev[-n:]) if ev else None
def first(ea, tag, n=10):
    if tag not in ea.Tags()['scalars']: return None
    ev = ea.Scalars(tag)
    return statistics.mean(e.value for e in ev[:n]) if ev else None
def maxv(ea, tag):
    if tag not in ea.Tags()['scalars']: return None,None
    ev = ea.Scalars(tag)
    if not ev: return None,None
    m = max(ev, key=lambda e: e.value)
    return m.value, m.step
def last_step(ea):
    ev = ea.Scalars('Environment/Cumulative Reward')
    return ev[-1].step if ev else None

print('Status + Coverage check:')
for k in runs:
    cv = tail(eas[k], 'AttentionAgent/CellsVisited')
    cv_s = f'{cv:.1f}' if cv is not None else 'N/A'
    print(f'  {k:25s} last_step={last_step(eas[k]):>8d}  CellsVisited={cv_s}')
print()

print('=== TAIL-50 final 7 runs ===')
metrics = ['Environment/Cumulative Reward','AttentionAgent/EpisodeKills','AttentionAgent/ClearedRoom','AttentionAgent/Died','AttentionAgent/EpisodeDamageDealt','AttentionAgent/EpisodeDamageTaken','AttentionAgent/TargetVisibleRate','AttentionAgent/AlignedAttackRate','Policy/Entropy','Losses/Value Loss','Losses/Policy Loss','AttentionAgent/BrokenEpisode']
order = ['run42_LSTM_clean','run41_LSTM_Cov','run40_Cov_only','run38_ICM_Cov','run36_RND_scratch','run35_ICM_scratch','run34_PPO_scratch']
hdr = f'{"metric":35s}'
for k in order:
    label = k.replace('run','r').replace('_scratch','').replace('_clean','')[:9]
    hdr += f' {label:>10s}'
print(hdr); print('-'*len(hdr))
for m in metrics:
    row = f'{m:35s}'
    for k in order:
        v = tail(eas[k], m)
        row += f' {v:>10.3f}' if v is not None else f' {"-":>10s}'
    print(row)

print()
print('=== KEY: LSTM clean vs PPO tran (run42 vs run34) ===')
for m in ['Environment/Cumulative Reward','AttentionAgent/EpisodeKills','AttentionAgent/ClearedRoom','AttentionAgent/Died','AttentionAgent/EpisodeDamageTaken','AttentionAgent/AlignedAttackRate']:
    v42 = tail(eas['run42_LSTM_clean'], m)
    v34 = tail(eas['run34_PPO_scratch'], m)
    if v42 is None or v34 is None: continue
    delta = v42 - v34
    pct = (delta/abs(v34)*100) if abs(v34) > 0.001 else 0
    print(f'  {m:40s} r42={v42:7.3f}  r34={v34:7.3f}  d={delta:+7.3f}  pct={pct:+5.1f}%')

print()
print('=== KEY: LSTM clean vs RND best (run42 vs run36) ===')
for m in ['Environment/Cumulative Reward','AttentionAgent/EpisodeKills','AttentionAgent/Died','AttentionAgent/EpisodeDamageTaken']:
    v42 = tail(eas['run42_LSTM_clean'], m)
    v36 = tail(eas['run36_RND_scratch'], m)
    if v42 is None or v36 is None: continue
    delta = v42 - v36
    pct = (delta/abs(v36)*100) if abs(v36) > 0.001 else 0
    print(f'  {m:40s} r42={v42:7.3f}  r36={v36:7.3f}  d={delta:+7.3f}  pct={pct:+5.1f}%')

print()
print('=== KEY: LSTM clean vs LSTM+Cov (run42 vs run41) — tach Coverage trap ===')
for m in ['Environment/Cumulative Reward','AttentionAgent/EpisodeKills','AttentionAgent/Died','Losses/Value Loss','Losses/Policy Loss']:
    v42 = tail(eas['run42_LSTM_clean'], m)
    v41 = tail(eas['run41_LSTM_Cov'], m)
    if v42 is None or v41 is None: continue
    delta = v42 - v41
    pct = (delta/abs(v41)*100) if abs(v41) > 0.001 else 0
    print(f'  {m:40s} r42={v42:7.3f}  r41={v41:7.3f}  d={delta:+7.3f}  pct={pct:+5.1f}%')

print()
print('=== run42 trend first10 vs tail50 ===')
for m in ['Environment/Cumulative Reward','AttentionAgent/EpisodeKills','AttentionAgent/ClearedRoom','AttentionAgent/Died','Policy/Entropy','Losses/Value Loss','Losses/Policy Loss']:
    f = first(eas['run42_LSTM_clean'], m, 10)
    t = tail(eas['run42_LSTM_clean'], m, 50)
    if f is None or t is None: continue
    print(f'  {m:42s} first10={f:7.3f}  tail50={t:7.3f}  delta={t-f:+7.3f}')

print()
print('=== Peak comparison ===')
for m in ['Environment/Cumulative Reward','AttentionAgent/EpisodeKills','AttentionAgent/ClearedRoom']:
    print(f'  {m}:')
    for k in order:
        p,s = maxv(eas[k], m)
        if p is not None:
            print(f'    {k:25s} peak={p:.3f} @ step {s}')

print()
print('=== Final ranking (tail50 reward) ===')
results = []
for k in order:
    r = tail(eas[k], 'Environment/Cumulative Reward')
    results.append((k, r))
results.sort(key=lambda x: -(x[1] or -999))
for i, (k, r) in enumerate(results, 1):
    print(f'  {i}. {k:25s} R={r:.3f}')
