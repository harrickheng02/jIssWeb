# JIssWeb

**Vue 3 + .NET 8 论坛单体仓库**，多后端边界上下文 + 单页前端，统一鉴权与本地依赖。

---

## 一、快速开始

### 环境要求

- **Node.js** 18+（前端与 `scripts/github-sync`）
- **.NET SDK** 8.x（`backend/src` 解决方案）
- **Docker**（本地 MongoDB / Redis）

### 首次配置

```bash
git clone <仓库地址> jIssWeb && cd jIssWeb
```

1. 复制根目录 **`.env.example`** → **`.env`**，填写 Mongo/Redis 端口、JWT Key、Vite 代理目标。
2. 各后端 API 目录：复制 **`appsettings.Local.example.json`** → **`appsettings.Local.json`**，填写连接串。
3. 规划同步：复制 **`scripts/github-sync/.env.example`** → **`scripts/github-sync/.env`**，填写 GitHub Token。

### 本地运行

```bash
# 基础设施
docker compose up -d mongo redis

# 前端
cd frontend && npm ci && npm run dev

# 后端（以 Model.Api 为例）
cd backend/src && dotnet build JIssWeb.sln && dotnet run --project JIssWeb.Model.Api
```

---

## 二、常用命令

| 目的 | 命令 |
|------|------|
| 前端开发服务器 | `cd frontend && npm run dev` |
| 前端构建（含类型检查） | `cd frontend && npm run build` |
| 前端测试 | `cd frontend && npm test` |
| 后端构建 | `cd backend/src && dotnet build JIssWeb.sln` |
| 后端测试 | `cd backend && dotnet test tests/JIssWeb.Model.Api.Tests` |
| 单条测试 | `dotnet test --filter "FullyQualifiedName~TestClassName"` |
| 启动基础设施 | `docker compose up -d mongo redis` |
| 全栈启动 | `docker compose up -d --build` |
| 安装规划脚本依赖 | `npm run pm:ci` |
| 拉取 GitHub Issue → 本地规划 | `npm run pm:pull` |
| 推送本地规划 → GitHub Issue | `npm run pm:push` |
| 预览推送变更 | `npm run pm:dry` |

---

## 三、配置说明

| 类别 | 位置 | 说明 |
|------|------|------|
| 端口、密钥、JWT、代理 | 根目录 `.env` | 从 `.env.example` 复制，**勿提交** |
| 前端 Vite 代理 | `.env` 中 `VITE_*` | `vite.config.ts` 从**仓库根**读取 |
| 后端连接串与外链 | 各 `*.Api/appsettings.Local.json` | 从 `*.Local.example.json` 复制，**勿提交** |
| GitHub 同步 Token | `scripts/github-sync/.env` | 从 `.env.example` 复制，**勿提交** |

---

## 四、职责分工与协作流程

本项目采用「产品端 → 工程端」双轨模型，每个角色有明确的文档类型和工具边界，**跨越边界必须经过交接步骤，不允许绕过**。

---

### 4.1 角色定义

| 角色 | 核心职责 | 禁止越权 |
|------|------|------|
| **产品端（PM）** | 定义做什么、为什么做、做到什么程度；输出 PRD、实施蓝图、Issue 规划 | 不修改 OpenSpec change 技术契约；不直接拆解工程任务 |
| **工程端（Engineering）** | 定义怎么做、做对了没有、做完了；输出技术契约、归档规范、PR | 不自行扩展功能范围；不修改 PRD 与实施蓝图 |

---

### 4.2 产品端工作流

**输入：** 业务需求、用户反馈、研究目标

**输出文档：**

| 产出物 | 位置 | 说明 |
|--------|------|------|
| PRD | `docs/PRD-*.md` | 用户旅程、功能说明、验收指标 |
| 实施蓝图 | `docs/superpowers/plans/YYYY-MM-DD-<feature>.md` | 文件路径级任务拆分与验收步骤，供工程端参考 |
| Issue 规划 | `scripts/github-sync/pm-plan.yaml` | 功能优先级、里程碑、state 状态 |

**执行步骤：**

```
1. 需求分析（brainstorming skill）
       ↓
2. 输出 PRD（docs/PRD-*.md）
       ↓
3. 输出实施蓝图（writing-plans skill → docs/superpowers/plans/）
       ↓
4. 在 pm-plan.yaml 新增 Issue（title / body / priority / milestone / module）
       ↓
5. npm run pm:push   ← 同步到 GitHub，获取 issue_number
   npm run pm:pull   ← 回填 issue_number 到本地
       ↓
6. git add + git commit + git push  ← 提交 PRD、蓝图、pm-plan.yaml
       ↓
【交接】Issue 编号 + PRD + 蓝图 → 工程端
```

---

### 4.3 工程端工作流

**输入：** pm-plan.yaml Issue + PRD + 实施蓝图（只读参考）

**输出文档：**

