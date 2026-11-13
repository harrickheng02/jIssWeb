> 施工清单。进入 `/opsx:apply` 时按 Construction SOP 逐项 TDD：每个后端任务先写失败测试再实现，`dotnet test --filter` 逐项验绿；前端任务跑 `npx vitest run` 并验证黄金路径。全部勾选后跑全量 + `change-review` 逐条核对 SHALL/MUST。

## 1. 配置、数据模型与索引

- [x] 1.1 新增 `ArenaRevealOptions`（周五 22:00 `Asia/Shanghai`、缺 `tzdata` 降级固定 +8、阈值默认 3 可配置、LLM 档案绑定、归档天数 21、启动补跑开关）；绑定进 `Model.Api` 的 `Program.cs`，不通过 `.env` 传密钥
- [x] 1.2 新增 `arena_accusation_dedupe`：唯一索引 `{accuserId,targetUserId}` + TTL 7d，专责验收①去重
- [x] 1.3 新增 `arena_accusations`：`weekKey` / `status=pending` / `settledAtUtc` 等，**不设 7d TTL**；索引支撑「扫描本周未结算记录」
- [x] 1.4 新增 `arena_target_tallies`（`weekKey + targetUserId` 唯一）、`reveal_jobs`（`weekKey` 唯一）、`arena_points_events`（`eventId` 唯一 `(weekId,userId,targetUserId,type)`）
- [x] 1.5 `Models/ForumContentStates.cs` 新增私有 `arena_pending` 常量（或独立字段）；确认不影响既有 `Published` / `Draft` / `Local` / `Deleted` 语义

## 2. 静默指认端点与统一 200

- [x] 2.1 `POST /api/arena/accusations`（Model.Api，`[Authorize]`，请求体 `targetUserId`）；调用链严格为 `dedupe` 原子插入 → `arena_accusations` 写 `pending` → `tallies $inc` CAS → 统一 200
- [x] 2.2 `dedupe` 插入遇 11000（7 天内重复）静默跳过，返回与其他分支**字节级一致**的 `200 {code:0,message:"ok",data:{}}`
- [x] 2.3 无效目标、越权、限流、参数错误、内部异常等全部分支归一到同一响应（含 headers）；不提供 GET、不提供「我的指认」、不提供待揭露数 / 红点 / 倒计时
- [x] 2.4 指认路径**零身份判定**：不调用 User.Api、不写 `arena_points_events`、不发通知、不判 hit/miss、不写 `accountType`；断言 `arena_points_events` 为空、`forum_in_app_notifications` 新增=0、审计 feed 无指认记录
- [x] 2.5 日志脱敏：`targetUserId` 及其哈希禁入日志；版主审计 feed 显式排除指认事件

## 3. 阈值计数与内部状态降权

- [x] 3.1 `arena_target_tallies` 对所有目标累加（不查 `accountType`），状态机 `active → threshold_reached → pending_reveal | void_human`
- [x] 3.2 `threshold_reached` 仅表示计数达阈值，SHALL NOT 作为 AI 身份信号出现在对外接口 / 日志 / 埋点 / 通知 / 运营后台 / 版主审计；tally 无路由，后台白名单仅 `weekKey` / `jobStatus` / `processedCount`
- [x] 3.3 阈值从配置读取、可配置，不建后台 UI

## 4. 定时任务与 weekKey 幂等

- [x] 4.1 新建 `ArenaRevealBackgroundService`（Model.Api 第 4 个 HostedService，复用同一 Mongo/Redis 单例），不复用 `AgentSchedulerEngine` 实例
- [x] 4.2 周五 22:00 `Asia/Shanghai`（UTC 14:00）触发；缺 `tzdata` 降级固定 +8；启动补跑防漏跑
- [x] 4.3 `reveal_jobs.weekKey` 原子领取（复用 `FindOneAndUpdateAsync` 范式），多实例下只有一个能领取
- [x] 4.4 批次状态 `settling → settled → revealing → completed` 分阶段幂等，失败从断点续跑；对照 R-6 评估多实例资源开销

