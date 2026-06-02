## Context

当前标签以 `List<string>` 内嵌于 `ForumPostRecord`，`ForumTagsController` 仅提供一个只读聚合接口 `GET /api/forum/tags/popular`（实时扫描帖子集合）。标签无生命周期、无状态、无治理能力。

重构目标：引入 `forum_tags` 集合作为权威注册表，建立完整的 Admin CRUD 路径，并将帖子写入约束为"只能使用已注册的 active 标签"。项目尚未上线，无生产数据风险，可做破坏性重构。

## Goals / Non-Goals

**Goals:**
- 建立 `ForumTagRecord` 数据模型与 `forum_tags` MongoDB 集合
- 实现标签状态机：`active → disabled / merged`
- Admin CRUD API（创建/编辑/禁用/启用/合并/列表）
- 公开 suggest 接口供发帖自动补全
- popular 接口切换为读注册表（按 UseCount）
- 发帖混合模式（hybrid）：自由标签允许，注册表仅做建议与 UseCount 追踪
- 合并操作同步迁移帖子数据（MongoDB `$[]` arrayFilter 更新）
- 前端 AdminTagsView（`/admin/tags`），`requiresAdmin` 路由守卫
- 发帖组件 tags 输入切换为 suggest API 自动补全

**Non-Goals:**
- 标签层级、分类、权重体系
- 用户自助申请创建标签流程
- 异步/后台合并队列（直接同步执行，数据量小）
- 多语言、同义词、向量语义搜索

## Decisions

### 决策 1：ForumTagRecord 存储 Name（字符串）而非 ID 引用

**结论**：帖子的 `Tags: List<string>` 继续存储标签 Name（规范化后的 canonical name），不改为存储 ObjectId。

**理由**：
- MongoDB 无外键约束，存 ID 不能换来引用完整性保证
- 存 Name 使帖子文档可读，日志/调试友好
- 合并时迁移帖子字段 Name 本身很简单（`arrayFilters` + 批量 updateMany）
- 存 ID 需要在所有帖子读取路径加 join/lookup，成本更高

**备选**：存 Slug（小写规范化）—— 但 Slug 不适合直接展示，额外的 Name→Slug 转换增加复杂度。

### 决策 2：UseCount 由写操作维护（denormalized）

**结论**：帖子发帖/编辑/删除时同步更新 `forum_tags.UseCount`，不在 popular 接口实时聚合。

**理由**：popular 接口是高频只读路径，实时聚合成本随帖子增多线性增长；反范式 UseCount 使该接口退化为简单索引查询。UseCount 偶尔不精确（并发边界）可接受，后台修复接口可补偿。

**备选**：事件驱动（Redis pub/sub 异步更新）—— 对当前规模过度复杂。

### 决策 3：合并同步执行，不走队列

**结论**：`POST /admin/tags/{id}/merge` 在请求内同步完成标签状态更新 + 帖子批量迁移 + UseCount 重算。

**理由**：项目未上线，帖子量极小（开发数据），同步执行 < 100ms；引入后台 job 显著增加复杂度。随着项目增长，后续可迁移为异步，设计上合并接口不承诺完成时间（返回 200 表示已完成）。

### 决策 4：发帖写入路径混合模式（hybrid mode）

**结论**：发帖时后端**不做注册表白名单校验**，用户可自由输入任意标签字符串（hybrid mode）。注册表仅作为建议来源（suggest/popular 接口）和算法加权依据，不阻断发帖。UseCount 仅对注册表中存在的标签（按 Name 匹配）执行 +1/-1，未注册标签的 UseCount 不被追踪。

**理由**：强制白名单会导致功能鸡肋——用户无法使用注册表中没有的标签，体验大幅受损；而 hybrid mode 既保留官方标签集合（可用于后续算法推荐、热度排序），也允许用户创意性自由标注。

**前端配合**：发帖组件 tags 输入使用 `el-select` 配置 `remote + filterable + allow-create + default-first-option`，调用 `/api/forum/tags/suggest` 实时搜索建议，用户也可直接输入自定义标签并按 Enter 确认。

**UseCount 大小写敏感性限制**：`UpdateManyAsync` 按 Name 字段精确匹配（MongoDB 默认 case-sensitive）。若用户输入"Python"而注册表存储"python"，UseCount 不会被更新。此为已知限制，在项目当前阶段（中文标签为主，大小写歧义极少）可接受；后续可在 Name 字段上建 collation 索引或在写入路径做规范化处理。

### 决策 5：管理员权限边界

**结论**：标签 CRUD 使用已有 `[RequireForumAdmin]` 属性（JWT `forumRole == "admin"`），不引入新权限层级。版主无标签管理权。

**理由**：标签是全站级资源，不绑定特定版块，版块级版主无合理的标签管辖依据。

### 决策 6：前端引入 canAdmin computed

**结论**：在 `frontend/src/stores/auth.ts` 新增 `canAdmin = computed(() => forumRole.value === 'admin')`，路由 meta 新增 `requiresAdmin` flag，路由守卫处理跳转。

**理由**：与现有 `canModerate` 模式对齐，避免在组件里散落 `forumRole === 'admin'` 判断。

## Risks / Trade-offs

- **UseCount 短暂不一致** → 后台 recalc 接口（`POST /admin/tags/recalc-counts`）可随时修正；popular 接口容忍轻微误差
- **发帖 breaking change** → 现有硬编码测试用 tag 需提前注册；集成测试 fixture 应在 seed 阶段写入测试用标签
- **管理 UI 工作量** → AdminTagsView 使用 Element Plus `el-table` + 弹窗，严格复用 forum-tokens.css 变量，避免自写样式
- **合并不可逆** → 合并后源标签 status=merged，帖子数据已被覆写；无回滚机制（项目未上线，可接受）

## Migration Plan

1. **seed 脚本**：admin API 启动后，调用 `POST /api/forum/admin/tags/seed-from-posts`（仅开发环境），聚合 forum_posts 现有 tags → 批量 upsert 到 forum_tags（全部 active）
2. **开关**：seed 接口仅在 `ASPNETCORE_ENVIRONMENT != Production` 时注册，防止误触
3. **回滚**：若需回滚（仅开发阶段），drop `forum_tags` 集合并还原 `ForumTagsController` 的 popular 聚合逻辑即可；帖子字段无变化

## Open Questions

- `GET /api/forum/tags/suggest` 是否需要支持按 boardId 过滤？（建议先不做，全局标签库更合理，后续可扩展）
- AdminTagsView 是否需要批量导入标签（CSV）？（当前 scope 内不做，M4 需求）
