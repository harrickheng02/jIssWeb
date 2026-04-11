# JIssWeb

**论坛方向 Web 应用骨架**：多领域后端服务 + Vue 3 单页前端，统一鉴权与本地依赖。

> 当前前端版本：`frontend/package.json` 中 `version`；后端目标框架：**.NET 8**。

---

## 一、项目简介

| 维度 | 说明 |
|------|------|
| **解决什么问题** | 在统一技术栈下拆分用户、客档、模型、账款、报表等后端边界，前端用一套壳接入；产品形态向**论坛**演进（首页 Feed、版区、发帖等）。 |
| **核心能力** | Vue 3 + Vite + Pinia 论坛首页壳与 `/auth` 统一认证；多 ASP.NET Core API + JWT；Docker 本地 MongoDB/Redis；可选 YARP 网关与 BFF。 |
| **适用人群** | 本仓库维护者、全栈/前后端在本地联调与按 OpenSpec 迭代功能的开发者。 |

---

## 二、快速开始

### 环境要求

- **Node.js**：建议 18+（用于前端与 `scripts/gitee-sync`）
- **.NET SDK**：8.x（构建 `backend/src` 解决方案）
- **Docker**：用于本地 MongoDB / Redis（及部分示例 API 镜像）

### 克隆与依赖

```bash
git clone <你的仓库地址> jIssWeb
cd jIssWeb
```

**前端**

```bash
cd frontend
npm ci
npm run dev
```

浏览器访问开发服务器提示的地址（一般为 `http://localhost:5173`，以 Vite 输出为准）。

**后端（示例：生成并运行 User.Api，具体端口以 `launchSettings.json` 为准）**

```bash
cd backend/src
dotnet build JIssWeb.sln
dotnet run --project JIssWeb.User.Api
```

**Docker Compose（默认：Mongo、Redis、user-api、frontend-bff、gateway-api）**

```bash
cd <仓库根目录>
docker compose up -d --build
```

首次需构建镜像。若只想起数据库与 Redis：`docker compose up -d mongo redis`。验证：各服务健康后，端口与 `appsettings` / `vite` 代理一致即可联调。

---

## 三、配置说明

| 类别 | 位置 / 方式 |
|------|-------------|
| 前端开发代理 | `frontend/vite.config.ts`（各后端路径前缀与端口） |
| 后端连接串 | 各 `*.Api/appsettings*.json`，含 `Jwt`、`Mongo`、`Redis` 等节 |
| 网关 / 网关环境变量示例 | `docker/gateway.env.example` |
| Gitee 同步脚本凭证 | `scripts/gitee-sync/.env.example` → 本地 `.env`（`GITEE_OWNER`、`GITEE_REPO`、`GITEE_ACCESS_TOKEN`；勿提交令牌） |

敏感信息一律通过环境变量或本地忽略文件注入，不要写入仓库。

---

## 四、技术架构

### 目录结构（节选）

```text
jIssWeb/
├── backend/src/           # .NET 解决方案：Common、Domain、各 *.Api、Gateway、Bff 等
├── frontend/              # Vue 3 + TypeScript + Vite + Element Plus
├── docker/                # Redis 配置、网关相关示例
├── scripts/gitee-sync/    # pm-plan YAML、Gitee 同步脚本
├── openspec/              # OpenSpec 变更与归档 specs
└── docker-compose.yml     # 本地依赖与 API、网关（默认一并启动）
```

### 技术栈

| 层次 | 技术 |
|------|------|
| 前端 | Vue 3、TypeScript、Vite、Pinia、vue-router、Element Plus、axios |
| 后端 | ASP.NET Core、JWT（用户服务签发，他服务校验） |
| 数据与缓存 | MongoDB、Redis（按服务配置节接入） |
| 容器与编排 | Docker Compose |

更细的契约见 `openspec/specs/` 下各能力说明。

---

## 五、本地数据库（Docker）

| 服务 | 宿主机端口 | 账号 | 密码 |
|------|------------|------|------|
| MongoDB | 37017 | harrickheng | qq!219673605 |
| Redis | 6380 | （默认用户，仅密码） | qq!219673605 |

容器内 Mongo 为 `27017`、Redis 为 `6379`；Compose 网络内服务名为 `mongo`、`redis`。

Redis 客户端填 Host `127.0.0.1`、Port `6380`、Password；用户名留空或 `default`。

若修改 `docker/redis.conf` 后仍无法连接，可先 `docker compose down`，删除对应 volume 后再 `docker compose up -d`（注意会清空 Redis 持久化数据）。
