# bff-page-init-bundles Specification

## Purpose

定义 BFF 页面级聚合端点的行为契约，包括论坛首页初始化聚合（`/api/bff/forum-init`）和用户状态聚合（`/api/bff/me`），以减少浏览器到服务端的往返次数。

## Requirements

### Requirement: BFF 提供论坛首页初始化聚合端点

BFF SHALL 提供 `GET /api/bff/forum-init` 端点，在服务端并发调用 Model API 的多个只读端点，将结果聚合后一次性返回给前端，减少浏览器到服务端的往返次数。

#### Scenario: 匿名用户请求论坛初始化数据

- **WHEN** 客户端（未登录）GET `/api/bff/forum-init?page=1&pageSize=20`（boardId 可选）
- **THEN** BFF SHALL 并发调用以下端点并聚合结果：
  - `GET /api/forum/boards`（板块列表）
  - `GET /api/forum/announcements?limit=5`（公告）
  - `GET /api/forum/posts?page={page}&pageSize={pageSize}&boardId={boardId}`（帖子列表）
  - `GET /api/forum/tags/popular?boardId={boardId}`（热门标签）
  - 返回 `{ success: true, data: { boards, announcements, posts, popularTags, unreadCount: 0 } }`

#### Scenario: 已登录用户请求论坛初始化数据

- **WHEN** 客户端（已登录，携带 Bearer Token）GET `/api/bff/forum-init`
- **THEN** BFF SHALL 在上述 4 个并发请求基础上额外调用 `GET /api/forum/notifications/unread-count`，并将结果合并至 `unreadCount` 字段返回

#### Scenario: 某个下游服务局部失败时降级返回

- **WHEN** BFF 在并发聚合时某个下游端点（如热门标签）返回错误
- **THEN** BFF SHALL 将该字段设为空数组或零值（而非整体 500），并在顶层附加 `warnings` 字段标注哪些数据获取失败，其余字段正常返回

#### Scenario: boardId 参数传递到下游

- **WHEN** 客户端请求携带 `boardId` 查询参数
- **THEN** BFF SHALL 将该参数透传到帖子列表和热门标签两个下游请求，其余端点不受影响

---

### Requirement: BFF 提供用户状态聚合端点

BFF SHALL 提供 `GET /api/bff/me` 端点，需有效 Bearer Token，在服务端并发调用 Customer API 和 Model API，将用户 profile 与论坛状态一次性返回。

#### Scenario: 已登录用户请求个人状态

- **WHEN** 已登录客户端携带有效 Bearer Token GET `/api/bff/me`
- **THEN** BFF SHALL 并发调用以下端点并聚合：
  - Customer API `GET /api/profile`（昵称、生日、性别）
  - Model API `GET /api/forum/notifications/unread-count`（未读通知数）
  - 返回 `{ success: true, data: { profile: { nickname, gender, birthDate }, forum: { unreadCount } } }`

#### Scenario: 未登录时返回 401

- **WHEN** 客户端未携带 Bearer Token GET `/api/bff/me`
- **THEN** BFF SHALL 返回 HTTP 401，不调用任何下游服务

#### Scenario: Bearer Token 转发到下游

- **WHEN** BFF 调用 Customer API 和 Model API
- **THEN** BFF SHALL 将客户端请求中的 Bearer Token 原样附加到下游请求的 `Authorization` Header，不缓存或替换

#### Scenario: profile 获取失败时论坛状态仍返回

- **WHEN** Customer API 返回错误（如超时）
- **THEN** BFF SHALL 将 `profile` 设为 `null` 并在 `warnings` 中标注，`forum` 字段仍正常返回（若 Model API 成功）

---

### Requirement: 聚合端点不做变更操作

所有 BFF 聚合端点（`/api/bff/forum-init`、`/api/bff/me`）SHALL 仅执行只读下游调用，不修改任何数据。

#### Scenario: 聚合端点收到非 GET 请求

- **WHEN** 客户端对 `/api/bff/forum-init` 或 `/api/bff/me` 发送 POST、PUT、DELETE 等变更方法
- **THEN** BFF SHALL 返回 HTTP 405 Method Not Allowed
