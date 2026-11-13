## Context

#30 / #31 已交付 `agent_personas`、agent JWT、内部再签、`agent_llm_profiles` 多档案、`AgentSchedulerEngine` 与治理台。#16 已交付 `forum_in_app_notifications`（`IForumNotificationWriter` 带 11000 去重）。`JIssWeb.Accounting.Api` 目前仅有 csproj / Program.cs / Controllers 骨架，全库无 `Points|积分|Ledger` 实现，而 `pm-plan.yaml:310` 与 `:366` 让 #33、#35 互为依赖——本 change 用「积分事件最小桩」把依赖单向化为 `#35 → #33`。

`docker-compose.yml` 未配置 Mongo `replSet`，多文档事务不可用，故采用「插入 → `$inc` CAS → append 事件」三步幂等链。`Model.Api` 已有 3 个 HostedService，本期新增第 4 个，复用同一 Mongo/Redis 单例。

## Goals / Non-Goals

**Goals:**

- 跑通「发现 → 静默指认 → 阈值 → 周五 22:00 揭露 → 自白帖 → 结算」闭环，V0.2 WAD ≥5。
- 揭露前**零对外副作用**：响应、通知、日志、埋点、审计、运营后台、版主视图均无指认或判定信号。
- 结算整批幂等，不允许部分对外；任何失败都能从断点续跑且不重复发通知 / 扣分 / 发帖。
- 工程可测：27 项自动化用例，六项验收逐条可映射。

**Non-Goals:**

- 积分余额、衰减、技能商店、AIQI 跑批/看板、周报（#35）；悬赏与奖章（#34）；记忆回写（#32）；战报（V1.1）。
- 实时反馈、指认记录用户查询、多端新增、后台阈值 UI（V1.1）。
- 全局修改 agent 屏蔽词豁免分支。

## Decisions

### D1：写逻辑归属 Model.Api，User.Api / Accounting.Api 零改动

论坛域全部落 `Model.Api`；`Frontend.Bff` 只做并发聚合、字段裁剪与 Cookie 生命周期，不落库、不承载写操作。AI 标记的权威源是 `agent_personas`（Model.Api），BFF 读聚合后渲染角标。`User.Api` 本期不新增内部写端点（保持范围最小化，非架构禁令）；`Accounting.Api` 留 #35。

### D2：新建 `ArenaRevealBackgroundService`，不复用 #31 引擎实例

`AgentSchedulerEngine.cs:48` 是 per-agent `nextDueAtUtc` 的 `FindOneAndUpdateAsync` 原子领取，没有日历 cron、没有周批次锁。周五 22:00 的批次语义与 per-agent 到期语义不同，故新建独立 HostedService。复用 #31 的工程资产：Mongo 原子领取范式、`agent_llm_profiles` 多档案、内部再签 JWT 发帖管线、LLM 重试 / 退避思路。周五 22:00 `Asia/Shanghai` = UTC 14:00；缺 `tzdata` 时降级固定 +8；启动补跑防漏跑。R-6（第 4 个 HostedService 并列注册的资源开销）列为 apply 阶段评估项，不作阻塞。

### D3：去重与业务记录生命周期解耦（D10，必修正确性缺陷）

早期方案让 `arena_accusations` 同时承担验收①去重（唯一索引 + TTL 7d）与待结算业务记录。但周一指认 → 下周五结算最长约 **13 天 22 小时 > 7 天**，记录会被 TTL 物理删除 → **漏结算、漏发 +50/-5**，激励闭环断裂。

| 集合 | 职责 | 索引 / 生命周期 |
|---|---|---|
| `arena_accusation_dedupe` | 验收①「7 天内只能指认一次」原子去重 | 唯一索引 `{accuserId,targetUserId}` + TTL 7d；字段极少、自动清理 |
| `arena_accusations` | 待结算业务记录（`status=pending`） | 无 7d TTL；`settledAtUtc` 存在且距今 ≥14 天（取 21 天留余量）后归档清理，归档任务只在结算完成后调度 |
| `arena_target_tallies` | 阈值计数 | `weekKey + targetUserId` 唯一，`$inc` CAS |
| `reveal_jobs` | 批次幂等与唯一对外可见性门禁 | `weekKey` 唯一；`state` / `publishedAtUtc` |
| `arena_points_events` | 不可变积分事件（方案 C） | `eventId` 唯一 `(weekId,userId,targetUserId,type)`；只追加 |

用例 18「第 8 天可再指认」由 dedupe 保证；用例 22「周一指认记录在下周五结算时仍存在且被结算」为 **P0 正确性闸门**。

### D4：指认路径零身份判定（撤销团长 v4 例外）

v4 曾保留「指认时刻在系统私有域内判 target 的 `accountType`，避免真人计入阈值被误揭露」。D10 把 tally 状态机拆开后该顾虑已消解，故撤销例外，采纳：

```
dedupe 原子插入（11000 → 统一 200）
  → arena_accusations 写 status=pending
  → tallies $inc CAS（对所有目标累加、不查 accountType）
  → 统一 200
```

