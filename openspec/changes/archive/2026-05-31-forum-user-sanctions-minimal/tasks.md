## 1. User.Api：数据模型与内网 API

- [x] 1.1 新增 `UserSanctionRecord` 模型与 `UserSanctionTypes`（warning/mute）、`DurationPresets`（24h/7d/30d）；`UserMongoSetup` 创建 `user_sanctions` 索引（`sub` + `expiresAtUtc`）
- [x] 1.2 绑定 `InternalServiceOptions`（`ApiKey`）；实现 `InternalSanctionsController`：`GET /api/internal/users/{sub}/forum-sanction-status`、`POST .../sanctions`、`POST .../sanctions/{id}/revoke`；Header `X-JIssWeb-Internal-Key` 校验
- [x] 1.3 实现 `durationPreset → expiresAtUtc` 服务端计算；mute 必填 preset；revoke 必填 `revokeReason`
- [x] 1.4 实现进程内 IMemoryCache（TTL 5 分钟）包装 status 查询；create/revoke 时 invalidate 对应 `sub`
- [x] 1.5 在 `appsettings.Local.example.json` 注释中补充 `InternalService:ApiKey` 示例

## 2. User.Api：测试

- [x] 2.1 新增 User.Api 集成/单元测试：active mute、expired mute、revoke、internal key 401、preset 计算
- [x] 2.2 运行 User.Api 相关测试确认通过

## 3. Model.Api：User 客户端与禁言 Guard

- [x] 3.1 新增 `UserSanctionClient`（HttpClient + `InternalService:ApiKey` + 配置 `UserService:BaseUrl`）；注册 DI
- [x] 3.2 实现 `ForumSanctionWriteGuard` filter 或 middleware：论坛写路径调用 status API；`isMuted` 时返回 `403 FORUM_MUTED` + `mutedUntilUtc`；可选 60s 本地缓存
- [x] 3.3 将 Guard 挂到发帖、回复、自编辑、草稿写/发布端点（对照 `forum-content-api` / draft spec 路由清单）
- [x] 3.4 在 `appsettings.Local.example.json` 注释中补充 `UserService:BaseUrl` 与 internal key 对齐说明

## 4. Model.Api：版主处罚端点

- [x] 4.1 新增 `ModUserSanctionsController`：`POST /api/mod/users/{sub}/sanctions`（warning/mute）、`POST .../sanctions/{id}/revoke`；鉴权 `RequireForumModerator` + report 版区校验
- [x] 4.2 下发 warning 时写 `ForumWarning` 通知（`InAppNotificationTypes` 新常量）；写 audit `user.warn` / `user.mute` / `user.unmute`，metadata 含 `reportId`、`reason`
- [x] 4.3 空 `reason` / 无效 `reportId`（举报队列流）返回 400

## 5. Model.Api：删内容 reportId + reason

- [x] 5.1 扩展 `DELETE /api/mod/posts/{id}` 与 `DELETE /api/mod/replies/{id}` 接受 JSON body（`reportId`、可选 `reason`）；含 `reportId` 时校验举报存在；删内容不向作者通知、不要求 reason
- [x] 5.2 删内容 audit metadata 写入 `reportId`；`reason` 可选（仅内部审计）

## 6. Model.Api：集成测试

- [x] 6.1 新增 `ForumUserSanctionsTests` fixture：版主 mute 24h、写操作 403、warning 不阻断写（过期/revoke 写路径由 User.Api `UserSanctionsTests` 覆盖）
- [x] 6.2 测试 report 队列流：带 `reportId` 删帖 audit 断言；无 reason 亦成功
- [x] 6.3 测试 `ForumWarning` 通知写入
- [x] 6.4 运行 `dotnet test tests/JIssWeb.Model.Api.Tests --filter "FullyQualifiedName~Sanction"` 确认通过

## 7. 前端：API 客户端

- [x] 7.1 在 `clients.ts` 新增 `postModUserSanction`、`postModReportSanction`；扩展 mod delete 调用支持 body（`reportId`、可选 `reason`）；revoke API 仅后端暴露，首期无前端 UI

## 8. 前端：举报队列 UI

- [x] 8.1 在举报队列展开区增加警告/禁言对话框：时长下拉（24h 默认 / 7d / 30d）、reason 必填、提交调 sanctions API 并带 `reportId`
- [x] 8.2 删帖/删回复从队列发起时仅传 `reportId`（无原因弹窗、无作者通知）
- [x] 8.3 样式遵循 `forum-tokens.css`；reason 为空时禁用主按钮

## 9. 前端：被禁言用户反馈

- [x] 9.1 compose / 回复提交捕获 `FORUM_MUTED`，展示解除时间文案
- [x] 9.2 运行 `npm run build` 确认无编译错误

## 10. 验证

- [x] 10.1 运行 `dotnet test tests/JIssWeb.Model.Api.Tests` 全量通过
- [x] 10.2 运行 `npm test`（若有新增 composable 单测则一并执行）
- [x] 10.3 change-review 对照 `openspec/changes/forum-user-sanctions-minimal/specs/**` 全部 SHALL 场景
