## Context

仓库已有 OpenSpec 全局 spec、`openspec/changes`、以及 `scripts/gitee-sync/pm-plan.yaml` 与 `.cursor/rules`；缺少机器可消费的「文档—规划—规则」关系与按需路由。根 `package.json` 已通过 `--prefix scripts/gitee-sync` 调用子包，宜沿用「独立子目录 Node 包 + 根 npm script 转发」模式。

## Goals / Non-Goals

**Goals:**

- 用 Node 实现只读索引：从 `openspec/specs/**`、`openspec/changes/**`（含 archive）、`scripts/gitee-sync/pm-plan.yaml`、`.cursor/rules/**` 抽取节点与边，生成可 diff 的图产物（JSON）。
- 提供 CLI：`build`（写图）、`route "<query>"`（输出有序路径列表与简短命中理由）、可选 `verify`（校验 pm-plan 正文引用的 `openspec/...` 路径存在）。
- 输出稳定 JSON 形状，便于 Cursor 会话粘贴或后续 hook 消费。

**Non-Goals:**

- **`data/graph.json` 不入库**：由 `build` 本地生成；根 `.gitignore` 已忽略。
- 图数据库服务、向量数据库、全仓库 AST 级代码依赖图。
- 自动改写规则或 spec；本工具只读。
- 与 Gitee API 交互（仍由 `gitee-pm-sync` 负责）。

## Decisions

- **包位置**：`scripts/repo-knowledge-router/`（自有 `package.json`），根 `package.json` 增加 `graph:build`、`graph:route` 等转发脚本，与 `pm:*` 并列。
- **运行时**：Node 20+、ESM、`yaml` 解析 `pm-plan.yaml`；Markdown 用轻量 frontmatter/标题扫描，不引入重型 MD AST（除非后续需求）。
- **图产物**：默认写入 `scripts/repo-knowledge-router/dist/graph.json`（或 `data/graph.json`，实现阶段二选一并在 spec 中固定），CI/本地可提交或 gitignore 二选一（倾向 **提交** 以便 PR 可见 diff，若体积过大再改 ignore + CI 生成）。
- **路由 v0**：关键词分词 + 路径 token 匹配 + 显式边扩展（issue→spec、change→spec）；预留后续换 BM25/向量接口。
- **与 Cursor rules 关系**：规则文件作为图节点；`route` 命中与当前工作区 glob 可选项后续再加；v0 仅「若查询包含 rule 文件名或描述则提升该节点权重」。

## Risks / Trade-offs

- [图体积增长] → 仅索引元数据（路径、标题、摘要字段），正文可截断或按需二次读取。
- [解析 fragile] → `verify` 与最小 golden 样例；失败时清晰报错路径。
- [提交 graph.json 噪音] → 若 churn 高可改为 CI-only 产物并在 spec 修订为不提交。

## Migration Plan

首次合并后：维护者在需要时运行 `npm run graph:build`；可选在 CI 加非阻塞或阻塞校验。无线上迁移。

## Open Questions

- `graph.json` 是否纳入版本控制待首版体积评估。
- `route` 是否输出 Markdown 块以便直接粘贴到 Cursor（可在 tasks 中定稿）。