## 5. prepare：批量判类型与不可变结算计划

- [x] 5.1 一次性批量**只读**判 `accountType` → 生成不可变结算计划（每目标 `verdict` + `accuserId` / `eventId` 清单）→ 落盘 `state=prepared`；此阶段零对外副作用
- [x] 5.2 批量查询失败：计划不落盘，整批不结算 / 不公开 / 不发通知，断点重跑
- [x] 5.3 结算计划 SHALL 仅系统私有（比 `threshold_reached` 更敏感），禁入日志 / 埋点 / 运营后台 / 版主审计 / 任何对外接口
- [x] 5.4 结算三分支写死：agent 且本周被揭露 → `hit`（+50 事件 + 揭露后通知）；human 任意阈值 → `void_human`、`miss`（-5 事件、零通知）；agent 未达阈值 → 零事件零通知

## 6. commit：整批原子对外门禁

- [x] 6.1 按序幂等执行：写 `arena_points_events`（私有）→ 更新 tally / persona 内部状态 → 生成自白帖（私有 `arena_pending`）→ `InsertMany` 批量 +50 通知 → 单 CAS 置 `reveal_jobs.state=completed` + `publishedAtUtc`
- [x] 6.2 通知插入复用 `IForumNotificationWriter` / `ForumNotificationWriter.cs:69` 的 `uniq_eventId` 11000 静默跳过范式；`ForumMongoSetup.cs:78/87-95` 的唯一复合索引 + PartialFilter 承载 `uniq_eventId`
- [x] 6.3 唯一对外可见性门禁 = `completed`：帖子在 completed 后一次 `UpdateMany` 转 `published`；通知按 `batchId==null || 批次已 completed` 过滤；BFF 仅在 completed 后渲染 AI 角标
- [x] 6.4 **门禁覆盖 `ForumNotificationsController` 三条读取路径**：`List`(:33) / `UnreadCount`(:78) / `MarkRead`(:93)，不得只改 List 而从「未读数」漏出通知已生成的侧信道
- [x] 6.5 门禁只作用本批次新增数据：历史已揭露账号的 AI 角标 / 自白帖 / 身份在 prepare 期间仍可见（F-05）；#16 既有非 arena 通知（`batchId==null`）仍正常可见
- [x] 6.6 自白帖**不复用 `ForumContentStates.Draft`**：`ForumPostsController.cs:280-285` 的 draft 对作者本人可见、deleted 对版主/管理员可见；改用 `arena_pending` 并同步过滤帖子详情、个人草稿列表、内容审核队列、版主视图
- [x] 6.7 任一步失败续跑，因 `eventId` / `postId` / `uniq_eventId` 全幂等，不重复发通知 / 扣分 / 自白帖

## 7. 自白帖生成与内容安全（P0）

- [x] 7.1 LLM 生成（角色故事 / 印象最深对话 / 临别感言），按 `agent_llm_profiles` 解析 Key；3 次退避 → 静态模板兜底，SHALL NOT 跳过揭露
- [x] 7.2 以 agent 账号身份发帖，复用 #31 内部再签 JWT 管线
- [x] 7.3 **LLM 生成路径与静态兜底模板路径发布前均显式调用 `IForumBlockedWordFilter.Evaluate`**，不使用 `isAgent` 豁免；命中不发布 / 降级模板 / 重试，仍失败则保留 `pending_reveal` 顺延下周
- [x] 7.4 断言「全库不存在未经 `Evaluate` 的已发布自白帖」；不改动 `ForumPostsController.cs:209` / `:632` 既有 agent 豁免

## 8. AI 标记、身份公开与归档

