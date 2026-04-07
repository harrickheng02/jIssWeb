## Context

项目为多 ASP.NET Core API + Vue 前端，配置中 MongoDB、Redis 默认指向 `localhost`。当前无 Docker 文件，开发者需自行安装数据库。目标是在不改变应用代码契约的前提下，用 Compose 提供可重复的依赖运行环境。

## Goals / Non-Goals

**Goals:**

- 提供 `docker-compose.yml`（可辅以 override）启动 MongoDB 与 Redis，端口与现有默认配置兼容或可通过环境变量覆盖。
- 文档化「宿主机跑 API + 容器跑依赖」与「未来全容器」两种场景下的连接串写法。
- 使用官方或广泛维护的基础镜像，固定主版本号以降低漂移。
- 提供 `.dockerignore` 规则模板，避免将构建产物打入镜像。

**Non-Goals:**

- 不规定生产环境 Kubernetes/Helm 形态。
- 不强制将所有五个 API 打包为同一镜像；若提供 Dockerfile，以单服务可扩展为原则。
- 不在本变更中实现自动迁移脚本或数据初始化业务数据。

## Decisions

| 决策 | 选择 | 理由 |
|------|------|------|
| 编排入口 | 仓库根目录 `docker-compose.yml`（或 `compose.yaml`） | 常见约定，IDE 与 CI 易发现 |
| 依赖镜像 | `mongo` 与 `redis` 官方镜像，标签带小版本 | 可复现、社区更新 |
| 数据持久化 | 命名卷（named volumes）挂载 Mongo 数据目录 | 重启容器不丢开发数据 |
| 网络 | 默认 Compose 网络，服务名作 DNS | 全栈容器内 API 用服务名连库 |
| 宿主机调试 | 映射 `27017`/`6379` 到宿主机 | 与 `localhost` 连接串一致 |
| 可选应用镜像 | 多阶段 `.NET SDK` 构建 + `aspnet` 运行时 | 若实施，减小镜像体积 |

## Risks / Trade-offs

| 风险 | 缓解 |
|------|------|
| Windows 与 Linux 路径/换行差异 | Compose 使用 POSIX 路径风格；文档注明 CRLF |
| 端口与本地已装 Mongo/Redis 冲突 | 文档说明改 `.env` 映射端口并同步改 `appsettings` |
| 镜像拉取失败或慢 | 文档可选国内镜像源说明；不写入仓库 |
| 全栈 Compose 与 Vite HMR | 若纳入前端 dev 容器，需单独卷挂载源码；首版可仅依赖服务 |

## Migration Plan

1. 合并后开发者安装 Docker Desktop（或等价）。
2. `docker compose up -d` 启动依赖；验证 `mongosh`/`redis-cli` 或应用健康检查。
3. 若端口调整，更新本地 `appsettings.Development.json` 或 User Secrets。
4. 回滚：删除容器与卷（注意数据）；恢复使用本机安装的数据库。

## Connectivity examples

**宿主机运行 API、Compose 仅起 Mongo/Redis（端口映射到本机）**

- MongoDB：`mongodb://localhost:27017`（若 `.env` 中 `MONGO_PORT` 改为其他端口，则改为对应端口）
- Redis：`localhost:6379`（同上，随 `REDIS_PORT`）

**API 与数据库均在同一 Compose 网络内（例如启用 `--profile api` 的 `user-api` 服务）**

- MongoDB：`mongodb://mongo:27017`（主机名为 Compose 服务名 `mongo`）
- Redis：`redis:6379`（主机名为服务名 `redis`）

应用内通过 `Mongo__ConnectionString`、`Redis__ConnectionString` 等环境变量覆盖 `appsettings` 即可，无需改代码。

## Open Questions

- 是否在首版 Compose 中包含五个 API 的 `dotnet watch` 服务（开发体验好但资源占用大）。
- 生产镜像是否由本仓库构建或交由外部流水线（影响 Dockerfile 细节）。
