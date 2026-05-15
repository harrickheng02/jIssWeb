## 1. 后端数据模型与索引

- [x] 1.1 在 `ForumPostRecord.cs` 中新增 `IsFeatured bool`、`FeaturedAtUtc DateTime?`、`FeaturedBySub string?` 字段，确认 BsonElement 映射
- [x] 1.2 在 MongoDB 索引初始化代码中追加复合索引 `{ IsFeatured: 1, FeaturedAtUtc: -1 }`（background build）

## 2. 后端版主端点实现

- [x] 2.1 在 `ModPostsController` 中新增 `POST /api/mod/posts/{id}/featured` 端点，接收 `{ "isFeatured": bool }` body，调用 `ForumModerationAccessService` 做鉴权
- [x] 2.2 实现加精逻辑：`isFeatured=true` 时写入 `IsFeatured=true`、`FeaturedAtUtc=UtcNow`、`FeaturedBySub=sub`；`isFeatured=false` 时三字段清空
- [x] 2.3 在加精/取消精华成功后写审计记录，actionLabel 分别为 "加精"/"取消精华"，与 sticky 审计模式对称

## 3. 后端查询过滤与排序

- [x] 3.1 在 `GET /api/forum/posts` 的查询逻辑中新增 `featured` query param 解析，`featured=true` 时追加 `IsFeatured=true` filter
- [x] 3.2 实现精华帖排序：`featured=true` 且无 `q` 时，按 `FeaturedAtUtc` 降序；null 回退 `CreatedAtUtc` 降序（禁止被 `sort` 参数覆盖）
- [x] 3.3 在帖子公开 DTO 中追加 `isFeatured` 字段，列表和详情接口均返回

## 4. 后端集成测试

- [x] 4.1 在 `JIssWeb.Model.Api.Tests` 中新增 `ForumFeaturedOperationsTests` fixture：覆盖加精成功、取消精华成功、无权限返回 403、不存在帖子返回 404
- [x] 4.2 新增 `ForumPostsFeaturedFeedTests` fixture：覆盖 `featured=true` 返回精华帖、精华排序规则（FeaturedAtUtc 降序）、与 boardId/tag 组合 filter、invalid `featured` value 返回 400
- [x] 4.3 新增审计日志验证：加精与取消精华写入正确 actionLabel 和操作人 sub

## 5. 前端 API 层

- [x] 5.1 在 `frontend/src/api/clients.ts` 的帖子 DTO 类型中追加 `isFeatured: boolean` 字段
- [x] 5.2 在 `clients.ts` 中新增 `setForumPostFeatured(postId: string, isFeatured: boolean)` 函数，调用 `POST /api/forum/mod/posts/{id}/featured`（复用 `createClient`，不另起 axios 实例）
- [x] 5.3 在 `getForumPosts` 函数的 params 类型中追加可选 `featured?: boolean`，并在调用时透传到 query string

## 6. 前端 composable 与 feed 激活

- [x] 6.1 在 `useForumHomeFeed.ts` 中修改 `feedSort==='featured'` 分支，传入 `featured: true` 参数调用 `getForumPosts`，移除 fallback 到 undefined 的占位逻辑

## 7. 前端组件——治理面板按钮

- [x] 7.1 在 `ForumPostGovernancePanel.vue` 中复用 `toggleSticky` 模式，新增 `toggleFeatured` 方法，调用 `setForumPostFeatured`
- [x] 7.2 新增加精/取消精华按钮 UI，使用 `el-button` 并通过 `forum-tokens.css` 变量控制样式，按钮文案根据当前 `isFeatured` 状态切换

## 8. 前端组件——精华角标

- [x] 8.1 在帖子列表卡组件（标题区）和帖子详情页（标题区）中，当 `isFeatured===true` 时展示「精华」角标，样式仅用 `forum-tokens.css` 变量，不引入新 CSS 文件

## 9. 验证与收尾

- [x] 9.1 运行前端构建 `cd frontend && npm run build` 确认无类型错误
- [x] 9.2 运行后端测试 `cd backend && dotnet test tests/JIssWeb.Model.Api.Tests` 确认全部通过
- [x] 9.3 本地端到端验证：启动 Model API，测试加精端点、精华 feed tab、GovernancePanel 按钮、精华角标展示
