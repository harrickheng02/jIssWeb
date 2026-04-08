## Why

多服务骨架已具备 User 鉴权与各域 API 壳，但客档（Customer）尚无真实业务数据与持久化，无法验证「登录后跨服务调业务 API」的闭环。需要在 Customer 域先落地最小可读写实体与 Mongo 集合，并强制与 JWT `sub` 关联以实现数据隔离。

## What Changes

- 在 Customer.Api 增加最小客档实体 CRUD（创建、列表、按 id 查询、更新、删除），数据存 MongoDB，集合与文档模型在本变更中定义。
- 每条客档记录必须绑定当前登录用户（从 JWT `sub` 解析，禁止客户端自报为唯一主键来源）。
- 前端增加最小交互（列表/新建或等价入口），经 Vite 代理以 Bearer 调用 Customer API，完成「登录 → 调客档 API」。
- **明确不包含**：Report 只读聚合（待有真实数据后再做）；Accounting 业务实现后置（本变更可仅在 design 中保留接口与依赖顺序说明，不实现账款逻辑）。

## Capabilities

### New Capabilities

- `customer-record-crud`: 客档最小实体、Mongo 集合、CRUD API 契约、`sub` 归属与查询隔离规则。

### Modified Capabilities

- `customer-profile-service`: 在骨架之上增加「受保护客档业务端点」与数据归属要求（相对纯占位 Sample 的规格级扩展）。
- `frontend-app-shell`: 增加最小客档调用面（路由或页面）以完成登录后跨域请求验证。

## Impact

- `JIssWeb.Customer.Api`：新增控制器/应用层/持久化与 Mongo 集合。
- `JIssWeb.Common`：若需共享小 DTO/约定可复用；否则保持 Customer 项目内聚。
- `frontend`：新增视图与路由，扩展 `api` 客户端或复用 `customerApi`。
- 与 User 服务无契约 **BREAKING**；与现有 Customer `SampleController` 可能并存或由任务约定是否替换占位。
