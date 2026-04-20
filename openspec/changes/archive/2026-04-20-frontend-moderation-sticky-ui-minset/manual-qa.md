## 手工验收步骤（版主置顶 + 审计）

### 1) 启动服务

- 启动 `JIssWeb.User.Api`（默认端口 5097）
- 启动 `JIssWeb.Model.Api`（默认端口 5099）
- 启动前端 `frontend`（Vite dev server）

### 2) 准备版主账号（按 JWT `sub` 配置）

1. 用任意账号登录一次，从浏览器/网络里拿到 access token，解码 JWT 取出 `sub`（用户 Id）。
2. 在 **`backend/src/JIssWeb.User.Api/appsettings.Local.json`**（可从 example 复制）里设置：

   - `Forum:RoleOverrides["<你的 sub>"] = "moderator"`

3. 在 **`backend/src/JIssWeb.Model.Api/appsettings.Local.json`** 里设置版主可管的版区（须与你的测试帖 `boardId` 一致）：

   - `Forum:Moderation:Moderators`: `{ "sub": "<你的 sub>", "boardIds": [ "..." ] }`

重启 User.Api、Model.Api、网关后重新登录（或刷新 token），access token 中应出现：

- `forumRole: "moderator"`

### 3) UI 验收：置顶标记

- 打开首页 `/`
- 任意进入一个帖子详情 `/posts/{id}`
- 若该帖为置顶：列表卡片与详情页都应显示“置顶”标记

### 4) UI 验收：置顶/取消置顶操作

- 使用版主账号进入帖子详情页
- 在详情页底部出现“治理”区块
- 点击“置顶”
  - 成功 toast：显示“已置顶”
  - 详情页置顶标记出现
- 点击“取消置顶”
  - 成功 toast：显示“已取消置顶”
  - 详情页置顶标记消失

### 5) UI 验收：审计

- 在帖子详情页“治理”区块点击“操作记录”
- 抽屉中应展示审计条目，至少包含：
  - **操作说明**（人读文案，例如「置顶帖子」「取消置顶」，对应接口字段 `actionLabel`）
  - **操作者**（昵称等展示名，对应 `operatorDisplayName`）
  - **时间**（本地化展示，来源 `occurredAtUtc`）

### 6) 治理接口 403 的两种含义（排障）

- **响应体为 `无权访问`、code `FORBIDDEN`**：JWT 已签发，但 **`forumRole` 不是 moderator/admin**（治理路由被 `RequireForumModerator` 拒绝）。
- **响应体为 `无权操作该帖子`、code `FORBIDDEN`**：角色已是版主，但 **该帖所在版块不在** `Forum:Moderation:Moderators` 里为你的 `sub` 配置的 **`boardIds`** 范围内。

管理员账号不受版区列表限制。版主版区范围以 **User.Api** `Forum:Moderation:Moderators` 为准签发 **JWT `forumBoardIds`**（非空时写入 token）；**Model.Api** 同路径配置用于 token 无该 claim、或 **Docker / 仅用 RoleOverrides.moderator** 时的兜底。仓库默认两份 `appsettings.json` 已与示例 **sub** 对齐；若仍 403：**核对当前账号 JWT 里 `sub` 与配置是否一致**、帖子的 **`Forum:Boards` 板块 id** 是否在 `boardIds` 内、**各服务 `Jwt:Key` 是否与签发 token 时一致**、改配置后 **重新登录** 换发 access token。

### 7) 审计与删帖（版主读「操作记录」）

- 新产生的置顶审计会在元数据里写入 **`boardId`**（稳定 id，与 `Moderators[].boardIds` 一致）以及展示用 **`board`**（板块标题）。
- **帖子已物理删除**、仅余审计时：版主是否允许拉取列表，以审计里的 **`boardId` 优先**判定；仅旧数据没有 `boardId` 时，才回退用 **`board` 标题** 与配置做匹配。
- 若旧审计**既无 `boardId` 也无法匹配标题**（或缺少元数据），版主可能收到 **404**，属预期；必要时补数据或重新触发一条带完整元数据的治理操作。
- **`Forum:Boards` 里若出现重复 `Title`（忽略大小写）**：Model.Api 启动校验会打 **Warning** 日志，提示 `ResolveBoardIdFromTitle` 只会命中列表中的第一条；生产配置应保持各板块标题唯一。
