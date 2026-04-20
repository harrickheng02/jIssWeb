## Context

- 规划来源：`scripts/github-sync/pm-plan.yaml` Issue #7「版主版区与帖子操作后台（最小集）」。
- 前置能力：Issue #17「论坛角色与 JWT 声明最小集（管理员 / 版主）」已交付，Model.Api 可从 token 中读取论坛角色 claim。
- 现状：论坛内容 API 已覆盖帖子列表与详情、发帖与回复等；治理能力（版主操作、审计）需在 M3 形成可演示闭环，并为后续举报处理与反垃圾占位复用。
- 约束：
  - 最小集只交付“帖子置顶/取消置顶”一种治理写操作（便于演示与测试），并让列表与详情一致反映状态。
  - 必须具备可核验审计字段（操作者 `sub`、目标帖子、时间、动作），并提供查询接口。

## Goals / Non-Goals

**Goals:**

- 提供帖子治理写接口：置顶 / 取消置顶，并持久化到帖子记录。
- 提供治理审计记录：每次治理操作写入审计日志，并支持按帖子查询。
- 建立权限边界：
  - 管理员可对全站帖子执行治理操作。
  - 版主可对其管理版区内的帖子执行治理操作（依赖“版主—版区”映射来源）。
- 在 `GET /api/forum/posts` 与 `GET /api/forum/posts/{postId}` 中返回置顶状态字段，且在列表排序中体现置顶优先。

**Non-Goals:**

- 完整 RBAC 中台、动态权限引擎、跨版区批量脚本、工单流。
- 精华、锁帖、删帖、移动版区等更复杂治理操作（可在同能力 spec 后续扩展）。
- 复杂审计检索（多维筛选、导出、SLA）。

## Decisions

### Decision: 以“置顶/取消置顶”作为最小治理操作

- 理由：对 UI 可见性强、对业务语义影响可控、实现联动面较小（不触发发帖/回帖链路规则变更）。
- 备选方案：锁帖/解锁覆盖面更广但需要联动回复创建校验；精华/取消精华需要额外筛选与展示规则。两者适合作为后续增量。

### Decision: 治理写接口与审计查询采用独立路由前缀

- 建议路由：
  - 治理写：`POST /api/mod/posts/{postId}/sticky`（body 指定 `isSticky`）
  - 审计查询：`GET /api/mod/audit`（按 `targetType=post&targetId=...` 查询）
- 理由：治理接口在鉴权与审计方面与公开读接口边界不同，采用独立前缀便于网关/BFF 做策略与日志分流。

### Decision: 帖子记录内持久化置顶状态，同时写入审计日志集合

- 帖子字段（最小集）：
  - `IsSticky`（bool）
  - `StickyAtUtc`（datetime?）
  - `StickyBySub`（string?）
- 审计集合字段（最小集）：
  - `TargetType`（固定 `"post"`）
  - `TargetId`（postId）
  - `Action`（`post.setSticky` / `post.unsetSticky`）
  - `OperatorSub`
  - `OccurredAtUtc`
  - `Metadata`（可选：boardId/boardTitle、old/new 值）
- 理由：帖子读路径低成本读取状态；审计独立存储便于后续扩展筛选与保留策略。

### Decision: 版主权限以“帖子的版区归属 + 版主可管理版区集合”判定

- 版主—版区映射来源（最小集实现优先级）：
  - ① 配置驱动（例如 `Forum:Moderators` 映射 `sub -> boardIds`），便于本地验收与联调。
  - ② 后续演进为持久化配置（集合/表），与后台管理页面联动。
- 判定规则：
  - 管理员：允许。
  - 版主：当且仅当目标帖子的 `BoardId`（或可反推出的 board 标识）在其可管理集合中时允许。

### Decision: 治理写接口 403 的两种语义

- **`RequireForumModerator` 未通过**：token 有效但 `forumRole` 仅为 member（或等效不可用角色）→ HTTP **403**，`ApiResult` 文案「无权访问」，code **`FORBIDDEN`**。
- **版主版区范围未覆盖目标帖**：`forumRole` 为 moderator，但帖子所属版块不在该版主配置的 `boardIds` 内 → HTTP **403**，文案「**无权操作该帖子**」，code **`FORBIDDEN`**。
- 排障时先确认 **User.Api** `Forum:RoleOverrides` 与 **Model.Api** `Forum:Moderation:Moderators` 是否指向同一 `sub`，且版区 id 与帖子一致。

## Risks / Trade-offs

- **[Risk]** 帖子记录当前只有 board title，缺少稳定 `boardId` 时，版主权限判定可能不稳定 → **Mitigation**：以 `boardId` 作为帖子归属的规范字段写入/返回（若现有实现仅存 title，则在实现阶段补齐双写或映射策略），并在 specs 中明确定义判定字段。
- **[Risk]** 置顶排序与现有 `sort=hot/latest`、`q` 搜索排序组合规则不明确 → **Mitigation**：在 `forum-content-api` 增量 spec 写清非搜索列表置顶优先；主规范 `openspec/specs/forum-post-search/spec.md` 已定义带 `q` 时按创建时间排序且 `isSticky` 不参与排序；实现见 `ForumPostsController.BuildPostListSortDefinition`，覆盖用例见 `ForumPostsSearchStickySortTests`。
- **[Risk]** 审计与帖子状态更新的原子性（写一半失败） → **Mitigation**：实现阶段以同一请求内的顺序写入策略为准，并在失败时返回统一错误；若引入事务成本过高，至少保证幂等与可重试（见 tasks 的实现要求）。

