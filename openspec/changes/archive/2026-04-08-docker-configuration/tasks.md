## 1. Compose 与依赖服务

- [x] 1.1 在仓库根目录添加 `docker-compose.yml`（或 `compose.yaml`），定义 `mongo`、`redis` 服务、命名卷、端口映射 `27017`/`6379`
- [x] 1.2 为 MongoDB/Redis 添加合适的 `HEALTHCHECK` 或文档化手工校验命令
- [x] 1.3 添加 `.env.example`，包含镜像标签、端口、卷名等可配置项（不含真实密钥）

## 2. 构建与忽略

- [x] 2.1 在仓库根目录添加 `.dockerignore`，排除 `**/bin/**`、`**/obj/**`、`**/node_modules/**`、`**/dist/**`、`.git` 等
- [x] 2.2 （可选）为单个 API 项目添加示例 `Dockerfile`（多阶段构建 net8.0），并在 compose 中增加可选 `profiles` 或注释说明如何启用

## 3. 文档与验证

- [x] 3.1 在 `design.md` 或本 change 内补充「宿主机 API + 容器依赖」与「全容器」连接串示例（Mongo/Redis 主机名差异）
- [x] 3.2 本地执行 `docker compose up -d` 后验证端口可连；执行 `docker compose config` 无语法错误

## 4. 收尾

- [x] 4.1 确认 `.gitignore` 已忽略 `.env`（若尚未则添加）
- [x] 4.2 若需同步主 specs，归档前执行 openspec 同步流程
