## Why

业务按领域拆分为五个独立服务（用户、客档、模型、账款、报表），需要一套与现有技术栈一致、可本地一键跑通的前后端骨架，以便团队并行开发并统一约定（鉴权、响应格式、配置与健康检查），避免后续联调时再返工。

## What Changes

- 在后端按五个领域各提供独立的 ASP.NET Core Web API 可运行项目（或等价可部署单元），共享 Common 类库（统一响应、异常处理、公共配置约定）。
- 用户服务负责 JWT 签发；其余服务仅校验 JWT 并暴露各自 Swagger。
- 为 MongoDB、Redis 保留按服务隔离的配置节与注册占位（连接串、库名/前缀）。
- 前端提供单页应用骨架：Pinia、路由、Element Plus、按服务划分的 API 基址或开发代理条目，能调用各服务健康检查或占位接口。
- 不实现真实业务逻辑与持久化细节，仅保留可扩展的目录与依赖注入占位。

## Capabilities

### New Capabilities

- `shared-foundation`: 跨服务共享的 API 契约与横切能力（统一响应结构、全局异常、配置节命名、健康检查约定）。
- `user-service`: 用户与认证领域骨架（JWT 签发端点占位、用户相关路由命名空间）。
- `customer-profile-service`: 客档领域 API 骨架与路由前缀。
- `model-service`: 模型领域 API 骨架与路由前缀。
- `accounting-service`: 账款领域 API 骨架与路由前缀。
- `report-service`: 报表领域 API 骨架与路由前缀（只读查询占位）。
- `frontend-app-shell`: Vue3 前端应用壳、多服务代理或 baseURL 策略、与 Bearer 传递方式。

### Modified Capabilities

- （无现有 spec，此项留空。）

## Impact

- 仓库目录结构从单 Api 扩展为多服务项目与共享库；CI 需支持多 `dotnet` 项目构建。
- 本地开发需多端口或网关；`launchSettings`/Vite proxy 需同步更新。
- 运维与环境变量中 JWT、Mongo、Redis 配置项按服务倍增或按前缀区分。
