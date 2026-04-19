# JIssWeb

**论坛方向 Web 应用骨架**：多领域后端服务 + Vue 3 单页前端，统一鉴权与本地依赖。

> 当前前端版本：`frontend/package.json` 中 `version`；后端目标框架：**.NET 8**。

---

## 一、项目简介

| 维度 | 说明 |
|------|------|
| **解决什么问题** | 在统一技术栈下拆分用户、客档、模型、账款、报表等后端边界，前端用一套壳接入；产品形态向**论坛**演进（首页 Feed、版区、发帖等）。 |
| **核心能力** | Vue 3 + Vite + Pinia + Vitest 论坛首页壳与 `/auth` 统一认证；多 ASP.NET Core API + JWT；Docker 本地 MongoDB/Redis；可选 YARP 网关与 BFF。 |
| **适用人群** | 本仓库维护者、全栈/前后端在本地联调与按 OpenSpec 迭代功能的开发者。 |

---

## 二、快速开始

### 环境要求

- **Node.js**：建议 18+（用于前端与 `scripts/github-sync`）
- **.NET SDK**：8.x（构建 `backend/src` 解决方案）
- **Docker**：用于本地 MongoDB / Redis（及部分示例 API 镜像）

### 克隆与依赖

```bash
git clone <你的仓库地址> jIssWeb
cd jIssWeb
```

**配置（必做）**

1. 复制仓库根目录 **`.env.example`** 为 **`.env`**，按其中键补全（Mongo/Redis、各服务端口、JWT、网关与 BFF URL、Vite 代理端口等）。勿提交 `.env`。
2. 各后端 API：复制对应 **`appsettings.Local.example.json`** 为 **`appsettings.Local.json`** 并填写连接串与邮件链接等（与 `.env` / 本地端口一致）。

**前端**

```bash
cd frontend
npm ci
npm run dev
```

开发服务器地址与端口由 **`.env`** 中 `VITE_DEV_SERVER_PORT` 等决定；代理目标由 `VITE_PROXY_*` 决定（`vite.config.ts` 从**仓库根**加载 `.env`）。

**后端（示例：User.Api）**

```bash
cd backend/src
dotnet build JIssWeb.sln
dotnet run --project JIssWeb.User.Api
```

监听地址与端口以各项目 `launchSettings.json` 及本地配置为准。

**Docker Compose**

```bash
cd <仓库根目录>
docker compose up -d --build
```

依赖根目录 **`.env`** 中全部必填变量。若只想起数据库与 Redis：`docker compose up -d mongo redis`。

---

## 三、配置说明

| 类别 | 位置 / 方式 |
|------|-------------|
| 编排与密钥 | 仓库根 **`.env`**（从 **`.env.example`** 复制） |
| 前端开发代理 | **`.env`** 中 `VITE_*`，由 `frontend/vite.config.ts` 读取仓库根 `.env` |
| 后端连接串与外链 | 各 `*.Api/appsettings.json`（占位）+ **`appsettings.Local.json`**（从 `*.Local.example.json` 复制） |
| 网关（本地 dotnet） | `JIssWeb.Gateway.Api` 的 **`appsettings.Local.json`**（见 `appsettings.Local.example.json`） |
| 远端同步脚本 | `scripts/github-sync/.env.example` → 本地 `.env`（勿提交令牌） |

敏感信息一律通过上述本地文件或环境变量注入，不要写入仓库。

---

## 四、需求与 OpenSpec 工作流

在 **GitHub** 维护 Issue 与里程碑。本地以 **`scripts/github-sync/pm-plan.yaml`** 为规划源，与 **`openspec/`** 配合；细则见 **`.cursor/skills/pm-plan/SKILL.md`**。

**前置**：本机安装 **openspec** CLI（`/opsx-*` 与命令行归档依赖）；在仓库根执行 **`npm run pm:ci`** 安装 **`scripts/github-sync`** 依赖。

**代码同步与 Issue 同步（勿混用）**