- [x] 8.1 账号头像「AI」标记，权威源落 `agent_personas`（Model.Api）；`state=eliminated`；身份永久公开
- [x] 8.2 `arena_accusations` 归档条件 = `settledAtUtc` 存在且距今 ≥14 天（取 21 天留余量）；归档任务只在结算完成后调度、不得早于结算完成；`dedupe` 保持 7d TTL

## 9. 积分事件桩与通知（方案 C）

- [x] 9.1 `arena_points_events` 字段冻结：`eventId` / `userId` / `type=accusation_hit|accusation_miss` / `delta=+50|-5`（整型）/ `targetUserId` / `accusationId` / `weekId` / `occurredAt` / `notifyEligible`
- [x] 9.2 事件只追加、永不 update、**零运行时状态字段**；`notifyEligible` 为创建时常量（hit=true / miss=false），spec 写死「常量标记、非发送状态」
- [x] 9.3 +50 通知在揭露批次成功（自白帖发布 + `eliminated`）后统一批量发，不得指认瞬间发；指认至揭露前全站通知新增 = 0
- [x] 9.4 -5 静默只写事件、零通知、离线批处理、明细不溯源
- [x] 9.5 `targetUserId` 禁入 API 响应 / 站内通知 / 积分明细 / 日志 / 前端埋点（含哈希）；#33 不 import `Accounting.Api` 类型，零回填

## 10. 前端、BFF、Gateway 与埋点

- [x] 10.1 前端指认入口（ArenaAccuseButton.vue：低调入口「怀疑是 AI？」+ 确认弹窗 + 静默 POST + 中性 toast，不展示成败 / 待揭露数 / 倒计时 / 红点）；vue-tsc 0 错误。组件交互单测受前端无 `.vue` 单测脚手架限制未单独补，隐私关键路径（白名单）由 10.2 覆盖
- [x] 10.2 前端埋点白名单 helper（仅 `ts` / `accuserIdHash` / `weekKey`；`assertAllowedAnalyticsKeys` / `sanitizeArenaAnalytics` 拒绝 `targetUserIdHash`）+ vitest 7 项全绿
- [x] 10.3 AI 角标仅在 completed（state=eliminated）后渲染：新增公开只读 `GET /api/arena/author-is-ai`（Model.Api），仅 `eliminated` 返回 `isAI=true`、其余 false（不泄露 pending_reveal 等内部态）；前端 AuthorAiBadge.vue 读取并渲染「AI」角标，模块级缓存跨卡片去重。**读取经 Gateway `/api/arena/*` → Model.Api 直连（与既有 forum 读路径一致），未单独建 BFF 聚合控制器**——该端点为单条布尔查询、非并发聚合，走 BFF 反多一跳；「仅 completed 后可见」由服务端按 `eliminated` 判定保证
- [x] 10.4 Gateway YARP 放行 `/api/arena/*`（appsettings.json arena-route；`ReverseProxy:Routes` 由基础配置继承，Docker/Local 仅覆盖 Clusters 故自动继承）；vue-tsc 0 错误、Arena 后端测试 18→22 项全绿、前端 analytics vitest 7 项全绿

## 11. 27 项自动化用例（六项验收映射）

> 用例 1–25 沿用 /opsx:explore 各轮定稿口径（D9 的 20 项、D10 新增 21/22、D11 新增 23/24/25）；用例 26/27 由本 change 依据 D12–D13 的门禁修正补齐（通知三读取路径一致性、门禁不误伤历史数据），为团长归纳编号。**若成员最终全量清单编号不同，apply 阶段只对齐编号、不改语义**；三份 spec 的 SHALL 条款是唯一权威。用例 22 为 **P0 正确性闸门**。

