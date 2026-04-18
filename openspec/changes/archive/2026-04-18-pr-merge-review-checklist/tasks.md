## 1. 规范与文档文件

- [x] 1.1 新增 `docs/engineering/pr-merge-checklist.md`：合并门禁说明、`graph:verify` / `repo-graph-verify`、与 Issue 11 对齐的可勾选条目展开、简短「演示门禁」说明。
- [x] 1.2 新增 `.github/pull_request_template.md`：勾选/小节 + 指向 `docs/engineering/pr-merge-checklist.md` 的链接。
- [x] 1.3 合并本 change 后将 delta 写入 `openspec/specs/repo-pr-merge-checklist/spec.md`（或归档时合并）。

## 2. 验证

- [x] 2.1 `npm run graph:verify` 通过；如需可 draft PR 目视模板渲染。
