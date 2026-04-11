---
name: jissweb-change-review
description: >-
  Read-only review of jIssWeb changes against OpenSpec tasks/specs, SOLID
  principles, and project conventions (Vue3/TS/Element Plus frontend, .NET
  backend). Produces a findings report only; does not modify code. Use when
  reviewing a PR or branch, validating an OpenSpec change after implementation,
  or when the user asks for a review / SOLID check / alignment / 对照规范 / 需求核对.
---

# jIssWeb Change Review（只读）

## 角色

- **只做审查与报告**：不执行修改、不运行会改动的命令（可只读查看文件、diff）。
- **目标**：对照 **OpenSpec 变更需求**（`openspec/changes/<change>/tasks.md`、`specs/**/*.md`）与 **仓库约定**，列出偏差与风险。

## 何时使用

- 实现完某个 OpenSpec change 后的验收核对。
- PR / 分支 diff 的规范与需求对齐检查。
- 用户明确要求「review / 审查 / 对照需求 / 代码规范」时。

## 输入（按用户给出的范围）

- 若指定 **change 目录**：优先读 `openspec/changes/<id>/tasks.md`、`proposal.md`、`specs/**/*.md`，再对关联路径做代码审查。
- 若指定 **文件列表或 diff**：逐项对照下方检查清单。
- 若未指定：根据当前对话或 git 范围，说明审查范围假设。

## 前端（`frontend/`）

栈：Vue 3、TypeScript、Vite、Vue Router、Pinia、Element Plus、Axios、Vitest。

| 维度 | 核对要点 |
|------|----------|
| Design tokens | 样式颜色/间距/圆角是否用 `forum-tokens.css` 的 `var(--color-primary)`、`--bg-*`、`--text-*`、`--space-*`、`--radius-*` 等；避免业务里写死 `#xxx`（阴影/装饰例外需在报告中标注）。 |
| 规则 | `.cursor/rules/forum-ui.mdc`；`docs/ui/design-spec.md`。 |
| Element Plus | 主操作是否 `type="primary"`；避免无必要 `:deep` 覆盖组件内部。 |
| 结构 | 组件 `scoped`；类名倾向 BEM；复用 UI 是否适合放在 `components/ui/`。 |
| 论坛列表 | 标题/摘要是否具备两行截断与省略（若适用）。 |
| 路由与鉴权 | `meta.requiresAuth`、未登录跳转与 `stores/auth` 使用是否一致。 |
| API | `api/clients` 或既有封装；错误与类型是否合理。 |

## 后端（`backend/`）

栈：.NET、多项目（`JIssWeb.*.Api`、BFF、Gateway、Domain/Application 等）。

| 维度 | 核对要点 |
|------|----------|
| 分层 | 新逻辑是否落在合理项目（Api / Application / Domain / Infrastructure）。 |
| API | 路由、DTO、错误码与前端/BFF 约定是否一致。 |
| 安全 | 认证、敏感数据、日志是否避免泄露。 |
| 风格 | 与现有 Controller/Service 命名一致；可空引用与异步约定。 |

## SOLID 检查（必做）

审查变更中的**类、服务、组件、composable、store** 时，按下列项核对；有违反则在报告中单独列出（可标注 **S / O / L / I / D**）。

| 原则 | 含义 | 后端（.NET）核对要点 | 前端（Vue/TS）核对要点 |
|------|------|----------------------|------------------------|
| **S** 单一职责 | 一类一事、一事一处 | Controller 只做 HTTP 编排；业务在 Application/Domain；Infrastructure 只做技术细节。一个类是否同时混了校验、持久化、外部调用等多类理由变更。 | 单文件/单函数是否职责过多（巨型 `setup`、视图+请求+格式化全在一起）；Pinia store 是否承载与状态无关的业务。 |
| **O** 开闭 | 对扩展开放、对修改封闭 | 新行为是否通过新实现类/策略/接口扩展，而非在核心方法里堆 `if/else` 分支改老逻辑。 | 新类型展示/策略是否用组件插槽、组合式函数或映射表扩展，而非反复改同一父组件内部。 |
| **L** 里氏替换 | 子类/实现不破坏契约 | 接口实现类是否可互相替换而不改变调用方正确性；异常与返回值是否与接口文档一致。 | 多实现同一 props/接口的组件是否可互换；`extends`/包装是否改变约定行为。 |
| **I** 接口隔离 | 不强迫依赖不需要的能力 | 接口是否过大；调用方是否被迫实现或依赖未使用的方法。DTO/服务接口是否可拆分。 | Composable/类型是否臃肿；是否可拆成多个小而专的 `useXxx` 或类型。 |
| **D** 依赖倒置 | 依赖抽象，而非具体实现 | Application 依赖领域/接口；具体仓储、HTTP 在 Infrastructure 注册；构造函数注入接口。避免 Domain 直接引用 EF/HttpClient 具体类型。 | 业务逻辑依赖抽象（类型、工厂、注入的 `api` 封装），避免在多处直接 `import` 底层 axios 细节；可测性是否被 concrete 绑死。 |

**步骤（执行顺序）**

1. 列出本次变更涉及的**主要类型/文件**（类、服务、Vue 文件、store、composable）。
2. 对每个文件快速问五句：**是否只有一个变更理由（S）？** 新需求是否主要靠**新增**而非改核心分支（O）？**子类/多实现**是否可替换（L）？**接口/composable** 是否过大（I）？**高层**是否依赖具体实现（D）？
3. 将明显违反项写入报告；边界情况标为「建议」并说明上下文。

## OpenSpec 对齐

- `tasks.md` 中勾选项：实现是否在代码中有对应体现（或明确延期/不在范围）。
- `specs/*.md` 中的需求/场景：是否有实现或显式缺口。
- 变更说明与 `design.md` 中的架构决定是否被遵守。

## 报告格式（输出给用户）

使用 Markdown，**中文**简述问题；每条包含：**严重程度**、**位置**、**问题**、**建议**（可选）。

```markdown
## 审查范围
- Change / 分支 / 文件：
- 对照文档：

## 结论摘要
- 通过项（简要）
- 待处理项数量

## 发现项（按严重度）

### 高
- **位置**：`path:行号或符号`
- **问题**：…
- **对齐**：对应 tasks/spec/规则条目（若有）
- **建议**：…

### 中
…

### 低 / 建议
…

## OpenSpec 任务对照
| 任务 | 状态 | 说明 |
|------|------|------|
| … | 满足 / 部分 / 未体现 | … |

## SOLID 相关发现（可选小节）
- 无则写「未发现明显违反」或略过。
- 有则逐条：**原则字母**、**位置**、**说明**。

## 规范与文档引用
- 列出违反的 token/规则文件名或 spec 条目。
```

严重度建议：**高** = 需求未实现、安全/鉴权错误、明显破坏规范；**中** = 可维护性、部分需求偏差；**低** = 风格、命名、可选优化。

## 禁止

- 不要自动修复、不要提交、不要替用户改配置。
- 若信息不足，在报告中列出**缺失输入**与**假设**，而非猜测实现细节。

## 参考路径（仓库内）

- UI 规范：`docs/ui/design-spec.md`、`frontend/src/styles/forum-tokens.css`
- Cursor 规则：`.cursor/rules/forum-ui.mdc`
- OpenSpec：`openspec/changes/` 下各 change
