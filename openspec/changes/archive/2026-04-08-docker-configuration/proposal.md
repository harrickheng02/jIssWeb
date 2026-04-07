## Why

本地开发依赖本机安装的 MongoDB 与 Redis，环境不一致时联调成本高；团队需要可重复的依赖启动方式，并为后续 CI/CD 容器化铺路。通过 Docker 在仓库内固化依赖与（可选的）应用镜像构建方式，可降低上手成本并统一端口与网络约定。

## What Changes

- 在仓库根目录（或 `docker/` 子目录）新增 `docker-compose` 编排，至少包含 **MongoDB** 与 **Redis** 服务，端口与现有 `appsettings` 默认值对齐或可覆盖。
- 提供 **`.env.example`**（或等价文档）说明连接串、端口、卷挂载路径等变量。
- （可选）为后端 API 与/或前端提供 **Dockerfile** 与 compose 中的 `build` 目标，便于本地一键起全栈或仅起依赖。
- 新增 **`.dockerignore`**（若存在构建上下文）以缩小镜像体积、排除 `bin/obj/node_modules`。
- **不**改变现有 API 契约与业务逻辑；**不**强制生产部署形态（Kubernetes 等另议）。

## Capabilities

### New Capabilities

- `docker-local-dependencies`: 使用容器提供 MongoDB、Redis，固定可文档化的镜像版本、端口映射与健康检查约定。
- `docker-compose-project`: 仓库内 Compose 文件布局、服务命名、网络与卷策略，以及与 `appsettings` / 环境变量的衔接方式。
- `docker-runtime-connectivity`: 应用在「仅依赖容器」与「全栈容器」场景下访问 Mongo/Redis 的主机名规则（如 `localhost` vs 服务名），不在 spec 层规定具体 YAML 语法细节。

### Modified Capabilities

- （无对现有 `openspec/specs/` 中行为级需求的修改；连接串仍由配置驱动。）

## Impact

- 开发者需安装 Docker / Docker Compose；可选地调整本地 `appsettings.Development.json` 或用户机密以指向容器。
- CI 可增加镜像构建与 `compose config` 校验任务（本变更可在 tasks 中列为后续项）。
- 仓库体积增加 compose 与忽略文件；不删除现有非 Docker 调试方式。
