## Why

M2 需要通知收件人、个人中心「我的帖子/回复」等按用户维度的查询；若各服务或 BFF 使用不同用户键或信任客户端传入的 `userId`，会与 JWT `sub` 及 user-service 主键分叉，造成错投通知与越权查询风险。需在规范层把「读路径」与既有 `sub` 契约对齐。

## What Changes

- 在身份规范中明确：**按当前用户过滤的 HTTP API** 必须以服务端从令牌解析的 `sub` 为唯一查询键；禁止仅以客户端路径或 query 中的用户标识查询而不校验其与 `sub` 一致。
- 在论坛 API 规范中增加：**当前用户的帖子列表、回复列表**（或等价查询参数语义），与 `sub` 存储的作者字段对齐；不接受与令牌 `sub` 冲突的调用方指定用户 id。

## Capabilities

### New Capabilities

### Modified Capabilities

- `token-identity-consistency`: 增补「按用户维度的读查询」与 `sub` 一致、不信任客户端自报用户 id 的要求。
- `forum-content-api`: 增补「我的帖子」「我的回复」类列表端点或查询语义及鉴权行为。

## Impact

- **规范**: `openspec/specs/token-identity-consistency`、`openspec/specs/forum-content-api` 归档后更新。
- **实现**: Model/论坛服务中列表过滤与索引；若存在 BFF 聚合「我的内容」，须用网关传入的 `sub` 或下游仅暴露「当前用户」语义接口；后续通知服务收件人字段与此对齐（可与 M2 通知 Issue 联调）。
- **非目标**: 跨租户、多账号合并（与 pm-plan 条目一致）。
