## Context

- 后端现状：
  - 公开读接口 `GET /api/forum/posts`、`GET /api/forum/posts/{postId}` 已返回 `isSticky`，且非搜索列表排序已体现置顶优先。
  - 治理端点已交付：`POST /api/mod/posts/{postId}/sticky` 与 `GET /api/mod/audit`，并以 `forumRole` claim 控制角色边界。
- 前端现状：缺少“版主/管理员可感知”的 UI 入口、详情页治理按钮与审计面板；用户看到的页面结构与普通会员一致，导致治理能力不可演示。
- 约束：本变更只覆盖“置顶/取消置顶 + 审计可查”的最小闭环；其他治理操作后续在同一 UI 框架增量加入。

## Goals / Non-Goals

**Goals:**

- 前端在鉴权态解析 access token 中的 `forumRole`，并形成可复用的角色状态（member/moderator/admin）。
- 帖子列表卡片与帖子详情呈现置顶状态（`isSticky` 的视觉标记），与后端排序一致。
- 帖子详情页对 moderator/admin 暴露“置顶/取消置顶”操作按钮，并在操作后刷新详情/列表状态。
- 帖子详情页对 moderator/admin 暴露“操作记录”面板，展示审计记录（action/operator/time）。
- 错误与会话处理与既有前端鉴权规范一致：401 触发既有 refresh/登录引导；403/404/500 提供用户可理解的提示。

**Non-Goals:**

- 独立后台站点（admin portal）与复杂治理工作台（列表筛选、批量脚本、工单流）。
- 锁帖/精华/删帖/移动版区等操作。
- 对审计的复杂检索与导出。

## Decisions

### Decision: 治理 UI 嵌入帖子详情页而非独立后台页

- 理由：最小闭环需要“看见状态 + 立刻操作 + 立刻验证”，详情页天然具备上下文与状态展示。
- 备选：独立后台页可扩展性更强，但需要额外导航、列表与筛选，超出最小集。

### Decision: `forumRole` 在前端以轻量方式解析并缓存

- 在 auth store 中提供 `forumRole` 派生字段（从 token 解码 claim）。
- 理由：避免每次渲染都解码 token；UI 权限判断保持一致。

### Decision: API 调用与反馈策略

- `POST /api/mod/posts/{postId}/sticky` 成功后刷新当前帖子详情并触发列表缓存失效（若存在）以反映排序变化。
- `GET /api/mod/audit` 在“操作记录”抽屉/弹窗打开时按需加载，失败显示错误态。

### Decision: 错误码到 UI 提示的映射

- 401：走既有 refresh/登录引导路径。
- 403：提示“无权限操作该帖子”。
- 404：提示“帖子不存在或已删除”。
- 5xx/网络错误：提示“操作失败，请稍后重试”并保留重试按钮。

## Risks / Trade-offs

- **[Risk]** 版主范围受后端配置影响（Model.Api `Forum:Moderation:Moderators`），前端可能持续收到 403 → **Mitigation**：在任务中补充本地验收说明与开发配置样例；UI 对 403 提示指向“版区范围限制”语义。
- **[Risk]** 列表/详情对 `isSticky` 的字段命名与 DTO 不一致导致渲染缺失 → **Mitigation**：前端 DTO 明确 `isSticky` 字段并在接口层做映射/类型校验。
- **[Risk]** 操作后列表排序变化带来“页面跳动” → **Mitigation**：操作后仅刷新详情与当前列表页数据；在 UI 上短暂提示“已置顶/已取消置顶”。

