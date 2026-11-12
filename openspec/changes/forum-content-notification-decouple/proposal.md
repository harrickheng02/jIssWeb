# Proposal: forum-content-notification-decouple

## Problem
ForumPostsController 直接持有 InAppNotificationRecord 集合引用，在创建回复时跨越通知子域直写 Mongo。任何通知 schema 变化都会牵动 Posts 控制器。

## Solution
提取 ForumNotificationWriter 服务，封装通知写入逻辑。Controller 只依赖服务接口，不再持有通知集合引用。

## Non-goals
- 不引入消息队列或异步事件
- 不改变通知的行为契约（in-app-notifications spec 不变）
- 不改前端
