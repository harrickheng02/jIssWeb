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

管理员账号不受版区列表限制；版主需同时配置 **User.Api** 的 `Forum:RoleOverrides` 与 **Model.Api** 的 `Forum:Moderation:Moderators`。

