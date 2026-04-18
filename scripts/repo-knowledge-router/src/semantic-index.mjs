import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { walkFiles } from "./walk-md.mjs";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export const SEMANTIC_INDEX_SCHEMA_VERSION = 1;

function dataDir() {
  return path.join(__dirname, "..", "data");
}

function indexPath() {
  return path.join(dataDir(), "semantic-index.json");
}

function toPosix(p) {
  return p.split(path.sep).join("/");
}

function chunkMarkdown(relPath, text) {
  const lines = text.split(/\r?\n/);
  const chunks = [];
  let buf = [];
  let len = 0;
  for (const line of lines) {
    const isH2 = /^##\s+/.test(line);
    if (isH2 && buf.length && len > 1500) {
      chunks.push(buf.join("\n"));
      buf = [line];
      len = line.length;
    } else {
      buf.push(line);
      len += line.length + 1;
    }
  }
  if (buf.length) chunks.push(buf.join("\n"));
  if (!chunks.length) return [{ path: relPath, text: text.slice(0, 12000) }];
  return chunks.map((t) => ({ path: relPath, text: t.slice(0, 12000) }));
}

async function fetchEmbeddings(texts, apiKey, url, model) {
  const r = await fetch(url, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${apiKey}`,
    },
    body: JSON.stringify({ model, input: texts }),
  });
  if (!r.ok) throw new Error(`embedding HTTP ${r.status}: ${await r.text()}`);
  const j = await r.json();
  return j.data.map((d) => d.embedding);
}

function cosine(a, b) {
  let dot = 0;
  let na = 0;
  let nb = 0;
  for (let i = 0; i < a.length; i++) {
    dot += a[i] * b[i];
    na += a[i] * a[i];
    nb += b[i] * b[i];
  }
  const d = Math.sqrt(na) * Math.sqrt(nb);
  return d === 0 ? 0 : dot / d;
}

function parseLimit(argv) {
  const a = [...argv];
  const i = a.indexOf("--limit");
  if (i >= 0) {
    const n = Number(a[i + 1]);
    a.splice(i, Number.isFinite(n) ? 2 : 1);
    if (Number.isFinite(n)) return { limit: Math.min(50, Math.max(1, n)), argv: a };
  }
  return { limit: 12, argv: a };
}

async function cmdBuild(repoRoot) {
  const apiKey = process.env.REPO_KNOWLEDGE_EMBEDDING_API_KEY || "";
  const url =
    process.env.REPO_KNOWLEDGE_EMBEDDING_URL || "https://api.openai.com/v1/embeddings";
  const model =
    process.env.REPO_KNOWLEDGE_EMBEDDING_MODEL || "text-embedding-3-small";
  if (!apiKey) {
    console.error("set REPO_KNOWLEDGE_EMBEDDING_API_KEY for semantic-index build");
    return 1;
  }
  const roots = [
    path.join(repoRoot, "openspec", "specs"),
    path.join(repoRoot, "openspec", "changes"),
  ];
  const items = [];
  for (const root of roots) {
    if (!fs.existsSync(root)) continue;
    for (const abs of walkFiles(root, (f) => f.endsWith(".md"))) {
      const rel = toPosix(path.relative(repoRoot, abs));
      const text = fs.readFileSync(abs, "utf8");
      for (const c of chunkMarkdown(rel, text)) {
        items.push({ path: c.path, text: c.text });
      }
    }
  }
  const embeddings = [];
  const batchSize = 16;
  for (let i = 0; i < items.length; i += batchSize) {
    const batch = items.slice(i, i + batchSize).map((x) => x.text);
    const em = await fetchEmbeddings(batch, apiKey, url, model);
    embeddings.push(...em);
  }
  const out = {
    schemaVersion: SEMANTIC_INDEX_SCHEMA_VERSION,
    model,
    embeddingUrl: url,
    generatedAt: new Date().toISOString(),
    chunks: items.map((it, i) => ({
      path: it.path,
      text: it.text.slice(0, 2000),
      embedding: embeddings[i],
    })),
  };
  fs.mkdirSync(dataDir(), { recursive: true });
  fs.writeFileSync(indexPath(), `${JSON.stringify(out)}\n`, "utf8");
  console.log(indexPath());
  return 0;
}

async function cmdSearch(repoRoot, argvRest) {
  const { limit, argv: a2 } = parseLimit(argvRest);
  const sep = a2.indexOf("--");
  const tail = sep >= 0 ? a2.slice(sep + 1) : a2;
  const query = tail.join(" ").trim();
  if (!query) {
    console.error("usage: semantic-index search [--limit N] -- <query>");
    return 1;
  }
  const p = indexPath();
  if (!fs.existsSync(p)) {
    console.error(`missing ${p}; run semantic-index build first`);
    return 1;
  }
  const raw = JSON.parse(fs.readFileSync(p, "utf8"));
  if (raw.schemaVersion !== SEMANTIC_INDEX_SCHEMA_VERSION) {
    console.error(`index schemaVersion ${raw.schemaVersion} != ${SEMANTIC_INDEX_SCHEMA_VERSION}; rebuild`);
    return 1;
  }
  const apiKey = process.env.REPO_KNOWLEDGE_EMBEDDING_API_KEY || "";
  const url =
    process.env.REPO_KNOWLEDGE_EMBEDDING_URL || "https://api.openai.com/v1/embeddings";
  const model = process.env.REPO_KNOWLEDGE_EMBEDDING_MODEL || raw.model || "text-embedding-3-small";
  if (!apiKey) {
    console.error("set REPO_KNOWLEDGE_EMBEDDING_API_KEY for semantic-index search");
    return 1;
  }
  const [qEmb] = await fetchEmbeddings([query], apiKey, url, model);
  const el0 = raw.chunks[0]?.embedding?.length;
  if (el0 != null && el0 !== qEmb.length) {
    console.error("embedding dimension mismatch; rebuild index or match REPO_KNOWLEDGE_EMBEDDING_MODEL");
    return 1;
  }
  const scored = raw.chunks.map((c, i) => ({
    path: c.path,
    score: cosine(qEmb, c.embedding),
    preview: String(c.text || "").replace(/\s+/g, " ").slice(0, 160),
    i,
  }));
  scored.sort((a, b) => b.score - a.score);
  for (const s of scored.slice(0, limit)) {
    console.log(`${s.score.toFixed(4)}\t${s.path}\t${s.preview}`);
  }
  return 0;
}

export async function runSemanticIndex(repoRoot, argv) {
  const sub = argv[0];
  if (sub === "build") return cmdBuild(repoRoot);
  if (sub === "search") return cmdSearch(repoRoot, argv.slice(1));
  console.error("usage: semantic-index build | search [--limit N] -- <query>");
  return 1;
}
