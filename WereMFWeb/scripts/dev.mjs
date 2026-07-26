import { createServer } from "node:http";
import { readFile, stat } from "node:fs/promises";
import { extname, join, normalize } from "node:path";
const root = join(import.meta.dirname, "..", "src");
const types = { ".html": "text/html; charset=utf-8", ".css": "text/css; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".png": "image/png" };
const server = createServer(async (req, res) => {
  try {
    let path = normalize(join(root, decodeURIComponent((req.url || "/").split("?")[0])));
    if (!path.startsWith(root)) throw new Error("bad path");
    if ((await stat(path)).isDirectory()) path = join(path, "index.html");
    res.setHeader("Content-Type", types[extname(path)] || "application/octet-stream");
    res.end(await readFile(path));
  } catch { res.statusCode = 404; res.end("Not found"); }
});
server.listen(4173, "127.0.0.1", () => console.log("Local URL: http://127.0.0.1:4173"));
