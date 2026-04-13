function tokenize(q) {
  const s = q.trim().toLowerCase();
  if (!s) return [];
  return s
    .split(/[\s/._\-,:;，。、]+/g)
    .map((t) => t.trim())
    .filter((t) => t.length >= 2);
}

function scoreNode(node, tokens) {
  if (!tokens.length) return 0;
  const hay = `${node.title || ""} ${node.path || ""} ${node.description || ""} ${node.bodyPreview || ""}`.toLowerCase();
  let s = 0;
  for (const t of tokens) {
    if (hay.includes(t)) s += 3;
    if ((node.path || "").toLowerCase().includes(t)) s += 2;
  }
  return s;
}

export function routeQuery(graph, query, limit = 15) {
  const tokens = tokenize(query);
  const cap = Math.min(25, Math.max(5, limit));
  const scored = new Map();

  const set = (id, score, reason) => {
    if (!id || id.startsWith("missing:")) return;
    const cur = scored.get(id);
    if (cur && cur.score >= score) return;
    const node = graph.nodes.find((x) => x.id === id);
    if (!node) return;
    scored.set(id, { node, score, reason });
  };

  for (const n of graph.nodes) {
    const sc = scoreNode(n, tokens);
    if (sc > 0) set(n.id, sc, "text match");
  }

  const snap = [...scored.entries()];
  for (const [id, v] of snap) {
    if (v.node.kind !== "issue") continue;
    for (const e of graph.edges) {
      if (e.from !== id || e.rel !== "references" || e.brokenReference) continue;
      set(e.to, Math.max(1, v.score - 1), `from issue "${v.node.title}"`);
    }
  }

  const byPath = new Map();
  for (const v of scored.values()) {
    const p = v.node.path || v.node.id;
    const cur = byPath.get(p);
    if (!cur || cur.score < v.score) byPath.set(p, { path: p, score: v.score, reason: v.reason });
    else if (cur.score === v.score && cur.reason !== v.reason)
      byPath.set(p, { path: p, score: v.score, reason: `${cur.reason}; ${v.reason}` });
  }

  const rows = [...byPath.values()];
  rows.sort((a, b) => {
    if (b.score !== a.score) return b.score - a.score;
    return String(a.path).localeCompare(String(b.path));
  });

  return rows.slice(0, cap);
}
