"""Extract thesis metrics from Unity ML-Agents TensorBoard event files.

The script intentionally depends only on TensorBoard and the Python standard
library so it can run in the current training environment without matplotlib.
It writes compact CSV files used by Chapter 4.
"""
from __future__ import annotations

import csv
import statistics
from pathlib import Path

from tensorboard.backend.event_processing import event_accumulator


ROOT = Path(__file__).resolve().parents[2]
OUT_DIR = ROOT / "Dự_án_công_nghệ" / "data"
WINDOW_STEPS = 200_000

RUNS = {
    "run34_ppo": {
        "label": "PPO baseline",
        "path": "Assets/results/run34_clean_env_ppo_baseline_v1/AttentionAgentConfig",
    },
    "run35_icm": {
        "label": "PPO + ICM",
        "path": "Assets/results/run35_clean_env_ppo_baseline_v1/AttentionAgentConfig",
    },
    "run36_rnd": {
        "label": "PPO + RND",
        "path": "Assets/results/run36_clean_env_rnd_scratch_v1/AttentionAgentConfig",
    },
    "run42_lstm": {
        "label": "PPO + LSTM",
        "path": "Assets/results/run42_clean_env_lstm_only_v2/AttentionAgentConfig",
    },
    "run46_curriculum": {
        "label": "PPO + Curriculum",
        "path": "Assets/results/run46_cir/AttentionAgentConfig",
    },
    "run47_curr_lstm": {
        "label": "Curriculum + LSTM",
        "path": "Assets/results/run47_curr_lstm/AttentionAgentConfig",
    },
    "run50_curr_icm": {
        "label": "Curriculum + ICM",
        "path": "Assets/results/run50_curr_icm/CombatAgentConfig",
    },
    "run54_fixed_v2": {
        "label": "4-tier fixed-to-random",
        "path": "Assets/results/run54_fixed_v2/AttentionAgentConfig",
    },
    "run55_reward_v2": {
        "label": "Curriculum + reward v2",
        "path": "Assets/results/run55_reward_v2/AttentionAgentConfig",
    },
}

SCALARS = {
    "reward": "Environment/Cumulative Reward",
    "entropy": "Policy/Entropy",
    "cleared": ["AttentionAgent/ClearedRoom", "CombatAgent/ClearedRoom"],
    "kills": ["AttentionAgent/EpisodeKills", "CombatAgent/EpisodeKills"],
    "damage_dealt": ["AttentionAgent/EpisodeDamageDealt", "CombatAgent/EpisodeDamageDealt"],
    "damage_taken": ["AttentionAgent/EpisodeDamageTaken", "CombatAgent/EpisodeDamageTaken"],
    "useful_dash_rate": ["AttentionAgent/UsefulDashRate", "CombatAgent/UsefulDashRate"],
    "wasteful_dash_rate": ["AttentionAgent/WastefulDashRate", "CombatAgent/WastefulDashRate"],
    "dash_actions_per_decision": ["AttentionAgent/DashActionsPerDecision", "CombatAgent/DashActionsPerDecision"],
    "blocked_dash_actions_per_decision": ["AttentionAgent/BlockedDashActionsPerDecision", "CombatAgent/BlockedDashActionsPerDecision"],
    "extrinsic_reward": "Policy/Extrinsic Reward",
    "extrinsic_value_estimate": "Policy/Extrinsic Value Estimate",
    "policy_loss": "Losses/Policy Loss",
    "value_loss": "Losses/Value Loss",
    "icm_inverse_loss": "Losses/Curiosity Inverse Loss",
    "curiosity_reward": "Policy/Curiosity Reward",
    "rnd_reward": "Policy/Rnd Reward",
}


def load_run(path: Path) -> event_accumulator.EventAccumulator:
    ea = event_accumulator.EventAccumulator(str(path), size_guidance={"scalars": 0})
    ea.Reload()
    return ea


def scalars(ea: event_accumulator.EventAccumulator, tag):
    tags = ea.Tags().get("scalars", [])
    if isinstance(tag, (list, tuple)):
        tag = next((candidate for candidate in tag if candidate in tags), None)
    if tag not in tags:
        return []
    return ea.Scalars(tag)


def mean(values):
    return statistics.mean(values) if values else None


def fmt(value):
    return "" if value is None else f"{value:.6f}"


def mean_window(events, lo=None, hi=None):
    return mean(
        [
            e.value
            for e in events
            if (lo is None or e.step >= lo) and (hi is None or e.step <= hi)
        ]
    )


def tail_mean(events, n=50):
    return mean([e.value for e in events[-n:]])


