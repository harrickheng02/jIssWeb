# CLAUDE.md

> **语言约定**：所有对话回复、分析、注释与报告一律使用**简体中文**。代码标识符、命令、文件路径保持原语言不变。

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project shape

JIssWeb is a Vue 3 + .NET 8 monorepo evolving toward a forum product. The frontend is a single SPA (`frontend/`) that talks to several ASP.NET Core APIs (`backend/src/JIssWeb.*.Api`), optionally fronted by a YARP gateway (`JIssWeb.Gateway.Api`) and a BFF (`JIssWeb.Frontend.Bff`). Local MongoDB and Redis are provisioned via Docker Compose at the repo root.

The User API is the **only** JWT issuer; every other API validates tokens it minted. Forum features (posts, replies, reports, moderation, notifications) all live in `JIssWeb.Model.Api` — that is the service to look at first for any forum domain change.

## Commands

All commands assume the repo root unless noted.

| Purpose | Command |
|------|------|
| Frontend dev server | `cd frontend && npm run dev` |
| Frontend build (type-check + Vite) | `cd frontend && npm run build` |
| Frontend tests (Vitest) | `cd frontend && npm test` (watch: `npm run test:watch`) |
| Single Vitest spec | `cd frontend && npx vitest run path/to/file.test.ts` |
| Backend build (full solution) | `cd backend/src && dotnet build JIssWeb.sln` |
| Run a single API locally | `cd backend/src && dotnet run --project JIssWeb.User.Api` (substitute the service) |
| Backend tests | `cd backend && dotnet test tests/JIssWeb.Model.Api.Tests` |
| Single backend test | `dotnet test --filter "FullyQualifiedName~ForumPostsSearchTests"` |
| Local infra (Mongo + Redis) | `docker compose up -d mongo redis` |
| Full local stack | `docker compose up -d --build` |
| Install pm-sync deps | `npm run pm:ci` |
| Pull GitHub Issues → `pm-plan.yaml` | `npm run pm:pull` |
| Push `pm-plan.yaml` → GitHub Issues | `npm run pm:push` (runs a dry-run first) |
| Dry-run pm-sync | `npm run pm:dry` |

## Configuration model (read before changing ports/URLs)

- The repo root `.env` is the single source for ports, secrets, JWT keys, and proxy targets. `vite.config.ts` calls `loadEnv` against the **repo root** (not `frontend/`) and will throw at startup if any `VITE_PROXY_*` / `VITE_DEV_SERVER_PORT` is missing — so never silently delete a key from `.env.example`.
- Each `*.Api/` has a `appsettings.Local.example.json` that must be copied to `appsettings.Local.json` for connection strings, mail, and forum board IDs. Program.cs files explicitly add `appsettings.Local.json` as a config source (e.g. `JIssWeb.Model.Api/Program.cs:16`).
- `docker-compose.yml` consumes the same root `.env`. Container ports vs. host ports are separate (`*_CONTAINER_PORT` vs `*_PORT`); changing one without the other breaks the gateway/BFF wiring.

## Frontend architecture

- Routing is in `frontend/src/router/index.ts`. Three meta flags drive guards: `requiresAuth` (token check), `requiresModerate` (delegates to `useAuthStore().canModerate`), and `hideAppShell` (auth pages render without the chrome).
- All HTTP calls go through `frontend/src/api/clients.ts`. `createClient(prefix)` attaches the bearer token, and on 401 (except for `/auth/*` paths themselves) it runs a **single-flight** refresh keyed by base prefix, then retries. If you add a new API client, reuse `createClient` so the refresh contract stays consistent — don't open a fresh axios instance.
- State lives in Pinia stores under `frontend/src/stores/` (`auth`, `theme`, `legalUi`). The auth store owns token persistence and the `canModerate` derivation used by route guards.
- Vite proxies (defined in `frontend/vite.config.ts`) split traffic by prefix: `/api-user`, `/api-customer`, `/api-model`, `/api-accounting`, `/api-report` strip their prefix and hit the corresponding service directly; `/api/forum` goes straight to the model API; everything else under `/api` falls through to the gateway. Forum work usually goes via `/api/forum`.