| # | 用例 | 验收 |
|---|---|---|
| 1 | 合法指认落 `pending` + dedupe 记录 + tally `$inc`，返回统一 200 | ① |
| 2 | 结算后 human → `void_human`、零通知、指认者 -5 事件 | ⑤ |
| 3 | dedupe 11000 重复指认 → 字节级一致 200，记录数仍 = 1 | ① |
| 4 | 阈值达 3 人 → `threshold_reached`（内部态，对外零可见） | ② |
| 5 | 阈值可配置（非 3 时按配置生效） | ② |
| 6 | 周五 22:00 触发 + `weekKey` 原子领取，多实例仅一个领取 | ③ |
| 7 | 自白帖内容安全三组：LLM 路径 / 静态模板路径均过 `Evaluate`、命中不发布、全库无未经 Evaluate 的已发布自白帖 | ③ |
| 8 | `arena_points_events` 事件级断言（字段 / delta / 类型 / `notifyEligible` 常量 / 只追加） | ④⑥ |
| 9 | LLM 3 次退避 → 静态模板兜底，不跳过揭露 | ③ |
| 10 | AI 标记 + `agent_personas.state=eliminated` + 身份永久公开 | ③ |
| 11 | 被指认方无感知（human 与 agent 揭露前均无通知 / 无站内可见变化） | ⑤ |
| 12 | 七分支统一 200 字节级一致（含 headers） | ① |
| 13 | 无 GET / 无「我的指认」/ 无红点 / 无倒计时 / 无待揭露数 | ⑤ |
| 14 | 日志脱敏 + 审计 feed 排除指认事件 + 版主/运营零可见 | ⑤ |
| 15 | 通知级断言（F-07 四条） | ④ |
| 16 | 启动补跑防漏跑 | ③ |
| 17 | 断点续跑恰好一次，不重复发通知 / 扣分 / 自白帖 | ⑥ |
| 18 | 第 8 天同一用户对同一目标可再指认（dedupe TTL 生效） | ① |
| 19 | 前端埋点字段白名单断言（无 `targetUserIdHash`） | ⑤ |
| 20 | 指认至揭露前零通知 + 阈值未达 / 本周未揭露不发奖励通知 | ④⑤ |
| 21 | 指认时刻零积分事件 / 零通知 / 零 `accountType` 跨服务查询 | ⑤ |
| 22 | **P0**：周一指认记录在下周五结算时仍存在且被结算；归档不得早于结算完成、保存 ≥14 天 | ①⑥ |
| 23 | `threshold_reached` 零外泄（接口 / 日志 / 埋点 / 通知 / 运营后台 / 版主审计） | ②⑤ |
| 24 | 整批原子对外：未 `completed` 时帖 / 通知 / 角标均不可见（含版主/运营与审核队列），`completed` 后同时可见，失败续跑无部分对外 | ③⑥ |
| 25 | 批量 `accountType` 查询失败 → 整批不结算 / 不公开 / 不发通知，续跑恰好一次 | ⑥ |
| 26 | 通知门禁三读取路径一致（`List` / `UnreadCount` / `MarkRead`），未读数不泄露批次进度 | ⑤⑥ |
| 27 | 门禁只作用本批次：历史已揭露账号与 `batchId==null` 既有通知不受影响 | ③⑤ |

## 12. 验证与交付

- [ ] 12.1 逐任务 `dotnet test --filter`；42 条既有环境性失败（本地 `appsettings.Local.json` 缺 Mongo/JWT/版主配置）显式隔离并说明，不属本 change 回归
- [ ] 12.2 `npx vitest run` + `npx vue-tsc -b` + 生产构建验证
- [ ] 12.3 全量 `dotnet test` + `npm test`，`verification-before-completion` 有输出证据
- [ ] 12.4 `change-review` 逐条核对三份 spec 的每个 SHALL / MUST，确认无非目标被误实现、三道上线闸门通过
- [ ] 12.5 按中文提交规范提交；PR 用 `.github/pull_request_template.md`；合并后 `/opsx:archive` → 更新 `pm-plan.yaml` #33 状态 → `npm run pm:push`（远程写操作需用户点头）
