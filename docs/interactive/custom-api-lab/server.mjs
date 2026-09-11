import {createServer} from 'node:http';
import {readFile,stat} from 'node:fs/promises';
import {dirname,extname,resolve} from 'node:path';
import {fileURLToPath} from 'node:url';

const here=dirname(fileURLToPath(import.meta.url));
const port=Number(process.env.CUSTOM_API_DOCS_PORT||5401);
const types={'.html':'text/html; charset=utf-8','.css':'text/css; charset=utf-8','.js':'text/javascript; charset=utf-8'};
const routes=new Map([
  ['/','index.html'],['/index.html','index.html'],['/styles.css','styles.css'],
  ['/content.js','content.js'],['/api-model.js','api-model.js'],['/app.js','app.js'],
  ['/shared-plugin-lab.css','../plugin-lab/styles.css'],['/plugin-lab/styles.css','../plugin-lab/styles.css']
]);
const server=createServer(async(req,res)=>{
  const pathname=new URL(req.url,'http://127.0.0.1').pathname;
  const route=routes.get(pathname);
  if(!route){res.writeHead(404);res.end('Not found');return;}
  try{
    const path=resolve(here,route);
    await stat(path);
    const body=await readFile(path);
    res.writeHead(200,{'Content-Type':types[extname(path)]||'application/octet-stream','Cache-Control':'no-store','Content-Security-Policy':"default-src 'self'; style-src 'self'; img-src 'self' data:; script-src 'self'; connect-src 'none'; object-src 'none'; base-uri 'none'"});
    res.end(body);
  }catch(error){res.writeHead(500);res.end(error.code==='ENOENT'?'Run npm run build first.':error.message);}
});
server.on('error',error=>{console.error(error.code==='EADDRINUSE'?'Port '+port+' is in use. Set CUSTOM_API_DOCS_PORT to another port.':error.message);process.exitCode=1;});
server.listen(port,'127.0.0.1',()=>console.log('Custom API Handbook: http://127.0.0.1:'+port+' · local documentation only'));
