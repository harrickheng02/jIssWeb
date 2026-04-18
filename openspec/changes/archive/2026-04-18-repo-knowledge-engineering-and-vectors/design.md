## Context

`scripts/repo-knowledge-router` 已提供离线图谱、`graph:verify`、`PM_OPEN_ISSUES.md`、关键词 `route`。Issue 12 曾将语义检索列为非目标；现需与「迁移友好、可抽离复用」并存：合入门禁以 OpenSpec 为准，向量能力为可选增强。

## Goals / Non-Goals

**Goals:**

- `pm-plan.yaml` 中 `requires_openspec_spec_reference: true` 的 Issue（人工打标，不写死里程碑名）须含至少一条存在的 `openspec/specs/**` 引用；`verify`/`refresh` 失败即不写产物、非零退出。
- `body` 中 `openspec/` 缺失路径仍阻断；`.cursor/rules/` 缺失默认不阻断。
- 可选语义索引：对 `openspec/specs/**/*.md`（及可选 `openspec/changes/**` 片段）分块、嵌入、本地持久化；独立子命令查询；无嵌入依赖时仓库仍可完整 `graph:build`/`verify`（除打标门禁外）。
- 包结构便于日后抽离：核心路由与语义模块边界清晰，配置经环境变量或 JSON。

**Non-Goals:**

- 替换 GitHub/Gitee Issue 标签体系。
- 默认 CI 调用云端大模型；默认 verify 不下载 GB 级模型。
- 多仓库联邦图谱（可留扩展点）。

## Decisions

1. **打标识别**：仅用 Issue 条目上的布尔字段 `requires_openspec_spec_reference: true`（缺省为 false）；与 `milestone`、`module`、远端标签无关。
2. **嵌入实现**：**首版已落地**为 HTTP 嵌入（环境变量见 `openspec/specs/repo-knowledge-semantic-index/spec.md` 中 `REPO_KNOWLEDGE_EMBEDDING_*`）。本地 `@xenova/transformers` 或等价纯 JS 为后续扩展，非首版范围。
3. **索引存储**：`scripts/repo-knowledge-router/data/` 下 gitignore 文件（如 `vector-index.json` + 分块 manifest 或单文件 sqlite）；与 `graph.json` 同目录策略，避免进主分支大文件。
4. **CLI**：保留现有 `route`；新增 `semantic-index build` / `semantic-index search`（或 `graph:semantic-*` npm 包装），不修改 `route` 默认输出，避免破坏确定性测试。
5. **抽离**：核心 `build-graph`、`verify`、`extract paths` 与 `semantic-index` 分目录或子包导出；宿主传入 `repoRoot` 与 globs。

## Risks / Trade-offs

- [漏标 / 误标] → 由编辑 `pm-plan.yaml` 修正；verify 不猜测业务语义。
- [向量依赖体积] → optional install；CI 不跑语义构建。
- [嵌入漂移] → 记录模型名与版本于索引 manifest；查询时校验。

## Migration Plan

1. 实现 verify 新规则 → 为需门禁的 Issue 打标并补全 `body` 直至 `graph:verify` 绿。
2. 再启用团队本地语义索引文档（可选 `core.hooksPath` 不强制）。
3. 抽离包：提取 npm 包时复制 spec 与脚本边界。

## Open Questions

- 首版嵌入选本地小模型还是仅 API（由实现根据仓库体积与合规选定）。