| 产出物 | 位置 | 说明 |
|--------|------|------|
| 技术契约 | `openspec/changes/<change>/proposal.md` | 接口定义、SHALL/MUST 验收条款、非目标 |
| 施工任务 | `openspec/changes/<change>/tasks.md` | TDD 逐步 checklist |
| 归档规范 | `openspec/specs/<capability>/spec.md` | 已交付能力的权威说明，由 `/opsx:archive` 生成 |

**执行步骤：**

```
1. git pull  ← 同步代码（与 npm run pm:pull 不同，只更新 Git 历史）
       ↓
2. /opsx:explore  ← 技术方案讨论（只读，不写业务代码）
       ↓
3. /opsx:propose  ← 开 OpenSpec change，写 proposal.md + tasks.md
   （关联对应 Issue 编号；一个 Issue 可拆多个 change）
       ↓
4. /opsx:apply    ← 按 tasks.md 施工
   Construction SOP（每个后端任务）：
     a. 写失败测试（dotnet test 验证 FAIL）
     b. 实现最少代码使测试通过
     c. dotnet test 验证 PASS
     d. git commit
   （每个前端任务）：
     a. 实现功能
     b. npx vitest run 验证
     c. 浏览器验证黄金路径
     d. git commit
       ↓
5. change-review  ← 对照 proposal.md 所有 SHALL/MUST 逐项自查
       ↓
6. git commit → Pull Request（按 .github/pull_request_template.md 填写）
       ↓
7. PR 合并进主分支后：
   /opsx:archive  ← 归档 spec
       ↓
8. 更新 pm-plan.yaml（state: closed）
   npm run pm:push  ← 同步关闭 GitHub Issue
       ↓
【交接】已合并 PR 链接 + 归档 spec 路径 → 产品端验收
```

---

### 4.4 关键边界约定

| 约定 | 原因 |
|------|------|
| `pm-plan.yaml` 只记录「做什么」，不记录「怎么做」 | 防止产品决策与工程实现混淆 |
| OpenSpec change 内容只记录「怎么做」，不重写功能范围 | 防止工程侧自行扩展需求 |
| `npm run pm:push` 必须在 PR 合并后执行 | 防止远端 Issue 状态超前于实际代码 |
| `git pull` ≠ `npm run pm:pull`；两者完全不同 | `git pull` 更新代码；`pm:pull` 用 API 回填 Issue 编号 |
| 实施蓝图（`docs/superpowers/plans/`）工程端只读 | 修改蓝图属于产品决策，需走产品端流程 |

---

### 4.5 异常处理

| 场景 | 处理方式 |
|------|------|
| 紧急 bug | 工程端直接 `/opsx:propose` → 实现 → PR；Issue 可事后由产品端补录 |
| `npm run pm:push` 失败 | 检查 `scripts/github-sync/.env` Token 权限与 `pm-plan.yaml` 格式 |
| Issue 范围不明确 | 工程端反向提问产品端，不自行假设 |
| 工程实现与蓝图不一致 | 以 OpenSpec proposal.md 的 SHALL 条款为准，蓝图仅参考 |

---

## 五、技术架构

### 目录结构

```
jIssWeb/
├── backend/src/
│   ├── JIssWeb.Common/          # 跨服务中间件与 hosting 扩展
│   ├── JIssWeb.Domain/          # 领域模型
│   ├── JIssWeb.Application/     # 应用服务层
│   ├── JIssWeb.Infrastructure/  # MongoDB、Redis、外部 API 客户端
│   ├── JIssWeb.User.Api/        # 唯一 JWT 签发方
│   ├── JIssWeb.Model.Api/       # 论坛核心 API（帖子、回复、举报、通知等）
│   ├── JIssWeb.Gateway.Api/     # YARP 网关（可选）
│   └── JIssWeb.Frontend.Bff/   # BFF（可选）
├── frontend/                    # Vue 3 + TypeScript + Vite + Element Plus
├── openspec/
│   ├── changes/                 # 进行中的技术契约（工程端写）
│   └── specs/                   # 已归档规范（由 /opsx:archive 生成）
├── docs/
│   ├── PRD-*.md                 # 产品需求文档（产品端写）
│   └── superpowers/plans/       # 实施蓝图（产品端写，工程端只读）
├── scripts/github-sync/         # pm-plan YAML + GitHub Issue 同步脚本
└── docker-compose.yml
```

### 技术栈

| 层次 | 技术 |
|------|------|
| 前端 | Vue 3、TypeScript、Vite、Pinia、Element Plus、axios、Vitest |
| 后端 | ASP.NET Core 8，JWT 由 User.Api 签发，其他服务只验证 |
| 数据层 | MongoDB、Redis |
| 容器 | Docker Compose |

接口细节见 `openspec/specs/` 下各能力归档文件。

---

## 六、本地数据库重置

```bash
docker compose down
docker volume rm jisweb_mongo_data jisweb_redis_data  # 按实际 volume 名调整
docker compose up -d mongo redis
```
