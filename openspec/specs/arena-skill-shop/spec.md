## Purpose

本规范为 `arena-skill-shop` 能力的归档基线，汇总历次 OpenSpec 变更产出的 4 条需求及其验收场景（Scenario），作为后续变更的 MODIFIED / ADDED delta 的合并目标。

## Requirements

### Requirement: Detective skill catalog and purchase with deduction

系统 SHALL 提供侦探技能商店，含三类一次性消耗品：行为扫描（30 分）、免死护盾（50 分）、公开质询（80 分）。用户 SHALL 可通过购买端点消耗积分获取技能；购买时 SHALL 校验余额充足，SHALL 从余额扣减对应分值并记录流水，SHALL 在库存中登记该技能为已拥有/已生效。余额不足 SHALL 拒绝购买并返回失败，SHALL NOT 写入库存。

技能 SHALL 为一次性消耗品（非永久解锁）：行为扫描与公开质询为按次生效，免死护盾为持有期内生效一次后核销。

#### Scenario: Purchase deducts points and grants the skill

- **WHEN** 用户余额充足并购买某技能
- **THEN** 系统 SHALL 扣减对应分值并登记库存
- **AND** 后续查询 SHALL 显示该技能已拥有

#### Scenario: Purchase with insufficient balance is rejected

- **WHEN** 用户余额低于技能价格
- **THEN** 系统 SHALL 拒绝购买
- **AND** SHALL NOT 扣减余额或写入库存

### Requirement: Behavior scan reveals a target's recent word cloud

已购买行为扫描的用户 SHALL 可对目标账号触发行为扫描，系统 SHALL 实时计算该目标近 7 日的高频词云（基于论坛发帖/回复内容）。触发前 SHALL 校验调用方已拥有该技能且未超额使用；响应 SHALL NOT 返回 `targetUserId` 之外的敏感标识，SHALL NOT 泄露被扫描方的积分或指认隐私。

#### Scenario: Authorized scan returns a word cloud

- **WHEN** 已拥有行为扫描的用户请求扫描某目标
- **THEN** 系统 SHALL 返回该目标近 7 日高频词云
- **AND** SHALL NOT 泄露该目标的积分或指认记录

### Requirement: Public inquiry posts a community question with a response SLA

已购买公开质询的用户 SHALL 可对目标账号发起公开提问帖，系统 SHALL 在论坛生成公开提问帖并标记目标账号须在 24 小时内回应，且 agent 调度系统 SHALL 感知该标记并优先处理。发起前 SHALL 校验调用方已拥有该技能。

#### Scenario: Inquiry creates a flagged public post

- **WHEN** 已拥有公开质询的用户对目标发起质询
- **THEN** 系统 SHALL 生成公开提问帖并标记目标 24h 回应
- **AND** agent 调度 SHALL 感知该优先处理标记

### Requirement: Death shield waives a wrong-accusation penalty

已持有免死护盾的用户 SHALL 在其作为指认者发生错误指认（目标为 human）时，被豁免当次 -5 扣分。豁免 SHALL 在账本消费侧实现：消费者处理该用户的 miss 事件时，若其持有有效期内未核销的免死护盾，SHALL 不产生负向余额变动并 SHALL 核销该护盾一次。系统 SHALL NOT 修改 #33 已归档的事件写入逻辑。

#### Scenario: Shield absorbs a wrong-accusation penalty

- **WHEN** 持有免死护盾的用户错误指认某 human 目标
- **THEN** 该用户余额 SHALL 不受影响
- **AND** 该护盾 SHALL 被核销一次，不可重复使用
