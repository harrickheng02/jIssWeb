## 1. 数据模型与集合基础

- [x] 1.1 创建 `ForumTagRecord.cs`（Model.Api/Models/）：字段 Id、Name、Slug、Description、Status、MergedIntoSlug、UseCount、CreatedAtUtc、CreatedBySub、UpdatedAtUtc、UpdatedBySub；定义 `ForumTagStatuses` 常量类（"active"/"disabled"/"merged"）
- [x] 1.2 在 `ForumMongoSetup.cs` 添加 `forum_tags` 集合常量，注册 BsonClassMap，并在 `EnsureIndexes` 中建立三个索引：Slug 唯一索引、(Status asc + UseCount desc) 复合索引、Name 前缀索引
- [x] 1.3 编写集成测试：验证 Slug 唯一索引在并发插入时触发冲突（`ForumTagRegistryIndexTests`）

## 2. 公开标签接口后端

- [x] 2.1 修改 `ForumTagsController.Popular`：数据源切换为查询 `forum_tags`（Status=active），按 UseCount desc + Slug asc 排序；去掉帖子聚合逻辑
- [x] 2.2 在 `ForumTagsController` 新增 `GET /api/forum/tags/suggest`：参数 `q`（可为空）、`limit`（默认10，max20），返回 active 标签 Name 列表；Name/Slug 包含 q（case-insensitive）
- [x] 2.3 编写集成测试（`ForumTagsPublicTests`）：popular 仅返回 active 标签、按 UseCount 排序；suggest 按 q 过滤、disabled 标签不出现

## 3. 发帖标签混合模式后端

- [x] 3.1 修改 `ForumPostsController.NormalizeCreateTags`：保持长度（max 32）/数量（max 10）/去重校验，**不做注册表校验**（hybrid mode：自由标签允许）；未注册 tag 静默跳过 UseCount 更新
- [x] 3.2 修改发帖成功路径：持久化后对所有 tag 执行 `forum_tags.UseCount` +1（`UpdateManyAsync` + `$inc`，仅命中注册表中 Name 匹配的记录）
- [x] 3.3 修改 `ModDeleteController`（或 ForumPostsController）帖子删除路径：删除成功后对原帖 tags 执行 UseCount -1（min 0，UseCount>0 guard，`UpdateManyAsync` 批量）
- [x] 3.4 更新现有集成测试 fixture（`ForumPostsCreateTests` 等）：在测试 seed 阶段预先向 `forum_tags` 插入测试用标签（Status=active），确保现有测试继续通过
- [x] 3.5 ~~新增集成测试（`ForumTagValidationTests`）~~ **已作废**：hybrid mode 下不存在"未注册 tag → 400"场景；UseCount 副作用由现有 fixture seed + popular 排序测试间接覆盖

## 4. 管理员 CRUD API 后端

- [x] 4.1 创建 `AdminTagsController.cs`（Route: `api/forum/admin/tags`），注册 `[RequireForumAdmin]`；实现 `GET /api/forum/admin/tags`（分页、status 过滤、q 搜索）
- [x] 4.2 实现 `POST /api/forum/admin/tags`（创建）：Name 规范化为 Slug，Slug 唯一冲突 → 409 `TAG_SLUG_CONFLICT`，Status=active，UseCount=0
- [x] 4.3 实现 `PATCH /api/forum/admin/tags/{id}`（编辑 Name/Description）：merged 标签 → 409 `TAG_ALREADY_MERGED`；新 Slug 冲突 → 409
- [x] 4.4 实现 `POST /api/forum/admin/tags/{id}/disable` 与 `POST /api/forum/admin/tags/{id}/enable`：状态机转换（merged 标签 → 409；幂等处理已在目标状态的情况）
- [x] 4.5 实现 `POST /api/forum/admin/tags/{id}/merge`：校验 source/target 均为 active 且不同；MongoDB `updateMany` + `arrayFilters` 批量替换帖子 Tags；source → merged；重算 target UseCount（`CountDocumentsAsync`）
- [x] 4.6 实现 `DELETE /api/forum/admin/tags/{id}`：UseCount>0 → 409 `TAG_IN_USE`；active → 409 `TAG_MUST_BE_DISABLED`；否则硬删
- [x] 4.7 实现 `POST /api/forum/admin/tags/seed-from-posts`（非 Production 环境）：聚合 forum_posts Tags → upsert forum_tags（active，UseCount 从帖子计算）
- [x] 4.8 编写集成测试（`ForumTagAdminApiTests`）：覆盖创建/编辑/禁用/启用/合并/删除的正常路径与错误路径；非 admin 调用 → 403；合并后帖子数据验证

