## Context

当前后端已具备站内通知雏形与关键链路：

- 数据模型：`InAppNotificationRecord`（收件人 `RecipientSubId`、行为人 `ActorSubId`、`PostId`、可选 `ReplyId`、`ReadAtUtc`、`CreatedAtUtc`）。
- 生成时机：`POST /api/forum/posts/{postId}/replies` 创建回复成功后，若回复作者 `sub` 与帖子作者 `AuthorSubId` 不同，则写入一条 `ReplyToPost` 通知；以 `ReplyId` 建唯一索引以保障幂等。
- 读写边界：通知列表与已读操作从 token 解析 `sub`，并以 `RecipientSubId == sub` 过滤，不接收客户端指定收件人。

本变更把“可演示、可测试”的端到端合同补齐，尤其是深链字段约定、分页稳定性与已读幂等语义。

## Goals / Non-Goals

**Goals:**
- 形成一套可实现的端到端合同：回复触发通知、通知列表分页与未读筛选、未读数、单条/全部标已读、深链跳转与回复定位。
- 明确身份与鉴权：收件人筛选与已读变更均以服务端解析的 token `sub` 为唯一依据。
- 明确客户端渲染合同：空态与错误态可区分；通知项字段足够渲染摘要与跳转。

**Non-Goals:**
- 站外通道（邮件/短信/WebPush）与消息中台。
- 复杂通知类型与聚合（点赞、收藏、@、系统公告等聚合/折叠）。
- 搜索、推荐与运营报表相关的通知策略。

## Decisions

### Decision: 以 `RecipientSubId` 作为收件人主键，严格来自 token `sub`

**Rationale**
- 与 `token-identity-consistency` 一致，服务端可验证且可审计。
- 避免客户端可控 user id 造成越权读取与写入已读状态。

**Alternatives**
- 客户端传入 recipient id：需要额外一致性校验与统一 403/404 策略，收益不足。

### Decision: 回复触发通知写入发生在“回复创建成功”之后

**Rationale**
- 事件语义单一：回复写入成功即意味着需要通知楼主。
- 失败隔离：回复创建失败不产生通知；通知写入失败不影响回复结果（后续可扩展为异步重试）。

**Alternatives**
- 以队列/事件总线异步投递：需要引入基础设施与幂等/重试策略，超出最小集范围。

### Decision: 通知列表采用页码分页，排序稳定键为 `(CreatedAtUtc desc, Id desc)`

**Rationale**
- 现有接口与前端常见分页模型一致，便于快速落地。
- 在新通知持续写入的场景下，稳定键用于前端去重与合并，降低重复渲染与跳页抖动。

**Alternatives**
- 游标分页（cursor）：更适合无限滚动与强一致分页，但需要额外合同字段与实现改造，后续可演进。

### Decision: 已读语义以“首次已读时间”为准，操作具备幂等性

**Rationale**
- `MarkRead` / `MarkAllRead` 重复调用应产生一致的最终状态。
- 保留首次已读时间便于后续统计与产品策略（例如未读时长）。

**Alternatives**
- 每次标已读覆盖 `ReadAtUtc`：实现简单但时间语义不稳定。

### Decision: 深链合同使用 `PostId` + 可选 `ReplyId`

**Rationale**
- 最小可用：通知项可跳到帖子详情；有 `ReplyId` 时可定位与高亮。
- 与后端已存字段一致，无需额外 join。

**Contract**
- 通知项 SHALL 返回 `PostId`，并在关联到具体回复时返回 `ReplyId`。
- 前端深链 SHALL 以“帖子详情路由 + 定位参数”实现：例如 `/forum/posts/{postId}?reply={replyId}` 或 `#reply-{replyId}`。
- 详情页在加载回复列表后，若能匹配 `ReplyId`，SHALL 滚动定位并短暂高亮；匹配失败 SHALL 回退到帖子顶部并保留“来自通知”的提示占位。

## Risks / Trade-offs

- **[通知写入与回复写入同请求链路]** → **Mitigation**：以 `ReplyId` 唯一索引保证幂等；通知写入异常记录日志并保持回复成功返回；后续可引入异步投递与重试。
- **[页码分页在新增数据下可能出现重复/跳页]** → **Mitigation**：稳定排序键 + 客户端按 `Id` 去重与合并；默认只增量刷新第一页。
- **[深链定位依赖回复列表加载量]** → **Mitigation**：最小集阶段先用“全量加载回复”或“加载到目标出现”为策略占位；后续可引入按楼层/游标的局部加载。
