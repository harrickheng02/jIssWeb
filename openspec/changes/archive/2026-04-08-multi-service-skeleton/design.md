## Context

仓库已具备单体的 `JIssWeb` 前后端雏形（Vue3 + ASP.NET Core + Common）。现需将边界扩展为五个领域服务，仍使用同一技术栈与共享类库，并在开发环境可并行启动、前端可分别代理或配置各服务基址。

## Goals / Non-Goals

**Goals:**

- 单仓内清晰的后端目录：共享 `JIssWeb.Common`（或等价命名）与各 `*.Api` 项目；每个服务独立 `Program`、端口、`launchSettings`、Swagger。
- 用户服务提供 JWT 签发（占位实现即可）；其余四个服务配置相同的 JWT 校验参数（Issuer/Audience/SigningKey），仅验证令牌不签发。
- 各服务注册 MongoDB `IMongoClient` 与 Redis `IConnectionMultiplexer` 的配置节独立（如 `Mongo:ConnectionString` 带服务前缀或分配置文件），避免键冲突。
- 前端通过 Vite `server.proxy` 将 `/api-user`、`/api-customer` 等前缀映射到对应本地端口，axios 实例或 `baseURL` 按模块划分；登录后 `Authorization: Bearer` 由共享拦截器附加。
- 各服务暴露 `GET /api/health`（或统一路径）返回统一 `ApiResult` 形状，便于探活与联调。

**Non-Goals:**

- 真实业务表结构、领域规则、报表物化视图与消息队列事件编排。
- Kubernetes/服务网格与生产级网关配置（仅文档中保留后续对接点）。
- 跨服务分布式事务与 Saga。

## Decisions

| 决策 | 选择 | 理由 | 备选 |
|------|------|------|------|
| 仓库布局 | 单仓 `backend/src` 下多 `.Api` 项目 + 共享类库 | 与现有结构一致，降低复制 Common 成本 | 多仓（否决：运维与版本同步成本高） |
| 鉴权 | 用户服务签发 HS256 JWT；他服务对称密钥校验 | 骨架阶段最简单，与当前单体一致 | JWKS/OIDC（后续可换） |
| 端口分配 | 用户 5097；客档 5098；模型 5099；账款 5100；报表 5101（示例，可改） | 避免冲突、文档化 | 单端口 + Path 路由（需网关，超出骨架） |
| 前端聚合 | 单 SPA + 多 proxy 前缀 | 一个 `npm run dev` 即可 | 五套前端工程（否决） |
| 数据隔离 | 每服务独立 Mongo 库名配置 + Redis key 前缀常量 | 同集群下逻辑隔离 | 物理分集群（非骨架范围） |

## Risks / Trade-offs

| 风险 | 缓解 |
|------|------|
| 对称密钥分发到多服务配置文件 | 开发环境可共享；生产改环境变量/密钥管理，设计保留 `Jwt:Key` 单点配置说明 |
| 多端口防火墙/脚本遗忘 | `tasks.md` 中列出启动顺序与端口表；可选 `dotnet` solution 多启动配置 |
| 前端环境变量与代理不一致 | 单一 `env.development` 表列出各 `VITE_*` 与 proxy 对齐 |
| 报表服务误写业务库 | 规范报表 API 只读；代码层仅注入读库或空实现 |

## Migration Plan

1. 从当前单体 `JIssWeb.Api` 抽出仍通用的 Common 与配置，复制/引用到新用户服务与其他服务模板。
2. 保留原单体项目一段时间或标记废弃，待新骨架验证后删除或改为 BFF（可选）。
3. 回滚：Git 回退到拆分前提交；无线上数据迁移（骨架无业务数据）。

## Open Questions

- 生产环境是否引入 **API 网关**（YARP / Kong）统一入口与 TLS，还是前端直连多域名。
- 「模型」服务是否与其他服务存在强同步依赖（若需要，后续在 spec 中增加事件契约）。
