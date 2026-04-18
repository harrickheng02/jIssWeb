import fs from "node:fs";
import path from "node:path";
import YAML from "yaml";
import {
  extractCursorRulesPaths,
  extractOpenspecPaths,
  hasResolvableOpenspecSpecPath,
} from "./openspec-paths.mjs";
import { getPmIssueNumber } from "./pm-issue-fields.mjs";
import { resolvePmPlanPaths } from "./pm-sync-dir.mjs";
import { routeQuery } from "./route-query.mjs";

const ACTIVE = new Set(["open", "progressing"]);

function escCell(s) {
  return String(s ?? "").replace(/\|/g, "\\|").replace(/\r?\n/g, " ");
}

export function writePmOpenIssuesMd(repoRoot, graph) {
  const { pmPlan: pmPlanPath, pmOpenIssues, pmPlanRel } = resolvePmPlanPaths(repoRoot);
  const doc = YAML.parse(fs.readFileSync(pmPlanPath, "utf8"));
  const issues = Array.isArray(doc.issues) ? doc.issues : [];
  const open = issues
    .map((issue, index) => ({ issue, index }))
    .filter(({ issue }) => ACTIVE.has(String(issue?.state || "open")));

  const lines = [];
  lines.push("# PM 进行中 Issue 索引");
  lines.push("");
  lines.push(
    `> 由 \`graph:build\` / \`graph:refresh\` 生成（\`npm run pm:pull\` 已串联 \`graph:refresh\`）。请 **\`@${path.posix.join(path.posix.dirname(pmPlanRel), "PM_OPEN_ISSUES.md")}\`** 引用；勿使用仓库根目录同名文件。`
  );
  lines.push("");
  lines.push("| # | Issue | 状态 | 里程碑 | 模块 | 标题 |");
  lines.push("|---|-------|------|--------|------|------|");
  open.forEach((x, j) => {
    const { issue } = x;
    lines.push(
      `| ${j + 1} | ${escCell(getPmIssueNumber(issue))} | ${escCell(issue?.state)} | ${escCell(issue?.milestone)} | ${escCell(issue?.module)} | ${escCell(issue?.title)} |`
    );
  });
  lines.push("");

  const maxSections = 40;
  let shown = 0;
  for (const { issue, index } of open) {
    if (shown >= maxSections) {
      lines.push(`> 其余 ${open.length - maxSections} 条进行中 issue 未展开，可改 pm-plan 或提高 maxSections。`);
      break;
    }
    shown++;
    const title = issue?.title || `issue-${index}`;
    const inum = getPmIssueNumber(issue);
    const gn = inum != null ? String(inum) : "—";
    const body = issue?.body || "";
    lines.push(`## ${gn} · ${title}`);
    lines.push("");
    lines.push(`- **state**: ${issue?.state || "open"}`);
    lines.push(`- **milestone**: ${issue?.milestone || ""}`);
    lines.push(`- **module**: ${issue?.module || ""}`);
    lines.push("");
    lines.push("### `body` 中的 openspec 路径");
    const refs = extractOpenspecPaths(body);
    if (!refs.length) lines.push("- （无）");
    else for (const r of refs) lines.push(`- \`${r}\``);
    lines.push("");
    lines.push("### `body` 中的 .cursor/rules 路径");
    const cr = extractCursorRulesPaths(body);
    if (!cr.length) lines.push("- （无）");
    else for (const r of cr) lines.push(`- \`${r}\``);
    lines.push("");
    if (issue?.requires_openspec_spec_reference && !hasResolvableOpenspecSpecPath(body, repoRoot)) {
      lines.push(
        "> **警告**：`requires_openspec_spec_reference: true` 但 body 中无已存在的 `openspec/specs/**` 路径；`graph:verify` 将失败。"
      );
      lines.push("");
    }
    lines.push("### `graph:route`（以标题为查询）");
    const rows = routeQuery(graph, title, 12);
    if (!rows.length) lines.push("- （无命中）");
    else for (const r of rows) lines.push(`- \`${r.path}\` — ${r.reason}`);
    lines.push("");
  }

  fs.writeFileSync(pmOpenIssues, `${lines.join("\n")}\n`, "utf8");
  return pmOpenIssues;
}
