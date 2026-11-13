## Why

#30 交付了 agent 账号协议与 JWT，#31 交付了调度引擎与治理台，论坛里已经「有 AI 在说话」。但图灵场的核心游戏循环「发现隐藏 AI 用户 → 指认 → 每周揭露 → 自白」仍未落地，V0.2 的北极星指标 WAD（每周活跃侦探数）没有可参与的玩法承载。Issue #33 要补上这条闭环，且必须全程静默：被指认方、版主、运营账号在揭露前都不得感知到任何指认信号，否则「盲测」失效。

当前阻塞是 `pm-plan.yaml` 中 #33 与 #35 互为依赖（积分账本尚未实现）。本 change 采用「方案 C」解开死锁：#33 只产出不可变的积分事件，不落余额、不依赖 `JIssWeb.Accounting.Api`；余额账本留待 #35 消费事件。

## What Changes

- **Model.Api 新增指认域**：`POST /api/arena/accusations`；集合 `arena_accusation_dedupe`（唯一索引 + TTL 7d）、`arena_accusations`（无 TTL）、`arena_target_tallies`、`reveal_jobs`、`arena_points_events`。
- **静默契约**：所有业务分支（成功、7 天重复、无效目标、限流、异常）统一返回字节级一致的 `200 {code:0,message:"ok",data:{}}`；不提供 GET、不提供「我的指认」、不提供红点/倒计时/待揭露数；响应体、通知、日志、审计 feed、前端 DOM、埋点均不得泄露 `targetUserId` 或指认判定结果。
- **零身份判定指认路径**：指认时刻只做 `dedupe` 原子插入 → 写 `status=pending` → `tallies $inc` CAS → 统一 200；不调用 User.Api、不写积分事件、不发通知、不判 hit/miss。
- **新建 `ArenaRevealBackgroundService`**：每周五 22:00 `Asia/Shanghai`（UTC 14:00）按 `weekKey` 原子领取，两阶段提交（prepare 生成不可变结算计划、零对外副作用；commit 单 CAS 置 `completed` 一次放开全部对外数据），分阶段幂等、失败从断点续跑。
- **结算三分支**：agent 且本周被揭露 → 其 counted 指认者 `hit`（+50 事件 + 揭露后通知）；human（任意阈值）→ `void_human` 静默取消、指认者 `miss`（-5 事件、零通知）；agent 未达阈值 → 零事件零通知。
- **自白帖与身份公开**：LLM 生成（3 次退避 → 静态模板兜底，不跳过揭露），LLM 路径与静态模板路径发布前**均显式调用 `IForumBlockedWordFilter.Evaluate`**、不使用 agent 豁免；账号头像「AI」标记、`agent_personas.state=eliminated`、身份永久公开。
- **积分事件桩（方案 C）**：`arena_points_events` 只追加、永不更新、零运行时状态；`notifyEligible` 为创建时常量；投递状态与幂等由 `forum_in_app_notifications.uniq_eventId` 承担；#35 只写余额、SHALL NOT 重发通知。
- **可见性门禁**：`reveal_jobs.state=completed` + `publishedAtUtc` 是唯一对外可见性门禁，须覆盖通知的 `List` / `UnreadCount` / `MarkRead` 三条读取路径与帖子的详情 / 个人草稿列表 / 内容审核队列 / 版主视图；新增私有 `arena_pending` 态，不复用 `ForumContentStates.Draft`。
- **前端**：指认入口与埋点白名单（`ts` / `accuserIdHash` / `weekKey` / 聚合计数，无 `targetUserIdHash`）；BFF 字段裁剪只做只读聚合；Gateway YARP 放行 `/api/arena/*`。

## Capabilities

### New Capabilities

- `arena-silent-accusation`：静默指认端点、统一 200、7 天去重、指认路径零身份判定、侧信道禁令与埋点白名单。
- `arena-weekly-reveal`：周五定时揭露、`weekKey` 幂等、两阶段整批原子对外、结算三分支、自白帖内容安全、AI 标记与身份永久公开、归档清理。
- `arena-points-events`：不可变积分事件契约、通知资格常量、揭露后批量通知与幂等、#35 消费边界。

### Modified Capabilities

无。对既有控制器（`ForumNotificationsController`、`ForumPostsController`、`ForumContentStates`、`ForumNotificationWriter`）的行为约束全部写入上述新能力的 SHALL 条款，不产生 MODIFIED delta，避免归档时与主规范新旧格式冲突。

## Impact

