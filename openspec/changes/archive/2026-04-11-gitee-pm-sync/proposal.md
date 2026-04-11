## Why

README 已定义论坛方向里程碑（M1–M3）、优先级与模块，但个人版 Gitee 仓库需手工维护 Issue/里程碑，易与文档漂移。通过 Gitee Open API v5 将规划**幂等同步**到仓库，可在网页直接看到里程碑与 Issue，支撑后续 AI 或脚本驱动项目管理。

## What Changes

- 增加可重复执行的同步工具（脚本或小 CLI）：创建/对齐里程碑、标签、Issue；**不依赖**个人版不提供的看板列 API。
- 配置通过环境变量注入 `owner`、`repo`、`access_token`；可选从结构化输入（如 YAML）读取任务列表，与 README 里程碑对齐。
- 文档：说明运行方式、幂等策略、与 README「Issue 与看板」小节的关系（看板列仍建议网页或标签模拟）。

## Capabilities

### New Capabilities

- `gitee-pm-sync`: 基于 Gitee API v5 的仓库外项目管理同步能力（里程碑、Issue、标签、幂等与验收约定）。

### Modified Capabilities

- （无）本变更不改变应用运行时行为或既有 `openspec/specs` 下产品能力需求。

## Impact

- 仓库新增脚本目录或工具入口（如 `scripts/`）、示例配置 `.env.example`；可选 CI  job 仅在使用方启用。
- 依赖 Gitee 私人令牌权限与 API 限流；令牌不得提交入库。
