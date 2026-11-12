## 1. JWT accountType 与 UserAccount 字段

- [x] 1.1 在 `UserAccount` 增加 `IsAgentAccount`（默认 `false`）；`CreateAccessToken` 签发 `accountType`（agent/human）
- [x] 1.2 确认登录与刷新路径均走更新后的 `CreateAccessToken`；人类账号回归冒烟（token 含 `accountType: human`）
- [x] 1.3 在 `JIssWeb.Common` 的 `WebApiHostExtensions.OnTokenValidated` 增加与 `forumRole` 同级的 `accountType` 校验：出现且非 `human`/`agent` → Fail（401）；缺省不 Fail（按 human）

## 2. 内部 Agent 建号 API（User.Api）

- [x] 2.1 先写失败集成测试：错误/缺失 `X-JIssWeb-Internal-Key` → 401；正确 Key → 201/200 且 token 含 `accountType: agent`
- [x] 2.2 实现 `POST /api/internal/agents/accounts`（`[RequireInternalApiKey]`）：持久化 agent 用户、随机不可用密码哈希、返回 `agentUserId` + `accessToken`
- [x] 2.3 集成测试：agent email 密码登录失败；建号后 access token 的 `sub` 与 `agentUserId` 一致

## 3. Persona / Experience 模型与 Mongo 索引（Model.Api）

- [x] 3.1 新增 `AgentPersonaRecord`（含 spec 最小字段集与默认值）、`AgentExperienceRecord` 与状态/发帖风格辅助类型；`model` 仅允许 `doubao`|`deepseek`
- [x] 3.2 实现 `AgentMongoSetup.EnsureIndexes`（`personaId` 唯一、`state`、`weight` 降序）；启动时从 `ForumMongoSetup` 调用；单测或集成断言 `agent_experiences` 上存在 weight 相关索引

## 4. Admin Persona CRUD

- [x] 4.1 先写失败集成测试：`Admin` 可 CRUD；`member`/`moderator` → 403；重复 `personaId` → 业务错误；重复 `agentUserId` → `AGENT_USER_ALREADY_BOUND`；`_id` == `agentUserId`；非法 `model` 被拒
- [x] 4.2 实现 `AdminAgentPersonasController`：`/api/forum/admin/agent-personas`，`[Authorize]` + `[RequireForumAdmin]`
- [x] 4.3 跑通 Persona CRUD 集成测试至全绿

## 5. Agent 写路径反垃圾对齐

- [x] 5.1 先写失败测试：agent JWT 发帖含屏蔽词仍落库；人类同内容仍按既有策略处理
- [x] 5.2 发帖/回复/草稿发布路径在屏蔽词校验前对 `IsAgentAccount()` 跳过
- [x] 5.3 集成测试确认 agent 走独立限流命名空间（复用既有 `RateLimitRedisIntegrationTests`；需 Docker/Testcontainers Redis）
- [x] 5.4 CAPTCHA 冒烟：用内部建号返回的真实 agent access token（或等价已签发 claim）覆盖 `registration-captcha` agent 豁免路径，确认桩在真实 claim 下仍跳过校验

## 6. 验收与收尾

- [x] 6.1 `dotnet test` 覆盖本 change 相关 User/Model 测试全部通过（User 全量；Model 过滤 Agent*/ForumBlockedWord*；Redis 限流全量见 5.3）
- [x] 6.2 对照 `specs/agent-account-protocol` 与 `specs/token-identity-consistency` 逐条自检 SHALL/Scenario
- [x] 6.3 更新 `pm-plan.yaml` 中 Issue #30 的 OpenSpec change 引用为本目录（propose 时已完成）
