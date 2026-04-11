## 动机

用户在单页应用停留期间 access JWT 会过期；目前仅靠启动时 refresh 或用户重新登录恢复，受保护接口在过期后持续返回 401。与 `scripts/gitee-sync/pm-plan.yaml`（Issue：HTTP 401 时自动 refresh 并重试请求）对齐，可补齐论坛 MVP 体验缺口。

## 变更内容

- 增加共享 axios **响应**逻辑：受保护接口返回 **401** 且存在 **refresh token** 时，调用 **`POST /auth/refresh`** 一次，经现有 Pinia 鉴权 API 更新会话后，对失败请求 **重试一次**；refresh 失败则 **清空会话** 并进入登录（与启动时 refresh 失败一致）。
- **合并并发 refresh**：并行 401 不触发多次 refresh 请求。
- 对 **`/auth/*`** 中不应触发「401→再 refresh」的路径（如 refresh、login）予以排除，避免循环；refresh 本身走不递归进入同一 401 处理的路径。
- 逻辑集中在 **`createClient`**，使当前各 API 客户端实例（`userApi`、`modelApi` 等）行为一致，避免逐文件复制。

## 能力范围

### 新增能力

- （无——在既有前端壳层鉴权 HTTP 约定上扩展行为。）

### 修改能力

- `frontend-app-shell`：补充规范性需求——401 触发的 refresh、单次重试、并发 refresh 合并、与启动静默 refresh 一致。

## 影响

- **代码**：`frontend/src/api/clients.ts`（拦截器、refresh 调用路径），必要时 `frontend/src/stores/auth.ts` 小辅助；若 `/auth/refresh` 已符合既有 user-service 约定则 **无** 后端契约变更。
- **运行**：会话中途 access 过期时减少误登出；过期后首次 401 多一次往返。
- **依赖**：沿用 axios、Pinia、vue-router。
