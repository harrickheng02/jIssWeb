## Why

M2 需要可演示的站内通知：用户在论坛被回复或被 @ 时能收到可查询的通知，且收件人标识与 JWT `sub` 一致，避免错投与越权。当前无持久化通知与列表契约，无法与 pm-plan「站内通知最小闭环」验收对齐。

## What Changes

- 引入**站内**通知投递与存储（不扩展邮件/推送矩阵）；至少一种可验证事件（被回复与/或被 @，最小实现可先覆盖其一再扩展）。
- 提供当前用户通知列表（分页）、已读/未读状态与标记已读；空列表与请求失败在 UI 可感知。
- 论坛写路径（发帖/回复）在适用时**生成**收件人为帖子作者或 @ 目标用户的通知记录；收件人字段仅使用服务端从 JWT 解析的对方身份或从内容解析出的目标 `sub`，不信任客户端自报。
- 顶栏或等价入口暴露通知入口；可选未读数展示（与规范一致即可）。

## Capabilities

### New Capabilities

- `in-app-notifications`: 站内通知领域契约：事件类型、收件人 `RecipientSubId` 与 `sub` 一致、已读未读、列表查询与分页、标记已读、与论坛事件的触发关系。

### Modified Capabilities

- `model-service`: 增加通知持久化与 HTTP 表面，实现 `in-app-notifications` 与既有 JWT 校验、`AuthorSubId` 存储约定一致。
- `forum-content-api`: 增补「创建回复（及若实现 @）时产生通知」的行为边界，避免重复定义用户键语义（引用 `token-identity-consistency` 与 `in-app-notifications`）。
- `forum-homepage-shell`: 顶栏增加通知入口与通知列表/中心页占位或路由衔接（空态、失败态可演示）。

## Impact

- 后端：`JIssWeb.Model.Api`（或既定论坛宿主）Mongo 集合、索引、控制器与集成测试；回复创建路径与通知写入同事务或最终一致策略在设计中定稿。
- 前端：路由、API 客户端、Pinia 或局部状态；Vite 代理若新增路径需对齐。
- 依赖：`openspec/specs/token-identity-consistency`；pm-plan Issue「站内通知最小闭环」「M2 用户主键与「我的帖子」查询约定」（已关闭项为键对齐依据）。
- **非目标**：全渠道推送、站内信会话、复杂模板引擎。
