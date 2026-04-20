# 手工验收：论坛 JWT 角色（forum-jwt-role-claims）

## 赋予全局版主 / 管理员

1. **持久化（推荐）**  
   在用户服务 MongoDB `users` 集合中，将目标用户的 `ForumRole` 字段设为 `moderator` 或 `admin`（小写）。  
   用户**下一次**通过登录、refresh、邮箱验证换票或密码重置完成换票拿到的新 access token 中会带上对应 `forumRole`。

2. **本地覆盖（开发/验收）**  
   在 User.Api 的 `appsettings.Local.json`（或 `appsettings.json`）的 `Forum:RoleOverrides` 中写入用户 Id（即 JWT `sub`）到角色的映射，例如：

   ```json
   "Forum": {
     "RoleOverrides": {
       "YOUR_USER_ID_HERE": "moderator"
     }
   }
   ```

   映射在签发 access token 时优先于库里的 `ForumRole` 字段。

## 如何验证 token

- 登录 User.Api 拿到 `accessToken` 后，用 [jwt.io](https://jwt.io) 解码 payload，确认存在 **`forumRole`**，值为 `member`、`moderator` 或 `admin`。
- 调用 Model.Api（**仅 `ASPNETCORE_ENVIRONMENT=Development` 时** `/api/forum/__debug/*` 会进入鉴权逻辑；生产等非 Development 环境对该前缀统一 **404**，避免暴露调试点）：  
  - `GET /api/forum/__debug/moderator`：`moderator` 或 `admin` → 200；`member` → 403，`code` 为 `FORBIDDEN`。  
  - `GET /api/forum/__debug/admin`：仅 `admin` → 200；`moderator` / `member` → 403。

## 与 pm-plan 的对应关系

- **Issue #17**（论坛角色与 JWT 声明最小集）：本 change 的实现与验收说明。  
- 后续 **举报处理**、**版主版区操作** 等 Issue 可复用同一 `forumRole` 声明做机读鉴权。
