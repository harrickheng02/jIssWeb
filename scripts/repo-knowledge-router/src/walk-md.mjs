import fs from "node:fs";
import path from "node:path";

export function walkFiles(root, predicate) {
  const out = [];
  if (!fs.existsSync(root)) return out;
  const stack = [root];
  while (stack.length) {
    const cur = stack.pop();
    let st;
    try {
      st = fs.statSync(cur);
    } catch {
      continue;
    }
    if (st.isDirectory()) {
      for (const name of fs.readdirSync(cur)) {
        if (name === "node_modules") continue;
        stack.push(path.join(cur, name));
      }
    } else if (st.isFile() && predicate(cur)) {
      out.push(cur);
    }
  }
  return out.sort();
}
