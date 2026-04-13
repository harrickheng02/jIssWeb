const RE = /\bopenspec\/[\w./-]+/g;

export function extractOpenspecPaths(text) {
  if (!text || typeof text !== "string") return [];
  const set = new Set();
  let m;
  while ((m = RE.exec(text)) !== null) {
    const raw = m[0].replace(/[.,;:)]+$/, "");
    set.add(raw);
  }
  return [...set];
}
