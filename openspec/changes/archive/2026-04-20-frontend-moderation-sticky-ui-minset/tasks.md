## 1. 角色识别与 UI 门禁

- [x] 1.1 在前端 auth store 解码 access token 的 `forumRole`，派生有效角色（member/moderator/admin）
- [x] 1.2 提供可复用的权限判断工具（例如 `canModerate`）供组件与路由使用

## 2. 置顶状态展示

- [x] 2.1 帖子列表卡片渲染置顶标记（依据 `isSticky`），对齐 `forum-homepage-shell` 增量规范
- [x] 2.2 帖子详情页渲染置顶标记（依据 `isSticky`）

## 3. 治理 API client

- [x] 3.1 增加 moderation API 调用封装：`POST /api/mod/posts/{postId}/sticky` 与 `GET /api/mod/audit`
- [x] 3.2 对 moderation API 的 401/403/404/5xx 做统一错误映射（复用既有 401 refresh 策略）

## 4. 详情页置顶/取消置顶操作

- [x] 4.1 在帖子详情页对 moderator/admin 显示“置顶/取消置顶”按钮，对 member 隐藏
- [x] 4.2 操作成功后刷新详情数据并给出成功反馈（toast/提示）
- [x] 4.3 操作失败时呈现明确错误（403 无权限、404 不存在、其他失败可重试）

## 5. 审计面板

- [x] 5.1 在帖子详情页提供“操作记录”入口（抽屉/弹窗）
- [x] 5.2 打开面板时按需加载审计列表并展示 **actionLabel**（人读操作说明）、**operatorDisplayName**（昵称等展示名）与 **occurredAtUtc**（时间）
- [x] 5.3 审计加载失败与空态展示（与其他列表一致的 loading/empty/error）

## 6. 导航入口与验收

- [x] 6.1 在应用壳对 moderator/admin 暴露治理入口（可先链接到任一帖子详情的治理区块或文档页）
- [x] 6.2 补一份手工验收步骤：如何用 `Forum:RoleOverrides`（按 `sub`）获得 moderator token，如何在 UI 上完成置顶与查看审计