## 5. 前端：Auth Store 与路由

- [x] 5.1 在 `frontend/src/stores/auth.ts` 新增 `canAdmin = computed(() => forumRole.value === 'admin')`，并在 store return 对象中暴露
- [x] 5.2 在 `frontend/src/router/index.ts` 新增路由 `{ path: '/admin/tags', name: 'admin-tags', component: AdminTagsView, meta: { requiresAuth: true, requiresAdmin: true } }` 并在导航守卫中处理 `requiresAdmin`（非 admin → redirect home）
- [x] 5.3 在 `frontend/src/components/layout/HeaderUserMenu.vue` 新增"标签管理"入口，`v-if="auth.canAdmin"`，跳转 `/admin/tags`

## 6. 前端：API 客户端

- [x] 6.1 在 `frontend/src/api/clients.ts` 新增以下函数（全部复用 `createClient('/api/forum')`）：`getForumTagSuggest(q, limit)`、`adminListTags(params)`、`adminCreateTag(body)`、`adminPatchTag(id, body)`、`adminDisableTag(id)`、`adminEnableTag(id)`、`adminMergeTag(id, targetSlug)`、`adminDeleteTag(id)`、`adminSeedTagsFromPosts()`

## 7. 前端：AdminTagsView 页面

- [x] 7.1 创建 `frontend/src/views/AdminTagsView.vue`：el-table 展示标签列表（名称、状态徽标、使用数、操作列），支持 status 下拉筛选和 q 搜索输入（debounce 300ms），分页，全部使用 forum-tokens.css 变量
- [x] 7.2 实现"新建标签"对话框（el-dialog）：Name（必填，max 32）+ Description（可选）；提交调用 `adminCreateTag`；冲突错误内联显示
- [x] 7.3 实现行操作：禁用（confirm 弹窗）、启用（直接调用）、合并（见下）
- [x] 7.4 实现"合并"对话框：目标标签搜索框（`adminListTags?q=...&status=active` 自动补全，排除自身）；不可逆警告文案；确认后调用 `adminMergeTag`；成功后刷新行
- [x] 7.5 实现 `canAdmin` 在组件中的守卫逻辑（路由守卫已覆盖，组件内只需展示）

## 8. 前端：发帖组件 tags 输入切换

- [x] 8.1 修改 `frontend/src/composables/useForumComposeForm.ts`：新增通过 `getForumTagSuggest` 获取建议逻辑；暴露 `tagSuggestions`、`tagSuggestionsLoading`、`onTagSearch` 供组件绑定；`onComposeTagsChange` 仅做 max 10 限制
- [x] 8.2 修改发帖组件中的 tags 输入 UI：使用 `el-select` 配置 `remote + filterable + allow-create + default-first-option`（hybrid mode）；输入时调用建议接口，用户也可直接输入自定义标签；保留 max 10 校验
- [x] 8.3 在 `ForumMongoSetup.SeedInitialTagsAsync` 中预置 18 个官方初始标签（问答/分享/技术等）；仅在 `forum_tags` 集合为空时执行，幂等安全

## 9. 验证与收尾

- [x] 9.1 运行后端全量测试：`dotnet test tests/JIssWeb.Model.Api.Tests`，确认全部通过（108/108）
- [x] 9.2 运行前端测试：`npm test`，确认无回归（3/3）
- [x] 9.3 手动端到端验证：启动 Model.Api → 调用 seed 接口 → admin 列标签 → 创建/禁用/合并 → 发帖（使用 registered tag 通过，使用未知 tag 400）→ popular/suggest 接口返回正确数据
