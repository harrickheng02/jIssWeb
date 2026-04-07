## 1. 共享基础与解决方案

- [x] 1.1 确认 `JIssWeb.Common` 可被多 Api 引用；必要时拆出仅 Web 需要的扩展，避免循环依赖
- [x] 1.2 在 `backend/src` 新增或调整 `JIssWeb.sln`，纳入五个 `*.Api` 项目与 Common、Application、Infrastructure（若按服务复制则每服务一套 Infrastructure 或共享注册扩展）
- [x] 1.3 统一各服务的 `ApiResult`、异常中间件、Swagger JWT 定义与 `appsettings` 中 `Jwt`/`Mongo`/`Redis` 节命名策略

## 2. 用户服务（签发 JWT）

- [x] 2.1 创建 `JIssWeb.User.Api`（或约定名称），配置独立端口（如 5097）
- [x] 2.2 实现 JWT Bearer 与至少一个签发占位接口（返回可校验的 token）
- [x] 2.3 添加受保护路由样例与 `GET /api/health`，验证 401/200 行为

## 3. 客档服务

- [x] 3.1 创建 `JIssWeb.Customer.Api`，端口 5098，仅校验 JWT、不签发
- [x] 3.2 注册 Mongo/Redis 与服务专属配置键；暴露 Swagger 与 `GET /api/health`

## 4. 模型服务

- [x] 4.1 创建 `JIssWeb.Model.Api`，端口 5099，校验 JWT
- [x] 4.2 占位控制器与基础设施注册；`GET /api/health`

## 5. 账款服务

- [x] 5.1 创建 `JIssWeb.Accounting.Api`，端口 5100，校验 JWT
- [x] 5.2 `GET /api/health` 与占位 API

## 6. 报表服务

- [x] 6.1 创建 `JIssWeb.Report.Api`，端口 5101，校验 JWT
- [x] 6.2 骨架仅暴露 GET 类占位与 `GET /api/health`

## 7. 前端应用壳

- [x] 7.1 在 `frontend` 配置 Vite 代理：`/api-user`→5097、`/api-customer`→5098、`/api-model`→5099、`/api-accounting`→5100、`/api-report`→5101（端口以 design 为准可调整但需前后一致）
- [x] 7.2 增加 axios 封装：登录后写入 token、请求拦截器附加 Bearer
- [x] 7.3 增加简单页面或面板，依次请求各服务 health 或占位接口并展示结果

## 8. 验证与收尾

- [x] 8.1 本地同时启动五后端 + 前端，`dotnet build` 与 `npm run build` 通过
- [x] 8.2 对照 `specs/**/spec.md` 逐条做一次手工验收勾选
- [x] 8.3 若废弃原单体 `JIssWeb.Api`，在仓库内标注迁移说明或从 solution 移除（与团队约定一致）
