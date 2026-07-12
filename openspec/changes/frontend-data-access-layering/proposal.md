# Proposal: frontend-data-access-layering

## Problem
图谱分析发现：useAuthStore 被 12 个非 auth 文件直接引用；10 个 View 直接调用 api/clients 跳过 composable 层；3 个展示组件直接调用 API。

## Solution
1. 新增 useCurrentUser() composable 统一暴露认证状态
2. 迁移 View 直调 api/clients → 对应 composable
3. 清理组件直调 api/clients

## Non-goals
- 不改认证流程 View（AuthView、ForgotPasswordView、RegisterVerifiedView）
- 不改 api/clients.ts 本身
- 不改后端
