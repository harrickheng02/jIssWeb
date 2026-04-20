## Why

M3 治理类能力（举报处理、版主操作）需要资源服务能**从 JWT 机读**调用者的论坛角色；当前 access token 仅有身份类 claim，无法区分普通用户、版主与管理员，`pm-plan` Issue #17 因此仍为待交付。

## What Changes

- 在 access token 中增加**单值** claim **`forumRole`**，取值限定为 **`member`**、**`moderator`**、**`admin`**，表示**全站**论坛角色（不按版区拆分）。
- 用户服务签发与刷新 access token 时写入 `forumRole`；角色变更在**下一次 access token 签发**时生效（登录、refresh、邮件验证后发会话等与既有换票路径一致）。
- Model.Api（模型服务）对「仅版主」「仅管理员」的受保护路由读取 `forumRole`，未授权时返回 **403** 与统一错误合同；公开只读路由行为不变。
- 在 `token-identity-consistency`、`user-service`、`model-service` 规范中增量描述上述契约；附手工验收说明（测试账号或配置映射路径）。

## Capabilities

### New Capabilities

（无独立新 capability；行为以既有三份 spec 的增量为准。）

### Modified Capabilities

- `token-identity-consistency`：增加全局论坛角色 claim 的语义、允许取值、缺省与非法取值的校验结果。
- `user-service`：JWT 签发面在登录、refresh、验证后发会话等路径上**必须**包含 `forumRole`，并与持久化或配置来源一致。
- `model-service`：在论坛相关 API 上增加基于 `forumRole` 的授权约束（含占位路由演示 403）。

## Impact

- **后端**：`JIssWeb.User.Api`（签发、用户存储或配置）；`JIssWeb.Model.Api`（授权策略或过滤器、占位路由）；共享 JWT 校验与 `ClaimsPrincipal` 扩展可能落在 `JIssWeb.Common`。
- **测试**：集成测试需覆盖三种角色 token 与 403；文档或 README 片段说明如何获取各角色 token。
- **前端**：若后续治理 UI 依赖角色，可解析 `forumRole`；本变更以**后端契约**为主，前端可并行。
