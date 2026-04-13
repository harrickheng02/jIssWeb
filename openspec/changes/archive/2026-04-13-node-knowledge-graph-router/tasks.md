## 1. 包与入口

- [x] 1.1 创建 `scripts/repo-knowledge-router/`（`package.json`、`type: module`、Node 引擎、`bin` 或 `node src/cli.mjs`）
- [x] 1.2 根 `package.json` 增加 `graph:build`、`graph:route`、`graph:verify`（`npm run --prefix scripts/repo-knowledge-router ...`）

## 2. 索引与图模型

- [x] 2.1 实现扫描器：`openspec/specs`、`openspec/changes`、`.cursor/rules`、可选 `.cursor/skills`
- [x] 2.2 解析 `pm-plan.yaml`（issues/modules/milestones）并从 issue `body` 提取 `openspec/` 路径
- [x] 2.3 写出符合 `repo-knowledge-router` spec 的 `graph.json`（含 `version`、`generatedAt`、`nodes`、`edges`）

## 3. 路由与校验

- [x] 3.1 实现 `route`：查询分词、路径匹配、沿 `references` 等边扩展，输出 capped 列表与一行理由
- [x] 3.2 实现 `verify`：校验 issue 引用的 `openspec/` 路径存在；缺失则非零退出
- [x] 3.3 空查询与缺参时非零退出与用法说明

## 4. 质量与集成

- [x] 4.1 文档化 `graph.json` 是否提交仓库的决策并在 `.gitignore` 或 PR 流程中落实
- [x] 4.2 （可选）CI 增加 `graph:verify` 或 `graph:build` 步骤
