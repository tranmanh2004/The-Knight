"""Run43 analysis: LSTM continue 2M (run42 -> run43)."""
from tensorboard.backend.event_processing import event_accumulator
import statistics

runs = {
    'run43_LSTM_cont':    'Assets/results/run43_clean_env_lstm_continue_2m_v1/AttentionAgentConfig',
    'run42_LSTM_clean':   'Assets/results/run42_clean_env_lstm_only_v2/AttentionAgentConfig',
    'run36_RND_scratch':  'Assets/results/run36_clean_env_rnd_scratch_v1/AttentionAgentConfig',
    'run35_ICM_scratch':  'Assets/results/run35_clean_env_ppo_baseline_v1/AttentionAgentConfig',
    'run34_PPO_scratch':  'Assets/results/run34_clean_env_ppo_baseline_v1/AttentionAgentConfig',
    'run33_ICM_init25':   'Assets/results/run33_clean_env_icm_v1/AttentionAgentConfig',
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

print('Status + sanity:')
for k in runs:
    cv = tail(eas[k], 'AttentionAgent/CellsVisited')
    cv_s = f'{cv:.1f}' if cv is not None else 'N/A'
    print(f'  {k:25s} last_step={last_step(eas[k]):>8d}  CellsVisited={cv_s}')
print()

print('=== TAIL-50 final ranking ===')
metrics = ['Environment/Cumulative Reward','AttentionAgent/EpisodeKills','AttentionAgent/ClearedRoom','AttentionAgent/Died','AttentionAgent/EpisodeDamageDealt','AttentionAgent/EpisodeDamageTaken','AttentionAgent/TargetVisibleRate','AttentionAgent/AlignedAttackRate','Policy/Entropy','Losses/Value Loss','Losses/Policy Loss','AttentionAgent/BrokenEpisode']
order = ['run43_LSTM_cont','run42_LSTM_clean','run36_RND_scratch','run35_ICM_scratch','run34_PPO_scratch','run33_ICM_init25']
hdr = f'{"metric":40s}'
for k in order:
    label = k.replace('run','r').replace('_scratch','').replace('_clean','').replace('_init25','_i25').replace('_LSTM','LSTM')[:11]
    hdr += f' {label:>11s}'
print(hdr); print('-'*len(hdr))
for m in metrics:
    row = f'{m:40s}'
    for k in order:
        v = tail(eas[k], m)
        row += f' {v:>11.3f}' if v is not None else f' {"-":>11s}'
    print(row)

print()
print('=== KEY: run43 (LSTM continue 2M) vs run42 (LSTM 1M) ===')
for m in ['Environment/Cumulative Reward','AttentionAgent/EpisodeKills','AttentionAgent/ClearedRoom','AttentionAgent/Died','AttentionAgent/EpisodeDamageDealt','AttentionAgent/EpisodeDamageTaken','Policy/Entropy','Losses/Value Loss','Losses/Policy Loss']:
    v43 = tail(eas['run43_LSTM_cont'], m)
    v42 = tail(eas['run42_LSTM_clean'], m)
    if v43 is None or v42 is None: continue
    delta = v43 - v42
    pct = (delta/abs(v42)*100) if abs(v42) > 0.001 else 0
    print(f'  {m:40s} r43={v43:7.3f}  r42={v42:7.3f}  d={delta:+7.3f}  pct={pct:+5.1f}%')

print()
print('=== KEY: run43 vs RND best ===')
for m in ['Environment/Cumulative Reward','AttentionAgent/EpisodeKills','AttentionAgent/ClearedRoom','AttentionAgent/Died','AttentionAgent/EpisodeDamageTaken']:
    v43 = tail(eas['run43_LSTM_cont'], m)
    v36 = tail(eas['run36_RND_scratch'], m)
    if v43 is None or v36 is None: continue
    delta = v43 - v36
    pct = (delta/abs(v36)*100) if abs(v36) > 0.001 else 0
    print(f'  {m:40s} r43={v43:7.3f}  r36={v36:7.3f}  d={delta:+7.3f}  pct={pct:+5.1f}%')

print()
print('=== run43 trend first10 vs tail50 ===')
for m in ['Environment/Cumulative Reward','AttentionAgent/EpisodeKills','AttentionAgent/ClearedRoom','AttentionAgent/Died','Policy/Entropy','Losses/Value Loss','Losses/Policy Loss']:
    f = first(eas['run43_LSTM_cont'], m, 10)
    t = tail(eas['run43_LSTM_cont'], m, 50)
    if f is None or t is None: continue
    print(f'  {m:42s} first10={f:7.3f}  tail50={t:7.3f}  delta={t-f:+7.3f}')

print()
print('=== Peak run43 vs others ===')
for m in ['Environment/Cumulative Reward','AttentionAgent/EpisodeKills','AttentionAgent/ClearedRoom']:
    print(f'  {m}:')
    for k in ['run43_LSTM_cont','run42_LSTM_clean','run36_RND_scratch','run35_ICM_scratch','run34_PPO_scratch','run33_ICM_init25']:
        p,s = maxv(eas[k], m)
        if p is not None:
            print(f'    {k:25s} peak={p:.3f} @ step {s}')

print()
print('=== Effective 2M curve: run42 (0..1M) + run43 (1M..2M) ===')
print('Reward at key effective steps:')
ev42 = eas['run42_LSTM_clean'].Scalars('Environment/Cumulative Reward')
ev43 = eas['run43_LSTM_cont'].Scalars('Environment/Cumulative Reward')
def at(ev, target):
    if not ev: return None
    return min(ev, key=lambda e: abs(e.step - target)).value
def roll(ev, step, w=200000):
    bucket = [e.value for e in ev if step - w <= e.step <= step]
    return statistics.mean(bucket) if bucket else None
print(f'  run42 step 100k:  R = {at(ev42, 100000):.3f}  rolling200k = {roll(ev42, 100000):.3f}')
print(f'  run42 step 500k:  R = {at(ev42, 500000):.3f}  rolling200k = {roll(ev42, 500000):.3f}')
print(f'  run42 step 1000k: R = {at(ev42, 1000000):.3f}  rolling200k = {roll(ev42, 1000000):.3f}')
print(f'  run43 step 100k:  R = {at(ev43, 100000):.3f}  rolling200k = {roll(ev43, 100000):.3f}  (eff 1.1M)')
print(f'  run43 step 500k:  R = {at(ev43, 500000):.3f}  rolling200k = {roll(ev43, 500000):.3f}  (eff 1.5M)')
print(f'  run43 step 1000k: R = {at(ev43, 1000000):.3f}  rolling200k = {roll(ev43, 1000000):.3f}  (eff 2M)')

print()
print('=== Final ranking ALL runs (tail50) ===')
results = []
for k in order:
    r = tail(eas[k], 'Environment/Cumulative Reward')
    results.append((k, r, last_step(eas[k])))
results.sort(key=lambda x: -(x[1] or -999))
for i, (k, r, s) in enumerate(results, 1):
    print(f'  {i}. {k:25s} R={r:.3f}  step={s}')
