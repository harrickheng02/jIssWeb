## 1. 规范与契约对齐

- [x] 1.1 归档本 change 前核对 `specs/token-identity-consistency`、`user-service`、`model-service` 增量与 `openspec/specs/**` 合并策略（apply 阶段落地主 spec）

## 2. User.Api：角色来源与签发

- [x] 2.1 为用户账户增加持久化字段（或等价存储）表示 `member` / `moderator` / `admin`，默认 `member`
- [x] 2.2 本地开发支持配置映射（`Forum:RoleOverrides`：`{ "user-id": "admin" }`），便于手工验收
- [x] 2.3 `CreateAccessToken`（及所有签发 access token 的代码路径）加入 `forumRole` claim，与存储/配置一致
- [x] 2.4 refresh、邮箱验证后发会话、密码重置完成等路径签出的 access token 均包含 `forumRole`（与「下次签发生效」一致）

## 3. Model.Api：校验与授权

- [x] 3.1 在 JWT 校验管线中校验 `forumRole`：若存在且非法枚举则 `OnTokenValidated` 失败（401）
- [x] 3.2 提供读取 `forumRole` 的辅助方法（省略时视为 `member`）
- [x] 3.3 注册授权策略或过滤器：`RequireForumModerator`、`RequireForumAdmin`（命名可调整，语义对齐 spec）
- [x] 3.4 增加至少一个 moderator-only 与一个 admin-only 占位路由（如 `GET /api/forum/__debug/moderator`），返回 200；`member` token 返回 403 + 统一错误码

## 4. 测试与文档

- [x] 4.1 集成测试：三种 `forumRole` 的 token 对占位路由的 200/403 行为
- [x] 4.2 在 `README` 或 change 目录 `README` 片段中写明：如何配置测试用户角色、如何换票验证版主/管理员

## 5. 联调预留

- [x] 5.1 在 PR 描述或 tasks 备注中指向 `pm-plan` Issue #17 及依赖的举报/版主 Issue，便于后续联调

**联调备注（PR 可粘贴）：** 对应 `scripts/github-sync/pm-plan.yaml` **Issue #17**「论坛角色与 JWT 声明最小集」；下游 **论坛举报与处理最小闭环**、**版主版区与帖子操作后台** 依赖可机读的 `forumRole`（`member` / `moderator` / `admin`）。验收步骤见本目录 `ACCEPTANCE.md`。
