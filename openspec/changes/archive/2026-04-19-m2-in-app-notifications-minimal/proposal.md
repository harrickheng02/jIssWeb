## Why

论坛回复与个人中心的闭环需要一套可核验的站内通知能力：回复发生后，楼主能在“通知列表”看到摘要并跳转到对应帖子/回复位置；通知的收件人与鉴权边界以 JWT `sub` 为权威标识，支持已读状态与未读数，支撑前端空态与错误态展示。

## What Changes

- 新增“站内通知”最小能力的端到端合同与实现任务：回复触发通知、通知列表分页、单条/全部标为已读、未读数。
- 明确深链字段合同：通知项携带 `PostId` 与可选 `ReplyId`，前端可跳转到帖子详情并定位回复。
- 明确身份与鉴权边界：用户范围内读写均以服务端解析的 token `sub` 作为收件人过滤条件。

## Capabilities

### New Capabilities

- `m2-in-app-notifications-minimal`: 站内通知最小集的实现合同与任务清单（对齐 `openspec/specs/in-app-notifications/spec.md` 的最小交付口径，覆盖列表分页、已读、深链字段与回复触发通知）。

### Modified Capabilities

- `in-app-notifications`: 固化深链字段合同与已读操作的幂等语义（保持首次已读时间），并明确列表排序稳定键以支持前端去重合并。

## Impact

- **Backend**: `JIssWeb.Model.Api` 新增/完善通知集合索引、通知控制器与“创建回复”链路中的通知写入；涉及 `api/forum/notifications` 与 `api/forum/posts/{id}/replies`。
- **Frontend**: 个人中心通知入口与通知列表页（分页、未读筛选、空态/错误态）、通知项深链跳转与回复定位。
- **Specs/Docs**: `openspec/specs/in-app-notifications/spec.md` 补充列表排序稳定键与深链字段约定；与 `openspec/specs/token-identity-consistency/spec.md` 一致。
