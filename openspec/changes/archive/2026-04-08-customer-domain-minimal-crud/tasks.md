## 1. 数据模型与 Mongo

- [x] 1.1 在 Customer.Api 定义客档文档模型（含 `ownerUserId` 与最小业务字段）与集合名
- [x] 1.2 注册 Mongo 索引（至少保证按 `ownerUserId` 查询列表）

## 2. Customer.Api CRUD

- [x] 2.1 实现 `POST/GET/GET{id}/PUT/DELETE`（路径以 `api/customers` 为前缀，与规格一致）并从 `ClaimsPrincipal`/`sub` 取当前用户 id
- [x] 2.2 列表与单条读校验 `ownerUserId == sub`；越权返回 404 或 403（与实现约定一致）
- [x] 2.3 响应统一使用 `ApiResult` 信封

## 3. 前端

- [x] 3.1 增加路由与页面（如 `/customers`），使用 `customerApi` + Pinia token 调用列表与创建
- [x] 3.2 未登录时提示登录或跳转首页，不发起受保护请求

## 4. 验证

- [x] 4.1 手工验证：登录后 CRUD 全流程；换用户 token 无法访问他人记录
- [x] 4.2 （可选）Accounting/Report 仅记依赖顺序，本任务不实现代码
