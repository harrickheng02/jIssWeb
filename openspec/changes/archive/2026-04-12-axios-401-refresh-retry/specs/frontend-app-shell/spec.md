## ADDED Requirements

### Requirement: 401 响应触发 refresh 且仅重试原请求一次

SPA 的 HTTP 客户端在同时满足以下条件时必须处理响应：HTTP 状态为 **401**、请求曾携带 `Authorization: Bearer` access token、应用状态中仍存在 refresh token。此时客户端必须经既有 user-service refresh 端点获取新的 access token（若返回轮换的 refresh token 则一并更新），并以与登录成功或启动 refresh 成功相同的方式持久化会话，且必须仅重试失败请求 **一次**（使用新 access token）。客户端不得进入无限 refresh 循环：refresh 请求及其他鉴权提交类端点不得以同一失败链再次触发本 401 处理逻辑。

#### Scenario: access 过期且 refresh 仍有效

- **当** 受保护 API 返回 401 且本地存有 refresh token
- **则** 客户端必须调用 token refresh，成功则更新会话，并以新 Bearer 重试原请求一次
- **且** 若重试成功，调用方必须能观察到成功结果

#### Scenario: refresh 失败或无 refresh token

- **当** 受保护请求返回 401 且无法 refresh 或 refresh 失败
- **则** 应用必须按启动 refresh 失败时的同样规则清空鉴权状态
- **且** 用户必须被引导至登录路由或等效的未登录入口

### Requirement: 并发 401 合并为单次 refresh

当多个在途请求并行收到 401 时，SPA 对该波失败至多执行 **一次** refresh；并发调用方必须在重试各自请求前等待同一 refresh 结果。

#### Scenario: access 过期后并行多个受保护请求

- **当** 两个及以上已鉴权请求同时因 401 失败且存在 refresh token
- **则** 客户端在重试前必须只发出一条 refresh 请求

### Requirement: 与启动时静默 refresh 一致

由 401 触发的 refresh 必须使用与启动 refresh、登录成功相同的 Pinia 鉴权 store 方法与存储键持久化 token，使「首次加载」与「会话中途过期」的 access/refresh 处理路径一致。

#### Scenario: 会话中途 refresh 成功后的会话形态

- **当** 由 401 驱动的 refresh 成功
- **则** 持久化凭据与 store 状态必须符合与 `main.ts` 启动 refresh 成功相同的规则
