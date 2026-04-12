## 1. 数据与索引

- [x] 1.1 定义通知 Mongo 文档模型（RecipientSubId、类型、关联帖子/回复、ActorSubId、ReadAt、时间戳）
- [x] 1.2 创建集合并为 RecipientSubId + 时间字段建索引

## 2. API（Model / 论坛宿主）

- [x] 2.1 实现 `GET /api/forum/notifications`（JWT、分页、仅当前 `sub`）
- [x] 2.2 实现标记已读（单条与全部）且幂等
- [x] 2.3 在创建回复成功路径写入「回复帖子」通知（排除自回复），与回复持久化同事务或等价一致性（等价：同一请求内顺序写回复→更新评论数→插通知；通知失败则请求失败；未用 Mongo 多文档事务，单机/无副本集环境常见）

## 3. 前端

- [x] 3.1 顶栏登录态通知入口并跳转通知列表路由
- [x] 3.2 通知列表页：加载中、空列表、请求失败三种可区分状态
- [x] 3.3 对接列表与已读 API；按需展示未读（若实现顶栏数字则一并接线）

## 4. 验证

- [x] 4.1 集成或 API 测试：跨用户回复产生通知、自回复不产生、列表仅本人、已读变更生效（`dotnet test backend/tests/JIssWeb.Model.Api.Tests/JIssWeb.Model.Api.Tests.csproj`）
