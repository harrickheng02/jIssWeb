## 关联

- **pm-plan / Issue**（若适用）：<!-- 例：Issue 11；或 pm-plan.yaml 中 title 关键词 -->
- **OpenSpec**（若适用）：<!-- 例：openspec/changes/foo/ 或 openspec/changes/archive/... -->

## 合并前自检

- [ ] **范围**：变更与 Issue / spec 描述一致；无无关大改。
- [ ] **自测**：已按改动做必要自测（如前端 `npm test` / 构建、后端 `dotnet build` 等）。
- [ ] **CI**：针对目标分支的 GitHub Actions（若有）已通过，或已在说明中注明例外（团队约定）。
- [ ] （可选）横切测试 / 契约说明：<!-- 无则写「无」-->

## 审阅提示（维护者）

- 需求与实现、OpenSpec / 任务（若适用）对齐。
- 受保护分支：在必过检查失败时不合并。
