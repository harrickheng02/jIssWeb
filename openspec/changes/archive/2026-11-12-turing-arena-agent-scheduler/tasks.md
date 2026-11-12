## 1. 配置与调度状态模型

- [x] 1.1 新增 `AgentSchedulerOptions` 全局默认 + `AgentRuntimeConfig` 全局开关模型 + `agent_llm_profiles` 档案集合与索引；更新 `appsettings.Local.example.json` / `.env.example`（不通过 env 传 Key）
- [x] 1.2 新增 `agent_scheduler_state`（配额、nextDue、paused、allowAutoPost/Reply、覆盖上限与定时参数）及索引
- [x] 1.3 Model.Api 绑定 Options；默认全局 Enabled=false；启动时一次性迁移旧全局 Key 为 LLM 档案并回绑 persona

## 2. User.Api：再签 token 与停用账号

- [x] 2.1 `POST /api/internal/agents/{agentUserId}/access-token`（Internal Key）
- [x] 2.2 `POST` 或 `DELETE` 内部停用 agent 账号（供治理删除联动）
- [x] 2.3 集成测试：再签成功/401；停用后再签失败；人类账号拒签

## 3. LLM 档案、运行时开关与提示

- [x] 3.1 `ILlmChatClient` / `DeepSeekChatClient`；Key 解析：按 persona 的 `llmProfileId` 从 `agent_llm_profiles` 档案读取
- [x] 3.2 Admin API：GET/PUT 全局 `schedulerEnabled` 运行时开关 + `agent_llm_profiles` CRUD（掩码；省略保留 Key；`clearApiKey`；`LLM_PROFILE_IN_USE`）；按档案测试连接 `LLM_PROBE_FAILED`；版主可访问、member 403 + 测试
- [x] 3.3 `AgentPromptBuilder`（N 默认 10）+ doubao 跳过单测

## 4. 动作选择、发布与配额 API

- [x] 4.1 热帖选择（7 日窗、`replyCount+likeCount`、排除本人帖与已回帖）+ 自主发帖版块选择；尊重 allow 开关
- [x] 4.2 `AgentForumHttpClient`（再签 token → 发帖/回帖成功落库）；成功后递增配额
- [x] 4.3 Admin：额度查询；更新定时规则/行为；暂停/恢复（不改全局开关）；立即发帖/回复（`QUOTA_EXHAUSTED` / `ACTION_NOT_ALLOWED`）
- [x] 4.4 配额、暂停仅挡自动、手动触发边界的单元/集成测试

## 5. BackgroundService

- [x] 5.1 `AgentSchedulerBackgroundService`：due 领取 → 选动作 → LLM → 发布 → nextDue/退避；跳过 paused
- [x] 5.2 注册 HostedService；全局关闭或无可用 LLM 档案时不写论坛

## 6. 删除联动、创建编排与 Persona Admin 扩展

- [x] 6.1 Admin DELETE：先/事务性停用 User agent → 删 Persona → 清 scheduler state；停用失败返回 `AGENT_DISABLE_FAILED`；集成测试
- [x] 6.2 创建 Persona 时初始化 scheduler state 默认值
- [x] 6.3 治理「新建智能体」编排（Internal 建号 + Persona + state）API 与集成测试

## 7. 治理前端「智能体」Tab

- [x] 7.1 路由 `moderation-agents` + Hub Tab；`canModerate`（admin/版主）可见/可用
- [x] 7.2 新建 + 列表/编辑基础信息；LLM 档案管理（掩码、省略保留 Key、`clearApiKey`）+ 测试连接 + 按 `llmProfileId` 绑定；额度查询
- [x] 7.3 定时规则与发帖/回复开关；暂停/继续；立即发帖/回复（含配额/开关错误提示）
- [x] 7.4 删除二次确认（展示 nickname/personaId）；遵循 forum-tokens 与 primary 按钮

## 8. 验收

- [x] 8.1 后端过滤测试：`AgentScheduler*` / runtime 全局开关 / LLM 档案 / mint / delete 联动
- [x] 8.2 对照 `agent-scheduler` 与 `agent-account-protocol` delta 自检 SHALL
- [x] 8.3 注明人工 7 日盲测为非 CI checklist
  人工验收：5 agent × 7 日盲测不进 CI（非自动化 checklist）
