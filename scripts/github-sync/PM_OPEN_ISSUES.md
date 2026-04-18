# PM 进行中 Issue 索引

> 由 `graph:build` / `graph:refresh` 生成（`npm run pm:pull` 已串联 `graph:refresh`）。请 **`@scripts/github-sync/PM_OPEN_ISSUES.md`** 引用；勿使用仓库根目录同名文件。

| # | Issue | 状态 | 里程碑 | 模块 | 标题 |
|---|-------|------|--------|------|------|
| 1 | 2 | open | M2 — 互动与发现 | 个人中心 | 个人中心与内容管理最小集 |
| 2 | 3 | open | M2 — 互动与发现 | 运营与公告 | 公告位与热门数据接口 |
| 3 | 4 | open | M3 — 治理与增长 | 治理与审核 | 论坛举报与处理最小闭环 |
| 4 | 5 | open | M3 — 治理与增长 | 治理与审核 | 反垃圾与自动处置占位 |
| 5 | 6 | open | M3 — 治理与增长 | 平台与基础设施 | 推荐排序与运营报表出口占位 |
| 6 | 7 | open | M3 — 治理与增长 | 治理与审核 | 版主版区与帖子操作后台（最小集） |
| 7 | 8 | open | M3 — 治理与增长 | 版区与标签 | 标签体系后台管理（CRUD 与绑定规则） |
| 8 | 9 | open | M3 — 治理与增长 | 搜索与发现 | 个性化推荐与算法化热榜 |
| 9 | 11 | open | W — 工作流程与工程效能 | 平台与基础设施 | 工作流程：变更审阅与合并门禁清单 |
| 10 | 12 | open | W — 工作流程与工程效能 | 平台与基础设施 | 工作流程：Issue 文档路径与知识路由工程化 |
| 11 | 14 | open | W — 工作流程与工程效能 | 平台与基础设施 | 工作流程：复盘沉淀与图谱刷新节奏 |

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

