## Context

Issue 11 验收依赖文档化；`repo-graph-verify` 与 `repo-ci-graph-verify` spec 已定义 CI 门禁。本 change 只补「人读」清单与 GitHub 默认 PR 描述结构。

## Goals / Non-Goals

**Goals:**

- 单一路径权威：检查单正文以 `docs/engineering/pr-merge-checklist.md` 为展开版；PR 模板为简短勾选引导并链向该文档。
- 模板字段覆盖 Issue 11：`pm-plan` Issue、OpenSpec、本地/CI `graph:verify`、可选横切测试说明。
- 用一小节说明「必过检查失败则不合并」与分支保护的关系（指向既有 workflow 名）。

**Non-Goals:**

- 不新增 CI job、不改 `repo-graph-verify.yml` 触发条件（除非后续单独 change）。
- 不规定 Code Review 轮次与人数。

## Decisions

1. **PR 模板路径**：`.github/pull_request_template.md`（GitHub 默认识别之一）。
2. **检查单路径**：`docs/engineering/pr-merge-checklist.md`（新建 `engineering` 目录）。
3. **与 pm-plan**：不在模板里写死 Issue 号；用占位说明「关联 `scripts/github-sync/pm-plan.yaml` 中条目或远端 Issue」。
4. **演示记录**：在检查单文末增加「如何演示门禁生效」三行内说明（例如：保护分支上失败 PR 不可合并 + 指向 Actions 检查名）。

## Risks / Trade-offs

- [模板过长] → 模板保持短；细节在 `docs/engineering/`。
- [多仓库 fork] → 模板内链接用相对路径指向本仓库文档。

## Migration Plan

1. 落地两个文件后本地打开 PR 预览（或 GitHub 上 draft PR）确认渲染。
2. Issue 11 关闭前由维护者在 body 或评论中引用两文件路径。

## Open Questions

- 无。
