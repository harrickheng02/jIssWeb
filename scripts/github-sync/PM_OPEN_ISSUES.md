# PM 进行中 Issue 索引

> 由 `graph:build` / `graph:refresh` 生成（`npm run pm:pull` 已串联 `graph:refresh`）。请 **`@scripts/github-sync/PM_OPEN_ISSUES.md`** 引用；勿使用仓库根目录同名文件。

| # | Issue | 状态 | 里程碑 | 模块 | 标题 |
|---|-------|------|--------|------|------|
| 1 | 1 | open | M2 — 互动与发现 | 互动与声望 | 帖子点赞与收藏基础 |
| 2 | 2 | open | M2 — 互动与发现 | 个人中心 | 个人中心与内容管理最小集 |
| 3 | 3 | open | M2 — 互动与发现 | 运营与公告 | 公告位与热门数据接口 |
| 4 | 4 | open | M3 — 治理与增长 | 治理与审核 | 论坛举报与处理最小闭环 |
| 5 | 5 | open | M3 — 治理与增长 | 治理与审核 | 反垃圾与自动处置占位 |
| 6 | 6 | open | M3 — 治理与增长 | 平台与基础设施 | 推荐排序与运营报表出口占位 |
| 7 | 7 | open | M3 — 治理与增长 | 治理与审核 | 版主版区与帖子操作后台（最小集） |
| 8 | 8 | open | M3 — 治理与增长 | 版区与标签 | 标签体系后台管理（CRUD 与绑定规则） |
| 9 | 9 | open | M3 — 治理与增长 | 搜索与发现 | 个性化推荐与算法化热榜 |
| 10 | 10 | open | W — 工作流程与工程效能 | 平台与基础设施 | 工作流程：策划案入库与 pm-plan/OpenSpec 衔接 |
| 11 | 11 | open | W — 工作流程与工程效能 | 平台与基础设施 | 工作流程：变更审阅与合并门禁清单 |
| 12 | 12 | open | W — 工作流程与工程效能 | 平台与基础设施 | 工作流程：Issue 文档路径与知识路由工程化 |
| 13 | 13 | open | W — 工作流程与工程效能 | 平台与基础设施 | 工作流程：CI 与可选 Git hooks 闭环 |
| 14 | 14 | open | W — 工作流程与工程效能 | 平台与基础设施 | 工作流程：复盘沉淀与图谱刷新节奏 |

## 1 · 帖子点赞与收藏基础

- **state**: open
- **milestone**: M2 — 互动与发现
- **module**: 互动与声望

### `body` 中的 openspec 路径
- `openspec/specs/token-identity-consistency`

### `graph:route`（以标题为查询）
- `scripts/github-sync/pm-plan.yaml` — text match

## 2 · 个人中心与内容管理最小集

- **state**: open
- **milestone**: M2 — 互动与发现
- **module**: 个人中心

### `body` 中的 openspec 路径
- `openspec/specs/customer-profile-service`
- `openspec/specs/token-identity-consistency`

### `graph:route`（以标题为查询）
- `scripts/github-sync/pm-plan.yaml` — text match

## 3 · 公告位与热门数据接口

- **state**: open
- **milestone**: M2 — 互动与发现
- **module**: 运营与公告

### `body` 中的 openspec 路径
- `openspec/specs/forum-homepage-shell/spec.md`

### `graph:route`（以标题为查询）
- `scripts/github-sync/pm-plan.yaml` — text match
- `openspec/specs/forum-homepage-shell/spec.md` — from issue "公告位与热门数据接口"

## 4 · 论坛举报与处理最小闭环

- **state**: open
- **milestone**: M3 — 治理与增长
- **module**: 治理与审核

### `body` 中的 openspec 路径
- （无）

### `graph:route`（以标题为查询）
- `scripts/github-sync/pm-plan.yaml` — text match

## 5 · 反垃圾与自动处置占位

- **state**: open
- **milestone**: M3 — 治理与增长
- **module**: 治理与审核

### `body` 中的 openspec 路径
- （无）

### `graph:route`（以标题为查询）
- `scripts/github-sync/pm-plan.yaml` — text match

## 6 · 推荐排序与运营报表出口占位

- **state**: open
- **milestone**: M3 — 治理与增长
- **module**: 平台与基础设施

### `body` 中的 openspec 路径
- `openspec/specs/report-service`

### `graph:route`（以标题为查询）
- `scripts/github-sync/pm-plan.yaml` — text match

## 7 · 版主版区与帖子操作后台（最小集）

- **state**: open
- **milestone**: M3 — 治理与增长
- **module**: 治理与审核

### `body` 中的 openspec 路径
- `openspec/specs/token-identity-consistency`

### `graph:route`（以标题为查询）
- `scripts/github-sync/pm-plan.yaml` — text match

## 8 · 标签体系后台管理（CRUD 与绑定规则）

