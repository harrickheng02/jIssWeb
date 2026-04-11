## Context

Gitee 个人版仓库无可靠「看板列」公开 API；README 与 `.gitee/ISSUE_TEMPLATE` 已约定里程碑与标签。同步工具必须在 **Gitee Open API v5** 上实现里程碑、Issue、标签的创建与更新，且可重复执行不重复堆砌数据。

## Goals / Non-Goals

**Goals:**

- 使用 `access_token` 调用 v5 REST（`https://gitee.com/api/v5`），对齐 README 中 M1/M2/M3 与优先级标签。
- 幂等：按里程碑标题、Issue 标题或约定键去重后再创建或 PATCH。
- 密钥仅来自环境变量或本地忽略文件，不进入版本库。

**Non-Goals:**

- 不实现 Gitee 网页看板列拖拽的 API 镜像（个人版通常不可用）。
- 不自动修改 README 正文（可选手工或另变更）。
- 不接入 Gitee Team 企业 OpenAPI。

## Decisions

| 决策 | 选择 | 理由 |
|------|------|------|
| 实现形态 | 单语言脚本（Node 或 Python，与仓库维护成本一致即可） | 无长期驻留服务，易在本地与 CI 触发 |
| 输入 | 默认 YAML 或 JSON 清单（里程碑、Issue 列表、标签）；与 README 手工对齐 | 比解析 Markdown 稳定，便于 AI 生成 |
| 幂等键 | 里程碑 `title`；Issue 用 `title` + 里程碑 number，或 body 内隐藏锚点 | API 可 GET 列表后比对 |
| HTTP | 原生 fetch/httpx + 简单重试（429/5xx） | 避免过重依赖 |

## Risks / Trade-offs

| 风险 | 缓解 |
|------|------|
| Token 泄露 | 仅 env；文档提示轮换令牌 |
| API 限流 | 串行请求、退避重试 |
| 人工改标题导致幂等失效 | 文档说明「同步键」约定；可选注释字段 |

## Migration Plan

无线上数据迁移。首次使用：在 Gitee 生成私人令牌 → 配置 env → 干跑 `--dry-run`（若实现）→ 实跑。

## Open Questions

- 是否用 Node（与 frontend 同仓库）或 Python（与部分 DevOps 习惯一致）由实现任务拍板。
