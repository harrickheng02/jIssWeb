import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export function findRepoRoot() {
  let d = path.resolve(__dirname, "..", "..", "..");
  for (let i = 0; i < 8; i++) {
    const specDir = path.join(d, "openspec", "specs");
    if (fs.existsSync(specDir)) return d;
    const p = path.dirname(d);
    if (p === d) break;
    d = p;
  }
  throw new Error("repo root not found (expected openspec/specs)");
}
