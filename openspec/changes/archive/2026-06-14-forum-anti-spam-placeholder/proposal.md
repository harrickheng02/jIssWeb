## Why

Issue #5（M3 治理底座）需在已有人审链路（举报、版主、禁言）之上补齐**自动化第一道防线**：屏蔽词过滤与发帖频率限流，减轻灌水和明显违规内容入库。Issue #15 限流基础设施与帖/回复控制器已就绪，本期可交付占位能力。

## What Changes

- Model.Api 新增 `appsettings` 静态屏蔽词表（`Forum:BlockedWords`），对 `POST /api/forum/posts` 与 `POST /api/forum/posts/{id}/replies` 做大小写不敏感子串匹配；命中返回 400 `BLOCKED_CONTENT`，不回显命中词。
- 复用 #15 进程内滑动窗口 limiter，新增独立配置节 `Forum:PostRateLimit`（默认 10 帖 + 30 回复 / 60s / user，sub 主键、IP 副键）；超限 429 `RATE_LIMITED`。
- 配置键、默认值与示例写入 `appsettings.json` 与 `appsettings.Local.example.json` 注释。

## Capabilities

### New Capabilities

- `forum-anti-spam-placeholder`：屏蔽词配置与过滤语义、发帖/回复频率限流、错误码与非目标边界。

### Modified Capabilities

- `forum-content-api`：`POST` 发帖与回复端点 SHALL 在持久化前执行屏蔽词校验与频率限流，行为以 `forum-anti-spam-placeholder` 为准。

## Impact

- **Model.Api**：Options、过滤 service、限流 middleware/filter、`Program.cs`、配置示例；无前端变更（Issue 无 UI 验收）。
- **依赖**：Issue #15 closed（`ForumSearchIpRateLimiter` 模式）；Issue #5 open。
- **服务边界**：仅 Model.Api；User/Gateway/BFF 无变更。

## 非目标

自建大模型审核、全站实时风控中台、CAPTCHA、分布式/Redis 限流、屏蔽词管理 UI、Mongo 词表（后续迭代）；草稿 publish 与自编辑 PUT 不过滤/不限流；谐音变体与分词。
