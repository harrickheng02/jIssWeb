## Why

Issue #5 屏蔽词硬拒绝（400）易激化争议；服务端 shadow 虽静默入库，但作者回首页刷「最新」刷不到自己的帖，仍会暴露拦截机制。产品改为**本地单机**：命中屏蔽词的帖/回复**不入库**，仅写入浏览器本地存储，由前端合并进作者 Feed/详情；清本地数据即消失，可接受。

## What Changes

- `Forum:BlockedWords:Handling` 调整为 `reject` | `local`（默认 **`local`**）；**移除 `shadow`**（若已实现则回滚）。
- `local` 模式：服务端 `POST` 发帖/回复命中屏蔽词时**不 Insert**、**不扣限流**、不写通知；返回 HTTP 200 与 `{ id, localOnly: true }` 等中性字段，不回显命中词。
- 前端：命中 `localOnly` 时将完整帖/回复写入 `localStorage`（按 `sub` 分桶）；首页 Feed、帖子详情、回复列表、我的帖子/回复对**当前浏览器**合并展示本地项；他人与换设备不可见。
- `reject` 保留 400 `BLOCKED_CONTENT`。

## Capabilities

### New Capabilities

- `forum-blocked-word-local-only`：本地存储模型、Feed/详情/回复合并规则、清数据语义、与 server 帖 ID 边界（`local:` 前缀）。

### Modified Capabilities

- `forum-anti-spam-placeholder`：`Handling` 为 `local` 时的无持久化成功响应；限流/通知不适用。

### Withdrawn（相对原 shadow 草案）

- 不再修改 `forum-content-api`、`forum-post-search`、`in-app-notifications` 的 shadow 可见性（服务端无 local 帖可读路径）。

## Impact

- **Model.Api**：Options/Filter/Controller 创建路径；回滚 shadow 读写与 `State: shadow`；集成测试改为 local 响应。
- **frontend**：新增 local 存储 composable、Feed/详情/回复/我的内容合并；遵循 forum-tokens；发帖成功仍跳详情（本地路由）。
- **服务边界**：仅 Model.Api + SPA；User/Gateway/BFF 无变更。

## 非目标

服务端保留敏感正文、跨设备同步、local 帖点赞/收藏/举报/版主可见、IndexedDB 加密、前端独立词表（仍以服务端判定为准）、草稿 publish / PUT 自编辑的屏蔽词处理、谐音检测、Mongo 词表。