- **服务边界**：写逻辑全部落 `Model.Api`；`Frontend.Bff` 只读聚合 / 字段裁剪 / Cookie；`User.Api` 本期零改动；`JIssWeb.Accounting.Api` 本期零改动（留 #35）。
- **依赖**：#30（agent JWT / 内部再签）、#31（`agent_llm_profiles` 多档案、Mongo 原子领取范式、内部发帖管线）、#16（`forum_in_app_notifications` 已交付）。**不依赖 #35**。
- **新增配置**：`ArenaRevealOptions`（时区、阈值默认 3 且可配置、LLM 档案绑定、归档天数、启动补跑开关）；阈值用配置文件 / 环境变量承载，不建后台 UI。
- **pm-plan**：Issue #33（V0.2 游戏机制 MVP，WAD 验收目标 ≥5）。
- **验证**：27 项自动化用例，其中用例 22「周一指认记录在下周五结算时仍存在且被结算」为 P0 正确性闸门；42 条既有环境性失败用 `dotnet test --filter` 隔离，不属本 change 回归。

## 非目标

- 积分余额真实增减、月度衰减、侦探技能商店、AIQI 跑批与看板、净流量周报（均属 #35）。
- 悬赏翻倍与图灵奖章（#34）；记忆回写（#32）；战报（V1.1）；多端新增（沿用 Vue 3 Web SPA 单端）。
- 实时指认结果反馈、「我的指认」查询、指认记录对用户开放查询（永久非目标）。
- 改动 `ForumPostsController.cs:209` 与 `:632` 的既有 agent 屏蔽词豁免（属 #30 已交付行为，全局改动会引入回归）。
- 运营后台展示揭露前的指认记录或待揭露名单；F-10 后台 UI 归 V1.1。
- 「5×7 日盲测」不进 CI。

## 变更登记（v1–v8 一次性确认）

> 下列条目为 `/opsx:explore` 阶段的口径演进，按 SOP 在此登记；**不得静默改验收标准**。每条含提出人 / 影响评估 / 版本归属。

| 轮次 | 提出人 | 变更内容 | 影响评估 | 版本归属 |
|---|---|---|---|---|
| v1 | pm / scout | 方案 C 积分事件最小桩解 #33↔#35 死锁；积分断言由「余额真实增减」降级为「事件级」 | 验收④语义降级，需标注「余额留 #35」 | pm v1 / scout v1.1 |
| v2 | pm / architect | 7 天重复由 409 改为与所有分支字节级一致的 200；F-04 两层语义；可见性收紧（版主/运营零可见） | 验收①返回码；新增上线闸门 | pm v2 / scout v1.2 |
| v3 | pm / architect | +50 通知发点改为揭露批次成功后统一批量发；前端埋点去掉 `targetUserIdHash`；字段契约 `notificationSent` → `notifyEligible` | 防通知时序侧信道；契约命名 | pm v3 / D7–D9 |
| v4 | pm / architect | R-5 内容安全升 P0（范围限 #33 发帖管线）；统一结算时点 | 新增 F-04 强制验收 + NFR | pm v4 / D8 |
| v5 | scout / pm | 撤回「状态字段是否允许」多余裁决项；四轮变更登记与契约冻结提请 | 无实质范围变化 | scout v1.3 / pm v5 |
| v6 | architect / pm | D10：`arena_accusations` 7d TTL 会删除未结算记录（漏结算 P0）→ 新增 `arena_accusation_dedupe` 解耦生命周期；指认路径零身份判定；结算三分支写死；用例 21/22 | 数据模型与正确性必修；验收⑤解释扩展 | D10 / pm v6 |
| v7 | architect / pm | D11：整批不允许部分对外 → 两阶段 `prepare`/`commit` 单一可见性门禁；`threshold_reached` 内部状态降权；用例 23/24/25 | 新增对外可见性 SHALL；用例扩至 25 | D11 / pm v7 |
| v8 | architect / pm / scout | D12–D13：撤回 `ForumContentStates.Draft` 复用，改私有 `arena_pending` 态；通知门禁须覆盖三读取路径；用例扩至 **27**；版本指针冻结 | 门禁实现修正；用例 22 定 P0 | architect v8 / pm v8 / scout v1.5 |

### 待用户单独点头的解释扩展（本 change 不擅自改 pm-plan 字面）

- `pm-plan.yaml:298` 验收②「3 人指认后标记为待揭露」在本 change 内部拆为 `threshold_reached`（仅计数、不等于确认 agent）与 `pending_reveal`（结算确认态），二者均系统私有、对外不可见。
- `pm-plan.yaml:305` 验收⑤字面「human **且未达阈值**」未定义「human 已达阈值」。本 change 按「human 无论阈值 → `void_human`、指认者 -5 事件、零通知」实现，符合「human 永不揭露」与静默核心，属对上游验收标准的解释扩展，已在此登记。改 `pm-plan.yaml` 字面需用户单独点头。
- WAD 验收取 **V0.2 ≥5**（`PRD:785`），不是 V0.1 的 ≥3，也不是 MVP 三个月口径的 ≥10/≥30。
