## Why

工程知识路由需要同时满足：迁移友好（合入门禁不绑定 Cursor）、pm-plan 里 **打标** Issue 与 OpenSpec 可对齐校验、以及体量增长后 agent/开发者用自然语言稳定命中规范。此前将「语义向量检索」列为非目标，现需正式纳入并与可抽离部署的长期目标一致。

## What Changes

- 约定 `pm-plan.yaml` 中 **显式打标** 的 Issue（`requires_openspec_spec_reference: true`，与里程碑/业务模块无关）在 `body` 中须含至少一条可解析且存在的 **`openspec/specs/**`** 路径；**`graph:verify` 对此失败即非零**。`.cursor/rules/**` 可抽取展示，**默认不参与 verify 失败条件**。
- **扩展** pm-plan `body` 路径抽取：除 `openspec/` 外识别 `.cursor/rules/*.mdc`（存在则建边）；缺失时仅省略或告警，不阻断默认 verify。
- **新增** 可选语义索引：对约定语料分块、嵌入、落盘索引；提供独立 CLI（如 `graph:semantic-search` 或等价），**不**并入默认 `graph:verify`；支持后续抽成独立包、宿主项目配置语料范围。
- **更新** `PM_OPEN_ISSUES.md` 生成：区分 OpenSpec 引用与可选 Cursor 引用；对 **已打标** 且仍无有效 `openspec/specs/**` 的 Issue 显式提示。

## Capabilities

### New Capabilities

- `repo-knowledge-semantic-index`: 基于仓库内规范语料的可选向量索引构建与查询；与默认离线图谱构建解耦；可配置嵌入来源；默认可在无向量依赖下跳过。

### Modified Capabilities

- `repo-knowledge-router`: 打标 Issue 的 OpenSpec 强制校验、路径抽取与 `PM_OPEN_ISSUES` 分区展示；Purpose 与若干 Requirements 随上行为更新（见 delta spec）。

## Impact

- `scripts/repo-knowledge-router/`：抽取逻辑、verify、route、write-pm-open-issues、CLI、可选新模块与 `package.json` 依赖（向量相关为可选子路径或条件安装）。
- 根 `package.json`：新增 `graph:*` 脚本项（语义子命令）；`pm:push` / CI 仍只依赖现有 `graph:verify` 行为扩展后的规则。
- `openspec/specs/repo-knowledge-router/spec.md`：归档时由本 change 的 delta 合并。
- 可选：`scripts/github-sync/pm-plan.yaml` 中 Issue 12 相关表述与依赖路径，与实现后的验收一致。
