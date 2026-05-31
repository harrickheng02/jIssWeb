## Why

Issue #19 需在举报最小闭环之上补齐对违规账号的治理能力。社区常见违规（灌水、辱骂、广告）用**警告 + 限时禁言**即可覆盖；全量封禁开发面过大且易引发争议。首期 change-A 建立 User.Api 处罚持久化与 Model.Api 写操作实时拦截，并为举报队列强制 `reportId` 审计关联，供 #18 操作台后续筛选。

## What Changes

- User.Api 新增 `user_sanctions` 集合与内网 `GET /api/internal/users/{sub}/forum-sanction-status`（5 分钟缓存，下发/解封 invalidate）。
- Model.Api 新增版主/管理员下发**警告**、**禁言**（preset：`24h` 默认 / `7d` / `30d`）及**提前解封**端点；写操作前查 User.Api，禁言返回 `403 FORUM_MUTED`。
- 禁言阻断：发帖、回复、自编辑、草稿发布；不禁登录、浏览、点赞/收藏、举报。
- 警告写站内通知；**处罚 reason 必填**（举报队列路径下的警告/禁言）。
- 举报队列发起的删帖/删回复 **强制 `reportId`** 写入 audit，**不向作者通知或披露删除原因**；处罚仍写 audit + 必要通知（警告）。
- 禁言到期自动失效（查询判定）；提前解封记录操作人与原因。

## Capabilities

### New Capabilities

- `forum-user-sanctions`：警告与限时禁言的数据模型、User.Api 内网状态查询、Model.Api 下发/解封 REST、审计与通知副作用。

### Modified Capabilities

- `forum-content-api`：论坛写端点在有效禁言期间 SHALL 拒绝并返回 `FORUM_MUTED`。
- `forum-moderation-delete-content`：自举报队列上下文调用时 SHALL 要求 `reportId` 并写入 audit metadata；删内容 **不**向作者发通知、不要求 reason。
- `forum-report-moderation-ui`：举报队列展开区增加警告/禁言（时长下拉默认 24h）及 reason 必填校验。
- `in-app-notifications`：新增警告类系统通知类型与渲染文案。
- `user-service`：补充内网 forum sanction 查询端点契约（不改变 JWT 签发语义）。

## Impact

- **User.Api**：Mongo `user_sanctions`、内网 controller、进程内缓存。
- **Model.Api**：SanctionGuard、Mod 处罚 controller、删内容 body 扩展、集成测试。
- **Frontend**：举报队列治理面板、403 禁言提示。
- **依赖**：Issue #4/#21 closed；与 Issue #18 共享 `audit.metadata.reportId`。
- **UI**：遵循 `forum-tokens.css`。

## 非目标

全量封禁（ban）；永久禁言；JWT claim 携带处罚；SLA/指派；已受理通知（change-B）；证据导出（change-C）；累犯自动升级禁言；法务级证据链；向被处罚人/举报人公开详细处理结论；**版主删帖/删回复向作者发送通知或披露删除原因**。
