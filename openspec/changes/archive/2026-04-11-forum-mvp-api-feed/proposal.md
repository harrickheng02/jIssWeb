## Why

论坛首页壳已就绪，但 Feed 与发帖仍为占位数据；M1 需要可演示的「读列表 / 读详情 / 发帖 / 回复」闭环，并与 JWT `sub` 身份一致。本变更将论坛最小领域 API 落在 Model 服务、经网关暴露给前端，完成端到端数据流。

## What Changes

- 在 **JIssWeb.Model.Api** 增加帖子与回复的持久化（Mongo）与 REST 端点：公开列表/详情，写操作需登录；作者标识与 token `sub` 对齐。
- 在 **YARP 网关** 增加 `/api/forum/{**catch-all}`（或等价路径）到 Model 服务集群，使 SPA 仅通过 `5094` 访问论坛 API。
- **前端**：首页 Feed、帖子详情、发帖与回复表单/动作对接上述 API，移除首页硬编码帖子列表；分类/标签可先只读占位或与最小字段对齐。
- **非目标**：全文搜索、通知、附件与富文本、BFF 聚合多服务（后续里程碑）。

## Capabilities

### New Capabilities

- `forum-content-api`: 论坛帖子与回复的最小 HTTP 契约（路径前缀、分页与错误约定、请求/响应字段、鉴权边界），与 `forum-homepage-shell` 卡片字段可对齐。

### Modified Capabilities

- `yarp-api-gateway`: 增加论坛 API 转发路由与集群目标，保持 Bearer 透传。
- `model-service`: 在现有 Mongo/JWT 骨架上，增加论坛领域持久化与受保护写操作的行为要求。

## Impact

- 后端：`JIssWeb.Model.Api`、网关 `appsettings`（及 Docker/compose 若需同步上游地址）、可能新增 Mongo 集合与索引脚本。
- 前端：`HomeView`、路由、API 客户端（`axios`）、鉴权请求头沿用现有模式。
- 依赖：user-service 签发 JWT；`openspec/specs/token-identity-consistency`；`openspec/specs/forum-homepage-shell`（展示字段）。
