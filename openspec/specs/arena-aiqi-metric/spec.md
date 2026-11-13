## Purpose

本规范为 `arena-aiqi-metric` 能力的归档基线，汇总历次 OpenSpec 变更产出的 2 条需求及其验收场景（Scenario），作为后续变更的 MODIFIED / ADDED delta 的合并目标。

## Requirements

### Requirement: Weekly AIQI metric batch

系统 SHALL 于每周日 00:00 自动计算 AIQI 指标，公式 SHALL 为 `(1 - 检测成功率) × min(本周指认次数 / (活跃用户数 × 0.5), 1.0)`。检测成功率 SHALL 以本周成功指认次数占本周总指认次数的比例计算；本周指认次数 SHALL 取自图灵场指认事实源；活跃用户数 SHALL 取本周活跃侦探数。计算结果 SHALL 写入运营仪表盘数据源。

当猎手参与修正系数低于 0.3（样本不足）时，结果 SHALL 标注「样本不足」，SHALL NOT 作为可信指标对外使用。周报 SHALL 按周密钥幂等：同一周 SHALL NOT 重复计算。

#### Scenario: AIQI computed from weekly accusation ratios

- **WHEN** 周日跑批执行
- **THEN** 系统 SHALL 按公式产出当周 AIQI 并写入数据源
- **AND** 同一周 SHALL NOT 产生重复结果

#### Scenario: Insufficient sample is flagged

- **WHEN** 本周指认样本不足以支撑可信检测成功率
- **THEN** 结果 SHALL 标注「样本不足」
- **AND** SHALL NOT 被计入可信指标

### Requirement: Weekly net-flow report with inflation and warm-up alerts

系统 SHALL 于每周日 00:00（随 AIQI 一起）计算积分净流量周报：本周净流量 = 本周积分流入 - 流出。当净流量 > 0 时 SHALL 触发通胀预警；当净流量 < -15% 存量时 SHALL 触发热身包发放（向低余额用户发放积分）。周报 SHALL 按周密钥幂等。

#### Scenario: Positive net flow raises an inflation alert

- **WHEN** 本周积分净流量为正
- **THEN** 系统 SHALL 触发通胀预警记录

#### Scenario: Sharp net outflow triggers warm-up packs

- **WHEN** 本周净流量低于存量 -15%
- **THEN** 系统 SHALL 向低余额用户发放热身包积分
- **AND** 热身包发放 SHALL 记录流水并保持幂等
