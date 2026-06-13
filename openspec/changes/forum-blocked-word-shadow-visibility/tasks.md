## 0. 回滚 shadow 实现（若已落地）

- [x] 0.1 移除 `State: shadow`、`ForumContentStates.Shadow`、shadow 读写过滤与相关集成测试
- [x] 0.2 `Handling` 枚举改为 `reject` | `local`（默认 `local`），删除 `shadow` 分支

## 1. 后端 local 响应

- [x] 1.1 `ForumBlockedWordsOptions` / `Evaluate()`：`Local` 结果；`Handling` 默认 `local`；更新 appsettings 注释
- [x] 1.2 `Create` / `CreateReply`：local 命中时不 Insert、不扣限流；200 + `{ id: "local:…", localOnly: true, state: "local" }`
- [x] 1.3 集成测试 `ForumBlockedWordLocalCreateTests`：无 Mongo 记录、reject 回归、响应不含命中词、local 不扣限流

## 2. 前端 localStorage

- [x] 2.1 新增 `forumLocalContent` 模块（按 `sub` 分桶读写 posts/replies）
- [x] 2.2 发帖/回复：`localOnly` 时写本地并跳详情；compose 仍显示「已发布」
- [x] 2.3 Feed 合并 local 帖；`local:` 详情与回复列表读本地 + 合并 server 回复
- [x] 2.4 `/me/posts`、`/me/replies` 合并本地项
- [x] 2.5 Vitest：`forumLocalContent` 合并与分桶

## 3. 验证

- [x] 3.1 `dotnet test --filter "FullyQualifiedName~ForumBlockedWord"`
- [x] 3.2 `cd frontend && npx vitest run`（ touched specs）
- [ ] 3.3 浏览器：发敏感帖 → 首页最新可见 → 清 localStorage 后消失

## 4. 归档准备

- [ ] 4.1 change-review 对照 `forum-blocked-word-local-only` 与 `forum-anti-spam-placeholder` delta；合并后 `/opsx:archive`
