## Why

标签当前以自由字符串内嵌于帖子记录（`ForumPostRecord.Tags`），无独立实体、无审核、无状态管理；`GET /tags/popular` 通过实时聚合帖子动态计算，无法禁用、合并或预设标签。随着论坛内容规模扩大，垃圾标签与标签碎片化问题将持续恶化，且无任何治理手段。Issue #8 要求在 M3 交付标签管理能力，为后续推荐、搜索、内容治理奠定基础。

## What Changes

- **新增** `forum_tags` MongoDB 集合，作为标签的权威数据源，取代从帖子聚合的做法
- **新增** 管理员 CRUD API（`/api/forum/admin/tags`），支持创建、编辑、禁用/启用、合并、查询
- **新增** 标签建议接口 `GET /api/forum/tags/suggest`，供发帖自动补全使用
- **修改** `GET /api/forum/tags/popular`：从帖子聚合改为查询 `forum_tags` 注册表（按 `UseCount` 排序，仅返回 active 标签）
- **修改** 发帖/编辑帖子：tags 字段严格校验，必须存在于注册表且为 active 状态；**BREAKING**（现有自由输入行为被约束）
- **新增** 一次性迁移：将现存帖子 tags 聚合后 seed 到 `forum_tags`
- **新增** 前端 `AdminTagsView.vue`（路由 `/admin/tags`），仅管理员可访问
- **修改** 发帖组件 tags 输入改为从 suggest 接口自动补全，替换现有自由文本输入

## Capabilities

### New Capabilities

- `forum-tag-registry`: 标签注册表——`forum_tags` 集合数据模型、状态机（active/disabled/merged）、UseCount 维护、合并时同步迁移帖子数据
- `forum-tag-admin-api`: 管理员标签 CRUD REST API，含创建、编辑、禁用/启用、合并、列表查询
- `forum-tag-admin-ui`: 后台标签管理页面（`AdminTagsView.vue`），`requiresAdmin` 路由守卫，使用 forum-tokens.css 约束

### Modified Capabilities

- `forum-content-api`: 发帖 tags 字段新增严格注册表校验（从允许任意字符串变为必须匹配 active 标签）；`/tags/popular` 数据源切换

## Impact

- **服务边界**：仅影响 `JIssWeb.Model.Api`（新集合、新控制器、修改现有标签和帖子接口）
- **前端**：新路由 `/admin/tags`，发帖组件 tags 输入行为变更（须符合 forum-tokens.css）
- **数据迁移**：需一次性 seed 脚本将帖子 tags 写入 `forum_tags`
- **鉴权**：管理 API 依赖已有 `RequireForumAdmin` attribute；前端依赖 auth store `forumRole === 'admin'`

## Non-goals

- 多语言同义词关系网
- 运营标签 A/B 实验
- 第三方标签云同步
- 标签层级/分类体系
- 用户自助创建标签（严格模式：只有管理员可创建）
