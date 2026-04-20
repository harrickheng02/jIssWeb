## 1. OpenAPI/契约对齐与路由规划

- [x] 1.1 明确治理端路由前缀与命名（采用 `POST /api/mod/posts/{postId}/sticky` 与 `GET /api/mod/audit`），并在相关服务路由表中落位
- [x] 1.2 对齐统一错误合同：为 401/403/404 场景补齐错误码与响应体格式（与现有统一错误中间件一致）

## 2. 数据模型与持久化

- [x] 2.1 为帖子持久化模型增加置顶字段（`IsSticky`、`StickyAtUtc`、`StickyBySub`）并确保读写路径覆盖
- [x] 2.2 新增审计日志存储模型（TargetType/TargetId/Action/OperatorSub/OccurredAtUtc/Metadata）
- [x] 2.3 定义并实现审计写入策略：治理操作成功时写审计记录，失败时保持一致性（可重试/幂等等价实现）

## 3. 权限与版区范围判定

- [x] 3.1 复用 Issue #17 的论坛角色 claim 解析能力，定义“管理员/版主”在服务内的统一判定函数
- [x] 3.2 实现“版主—版区”映射的最小来源（优先配置驱动：`sub -> boardIds`）
- [x] 3.3 在治理写接口中实现授权：管理员全站允许；版主仅允许其管理版区内帖子；其余返回 403

## 4. 治理写接口：置顶/取消置顶

- [x] 4.1 实现 `POST /api/mod/posts/{postId}/sticky`：校验 body `isSticky`，读取帖子存在性，执行授权判定
- [x] 4.2 持久化状态变更：写入 `IsSticky`、时间与操作者 `sub`
- [x] 4.3 写入审计：根据 `isSticky` 记录 `post.setSticky`/`post.unsetSticky`
- [x] 4.4 返回成功响应（按现有 API 成功包装合同），并保证重复调用具备幂等表现

## 5. 公开读接口反映与排序

- [x] 5.1 扩展 `GET /api/forum/posts` 返回项：增加 `isSticky`
- [x] 5.2 扩展 `GET /api/forum/posts/{postId}` 返回：增加 `isSticky`
- [x] 5.3 在非搜索列表中实现置顶优先排序：先按 `isSticky` 降序分组，再应用 `latest/hot` 的既有排序规则
- [x] 5.4 保持搜索排序语义：当 `q` 生效时沿用 `forum-post-search` 的排序，`isSticky` 仅用于展示

## 6. 审计查询接口

- [x] 6.1 实现 `GET /api/mod/audit`：支持 `targetType=post` 与 `targetId` 过滤并分页（或限制条数）
- [x] 6.2 对审计查询施加同等治理角色授权（管理员/版主），并返回统一错误合同

## 7. 测试与验收用例

- [x] 7.1 添加集成测试：管理员置顶成功，列表与详情反映 `isSticky=true`
- [x] 7.2 添加集成测试：管理员取消置顶成功，列表与详情反映 `isSticky=false`
- [x] 7.3 添加集成测试：普通用户调用治理接口返回 403（或按统一约定返回）
- [x] 7.4 添加集成测试：无 token 调用治理接口返回 401
- [x] 7.5 添加集成测试：治理操作写入审计记录，可通过审计查询接口检索到
- [x] 7.6 添加集成测试：带 `q` 的搜索列表按时间排序、不因置顶插队（`ForumPostsSearchStickySortTests`；验证命令：`dotnet test backend/tests/JIssWeb.Model.Api.Tests/JIssWeb.Model.Api.Tests.csproj`）

