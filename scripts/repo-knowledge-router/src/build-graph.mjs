import fs from "node:fs";
import path from "node:path";
import YAML from "yaml";
import { extractOpenspecPaths } from "./openspec-paths.mjs";
import { walkFiles } from "./walk-md.mjs";

function toPosix(p) {
  return p.split(path.sep).join("/");
}

function readFirstHeading(md) {
  const line = md.split(/\r?\n/).find((l) => /^#\s+/.test(l));
  if (!line) return "";
  return line.replace(/^#+\s+/, "").trim();
}

export function buildGraph(repoRoot) {
  const nodes = [];
  const edges = [];
  const nodeById = new Map();

  function addNode(n) {
    if (nodeById.has(n.id)) return nodeById.get(n.id);
    nodeById.set(n.id, n);
    nodes.push(n);
    return n;
  }

  function addEdge(e) {
    edges.push(e);
  }

  const specRoot = path.join(repoRoot, "openspec", "specs");
  for (const abs of walkFiles(specRoot, (f) => f.endsWith(".md"))) {
    const rel = toPosix(path.relative(repoRoot, abs));
    const text = fs.readFileSync(abs, "utf8");
    addNode({
      id: `spec:${rel}`,
      kind: "spec",
      path: rel,
      title: readFirstHeading(text) || rel,
    });
  }

  const changesRoot = path.join(repoRoot, "openspec", "changes");
  for (const abs of walkFiles(changesRoot, (f) => f.endsWith(".md"))) {
    const rel = toPosix(path.relative(repoRoot, abs));
    const text = fs.readFileSync(abs, "utf8");
    addNode({
      id: `change:${rel}`,
      kind: "change",
      path: rel,
      title: readFirstHeading(text) || rel,
    });
  }

  const rulesRoot = path.join(repoRoot, ".cursor", "rules");
  for (const abs of walkFiles(rulesRoot, (f) => f.endsWith(".mdc"))) {
    const rel = toPosix(path.relative(repoRoot, abs));
    const text = fs.readFileSync(abs, "utf8");
    addNode({
      id: `cursor-rule:${rel}`,
      kind: "cursor-rule",
      path: rel,
      title: readFirstHeading(text) || rel,
    });
  }

  const skillsRoot = path.join(repoRoot, ".cursor", "skills");
  for (const abs of walkFiles(
    skillsRoot,
    (f) => f.endsWith(`${path.sep}SKILL.md`)
  )) {
    const rel = toPosix(path.relative(repoRoot, abs));
    const text = fs.readFileSync(abs, "utf8");
    addNode({
      id: `skill:${rel}`,
      kind: "skill",
      path: rel,
      title: readFirstHeading(text) || rel,
    });
  }

  const pmPlanPath = path.join(repoRoot, "scripts", "gitee-sync", "pm-plan.yaml");
  const pmPlanRel = "scripts/gitee-sync/pm-plan.yaml";
  const doc = YAML.parse(fs.readFileSync(pmPlanPath, "utf8"));
  const modules = Array.isArray(doc.modules) ? doc.modules : [];
  modules.forEach((m, i) => {
    if (!m?.name) return;
    addNode({
      id: `module:${m.name}`,
      kind: "module",
      path: pmPlanRel,
      title: m.name,
      description: m.description || "",
    });
  });

  const issues = Array.isArray(doc.issues) ? doc.issues : [];
  issues.forEach((issue, i) => {
    const title = issue?.title || `issue-${i}`;
    const idKey = issue?.gitee_number ? String(issue.gitee_number) : `i${i}`;
    const id = `issue:${idKey}`;
    const body = issue?.body || "";
    addNode({
      id,
      kind: "issue",
      path: pmPlanRel,
      title,
      state: String(issue?.state || "open"),
      gitee_number: issue?.gitee_number ?? null,
      bodyPreview: body.slice(0, 240),
      milestone: issue?.milestone || "",
      module: issue?.module || "",
    });
    const refs = extractOpenspecPaths(body);
    for (const ref of refs) {
      const absRef = path.join(repoRoot, ...ref.split("/"));
      const exists = fs.existsSync(absRef);
      if (!exists) {
        addEdge({
          from: id,
          to: `missing:${ref}`,
          rel: "references",
          brokenReference: true,
        });
        continue;
      }
      let toId = null;
      if (nodeById.has(`spec:${ref}`)) toId = `spec:${ref}`;
      else if (nodeById.has(`change:${ref}`)) toId = `change:${ref}`;
      else {
        const hit = [...nodeById.values()].find((n) => n.path === ref);
        if (hit) toId = hit.id;
      }
      if (toId) addEdge({ from: id, to: toId, rel: "references" });
    }
    if (issue?.module) {
      const mid = `module:${issue.module}`;
      if (nodeById.has(mid)) addEdge({ from: id, to: mid, rel: "module-of" });
    }
  });

  nodes.sort((a, b) => a.id.localeCompare(b.id));
  edges.sort((a, b) => {
    const k = `${a.from}|${a.rel}|${a.to}`;
    const k2 = `${b.from}|${b.rel}|${b.to}`;
    return k.localeCompare(k2);
  });

  return {
    version: 1,
    generatedAt: new Date().toISOString(),
    nodes,
    edges,
  };
}

export function listBrokenReferences(graph) {
  const bad = [];
  for (const e of graph.edges) {
    if (!e.brokenReference) continue;
    const ref = e.to.startsWith("missing:") ? e.to.slice("missing:".length) : e.to;
    bad.push({ issue: e.from, path: ref });
  }
  const seen = new Set();
  return bad.filter((x) => {
    const k = `${x.issue}|${x.path}`;
    if (seen.has(k)) return false;
    seen.add(k);
    return true;
  });
}
