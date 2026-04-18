## 1. 路径抽取与校验

- [x] 1.1 扩展 `openspec-paths` 或等价模块：抽取 `openspec/` 与 `.cursor/rules/**/*.mdc` 字符串；解析存在性；缺失的 `openspec/` 仍记 broken；缺失的 `.cursor/` 不提升 verify 失败（仅省略或警告）。
- [x] 1.2 实现 `requires_openspec_spec_reference === true` 判定并接入 `verify`/`refresh` 与 `listBrokenReferences` 或并列校验逻辑（不读里程碑）。
- [x] 1.3 `build-graph`：为 `.cursor/rules` 命中建 `references` 边（存在时）。

## 2. PM_OPEN_ISSUES 与 CLI

- [x] 2.1 重写 `write-pm-open-issues.mjs`：分小节列出 OpenSpec 路径、可选 Cursor 路径；`requires_openspec_spec_reference` 且无有效 `openspec/specs/**` 时输出可见警告。
- [x] 2.2 确认 `graph:route` 行为不变；更新 CLI 帮助文案。

## 3. 语义索引（新模块）

- [x] 3.1 语料分块与持久化格式、`data/` gitignore、manifest（模型 id / schema version）。
- [x] 3.2 嵌入后端（二选一为首版：本地或 HTTP API），环境变量文档化。
- [x] 3.3 子命令 `build` / `search`（或 `graph:semantic-index` / `graph:semantic-search` npm 包装）；空查询非零退出。
- [x] 3.4 根 `package.json` 脚本与可选依赖策略；CI 不强制跑语义构建。

## 4. 规范与 pm-plan

- [x] 4.1 本 change 归档后合并 delta 至 `openspec/specs/repo-knowledge-router/spec.md` 并新增 `openspec/specs/repo-knowledge-semantic-index/spec.md`。
- [x] 4.2 为需门禁的 Issue 设置 `requires_openspec_spec_reference: true` 并补全 `body`；按需收紧 Issue 12 表述与「非目标」删除语义检索冲突。

## 5. 验证

- [x] 5.1 本地跑通 `npm run graph:verify`、`graph:refresh`；语义子命令手工抽检；自动化测试未加（保持脚本包无测试依赖）。
