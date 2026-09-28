import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const here = dirname(fileURLToPath(import.meta.url));
const port = Number(process.env.CRUD_GUIDE_PORT || 5431);
const routes = new Map([
  ['/', ['index.html', 'text/html']],
  ['/main.js', ['dist/main.js', 'text/javascript']],
  ['/main.css', ['dist/main.css', 'text/css']],
]);
const server = createServer(async (req, res) => {
  const pathname = new URL(req.url, 'http://localhost').pathname;
  if (pathname === '/favicon.ico') { res.writeHead(204); res.end(); return; }
  const route = routes.get(pathname);
  if (!route) { res.writeHead(404); res.end('Not found'); return; }
  try {
    const body = await readFile(resolve(here, route[0]));
    res.writeHead(200, {
      'Content-Type': route[1] + '; charset=utf-8', 'Cache-Control': 'no-store',
      'Content-Security-Policy': "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'none'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'",
      'X-Content-Type-Options': 'nosniff',
    });
    res.end(body);
  } catch { res.writeHead(503); res.end('Run npm run build first.'); }
});
server.on('error', error => { console.error(error.message); process.exitCode = 1; });
server.listen(port, '127.0.0.1', () => console.log(`React CRUD guide: http://127.0.0.1:${port}/ (documentation only)`));
