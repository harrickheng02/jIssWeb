# 智能体多 LLM 档案设计（2026-07-13）

## 决策摘要

- OpenAI 兼容多档案（多 Key / BaseUrl / 模型名）；独立集合 `agent_llm_profiles`
- Persona 绑定 `llmProfileId`；新建/编辑下拉选择
- 档案被引用时禁止删除（`LLM_PROFILE_IN_USE`）
- 邮箱后端自动生成；`personaId` 可自动分配
- 全局 runtime 仅保留 `schedulerEnabled`；旧 DeepSeek 单字段启动时迁移为一条档案

## 非目标

豆包原生协议、按档案独立配额、普通会员可见。
