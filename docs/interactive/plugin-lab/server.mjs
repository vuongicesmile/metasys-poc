import { createServer } from 'node:http';
import { readFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here=dirname(fileURLToPath(import.meta.url));
const port=Number(process.env.PLUGIN_DOCS_PORT||5400);
const files = new Map([
  ['/','index.html'],['/index.html','index.html'],
  ['/styles.css','styles.css'],['/app.js','app.js'],
  ['/content.js','content.js'],['/rule-model.js','rule-model.js']
]);
const mime={html:'text/html; charset=utf-8',css:'text/css; charset=utf-8',js:'text/javascript; charset=utf-8'};
const server=createServer(async(req,res)=>{
  if (req.method!=='GET'&&req.method!=='HEAD') {res.writeHead(405,{Allow:'GET, HEAD'});res.end();return;}
  const path=new URL(req.url,'http://localhost').pathname;
  const file=files.get(path);
  if (!file){res.writeHead(404,{'Content-Type':'text/plain'});res.end('Not found');return;}
  try {
    const data=await readFile(resolve(here,file));
    res.writeHead(200,{
      'Content-Type':mime[file.split('.').at(-1)],
      'Cache-Control':'no-store',
      'X-Content-Type-Options':'nosniff',
      'Content-Security-Policy':"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'"
    });
    res.end(req.method==='HEAD'?undefined:data);
  }catch{res.writeHead(500,{'Content-Type':'text/plain'});res.end('Build documentation first: npm run build');}
});
server.on('error',error=>{console.error(error.code==='EADDRINUSE'?'Port '+port+' is in use. Use PLUGIN_DOCS_PORT to select another.':error.message);process.exitCode=1;});
server.listen(port,'127.0.0.1',()=>console.log('Plugin Handbook: http://127.0.0.1:'+port+' · local documentation only'));
