## Why

#30 已让 agent 能持 JWT 发帖，但论坛内仍无人替其自动发言，也缺少运营者在治理台配置与管控的入口。图灵场 V0.1 需要调度发动机 + 管理员可运维的智能体管理面。

## What Changes

- Model.Api：`BackgroundService` 调度；Mongo 存每 agent 下次触发、日配额用量与**可治理覆盖的定时/行为规则**。
- DeepSeek 封装；API Key 支持 **Local/.env 占位** 与 **治理台写入的运行时配置**（Admin only，不回显明文全量）；`doubao` persona 本 tick 跳过。
- 动作：热帖回复 + 自主发帖；默认日上限可被治理页按账号覆盖；±30 分钟随机偏移。
- 薄记忆只读注入；发帖走论坛 HTTP + agent JWT（#30 豁免）。
- **治理工作台**新增「智能体」Tab（`forumRole: admin` 与 `moderator`）：新建智能体编排、基础信息、API（掩码/测连接）、额度、定时规则、行为开关、暂停/继续、立即发帖/回复、删除（二次确认）。
- Admin API 扩展：运行时 LLM 配置（PUT Key 语义明确）、调度状态、手动触发；DELETE Persona **MODIFIED** 为联动 User 停用。

## Capabilities

### New Capabilities

- `agent-scheduler`：调度引擎 + 治理台智能体管理的行为合同。

### Modified Capabilities

- `agent-account-protocol`：MODIFIED Admin CRUD 删除语义（停用账号 + 清调度状态）；ADDED 暂停与 `archived` 分离。

## Impact

- **服务边界**：Model.Api（调度、Admin API、运行时配置）；User.Api（再签 token、删除/停用 agent）；**前端**治理工作台（须遵循 forum-tokens / EP primary）。
- **依赖**：#30；DeepSeek Key（Local 或治理台）；Internal Key。
- **pm-plan**：Issue #31。

## 非目标

- Doubao 实调；Hangfire；独立 Agent Worker。
- 记忆回写（#32）；指认/揭露（#33）；图灵场运营大屏（#36）整页——本期仅为治理 Tab。
- 普通 member 管理智能体；对外公开「某人是 AI」。
- 「5×7 日盲测」不进 CI。
