import fs from "node:fs";
import YAML from "yaml";
import { resolvePmPlanPaths } from "./pm-sync-dir.mjs";
import { getPmIssueNumber } from "./pm-issue-fields.mjs";
import { hasResolvableOpenspecSpecPath } from "./openspec-paths.mjs";

const ACTIVE = new Set(["open", "progressing"]);

export function listOpenspecGateViolations(repoRoot) {
  const { pmPlan: pmPlanPath } = resolvePmPlanPaths(repoRoot);
  const doc = YAML.parse(fs.readFileSync(pmPlanPath, "utf8"));
  const issues = Array.isArray(doc.issues) ? doc.issues : [];
  const bad = [];
  for (const issue of issues) {
    if (!issue?.requires_openspec_spec_reference) continue;
    if (!ACTIVE.has(String(issue?.state || "open"))) continue;
    const body = issue.body || "";
    if (!hasResolvableOpenspecSpecPath(body, repoRoot)) {
      const num = getPmIssueNumber(issue);
      const id = num != null ? `issue:${num}` : "issue:?";
      bad.push({
        issue: id,
        path: "(requires existing openspec/specs/**)",
        number: num,
        title: issue?.title || "",
      });
    }
  }
  return bad;
}
