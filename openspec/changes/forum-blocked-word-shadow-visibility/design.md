## Context

已实现一版服务端 `shadow`（`State: shadow` 入库、作者 `/me` 可见但 Feed 不可见）。产品验证后认为 Feed 缺失仍会暴露机制，且敏感内容**无必要入库**。本 change 改为 **local-only**：服务端只负责判定与返回中性成功信号，正文仅存浏览器。

## Goals / Non-Goals

**Goals:**

- `Handling: local`（默认）| `reject`。
- 命中 local：Mongo **无记录**；200 + `localOnly: true` + 客户端生成/`local:` 前缀 id；message 中性。
- 未命中：现有 `published` 持久化路径不变。
- 前端按 JWT `sub` 分桶存 `localStorage`；合并进 Feed（仅当前用户自己看到本地帖）、详情、回复列表、`/me/*`。
- 本地帖下的回复同样 local；对 server 已发布帖的 local 回复仅合并进**回复作者**在该帖下的视图。
- 集成测试：local 不 Insert、reject 回归、响应不含命中词。

**Non-Goals:**

- 跨设备/跨浏览器同步、服务端 shadow 状态、版主审计 local 内容、local 互动计数、加密存储。

## Decisions

1. **弃用 shadow**：删除 `State: shadow` 及读写过滤；若分支已合并则本 change 含回滚 commit。

2. **服务端响应（local 命中）**：
   - 发帖：`{ id: "local:{uuid}", localOnly: true, state: "local" }`（id 由服务端生成 UUID 供客户端键一致，或客户端生成 — **选用服务端生成 UUID** 便于与 reject 响应形状一致）。
   - 回复：同上；`postId` 原样回传（可为 server id 或 `local:` id）。
   - **不**调用 Insert；**不** `RecordSuccessful*` 限流。

3. **判定仍走服务端** `IForumBlockedWordFilter`：避免前端词表绕过；词表仍在 `appsettings`。

4. **localStorage 键**：`jissweb:forum:local:{sub}:posts` / `...:replies`；JSON 数组；登出不清（同设备换账号读各自 sub 桶）。

5. **Feed 合并**：拉取 server 列表后，将当前 `sub` 的 local 帖按 `createdAtUtc` 插入合并（仅发帖作者浏览自己的 Feed 时合并 — **首页 Feed 对登录用户合并自己的 local 帖**）；匿名不合并。

6. **详情路由**：`post-detail` 对 `local:` id 只读 localStorage，不调 `GET /api/forum/posts/{id}`；server id 仍走 API。

7. **回复 on local 帖**：仅写 local replies 桶；parent `postId` 为 `local:` id。

8. **回复 on server 帖命中词**：local reply 挂 `postId=serverId`；合并进该帖详情/回复列表（仅回复作者可见自己的 local 回复）。

9. **互动**：local 帖/回复不支持点赞、收藏、举报、搜索、通知；UI 可隐藏或 no-op。

10. **清数据**：用户清站点数据后 local 内容消失；产品可接受，不做云端备份。

11. **发帖成功 UX**：保留「已发布」toast + 跳详情；详情页展示 server 或 local 内容，作者刷 Feed 能看到 local 帖（合并后）。

## Risks / Trade-offs

- **[Risk]** localStorage 容量与明文 → **Mitigation**：仅敏感帖走 local、体量小；非目标加密。
- **[Risk]** 用户换设备看不到 → **Mitigation**：产品已接受。
- **[Risk]** 恶意用户可改 localStorage 自嗨 → **Mitigation**：不影响公众区；无服务端污染。
- **[Risk]** shadow 已实现需回滚 → **Mitigation**：tasks 第 0 节显式回滚清单。

## Migration Plan

- 部署：默认 `Handling: local`；无 Mongo 迁移。
- 若环境曾写入 `shadow` 记录：可保留只读或一次性脚本删除；非本期必做（本地 dev 可手工清库）。
- 回滚：`Handling: reject` 或 `Enabled: false`；前端忽略 `localOnly` 字段。

## Open Questions

- 无（local 合并 Feed 已拍板，解决 shadow 暴露问题）。
