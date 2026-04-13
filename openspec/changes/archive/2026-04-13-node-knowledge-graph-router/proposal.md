## Why

需求与规范分散在 OpenSpec、`pm-plan` 与 Cursor 规则中，会话里难以稳定选取最小相关文档集；需要可重复的索引与路由，并以 Node 落地以便与现有 `scripts` 生态一致。

## What Changes

- 新增 Node 工具：从仓库权威源构建轻量知识图谱（JSON），并提供基于查询文本的路由 CLI，输出有序上下文包（路径列表与可选摘要）。
- 可选：PR/本地校验（Issue 引用的 spec 路径存在、图与源文件同步）。
- 不引入图数据库服务端；v0 为文件型产物与只读索引。

## Capabilities

### New Capabilities

- `repo-knowledge-router`：数据源约定、图模型与边类型、索引与增量重建、路由 CLI 输出格式、与 `.cursor/rules`/`skills` 的衔接约定。

### Modified Capabilities

- （无）本变更为工具链与规范扩展，不改变既有业务 spec 行为。

## Impact

- 新增 `tools/` 或 `scripts/` 下 Node 包（具体路径见 design）；可能增加 `package.json` 工作区或根脚本入口。
- CI 可选新增一步调用索引/校验。
- 开发者工作流：开工前可运行路由生成 `context-pack`；与现有 `gitee-pm-sync` 无行为冲突。
