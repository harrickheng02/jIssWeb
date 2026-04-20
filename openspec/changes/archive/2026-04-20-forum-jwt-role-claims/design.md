## Context

仓库已落地 `sub` / `userId` 一致性（`WebApiHostExtensions`、`ClaimsPrincipalExtensions`）与 User.Api `CreateAccessToken` 的 identity claims。论坛写接口普遍 `[Authorize]`，尚无版主/管理员分层。`pm-plan` Issue #17 要求最小 JWT 角色声明，并排除完整 RBAC 与按资源 ACL。

## Goals / Non-Goals

**Goals:**

- 单一 claim **`forumRole`**，字符串枚举三档：**`member`**、**`moderator`**、**`admin`**，表示**全站**论坛角色。
- 角色变更仅在**新 access token 签发**时体现（登录、refresh、验证后发会话、密码重置后发会话等与现有 issuance 对齐）。
- Model.Api 对治理类占位路由演示 **403** + 统一 `ApiResult` 错误码（与项目现有未授权资源访问策略一致）。
- 提供可重复的**手工验收**路径（文档化测试账号 ID 或 `appsettings` 映射）。

**Non-Goals:**

- 按版区的版主、动态权限引擎、集中式权限中台、角色 claim 的多值数组。
- 单独「撤销角色」端点；即时吊销旧 access token（仍依赖 access TTL）。

## Decisions

| 决策 | 选择 | 说明 |
|------|------|------|
| Claim 名 | `forumRole` | 与探索结论一致，避免与通用 `role` 混淆。 |
| 取值 | `member` \| `moderator` \| `admin` | 小写单值，解析简单。 |
| 缺省 | 省略 claim 时视为 **`member`** | 兼容旧 token 迁移期可选；若实现选择「必须出现」，在 tasks 中改为全路径签发并更新 scenario。 |
| 非法取值 | 校验失败后 **401** | 与无效 JWT 载荷一致，在 `OnTokenValidated` 或等价层失败。 |
| 角色存储 | 用户文档字段优先，本地开发可用配置覆盖 | 与「种子/配置映射」验收方式一致。 |
| 授权不足 | **403** | 已登录但角色不够；未登录仍为 **401**。 |

**备选方案（未采纳）：** 使用标准 `role` claim 与 ASP.NET Role 授权——与现有短名 claim 风格并存成本高；**`scope` 字符串**——解析与文档不如枚举清晰。

## Risks / Trade-offs

| 风险 | 缓解 |
|------|------|
| 旧客户端忽略 `forumRole` | 服务端不依赖客户端上报；缺省 `member` 时行为与今日「仅登录」一致。 |
| 角色滞后于管理操作 | 接受「下次换票生效」；文档说明运营预期。 |
| 全站版主误操作面 | M3 最小集；后续版区级在独立 change 扩展 claim 或服务端校验。 |
| `__debug` 占位路由在生产暴露 | Model.Api 在非 Development 环境对 `/api/forum/__debug` 前缀中间件直接返回 404；集成测试固定 `Development`。 |

## Migration Plan

1. 部署 User.Api：新签发带 `forumRole`。
2. 部署 Model.Api：读 claim 并保护新路由。
3. 为已有用户数据补默认 `member`（若存储驱动）。

回滚：移除 Model.Api 策略与 User.Api claim 写入；旧行为恢复为仅 `[Authorize]`。

## Open Questions

- 生产环境角色**管理入口**（管理后台 API）是否与本 change 同批；若否，仅依赖数据库手工改字段 + 用户重新登录验收。