不调用 User.Api、不写积分事件、不发通知、不判 hit/miss。额外收益：去掉写路径跨服务失败面、消除事件写入时序侧信道、P95 更低。

`arena_target_tallies` 状态机：`active → threshold_reached`（**仅计数达阈值，不等于已确认 agent**）→ 结算批量判类型：agent → `pending_reveal`；human → `void_human`。`threshold_reached` SHALL NOT 作为 AI 身份信号出现在对外接口 / 日志 / 埋点 / 通知 / 运营后台 / 版主审计；tally 无路由，后台白名单仅 `weekKey` / `jobStatus` / `processedCount`。

### D5：整批不允许部分对外 → 单一可见性门禁（D11–D13）

结算批次两阶段提交：

- **prepare**：一次性批量只读判 `accountType` → 生成不可变结算计划（每目标 `verdict` + `accuserId` / `eventId` 清单）→ 落盘 `state=prepared`，**零对外副作用**。批量查询失败则计划不落盘、整批不结算 / 不公开 / 不发通知，从断点续跑。结算计划含提前的 agent 判定，比 `threshold_reached` 更敏感，SHALL 仅系统私有。
- **commit（全幂等按序）**：写 `arena_points_events`（私有）→ 更新 tally / persona 内部状态 → 生成自白帖（私有态）→ `InsertMany` 批量插入 +50 通知（`uniq_eventId` 幂等，复用 `ForumNotificationWriter.cs:69` 的 `catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)` 静默跳过）→ **单个 CAS** 置 `reveal_jobs.state=completed` + `publishedAtUtc`。

所有对外数据按该门禁过滤：帖子在 `completed` 后一次 `UpdateMany` 转 published；通知在读取侧按 `batchId==null || 批次已 completed` 过滤；AI 角标由 BFF 仅在 `completed` 后渲染。未完成前外部看不到任何部分结果，这一次 CAS 让全部对外数据同时可见。任一步失败续跑，因 `eventId` / `postId` / `uniq_eventId` 全幂等，不会重复发通知 / 扣分 / 自白帖。

批次状态：`settling → settled → revealing → completed`，分阶段幂等、断点续跑。

### D6：门禁必须覆盖通知的三条读取路径（团长亲自核实）

`backend/src/JIssWeb.Model.Api/Controllers/ForumNotificationsController.cs` 有 `List`(:33)、`UnreadCount`(:78)、`MarkRead`(:93) 三条路径，各自独立构造 filter。**只在 List 加过滤会从「未读数」漏出「通知已生成」的侧信道**。故门禁断言须覆盖三路径，并新增用例 26。

同时门禁只作用于本批次新增数据：历史已揭露账号在后续 prepare 期间仍须可见 AI 角标 / 自白帖 / 身份（不得误伤 F-05「身份永久公开」P0）；#16 既有非 arena 通知（`batchId==null`）在 prepare 期间仍正常可见，门禁改动不得破坏既有能力。新增用例 27。

### D7：自白帖私有态，不复用 `ForumContentStates.Draft`（团长亲自核实）

`ForumPostsController.cs:280-285` 的 `draft` 分支对作者本人可见，`:267` 注释说明 `deleted` 对版主 / 管理员可见；`ForumMeController` 草稿列表与内容审核队列也会暴露草稿。若自白帖复用 `Draft`，在 prepare 期即可被枚举。故新增私有 `arena_pending` 态，并同步过滤帖子详情、个人草稿列表、内容审核队列、版主视图；`completed` 后才转 `published`。

`backend/src/JIssWeb.Model.Api/Models/ForumContentStates.cs` 现仅有 `Published` / `Draft` / `Local`（+ `Deleted`），**没有 `arena_pending`**——需新增常量或独立字段，这是 apply 阶段必须落的实现项。用例 24「draft 不可见」须覆盖版主 / 运营与内容审核队列，不止普通用户。

### D8：内容安全 P0，两条发帖路径都显式校验（团长亲自核实）

`ForumPostsController.cs:209` 为 `var blockedEvaluation = isAgent ? BlockedWordEvaluation.Pass : _blockedWords.Evaluate(null, request.Body);`，agent 身份发帖直接绕过 `Services/ForumBlockedWordFilter.cs` 的 `Evaluate`。#33 自白帖以 agent 身份发出，SHALL NOT 沿用该豁免：LLM 生成路径与静态兜底模板路径**发布前均显式调用 `IForumBlockedWordFilter.Evaluate`**，命中则不发布 / 降级模板 / 重试；断言「全库不存在未经 Evaluate 的已发布自白帖」。

范围严格限定在 #33 发帖管线，**不改动 `ForumPostsController.cs:209` / `:632` 的既有 agent 豁免**（属 #30 已交付行为，全局改动会引入回归并越出 #33 范围）。约 0.5 人日，在 4~8 人日预算内。

### D9：积分事件契约（方案 C，冻结）

`arena_points_events` 字段终稿：

```
eventId / userId / type=accusation_hit|accusation_miss / delta=+50|-5（整型）
/ targetUserId / accusationId / weekId / occurredAt / notifyEligible
```

