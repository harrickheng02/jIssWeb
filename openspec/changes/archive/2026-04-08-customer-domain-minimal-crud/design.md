## Context

JIssWeb 已具备 User 服务鉴权、各域独立 API 与 Mongo/Redis 配置；Customer.Api 仅有健康检查与占位 `SampleController`。本变更在客档域引入最小持久化实体与 CRUD，使「登录后访问客档业务接口」成为可验证路径。Report、Accounting 按产品节奏后置：Report 待有真实数据再做聚合；Accounting 可与本变更并行做接口草图，但实现不在本变更范围。

## Goals / Non-Goals

**Goals:**

- 定义 MongoDB 集合与文档字段（最小字段集：如名称、备注、创建时间，及 `ownerUserId` 与 JWT `sub` 对齐）。
- 实现 REST 风格 CRUD，所有写操作与按用户维度的列表/查询必须基于当前令牌 `sub` 过滤或校验，禁止跨用户读写。
- 前端提供最小页面与路由，使用已登录 Bearer 调用 Customer API。
- 保持统一 `ApiResult` 响应信封与现有 JWT 校验方式一致。

**Non-Goals:**

- Report 报表聚合、导出、复杂查询。
- Accounting 账款业务实现与过账。
- 多租户、组织/团队、细粒度 RBAC（本变更仅「当前用户 = sub」隔离）。

## Decisions

1. **归属字段**  
   - 决策：每条客档文档包含 `ownerUserId`（字符串），写入与查询时等于 JWT `sub`。  
   - 备选：仅用 `userId` 命名。未采用：与既有 `sub` 语义文档一致，字段名明确为存储侧用户 id。

2. **路由与资源名**  
   - 决策：使用 `api/customers`（或 `api/customer-profiles`）前缀，与 `api` 根约定一致。  
   - 具体路径在实现时与 tasks 对齐，避免与现有 `api/sample` 冲突。

3. **与占位 Sample 的关系**  
   - 决策：保留 `GET /api/health`；`SampleController` 可保留或合并为健康检查文档中的引用，以 tasks 为准。

4. **Report / Accounting**  
   - 决策：本 design 不新增 Report/Accounting 代码路径；若需预留，仅在 openspec 或后续 change 中描述依赖顺序。  
   - Accounting「并行设计接口」：可选在单独 ADR 或后续 proposal 中列出 REST 草图，本变更 tasks 不强制。

5. **索引**  
   - 决策：对 `(ownerUserId, _id)` 或列表查询常用字段建立索引，避免全表扫描（具体以 Mongo 集合设计为准）。

## Risks / Trade-offs

- [风险] 未登录或 token 过期导致 CRUD 失败 → [缓解] 前端统一 401 提示与登录态；与现有 `ApiResult` 错误码对齐。  
- [风险] 字段过少导致后续扩展迁移 → [缓解] 文档中保留 `metadata` 或扩展字段占位（可选，由实现任务决定）。  
- [权衡] 最小 CRUD 与「客档」完整业务（联系人、标签）差距 → [缓解] 本变更明确 MVP，后续独立 change 扩展。

## Migration Plan

1. 部署新版本 Customer.Api 时 Mongo 空库即可创建集合与索引。  
2. 无旧数据迁移（新功能）。  
3. 回滚：移除新控制器与注册；保留 Mongo 数据不影响其他服务。

## Open Questions

- 客档最小字段集是否仅需「名称 + 备注」，或必须包含「电话/地址」其一。  
- 列表默认分页大小与排序方式。  
- Accounting 并行设计是否单独开 change 以免阻塞本变更。
