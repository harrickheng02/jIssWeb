import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { buildGraph, listBrokenReferences } from "./build-graph.mjs";
import { listOpenspecGateViolations } from "./openspec-gate.mjs";
import { runSemanticIndex } from "./semantic-index.mjs";
import { findRepoRoot } from "./repo-root.mjs";
import { routeQuery } from "./route-query.mjs";
import { writePmOpenIssuesMd } from "./write-pm-open-issues.mjs";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

function usage() {
  console.error(
    "usage: node src/cli.mjs build | refresh | verify | route [--limit N] -- <query>\n       node src/cli.mjs semantic-index build\n       node src/cli.mjs semantic-index search [--limit N] -- <query>\n       node src/cli.mjs route --limit 10 -- 帖子点赞"
  );
}

function graphPath() {
  return path.join(__dirname, "..", "data", "graph.json");
}

function parseLimit(argv) {
  const a = [...argv];
  const i = a.indexOf("--limit");
  if (i >= 0) {
    const n = Number(a[i + 1]);
    a.splice(i, Number.isFinite(n) ? 2 : 1);
    if (Number.isFinite(n)) return { limit: n, argv: a };
  }
  return { limit: 15, argv: a };
}

function runValidation(repoRoot) {
  const g = buildGraph(repoRoot);
  const bad = listBrokenReferences(g);
  const gate = listOpenspecGateViolations(repoRoot);
  return { g, bad, gate };
}

function printValidationFailure(bad, gate) {
  if (bad.length) {
    for (const b of bad) console.error(`${b.issue}\t${b.path}`);
    return true;
  }
  if (gate.length) {
    for (const x of gate) console.error(`${x.issue}\t${x.path}\t${x.title || ""}`);
    return true;
  }
  return false;
}

async function main() {
  const argv = process.argv.slice(2);
  const cmd = argv[0];

  try {
    const repoRoot = findRepoRoot();

    if (cmd === "build") {
      const { g, bad, gate } = runValidation(repoRoot);
      if (printValidationFailure(bad, gate)) process.exit(1);
      const out = graphPath();
      fs.mkdirSync(path.dirname(out), { recursive: true });
      fs.writeFileSync(out, `${JSON.stringify(g, null, 2)}\n`, "utf8");
      const idx = writePmOpenIssuesMd(repoRoot, g);
      console.log(`${out}\n${idx}`);
      process.exit(0);
    }

    if (cmd === "refresh") {
      const { g, bad, gate } = runValidation(repoRoot);
      if (printValidationFailure(bad, gate)) process.exit(1);
      const out = graphPath();
      fs.mkdirSync(path.dirname(out), { recursive: true });
      fs.writeFileSync(out, `${JSON.stringify(g, null, 2)}\n`, "utf8");
      const idx = writePmOpenIssuesMd(repoRoot, g);
      console.log(`${out}\n${idx}`);
      process.exit(0);
    }

    if (cmd === "verify") {
      const { bad, gate } = runValidation(repoRoot);
      if (printValidationFailure(bad, gate)) process.exit(1);
      process.exit(0);
    }

    if (cmd === "semantic-index") {
      const exitCode = await runSemanticIndex(repoRoot, argv.slice(1));
      process.exit(exitCode);
    }

    if (cmd === "route") {
      const { limit, argv: a2 } = parseLimit(argv.slice(1));
      const sep = a2.indexOf("--");
      const tail = sep >= 0 ? a2.slice(sep + 1) : a2;
      const query = tail.join(" ").trim();
      if (!query) {
        usage();
        process.exit(1);
      }
      const gp = graphPath();
      if (!fs.existsSync(gp)) {
        console.error(`missing ${gp}; run build first`);
        process.exit(1);
      }
      const graph = JSON.parse(fs.readFileSync(gp, "utf8"));
      const rows = routeQuery(graph, query, limit);
      for (const r of rows) console.log(`${r.path}\t${r.reason}`);
      process.exit(0);
    }

    usage();
    process.exit(1);
  } catch (e) {
    console.error(e?.message || e);
    process.exit(1);
  }
}

main();