## Forum UI rules (enforced)

`.cursor/rules/forum-ui.mdc` is authoritative for any `frontend/**/*.vue` or `frontend/**/*.css` change. The non-negotiables:

- **No hardcoded colors or spacing.** Use the CSS variables defined in `frontend/src/styles/forum-tokens.css` (`--color-primary`, `--bg-main`, `--text-primary`, `--border-color`, `--space-*`, `--radius-*`, `--font-*`). Spacing is on an 8px grid (4/8/12/16/24/32/40); arbitrary values like 13px are forbidden.
- Primary actions use `el-button type="primary"` (or `BaseButton type="primary"`). Don't hand-roll button styles.
- Avoid `::v-deep` / `:deep()` to override Element Plus internals — prefer EP slots/classes or the forum-tokens EP bridge variables.
- Post titles and summaries are clamped to **2 lines max**.
- Dark mode is driven by `html.dark[data-theme='dark']` overrides in `forum-tokens.css`.

## BFF conventions

`JIssWeb.Frontend.Bff` 是前端专属的编排层，介于 SPA 和域 API 之间。以下约定必须遵守，否则会让 BFF 退化成透明代理或变成第二个业务层。

**何时加 BFF 端点**

| 场景 | 做法 |
|------|------|
| SPA 首屏需要并发调 2+ 个下游 | 新增 BFF 聚合端点 |
| 鉴权生命周期操作（登录/刷新/登出） | 走 `BffAuthController`，写 HttpOnly Cookie |
| 下游响应字段需裁剪/重命名才适合前端 | BFF 做 response shaping |
| 单个下游调用、无聚合需求 | 前端直调域 API，不过 BFF |
| 写操作（创帖、回复、投票等） | 永远直调域 API，BFF 不承载写操作 |

**后端：新聚合端点模板**

参考 `BffForumInitController` / `BffMeController`：

1. 所有下游请求用 `Task.WhenAll` 并发，单个失败不中断整体
2. 失败的下游把 `label` 写入 `warnings` 列表，返回 `null`
3. `warnings` 字段放在 response record 内，通过 `ApiResult<T>.Ok()` 统一序列化为 `data.warnings`——不要在 HTTP 根层返回 `warnings`
4. 需要 Bearer 的端点：从 `Request.Headers.Authorization` 读取并通过 `CreateAuthorizedClient(bearer)` 转发，不读 Cookie、不重签 token
5. 每个新端点都要有降级测试（一路下游返回 503，断言响应仍为 200 且 `data.warnings` 非空）

**前端：消费 BFF 数据的模式**

- **页面首次加载**：走 BFF 束（如 `getForumInit`），在 composable 里用 `_firstLoad = true` 标记；首次之后的翻页/筛选直调轻量域 API（如 `listForumPosts`）
- **用户状态**：`/bff/me` 返回的数据由 `useCurrentUser` 单例持有，其他 composable 通过 `useCurrentUser()` 读取，不要各自再调 `/bff/me`
- **token watch 守卫**：watch `auth.token` 时始终用 `(t, prevT)` 双参数形式，跳过 token 轮换（非空→非空）；仅在 `null→token`（登录）和 `token→null`（登出）时触发副作用

**BFF 的边界**

BFF 不做权限判断、不访问数据库、不承载论坛业务规则——这些属于 Model API / User API。BFF 只做：HTTP 编排、并发聚合、Cookie 生命周期管理。

## Backend architecture

