## Why

当前前端直接按服务前缀访问 `user`、`customer`、`model`、`accounting`、`report` 等 API，开发期依赖 Vite 代理，缺少统一入口、统一路由治理与面向前端的聚合层。随着后续引入 Cloudflare、Nginx 负载均衡、更多业务服务与生产环境部署，仓库需要先建立 YARP API Gateway 与 ASP.NET Core BFF 的基础骨架，明确各层职责与路由边界。

## What Changes

- 新增 **YARP API Gateway** 基础能力：统一对内路由、转发、认证头透传、服务前缀治理与网关进程启动约束。
- 新增 **BFF** 基础能力：面向前端的统一 API 入口、聚合接口约束、前端专属响应整形与会话边界。
- 明确 **Nginx / YARP / BFF / 业务服务** 的职责切分：Nginx 做外层反向代理与负载均衡，YARP 做服务路由与流量治理，BFF 做前端编排，业务服务保留领域职责。
- 调整现有前端接入模型，使前端逐步从“直连多服务前缀”迁移到“统一入口 + BFF/网关”模式。
- 补充本地与 Docker 运行路径，使网关、BFF、业务服务与依赖的连接方式在宿主机和容器网络下都有明确规范。

## Capabilities

### New Capabilities
- `yarp-api-gateway`: YARP 网关进程、路由表、Cluster/Route 配置、请求透传与统一入口治理。
- `frontend-bff-service`: 面向 SPA 的 BFF 服务骨架、聚合端点边界、前端专属响应模型与会话代理约束。

### Modified Capabilities
- `frontend-app-shell`: 前端 API 调用入口从多服务开发代理模式演进为统一入口模式，减少前端直接感知后端服务拓扑。
- `docker-compose-project`: Docker 组合项目需要纳入网关/BFF 相关服务或预留结构，并明确环境变量模板。
- `docker-runtime-connectivity`: 文档与配置需要覆盖 Nginx → YARP → BFF → 业务服务的本地/容器网络连通方式。

## Impact

- **后端**：新增 YARP Gateway 项目与 BFF 项目（或等价服务），调整服务发现/转发配置。
- **前端**：`clients.ts`、代理配置、认证与业务请求入口将逐步收口。
- **部署**：Nginx、Docker Compose、环境变量、网关/BFF 端口规划、未来生产流量入口模型。
- **架构治理**：统一认证透传、错误码整形、聚合接口、限流/熔断/观测扩展点。
