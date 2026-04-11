## 1. 配置与文档

- [x] 1.1 新增 `scripts/gitee-sync/`（或等价路径）与 `.env.example`：`GITEE_OWNER`、`GITEE_REPO`、`GITEE_ACCESS_TOKEN`
- [x] 1.2 增加示例输入 `scripts/gitee-sync/pm-plan.example.yaml`（含 M1–M3、示例 Issue、P0/P1/P2 标签）
- [x] 1.3 在根 `README.md`「Issue 与看板」下增加一行：可用同步脚本对齐里程碑与 Issue（指向脚本说明）

## 2. API 客户端与幂等

- [x] 2.1 实现 v5 HTTP 封装：GET/POST/PATCH milestones、issues、labels（路径以 Swagger 为准）
- [x] 2.2 实现里程碑幂等：按标题 list 后 create 缺失项
- [x] 2.3 实现 Issue 幂等：按 `title`（或设计约定）list 筛选后 create 或 patch；关联 `milestone` 字段
- [x] 2.4 实现标签：确保 P0/P1/P2（及模块标签）存在或预创建后再挂到 Issue

## 3. CLI 入口与验证

- [x] 3.1 提供入口命令（如 `node scripts/gitee-sync/index.mjs` 或 `python -m gitee_sync`）与 `--dry-run`（可选，仅打印计划）
- [x] 3.2 在个人测试仓库实跑：网页可见里程碑、Issue、标签与 README 规划一致
- [x] 3.3 `npm`/`pip` 依赖若有则锁版本；CI 不默认跑（避免泄露 token）