- Solution layout: `JIssWeb.Common` (cross-cutting hosting / middleware / options), `JIssWeb.Domain` and `JIssWeb.Application` (DDD layers), `JIssWeb.Infrastructure` (Mongo, etc.), and one `*.Api` per bounded context (User, Customer, Model, Accounting, Report) plus `Gateway.Api` (YARP) and `Frontend.Bff`.
- Each API's `Program.cs` follows the same pattern: load `appsettings.Local.json`, call `builder.UseJIssWebHttpPort(<default>)`, bind option sections, register Mongo via `AddMongoInfrastructure`, register the shared API plumbing via `AddJIssWebCoreApi`, then `app.UseExceptionHandling()` + `app.UseCors()`. When adding a new service, mirror this pattern rather than reinventing it.
- Forum domain controllers (`backend/src/JIssWeb.Model.Api/Controllers/`) are split into public-facing (`ForumPostsController`, `ForumReportsController`, `ForumMeController`, `ForumNotificationsController`, `ForumAnnouncementsController`, `ForumTagsController`, `ForumConfigController`) and moderator-only (`Mod*Controller`). Moderator authorization flows through `ForumModerationAccessService`; access decisions ultimately rely on the JWT's `forumBoardIds` claim being aligned with `ForumBoardsOptions`.
- Backend tests live under `backend/tests/JIssWeb.Model.Api.Tests/` and use integration fixtures (`*IntegrationFixture.cs`) plus `JwtTestTokens.cs` for forging signed tokens. New forum endpoints should add a fixture/spec there.

## OpenSpec + pm-plan workflow

This repo uses a **spec-driven** workflow. Before non-trivial implementation:

1. Discuss with `/opsx:explore` (read-only).
2. Open a change with `/opsx:propose` (writes `openspec/changes/<change>/proposal.md`, `tasks.md`, etc.).
3. Implement with `/opsx:apply` — follow the **Construction SOP** below.
4. Self-review with `change-review`, then PR using `.github/pull_request_template.md`.
5. Archive with `/opsx:archive` once merged. Specs land in `openspec/specs/<capability>/spec.md`.

### Construction SOP (施工 SOP)

Execute when entering `/opsx:apply`:

1. **Load tasks** — read `openspec/changes/<change>/tasks.md`; create a `TodoWrite` checklist mirroring every task.
2. **Per backend task** — invoke `test-driven-development`: write the failing test first, then implement until green. Run `dotnet test --filter` after each task.
3. **Per frontend task** — implement, then verify the golden path in a browser (or note explicitly if UI cannot be tested). Run `npx vitest run` for any touched composable/util.
4. **Mark done** — tick the `TodoWrite` item immediately after the task passes.
5. **After all tasks** — invoke `verification-before-completion`: run the full test suite (`dotnet test` + `npm test`) and confirm output before claiming done.
6. **change-review** — check implementation against every `SHALL`/`MUST` in the relevant `openspec/specs/` files; verify no non-goals were accidentally implemented.
7. **Commit & PR** — follow `chinese-commit-conventions`; use `.github/pull_request_template.md`.
8. **Archive** — run `/opsx:archive` after the PR merges to main; then update `pm-plan.yaml` state and run `npm run pm:push`.

**Parallel changes**: when two changes are independent (no shared files), spawn them in separate git worktrees via `using-git-worktrees` or Agent `isolation: "worktree"` to avoid conflicts.

Planning lives in `scripts/github-sync/pm-plan.yaml`, mirrored to GitHub Issues. Important distinctions:

- `git pull` only updates committed files. To refresh open Issues into `pm-plan.yaml`, run **`npm run pm:pull`** — never confuse the two.
- `npm run pm:push` should run **after** the related code merges to main, so remote Issue state and main stay aligned.
- Closed/rejected entries keep their `title`, `body`, and `issue_number` with `state` flipped — don't delete them.
- `priority` uses the five Chinese tiers from `priority_definitions`. Never write `P0`/`P1`/etc. The `remote_priority` and `issue_content_classifications` contract sections are required — don't remove them. See `.cursor/rules/pm-plan.mdc` for the full contract.

## Docs

- `docs/ui/design-spec.md` — canonical design spec (the forum-ui rule is the short summary).
- `openspec/specs/<capability>/spec.md` — authoritative behavior contracts. Read the relevant spec before changing a forum endpoint.

<!-- superpowers-zh:begin (do not edit between these markers) -->
# Superpowers-ZH 中文增强版

本项目已安装 superpowers-zh 技能框架（20 个 skills）。

## 核心规则

