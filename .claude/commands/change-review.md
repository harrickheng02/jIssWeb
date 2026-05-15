---
name: "Change Review"
description: 只读审查变更，对照 OpenSpec tasks/specs、SOLID 原则与仓库约定
category: Workflow
tags: [review, openspec, quality]
---

对照 OpenSpec 变更需求与仓库约定，输出审查报告（不修改代码）。

**输入**：`/change-review <change名称 或 文件路径>`，若省略则从对话上下文推断。

遵循 `.claude/skills/change-review/SKILL.md` 中的完整检查清单与报告格式。