- **state**: open
- **milestone**: M3 — 治理与增长
- **module**: 版区与标签

### `body` 中的 openspec 路径
- （无）

### `graph:route`（以标题为查询）
- `scripts/github-sync/pm-plan.yaml` — text match

## 9 · 个性化推荐与算法化热榜

- **state**: open
- **milestone**: M3 — 治理与增长
- **module**: 搜索与发现

### `body` 中的 openspec 路径
- （无）

### `graph:route`（以标题为查询）
- `scripts/github-sync/pm-plan.yaml` — text match

## 10 · 工作流程：策划案入库与 pm-plan/OpenSpec 衔接

- **state**: open
- **milestone**: W — 工作流程与工程效能
- **module**: 平台与基础设施

### `body` 中的 openspec 路径
- `openspec/changes/`
- `openspec/specs/shared-foundation/spec.md`

### `graph:route`（以标题为查询）
- `scripts/github-sync/pm-plan.yaml` — text match
- `openspec/specs/shared-foundation/spec.md` — from issue "工作流程：策划案入库与 pm-plan/OpenSpec 衔接"
- `openspec/specs/forum-homepage-shell/spec.md` — from issue "公告位与热门数据接口"
- `openspec/specs/repo-knowledge-router/spec.md` — from issue "工作流程：变更审阅与合并门禁清单"
- `.cursor/rules/pm-plan.mdc` — text match
- `.cursor/skills/pm-plan/SKILL.md` — text match
- `openspec/changes/archive/2026-04-11-gitee-pm-sync/design.md` — text match
- `openspec/changes/archive/2026-04-11-gitee-pm-sync/proposal.md` — text match
- `openspec/changes/archive/2026-04-11-gitee-pm-sync/specs/gitee-pm-sync/spec.md` — text match
- `openspec/changes/archive/2026-04-11-gitee-pm-sync/tasks.md` — text match
- `openspec/specs/github-pm-sync/spec.md` — text match
- `.cursor/skills/openspec-apply-change/SKILL.md` — text match

## 11 · 工作流程：变更审阅与合并门禁清单

- **state**: open
- **milestone**: W — 工作流程与工程效能
- **module**: 平台与基础设施

### `body` 中的 openspec 路径
- `openspec/specs/repo-knowledge-router/spec.md`

### `graph:route`（以标题为查询）
- `scripts/github-sync/pm-plan.yaml` — text match
- `openspec/specs/repo-knowledge-router/spec.md` — from issue "工作流程：变更审阅与合并门禁清单"

## 12 · 工作流程：Issue 文档路径与知识路由工程化

- **state**: open
- **milestone**: W — 工作流程与工程效能
- **module**: 平台与基础设施

### `body` 中的 openspec 路径
- `openspec/specs/`
- `openspec/specs/repo-knowledge-router/spec.md`

### `graph:route`（以标题为查询）
- `scripts/github-sync/pm-plan.yaml` — text match
- `openspec/specs/repo-knowledge-router/spec.md` — from issue "工作流程：Issue 文档路径与知识路由工程化"

## 13 · 工作流程：CI 与可选 Git hooks 闭环

- **state**: open
- **milestone**: W — 工作流程与工程效能
- **module**: 平台与基础设施

### `body` 中的 openspec 路径
- （无）

### `graph:route`（以标题为查询）
- `scripts/github-sync/pm-plan.yaml` — text match
- `openspec/changes/archive/2026-04-11-gitee-pm-sync/design.md` — text match
- `openspec/changes/archive/2026-04-11-gitee-pm-sync/proposal.md` — text match
- `openspec/changes/archive/2026-04-11-gitee-pm-sync/specs/gitee-pm-sync/spec.md` — text match
- `openspec/changes/archive/2026-04-11-gitee-pm-sync/tasks.md` — text match
- `openspec/specs/github-pm-sync/spec.md` — text match
- `openspec/specs/forum-homepage-shell/spec.md` — from issue "公告位与热门数据接口"
- `openspec/specs/repo-knowledge-router/spec.md` — from issue "工作流程：变更审阅与合并门禁清单"
- `openspec/specs/shared-foundation/spec.md` — from issue "工作流程：策划案入库与 pm-plan/OpenSpec 衔接"
- `.cursor/rules/pm-plan.mdc` — text match

## 14 · 工作流程：复盘沉淀与图谱刷新节奏

- **state**: open
- **milestone**: W — 工作流程与工程效能
- **module**: 平台与基础设施

### `body` 中的 openspec 路径
- `openspec/specs`
- `openspec/specs/repo-knowledge-router/spec.md`

### `graph:route`（以标题为查询）
- `scripts/github-sync/pm-plan.yaml` — text match
- `openspec/specs/repo-knowledge-router/spec.md` — from issue "工作流程：复盘沉淀与图谱刷新节奏"