1. **收到任务时，先检查是否有匹配的 skill** — 哪怕只有 1% 的可能性也要检查
2. **设计先于编码** — 收到功能需求时，先用 brainstorming skill 做需求分析
3. **测试先于实现** — 写代码前先写测试（TDD）
4. **验证先于完成** — 声称完成前必须运行验证命令

## 可用 Skills

Skills 位于 `.claude/skills/` 目录，每个 skill 有独立的 `SKILL.md` 文件。

- **brainstorming**: 在任何创造性工作之前必须使用此技能——创建功能、构建组件、添加功能或修改行为。在实现之前先探索用户意图、需求和设计。
- **chinese-code-review**: 中文 review 沟通参考——话术模板、分级标注（必须修复/建议修改/仅供参考）、国内团队常见反模式应对。仅在用户显式 /chinese-code-review 时调用，不要根据上下文自动触发。
- **chinese-commit-conventions**: 中文 commit 与 changelog 配置参考——Conventional Commits 中文适配、commitlint/husky/commitizen 中文模板、conventional-changelog 中文配置。仅在用户显式 /chinese-commit-conventions 时调用，不要根据上下文自动触发。
- **chinese-documentation**: 中文文档排版参考——中英文空格、全半角标点、术语保留、链接格式、中文文案排版指北约定。仅在用户显式 /chinese-documentation 时调用，不要根据上下文自动触发。
- **chinese-git-workflow**: 国内 Git 平台配置参考——Gitee、Coding.net、极狐 GitLab、CNB 的 SSH/HTTPS/凭据/CI 接入差异与镜像同步配置。仅在用户显式 /chinese-git-workflow 时调用，不要根据上下文自动触发。
- **dispatching-parallel-agents**: 当面对 2 个以上可以独立进行、无共享状态或顺序依赖的任务时使用
- **executing-plans**: 当你有一份书面实现计划需要在单独的会话中执行，并设有审查检查点时使用
- **finishing-a-development-branch**: 当实现完成、所有测试通过、需要决定如何集成工作时使用——通过提供合并、PR 或清理等结构化选项来引导开发工作的收尾
- **mcp-builder**: MCP 服务器构建方法论 — 系统化构建生产级 MCP 工具，让 AI 助手连接外部能力
- **receiving-code-review**: 收到代码审查反馈后、实施建议之前使用，尤其当反馈不明确或技术上有疑问时——需要技术严谨性和验证，而非敷衍附和或盲目执行
- **requesting-code-review**: 完成任务、实现重要功能或合并前使用，用于验证工作成果是否符合要求
- **subagent-driven-development**: 当在当前会话中执行包含独立任务的实现计划时使用
- **systematic-debugging**: 遇到任何 bug、测试失败或异常行为时使用，在提出修复方案之前执行
- **test-driven-development**: 在实现任何功能或修复 bug 时使用，在编写实现代码之前
- **using-git-worktrees**: 当需要开始与当前工作区隔离的功能开发或执行实现计划之前使用——创建具有智能目录选择和安全验证的隔离 git 工作树
- **using-superpowers**: 在开始任何对话时使用——确立如何查找和使用技能，要求在任何响应（包括澄清性问题）之前调用 Skill 工具
- **verification-before-completion**: 在宣称工作完成、已修复或测试通过之前使用，在提交或创建 PR 之前——必须运行验证命令并确认输出后才能声称成功；始终用证据支撑断言
- **workflow-runner**: 在 Claude Code / OpenClaw / Cursor 中直接运行 agency-orchestrator YAML 工作流——无需 API key，使用当前会话的 LLM 作为执行引擎。当用户提供 .yaml 工作流文件或要求多角色协作完成任务时触发。
- **writing-plans**: 当你有规格说明或需求用于多步骤任务时使用，在动手写代码之前
- **writing-skills**: 当创建新技能、编辑现有技能或在部署前验证技能是否有效时使用

## 如何使用

当任务匹配某个 skill 时，使用 `Skill` 工具加载对应 skill 并严格遵循其流程。绝不要用 Read 工具读取 SKILL.md 文件。

如果你认为哪怕只有 1% 的可能性某个 skill 适用于你正在做的事情，你必须调用该 skill 检查。
<!-- superpowers-zh:end -->

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
