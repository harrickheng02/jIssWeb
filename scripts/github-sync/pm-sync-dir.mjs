import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";

function gitOrigin(repoRoot) {
  try {
    return execFileSync(
      "git",
      ["-C", repoRoot, "remote", "get-url", "origin"],
      { encoding: "utf8" }
    ).trim();
  } catch {
    return "";
  }
}

function hasPlan(dir) {
  return fs.existsSync(path.join(dir, "pm-plan.yaml"));
}

export function resolvePmSyncDir(repoRoot) {
  const origin = gitOrigin(repoRoot);
  const ghDir = path.join(repoRoot, "scripts", "github-sync");
  const gtDir = path.join(repoRoot, "scripts", "gitee-sync");
  const isGh = /github\.com/i.test(origin);
  const isGt = /gitee\.com/i.test(origin);
  if (isGh && hasPlan(ghDir)) return ghDir;
  if (isGt && hasPlan(gtDir)) return gtDir;
  if (isGh && hasPlan(gtDir)) return gtDir;
  if (isGt && hasPlan(ghDir)) return ghDir;
  if (hasPlan(gtDir)) return gtDir;
  if (hasPlan(ghDir)) return ghDir;
  return isGt ? gtDir : ghDir;
}
