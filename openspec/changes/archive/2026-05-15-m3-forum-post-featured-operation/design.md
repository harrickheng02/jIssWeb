## Context

论坛帖子治理已有置顶（`IsSticky`）和锁回复（`RepliesLocked`）功能，均由 `ModPostsController` 提供版主端点，并通过 `ForumModerationAccessService` 做鉴权，写审计记录到 `ForumAuditLogRecord`。加精是同类型的帖子元数据操作，与置顶的设计模式完全对称，唯一差异是加精需要额外的时间戳和操作人信息以支持精华列表排序。

前端 `HomeView.vue` 已有 `featured` tab 和 `useForumHomeFeed` composable，但 `feedSort==='featured'` 时没有实际传参，数据等同于 `latest`。本次工作是把这条数据通路打通。

## Goals / Non-Goals

**Goals:**

- 在 `ForumPostRecord` 上持久化加精状态（字段 + MongoDB 索引）
- 暴露 `POST /api/mod/posts/{id}/featured` 端点，版主鉴权 + 审计日志
- `GET /api/forum/posts` 支持 `featured=true` 独立 filter
- 精华帖按 `FeaturedAtUtc` 降序排序（null 回退 `CreatedAtUtc` 降序）
- 公开 DTO 暴露 `isFeatured` 字段
- 前端激活精华 feed（composable + API 层），`ForumPostGovernancePanel` 加加精按钮，列表卡/详情页加精华角标

**Non-Goals:**

- 沉帖操作
- 加精多级评级
- 批量操作
- 精华帖专属详情页

## Decisions

### D1：字段设计——记录操作时间与操作人

**决策**：新增 `IsFeatured bool`、`FeaturedAtUtc DateTime?`、`FeaturedBySub string?`，而不是只加布尔值。

**理由**：精华 feed 的排序需要 `FeaturedAtUtc`（新加精的优先展示）；`FeaturedBySub` 与已有 `IsSticky` + `LockedBySub` 的模式对称，审计链路复查时可追溯到具体版主，避免查 AuditLog 集合。

**备选方案**：只存 `IsFeatured`，时间和操作人仅写审计记录——被否，因排序查询需从 `ForumPostRecord` 自身取字段。

### D2：排序规则——`FeaturedAtUtc` 降序 + null fallback

**决策**：精华帖列表（`featured=true` filter）按 `FeaturedAtUtc` 降序；若 `FeaturedAtUtc` 为 null（历史数据）则以 `CreatedAtUtc` 降序兜底。

**理由**：新加精的内容比旧加精的更有推荐价值；null 兜底保证历史数据迁移零风险。

**备选方案**：按 `CreatedAtUtc` 统一排序——被否，失去精华 feed 的编辑策展语义。

### D3：`featured` filter 与其他 filter 正交

**决策**：`featured=true` 作为独立 query param，与 `boardId`、`tag`、`q` 可自由组合（包括 `q` + `featured` 叠加）。当 `q` 存在时，`featured` 仅作过滤条件，不改变搜索排序（遵循 `forum-post-search` 规范）。

**理由**：精华帖筛选是元数据维度，不应与现有 boardId/tag 语义冲突；同时允许 "精华帖中搜索关键词" 场景。

### D4：MongoDB 复合索引 `{ IsFeatured: 1, FeaturedAtUtc: -1 }`

**决策**：建立此复合索引，而非单字段索引。

**理由**：精华 feed 的查询模式是 `WHERE IsFeatured=true ORDER BY FeaturedAtUtc DESC`，复合索引可以用单次 index scan 完成，避免全集合扫描。

### D5：前端加精按钮复用 `toggleSticky` 模式

**决策**：在 `ForumPostGovernancePanel` 中按 `toggleSticky` 的事件 + API 调用模式新增 `toggleFeatured`，而不是抽象通用 `toggleModerationFlag`。

**理由**：两者参数和 UI 反馈基本对称，短期内抽象收益低、风险高。代码复用通过同文件复制模式实现，不引入额外抽象层。

### D6：精华角标——轻量 inline badge

**决策**：在列表卡标题区和详情页标题区展示一个文字角标（如「精华」），样式仅用 `forum-tokens.css` 变量，不引入新的 CSS 文件。

**理由**：与置顶角标的处理方式一致；保持 token-driven 样式约束。

## Risks / Trade-offs

- **[风险] 历史帖子 `FeaturedAtUtc` 为 null** → 缓解：排序时 null 回退 `CreatedAtUtc` 降序，查询层面无需数据迁移脚本
- **[风险] `featured=true` + `sort=hot` 组合行为未明确** → 缓解：在 spec 中明确：`featured=true` 仅过滤，排序遵循 `sort` 参数（hot/latest）；`q` 存在时搜索排序优先
- **[风险] 前端 `useForumHomeFeed` 修改影响已有 tab** → 缓解：修改仅在 `feedSort==='featured'` 分支内，其他 tab（latest/hot）无副作用
- **[取舍] 不做 featured 帖子置顶前置** → 精华帖在 `featured=true` filter 下不自动 sticky 置顶；版主若需置顶精华帖仍需单独操作。保持两个状态正交，降低复杂度

## Migration Plan

1. 发布后端：`ForumPostRecord` 加字段（MongoDB schema-less，存量文档无需迁移）+ 索引建立（可后台 background build，不锁集合）+ 新端点
2. 发布前端：激活 featured feed + GovernancePanel 按钮 + 角标
3. 无需回滚脚本：新字段默认 false/null，存量帖子不受影响

## Open Questions

- 无（所有设计决策已在 proposal 阶段与需求方对齐）
