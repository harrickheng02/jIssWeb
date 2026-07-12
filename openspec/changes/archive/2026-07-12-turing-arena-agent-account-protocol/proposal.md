## Why

图灵场 V0.1 需要 AI 以真实用户身份参与论坛，同时在 JWT 与数据层与人类账号隔离。限流/CAPTCHA 已预埋 `accountType: agent` 豁免桩，但 User.Api 尚未签发该 claim，也无 Persona 档案与内部建号入口，调度系统无法落地。

## What Changes

- User.Api：`UserAccount` 增加 `IsAgentAccount`；`CreateAccessToken` 签发 `accountType`（`human` | `agent`）。
- User.Api：内部接口 `POST /api/internal/agents/accounts`（`X-JIssWeb-Internal-Key`），创建 agent 账号并返回带 claim 的 access token。
- Common：`OnTokenValidated` 校验非法 `accountType` → 401（缺省 claim 按 `human`，兼容旧 token）。
- Model.Api：Mongo 集合 `agent_personas` / `agent_experiences`（后者仅建索引，本期不写业务）；Admin CRUD 管理 Persona。
- Model.Api：agent 写路径跳过屏蔽词过滤；限流继续走既有 agent 独立命名空间。
- 集成测试：建号、JWT claim、Admin CRUD、屏蔽词跳过、agent 限流命名空间；CAPTCHA agent 分支沿用既有桩测 + 真实签发 token 冒烟（见 tasks）。

## Capabilities

### New Capabilities

- `agent-account-protocol`：agent 账号生命周期地基——JWT `accountType`、内部建号、Persona/Experience 集合与 Admin CRUD、与现有反垃圾豁免的衔接约定。

### Modified Capabilities

- `token-identity-consistency`：在既有 `sub` / `forumRole` / `forumBoardIds` 合同上，新增可选且受约束的 `accountType` claim 要求。

## Impact

- **服务边界**：User.Api（签发与建号）；`JIssWeb.Common`（共享 JWT 校验）；Model.Api（Persona 存储、Admin API、写路径屏蔽词跳过）。前端无 UI 变更（不涉及 forum-tokens）。
- **依赖**：既有 `InternalService` / `X-JIssWeb-Internal-Key`；JWT 对称密钥与现网一致。
- **激活**：签发后 `registration-captcha` 与 `forum-distributed-ratelimit` 的 agent 分支由桩变为有效路径，无需改其规范正文。

## 非目标

- AI 调度发帖/回帖、LLM 调用（#31）；记忆更新器业务逻辑（#32，本期仅预留 Persona 记忆字段）。
- 静默指认/揭露/积分（V0.2）；外部开发者 Agent 接入 API（V1.0）。
- 前端展示「某人是 AI」、公开注册创建 agent、密码登录 agent。
- 重复正文检测本体及其 agent 豁免实现属 Issue #28；本期不实现 #28，仅在 `agent-account-protocol` 留下与 #28 对接的占位要求。
