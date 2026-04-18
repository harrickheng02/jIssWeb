import fs from "node:fs";
import path from "node:path";

const RE = /\bopenspec\/[\w./-]+/g;
const CURSOR_RE = /\.cursor\/rules\/[\w./-]+\.mdc/g;

function stripTrailing(s) {
  return s.replace(/[.,;:)]+$/, "");
}

export function extractOpenspecPaths(text) {
  if (!text || typeof text !== "string") return [];
  const set = new Set();
  let m;
  while ((m = RE.exec(text)) !== null) {
    set.add(stripTrailing(m[0]));
  }
  return [...set];
}

export function extractCursorRulesPaths(text) {
  if (!text || typeof text !== "string") return [];
  const set = new Set();
  let m;
  while ((m = CURSOR_RE.exec(text)) !== null) {
    set.add(stripTrailing(m[0]));
  }
  return [...set];
}

export function hasResolvableOpenspecSpecPath(body, repoRoot) {
  for (const ref of extractOpenspecPaths(body)) {
    if (!ref.startsWith("openspec/specs/")) continue;
    const abs = path.join(repoRoot, ...ref.split("/"));
    if (fs.existsSync(abs)) return true;
  }
  return false;
}
