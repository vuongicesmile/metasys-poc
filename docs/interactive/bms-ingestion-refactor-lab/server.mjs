import {createServer} from 'node:http';
import {readFile} from 'node:fs/promises';
import {dirname, extname, resolve} from 'node:path';
import {fileURLToPath} from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '../../..');
const port = Number(process.env.BMS_REFACTOR_LAB_PORT || 5402);
const types = {'.html':'text/html; charset=utf-8','.css':'text/css; charset=utf-8','.js':'text/javascript; charset=utf-8','.md':'text/markdown; charset=utf-8'};
const routes = new Map([['/',resolve(here,'index.html')],['/index.html',resolve(here,'index.html')],['/styles.css',resolve(here,'styles.css')],['/raw-map.css',resolve(here,'raw-map.css')],['/lab.js',resolve(here,'app.js')],['/app.js',resolve(here,'app.js')],['/plans/bms-ingestion-layered-refactor.vi.md',resolve(root,'docs/plans/bms-ingestion-layered-refactor.vi.md')],['/docs/plans/bms-ingestion-layered-refactor.vi.md',resolve(root,'docs/plans/bms-ingestion-layered-refactor.vi.md')]]);
const server = createServer(async (request,response) => {const pathname = new URL(request.url,'http://127.0.0.1').pathname; const file = routes.get(pathname); if (!file) {response.writeHead(404);response.end('Not found');return;} try {const body = await readFile(file);response.writeHead(200,{'Content-Type':types[extname(file)]||'application/octet-stream','Cache-Control':'no-store','Content-Security-Policy':"default-src 'self'; style-src 'self'; script-src 'self'; connect-src 'none'; object-src 'none'; base-uri 'none'"});response.end(body);} catch(error) {response.writeHead(500);response.end(error.message);}});
server.on('error',error=>{console.error(error.code==='EADDRINUSE'?`Port ${port} is in use. Set BMS_REFACTOR_LAB_PORT to another port.`:error.message);process.exitCode=1;});
server.listen(port,'127.0.0.1',()=>console.log(`BMS Ingestion Refactor Lab: http://127.0.0.1:${port}`));
