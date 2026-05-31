## Context

Issue #19 change-A 在举报最小闭环（Issue #4）与结案通知（Issue #21）之上，引入**警告 + 限时禁言**。当前 `UserAccount` 无处罚字段；Model.Api 写端点无禁言检查；删帖/举报 PATCH 与 audit 无 `reportId` 关联。

跨服务边界：User.Api 持久化 `user_sanctions` 并供查询；Model.Api 下发处罚、写操作 enforcement、删内容扩展；两服务均为独立 Mongo 库。

## Goals / Non-Goals

**Goals:**

- 版主/管理员可对违规账号下发警告（通知 + 留痕）或禁言（preset 24h/7d/30d，默认 24h）。
- 论坛写操作实时查 User.Api 拦截有效禁言（`403 FORUM_MUTED` + `mutedUntilUtc`）。
- 举报队列路径：删内容 **reportId 强制**、**不向作者通知或披露原因**；警告/禁言 **reason 必填**、**reportId 强制**，写入 `forum_moderation_audit.metadata`。
- 禁言到期自动失效；支持提前解封（操作人 + 原因 + audit）。
- User.Api 5 分钟进程内缓存 + 下发/解封 invalidate；Model.Api 可选 ≤60s 本地缓存。

**Non-Goals:**

- 全量封禁（login/refresh 阻断）；永久禁言；JWT claim 携带处罚状态。
- SLA/指派；已受理通知（change-B）；证据导出（change-C）；累犯自动升级禁言。
- 分布式消息队列；法务级不可篡改存储。

## Decisions

### 决策 1：处罚持久化在 User.Api `user_sanctions`

**选择**：Mongo 集合 `user_sanctions`，字段含 `type`（warning|mute）、`sub`、`reportId?`、`operatorSub`、`reason`、`durationPreset?`、`startsAtUtc`、`expiresAtUtc`、`revokedAtUtc?`、`revokedBySub?`、`revokeReason?`。

**理由**：账号级状态归 User 边界；Model.Api 不复制处罚主数据，仅查询与下发代理。

**备选**：Model.Api 存 sanctions——与 User 身份割裂，login 路径无法复用。

### 决策 2：写操作 enforcement 为同步 HTTP 查 User.Api

**选择**：Model.Api 在论坛写 filter/middleware 调用 `GET /api/internal/users/{sub}/forum-sanction-status`；有效 mute 当且仅当存在未 revoke 且 `UtcNow < expiresAtUtc` 的 mute 记录。

**理由**：处罚下达后实时生效；JWT claim 在 access token 有效期内会漏拦。

**备选**：JWT claim——性能好但实时性差，首期不采用。

### 决策 3：内网鉴权用共享密钥 Header

**选择**：配置节 `InternalService:ApiKey`（User.Api 与 Model.Api 同值）；请求头 `X-JIssWeb-Internal-Key`；缺失/错误返回 401。不暴露于网关公网路由。

**理由**：仓库尚无 service mesh；共享密钥与现有 monorepo 本地 dev 模式一致。

### 决策 4：Model.Api 下发端点聚合 User.Api 写入

**选择**：`POST /api/mod/users/{sub}/sanctions`（warning|mute）与 `POST .../sanctions/{id}/revoke` 在 Model.Api 实现；鉴权走 `ForumModerationAccessService` + report 版区范围；成功后 HTTP 调 User.Api internal write API，写 audit，warning 时写站内通知。

**理由**：版主 JWT 只在 Model.Api 校验版区；User.Api 不解析 `forumBoardIds`。

**备选**：前端直调 User.Api——版区鉴权重复且泄漏内网面。

### 决策 5：durationPreset 服务端计算 expiresAtUtc

**选择**：客户端仅传 `24h`|`7d`|`30d`；服务端 `startsAtUtc=UtcNow`，`expiresAtUtc=starts+ preset`；不接受任意 timestamp。

**理由**：防止误操作与 TZ 混乱；UI 下拉即可。

### 决策 6：举报队列 reportId 强制

**选择**：`DELETE /api/mod/posts|replies` 当 body 含 `reportId` 时校验 report 存在且 caller 有版区权限；**从举报队列 UI 发起的删除 MUST 带 reportId**，不要求 reason，不向内容作者发站内通知。`POST .../sanctions` 仍要求 `reason` 非空（trim 后 ≥4 字，可选实现为仅非空）。audit `metadata`: 删除 `{ reportId, reason? }`；处罚 `{ reportId, reason, sanctionId?, durationPreset? }`。

**理由**：首期完整审计链；非举报入口 omit reportId 仍允许（帖子详情直删）。

### 决策 7：警告通知类型 `ForumWarning`

**选择**：新 `InAppNotificationTypes.ForumWarning`；`RecipientSubId`=被警告用户；`ActorSubId=""`；文案不暴露版主身份；不披露 report 细节。

## Risks / Trade-offs

- **[跨服务调用延迟]** → User.Api 5min 缓存 + Model 60s 缓存；内网 HTTP；写 QPS 社区量级可接受。
- **[处罚写入成功、audit 失败]** → audit 失败返回 500，User 侧可通过 idempotency key 重试；或补偿 job（首期日志 + 人工）。
- **[旧 access token 禁言后仍可读]** → 写操作已拦；只读不受影响，符合产品定义。
- **[内网密钥泄露]** → 仅绑定 localhost/docker 网络；生产通过 env 注入，不进 git。

## Migration Plan

1. 部署 User.Api（新集合 + internal 端点 + 索引 `sub + type + expiresAtUtc`）。
2. 部署 Model.Api（guard + mod endpoints + delete body 扩展）。
3. 配置两服务 `InternalService:ApiKey` 与 Model `UserService:BaseUrl`。
4. 部署前端举报队列治理面板。
5. 回滚：关闭 guard feature flag 或移除 middleware 注册；`user_sanctions` 保留无害。

## Open Questions

- User.Api internal write API 路径命名：`POST /api/internal/users/{sub}/sanctions` 与 status GET 同前缀——实现时统一。
- 警告通知是否需 `ReportId` 幂等：同一 report 多次警告是否允许——建议允许（无 unique 约束），与禁言独立。
