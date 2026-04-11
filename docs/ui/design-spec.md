# 设计规范（Forum / Vue3）

## 三层约束

1. **本文件** — 产品与视觉约定（色板、版式、内容规则）。
2. **`frontend/src/styles/forum-tokens.css`** — 唯一 Design Tokens 源；实现层只引用 `var(--…)`。
3. **组件** — 以 Element Plus 为基础；重复形态封装到 `components/ui`（如 `BaseButton`），再写页面。

Cursor 侧见 `.cursor/rules/`：`forum-ui-implementation.mdc`（工程纪律）、`ui-design.mdc`（设计值摘要）。

---

## 色板

| 语义 | 十六进制 |
|------|----------|
| Primary | `#1677FF` |
| Primary Hover | `#4096FF` |
| Primary Active | `#0958D9` |
| Text Primary | `#1F1F1F` |
| Text Secondary | `#595959` |
| Text Disabled | `#BFBFBF` |
| Background Main | `#F5F5F5` |
| Card | `#FFFFFF` |
| Border | `#E5E6EB` |

深色模式覆盖见 `forum-tokens.css` 中 `html.dark[data-theme='dark']`。

---

## 间距与圆角

- 间距刻度：`4 / 8 / 12 / 16 / 24 / 32`（px），对应 token：`--space-xs` … `--space-xl`，另有 `--space-12`（12px）。
- 圆角：`--radius-sm` 6px、`--radius-md` 8px、`--radius-lg` 12px、`--radius-pill` 胶囊。

---

## 版式

- 主容器最大宽度 **1200px**（`--container-max`）。
- 论坛三栏：左约 **240px**、中自适应（内容区约 600–720px 为佳）、右 **300px**。
- 布局优先 **flex**；避免滥用绝对定位。

---

## 论坛内容块

- 标题：**最多 2 行**，超出省略。
- 摘要：**最多 2 行**，超出省略。

---

## Element Plus

- 主操作使用 `type="primary"`。
- 全局通过 CSS 变量桥接 EP（`--el-color-primary`、`--el-border-radius-base` 等）；非必要不堆 `:deep` 覆盖。

---

## 类名

推荐使用 **BEM**：`block__element--modifier`。