| 命令 | 作用 |
|------|------|
| **`git pull`** | 只更新本仓库 **Git** 历史中的代码与已提交的 `pm-plan.yaml`；**不**调用 GitHub Issues API。 |
| **`npm run pm:pull`** | 用 GitHub API 把远端 **open** Issue / 里程碑回填进 **`pm-plan.yaml`**。 |

下表中的「同步代码」指 Git；「同步 Issue → 本地规划」指 `pm:pull`。

| 步骤 | 操作 |
|------|------|
| 同步代码与依赖 | 开工前 **`git pull`**；多人改 **`pm-plan.yaml`** 前先拉再改，减少冲突 |
| 同步 Issue → 本地规划 | 配置 **`scripts/github-sync/.env`**（从 **`.env.example`**，勿提交令牌）。需用 GitHub API 回填 **`pm-plan.yaml`** 时：**`npm run pm:pull`**（**不是** `git pull`）。Cursor 中可用 **`/pm-pull`** |
| 查看进行中 Issue | 在 **`scripts/github-sync/pm-plan.yaml`** 中筛选 `state` 为 `open` / `progressing` 的条目 |
| 讨论方案 | **`/opsx-explore`**（探索不写业务代码） |
| 立项与实现 | **`/opsx-propose`** 建 change → **`/opsx-apply`** 实现；**一条 Issue 可拆多个 change/多 PR**，与条目、里程碑对齐即可 |
| 自测与审查 | 自测后可用 **`change-review`**（只读，对照 OpenSpec / 约定） |
| 提交与合并 | **`git commit`** 后开 **Pull Request**；描述中按 **`.github/pull_request_template.md`** 填写关联与自检项；合并与分支保护按团队约定 |
| 归档 | **`/opsx-archive`** |
| 回写规划并推送 | 编辑 **`pm-plan.yaml`** 后 **`npm run pm:push`**。**默认在实现已合并进主分支后再执行**，避免远端规划与主分支代码不一致 |

**复盘与归档（建议节奏）**

- 迭代复盘或 **`/opsx-archive`** 后若更新了 **`.cursor/rules`** 或 **`openspec/specs/**`**：按需提交；若需把规划同步到 GitHub Issue，使用 **`npm run pm:push`**。

**异常与排查**

- **`npm run pm:push` 失败**：检查 **`scripts/github-sync/.env`** 与 GitHub API 权限、**`pm-plan.yaml`** 格式与远端同步脚本报错信息。
- **紧急修复**：可先合代码，再补 OpenSpec / **`pm-plan`**，由团队约定。

**仓库根常用命令**

| 目的 | 命令 |
|------|------|
| 拉 Issue 写回 yaml | `npm run pm:pull` |
| 将本地规划推远端（GitHub） | `npm run pm:push` |

---

## 五、技术架构

### 目录结构（节选）

```text
jIssWeb/
├── backend/src/           # .NET 解决方案：Common、Domain、各 *.Api、Gateway、Bff 等
├── frontend/              # Vue 3 + TypeScript + Vite + Element Plus + Vitest
├── docker/                # Redis 配置、Dockerfile 等
├── scripts/github-sync/   # pm-plan YAML、远端同步脚本
├── openspec/              # OpenSpec 变更与归档 specs
└── docker-compose.yml     # 本地依赖与 API、网关（变量来自 .env）
```

### 技术栈

| 层次 | 技术 |
|------|------|
| 前端 | Vue 3、TypeScript、Vite、Pinia、vue-router、Element Plus、axios、Vitest（`npm test`） |
| 后端 | ASP.NET Core、JWT（用户服务签发，他服务校验） |
| 数据与缓存 | MongoDB、Redis（按服务配置节接入） |
| 容器与编排 | Docker Compose |

更细的契约见 `openspec/specs/` 下各能力说明。

---

## 六、本地数据库（Docker）

Mongo / Redis 的宿主机端口、账号及连接串均在根目录 **`.env`** 中配置；与容器内监听、数据卷名等对应关系见 **`.env.example`** 中的键名。

若修改 Redis 配置或更换初始化凭据后无法连接，可先 `docker compose down`，按需删除对应 volume 后再 `docker compose up -d`（注意会清空持久化数据）。