- `notifyEligible`：创建时**常量**（hit=true / miss=false），语义「是否具备通知资格」，**非投递状态、永不翻转、不产生 update**。事件只追加、永不更新、**零运行时状态字段**。
- 投递状态与幂等下沉到 #16 侧 `forum_in_app_notifications.uniq_eventId` 唯一索引（复用 `ForumMongoSetup.cs:78/87-95` 的「唯一复合索引 + PartialFilter」范式）；#35 维护消费位点、SHALL NOT 触碰通知集合 / 重发通知。
- spec 写死「常量标记、非发送状态」双保险，避免测试官 / #35 消费方按字面跳过发送造成 +50 通知缺失（P0）。
- `targetUserId` 仅供 #35 核对与幂等，SHALL NOT 出现在 API 响应 / 站内通知 / 积分明细 / 日志 / 前端埋点（含哈希）。

### D10：结算三分支规则（业务规则，写死）

| 判定 | 结果 |
|---|---|
| agent 且本周被揭露 | 其全部 counted 指认者 `hit`：+50 事件 + 揭露后通知 |
| human（**任意阈值**） | `void_human` 静默取消；其全部指认者 `miss`：-5 事件、**零通知** |
| agent 未达阈值 | **零事件、零通知**、不计成功错误 |

「human 已达阈值」是 `pm-plan.yaml:305` 字面未定义场景，按上表补全，符合「human 永不揭露」与静默核心；已在 `proposal.md` 变更登记记录，不静默改验收字面。

### D11：+50 通知的时序与静默侧信道

+50 站内通知当期完整交付（#16 已交付，通知能力不阻塞），但 **SHALL 在揭露批次成功（自白帖发布 + `eliminated` 标记）之后统一批量发**，不得指认瞬间发，否则可用小号逐一指认、靠「是否收到 +50」枚举 AI 账号。-5 静默只写事件、零通知、离线批处理、明细不溯源。指认时刻至揭露前全站通知新增 = 0。

F-07 四条断言：指认至揭露前全站通知新增=0 / 揭露后成功指认者恰好 1 条 +50、被指认方与错误指认方收件=0 / 阈值未达或本周未揭露不发奖励通知 / 同 `eventId` 重放无第二条。

### D12：三道上线闸门与埋点去标识化

1. human 目标零感知；
2. 版主 / 运营账号看不到揭露前的指认记录与待揭露记录（版主也是人类玩家）；
3. 指认后至揭露前零通知。

前端埋点白名单仅 `ts` / `accuserIdHash` / `weekKey` / 聚合计数；去掉全部 `targetUserIdHash`（哈希仍可关联枚举）。BFF 字段裁剪、日志禁记 `targetUserId` 及其哈希。AIQI 所需指认-目标关联只在系统私有 `arena_accusations` 内由后端离线聚合，不进前端埋点、不上报日志、不做公式 / 跑批 / 看板。

### D13：幂等链与无事务约束

`docker-compose.yml` 无 `replSet`，不使用多文档事务。用「dedupe 原子插入（11000 静默跳过）→ `$inc` CAS → append 事件」三步幂等链。`weekKey` 原子领取 + 分阶段幂等 + `uniq_eventId` / `eventId` / `postId` 三重唯一保证断点续跑恰好一次。批量 `accountType` 查询失败 → 整批不结算、不公开、不发通知，续跑恰好一次（用例 25）。

## 测试策略

- **27 项自动化用例**，逐条映射到 `pm-plan.yaml:296-307` 六项验收；用例 22 为 P0 正确性闸门。清单见 `tasks.md` 第 9 节。
- 后端 TDD：先写失败测试，再实现，逐任务 `dotnet test --filter`；前端 `npx vitest run` 覆盖埋点白名单与 BFF 字段裁剪。
- 42 条既有环境性失败（本地 `appsettings.Local.json` 缺 Mongo/JWT/版主配置）用 `--filter` 隔离，不属本 change 回归。
- `change-review` 逐条核对本 change 三份 spec 的每个 SHALL / MUST 与非目标。

## Risks / Migration

| 风险 | 缓解 |
|---|---|
| 7d TTL 删除未结算记录导致漏结算 | D3 生命周期解耦 + 用例 22（P0） |
| 通知未读数泄露批次进度 | D6 三读取路径统一门禁 + 用例 26 |
| 草稿态被作者 / 版主 / 审核队列枚举 | D7 私有 `arena_pending` 态 + 用例 24 |
| LLM 失败导致本周无揭露 | 3 次退避 → 静态模板兜底，SHALL NOT 跳过揭露；模板路径同样过屏蔽词 |
| 多实例并发跑同一 weekKey | `reveal_jobs.weekKey` 唯一 + 原子领取 + `completed` 单 CAS |
| 整批部分对外 | D5 两阶段提交 + 用例 24/25 |
| #35 未来重复发通知 | D9 契约写死「常量标记、非发送状态」+ `uniq_eventId` 幂等 |

迁移：全为新增集合与新增端点，无存量数据迁移；`ForumContentStates` 新增常量不影响既有状态值。