def max_pair(events):
    if not events:
        return None, None
    best = max(events, key=lambda e: e.value)
    return best.value, best.step


def rolling_trailing(events, window_steps=WINDOW_STEPS):
    points = []
    for event in events:
        lo = event.step - window_steps
        bucket = [e.value for e in events if lo <= e.step <= event.step]
        points.append((event.step, statistics.mean(bucket)))
    return points


def main():
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    loaded = {}
    for run_id, meta in RUNS.items():
        path = ROOT / meta["path"]
        if not path.exists():
            print(f"[skip] {run_id}: missing {path}")
            continue
        loaded[run_id] = load_run(path)

    summary_path = OUT_DIR / "run_metrics.csv"
    with summary_path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(
            [
                "run_id",
                "label",
                "last_step",
                "reward_tail50",
                "reward_hard_from_140k",
                "reward_hard_from_280k",
                "reward_max",
                "reward_max_step",
                "entropy_tail50",
                "cleared_tail50",
                "cleared_hard_from_140k",
                "cleared_max",
                "kills_tail50",
                "damage_dealt_tail50",
                "damage_taken_tail50",
                "useful_dash_rate_tail50",
                "wasteful_dash_rate_tail50",
                "dash_actions_per_decision_tail50",
                "blocked_dash_actions_per_decision_tail50",
                "extrinsic_reward_tail50",
                "extrinsic_value_estimate_tail50",
                "policy_loss_tail50",
                "value_loss_tail50",
                "icm_inverse_loss_tail50",
                "curiosity_reward_tail50",
                "rnd_reward_tail50",
            ]
        )
        for run_id, ea in loaded.items():
            reward = scalars(ea, SCALARS["reward"])
            cleared = scalars(ea, SCALARS["cleared"])
            reward_max, reward_max_step = max_pair(reward)
            cleared_max, _ = max_pair(cleared)
            row = [
                run_id,
                RUNS[run_id]["label"],
                reward[-1].step if reward else "",
                fmt(tail_mean(reward)),
                fmt(mean_window(reward, 140_000, None)),
                fmt(mean_window(reward, 280_000, None)),
                fmt(reward_max),
                reward_max_step or "",
                fmt(tail_mean(scalars(ea, SCALARS["entropy"]))),
                fmt(tail_mean(cleared)),
                fmt(mean_window(cleared, 140_000, None)),
                fmt(cleared_max),
                fmt(tail_mean(scalars(ea, SCALARS["kills"]))),
                fmt(tail_mean(scalars(ea, SCALARS["damage_dealt"]))),
                fmt(tail_mean(scalars(ea, SCALARS["damage_taken"]))),
                fmt(tail_mean(scalars(ea, SCALARS["useful_dash_rate"]))),
                fmt(tail_mean(scalars(ea, SCALARS["wasteful_dash_rate"]))),
                fmt(tail_mean(scalars(ea, SCALARS["dash_actions_per_decision"]))),
                fmt(tail_mean(scalars(ea, SCALARS["blocked_dash_actions_per_decision"]))),
                fmt(tail_mean(scalars(ea, SCALARS["extrinsic_reward"]))),
                fmt(tail_mean(scalars(ea, SCALARS["extrinsic_value_estimate"]))),
                fmt(tail_mean(scalars(ea, SCALARS["policy_loss"]))),
                fmt(tail_mean(scalars(ea, SCALARS["value_loss"]))),
                fmt(tail_mean(scalars(ea, SCALARS["icm_inverse_loss"]))),
                fmt(tail_mean(scalars(ea, SCALARS["curiosity_reward"]))),
                fmt(tail_mean(scalars(ea, SCALARS["rnd_reward"]))),
            ]
            writer.writerow(row)

    rolling_path = OUT_DIR / "rolling_reward_200k.csv"
    sample_steps = [
        100_000,
        300_000,
        500_000,
        700_000,
        1_000_000,
        1_500_000,
        2_000_000,
        2_500_000,
        3_000_000,
    ]
    with rolling_path.open("w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["run_id", "label", *sample_steps])
        for run_id, ea in loaded.items():
            reward = scalars(ea, SCALARS["reward"])
            rolling = rolling_trailing(reward)
            row = [run_id, RUNS[run_id]["label"]]
            for step in sample_steps:
                value = next((v for s, v in reversed(rolling) if s <= step), None)
                row.append(fmt(value))
            writer.writerow(row)

    print(f"[saved] data/{summary_path.name}")
    print(f"[saved] data/{rolling_path.name}")


if __name__ == "__main__":
    main()
