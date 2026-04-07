## Why

当前多服务鉴权依赖 JWT，但用户唯一标识字段语义尚未收敛，容易出现 `sub` 与 `userId` 不一致导致的身份解析歧义。需要在一期内统一声明优先级与一致性约束，避免后续服务间鉴权行为分叉。

## What Changes

- 统一 JWT 身份标识规则：`sub` 为唯一主标识，服务端鉴权优先依赖 `sub`。
- 允许保留 `userId` 作为业务语义字段，但强制要求其值与 `sub` 完全一致。
- 明确签发与校验约束：令牌签发端必须同时写入一致值；资源服务在发现不一致时按无效令牌处理。
- 补充登录/刷新令牌相关声明一致性要求，确保 access/refresh 流程下身份主键不漂移。

## Capabilities

### New Capabilities

- `token-identity-consistency`: 定义 JWT 内 `sub` 与 `userId` 的一致性、优先级与校验行为。

### Modified Capabilities

- `user-service`: 用户服务的令牌签发与刷新流程需遵循 `sub` 主标识与 `userId==sub` 约束。
- `shared-foundation`: 统一跨服务身份解析规范，约束不一致声明的失败语义。

## Impact

- 影响后端用户服务鉴权相关 API 契约（登录、刷新）。
- 影响各业务服务从 JWT 提取用户标识的通用规范。
- 需要在后续实现中更新令牌签发逻辑、令牌校验逻辑与错误返回语义。
