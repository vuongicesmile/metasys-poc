import { readFile, writeFile } from 'node:fs/promises';
import { dirname, resolve, relative, basename } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { Marked } from 'marked';
import hljs from 'highlight.js';

const here = dirname(fileURLToPath(import.meta.url));
const root = resolve(here, '../../..');
const guidePath = 'docs/runbooks/dataverse-plugin-step-by-step.vi.md';
const planPath = 'docs/plans/dataverse-plugins.vi.md';
const sourcePath = 'plugins/FMCentralBms.Plugins/RequireEquipmentBuilding.cs';
const allowedPaths = [
  guidePath, planPath, sourcePath,
  'plugins/FMCentralBms.Plugins/FMCentralBms.Plugins.csproj',
  'DataverseSyncWorker/Program.cs',
  'DataverseSyncWorker/Services/DataversePluginProvisioner.cs',
  'DataverseSyncWorker/Services/DataverseConnection.cs',
  'docs/reference/dataverse-deployment.md',
  'docs/runbooks/sql-to-dataverse-runbook.vi.md',
  'docs/runbooks/power-automate-sql-sync.vi.md',
  'dataverse/FMCentralBms/SdkMessageProcessingSteps/{00029f22-bcac-f111-aaad-00224819a344}.xml',
  'dataverse/FMCentralBms/SdkMessageProcessingSteps/{27894c29-bcac-f111-aaad-00224819a344}.xml'
];
// Whitelist deliberately excludes credentials, .snk keys and appsettings.json.
const sourceFiles = Object.fromEntries(await Promise.all(allowedPaths.map(async path =>
  [path, (await readFile(resolve(root, path), 'utf8')).replace(/^\uFEFF/, '').replace(/\r\n/g, '\n')]
)));
const guide = sourceFiles[guidePath];
const plan = sourceFiles[planPath];
const codeSamples = [...guide.matchAll(/^```csharp\n([\s\S]*?)^```/gm)].map(m => m[1].trimEnd());
if (codeSamples[0] !== sourceFiles[sourcePath].trimEnd()) throw new Error('Runbook C# differs from actual plugin source.');
const apiTest = [...plan.matchAll(/^```powershell\n([\s\S]*?)^```/gm)].find(m => m[1].includes('function Invoke-BmsPluginDemoApi'))?.[1];
if (!apiTest) throw new Error('API test script missing from plan.');

const esc = s => String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const slug = s => s.toLowerCase().replace(/[^\p{L}\p{N}_\-\s]/gu, '').replace(/\s/g, '-');
const languageOf = path => path.endsWith('.cs') ? 'csharp' : /\.(xml|csproj)$/.test(path) ? 'xml' : 'text';
const highlight = (code, language) => hljs.getLanguage(language) ? hljs.highlight(code, {language, ignoreIllegals:true}).value : esc(code);
const titles = [
  ['NỀN TẢNG','Bắt đầu ở đây','Từ dòng code<br>đến Dataverse.','Một hướng dẫn thực hành để bạn hiểu code, tự đăng ký plug-in và kiểm chứng kết quả. Đọc từng bước, thử ví dụ ngay bên cạnh.'],
  ['NỀN TẢNG','Tooling & môi trường','Chuẩn bị đúng.<br>Bắt đầu tự tin.','Kiểm tra .NET, PAC và đúng Developer organization trước khi đưa code lên cloud.'],
  ['NỀN TẢNG','Tạo project local','Một project.<br>Những file cần biết.','Phân biệt source code, cấu hình build, signing key và file được sinh ra. Biết chính xác mình cần copy gì.'],
  ['VIẾT & BUILD','Hiểu từng dòng C#','47 dòng code.<br>Hiểu đến từng dòng.','Chọn một dòng ở khung bên phải để xem ý nghĩa. Theo dõi cách context và Target dẫn tới quyết định cho phép hoặc từ chối.'],
  ['VIẾT & BUILD','Build & chọn DLL','Code đã sẵn sàng.<br>Chọn đúng DLL.','Compile, ký assembly và xác định chính xác file Release để upload. Bản DLL mới và bản export cũ có vai trò khác nhau.'],
  ['ĐƯA LÊN DATAVERSE','Đăng ký assembly','Đưa code lên<br>Dataverse.','Mở Plug-in Registration Tool, chọn DLL và đăng ký assembly trong Sandbox. Đây là bước upload code.'],
  ['ĐƯA LÊN DATAVERSE','Đăng ký hai steps','Khi nào plugin<br>được gọi?','Message, table, filter, stage và mode quyết định lúc code chạy. Thử thay từng ô trong tab “Tạo step”.'],
  ['ĐƯA LÊN DATAVERSE','Thêm vào solution','Đóng gói đủ<br>code và cấu hình.','Đưa assembly và cả hai steps vào FMCentralBms để export mang đủ các thành phần.'],
  ['ĐƯA LÊN DATAVERSE','Deploy bằng SDK','Tự động hóa<br>việc đăng ký.','Đọc cách helper của repo biến một DLL local thành assembly, class, steps và solution components trên Dataverse.'],
  ['KIỂM CHỨNG & MỞ RỘNG','Test API & sync','Đã deploy.<br>Giờ chứng minh nó chạy.','Thử trường hợp đúng, sai và cập nhật từng phần. Sau đó kiểm tra đường dữ liệu SQL → worker → Dataverse.'],
  ['KIỂM CHỨNG & MỞ RỘNG','Update & export','Sửa code.<br>Phát hành bản tiếp theo.','Giữ identity, tăng version, update assembly, kiểm thử lại và lưu solution export.'],
  ['KIỂM CHỨNG & MỞ RỘNG','Tự viết rule mới','Đến lượt bạn<br>viết một rule mới.','Từ lookup của Equipment sang tên Building: biết phải đổi class, table, field, kiểu dữ liệu và registration nào.'],
  ['KIỂM CHỨNG & MỞ RỘNG','Gỡ lỗi thường gặp','Tìm đúng chỗ.<br>Sửa đúng nguyên nhân.','Bắt đầu từ triệu chứng: build, assembly, step, payload hay worker. Mỗi vấn đề có một điểm kiểm tra cụ thể.']
];
const chapterParts = [...guide.matchAll(/^## (\d+)\. (.+)\n([\s\S]*?)(?=^## \d+\. |$(?![\s\S]))/gm)];
if (chapterParts.length !== 13) throw new Error('Expected all 13 runbook chapters.');
const snippets = {};
let blockId = 0;
const addSnippet = (key, title, code, lang='csharp', origin='Ví dụ trong bài') => {
  snippets[key] = {key,title,code:code.trimEnd(),lang,origin,lines:code.trimEnd().split('\n').map(line => highlight(line,lang))};
  return key;
};
addSnippet('plugin','RequireEquipmentBuilding.cs',sourceFiles[sourcePath],'csharp','Source thật · net48');
addSnippet('project','FMCentralBms.Plugins.csproj',sourceFiles[allowedPaths[3]],'xml','Project thật');
addSnippet('provisioner','DataversePluginProvisioner.cs',sourceFiles[allowedPaths[5]],'csharp','Deployment helper thật');
addSnippet('program','Program.cs · --register-plugin',sourceFiles[allowedPaths[4]],'csharp','Worker thật');
addSnippet('api-test','Test-EquipmentBuildingPlugin.ps1',apiTest,'powershell','Test API thật · có ghi cloud');
addSnippet('exercise','RequireBuildingName.cs',codeSamples[1],'csharp','Bài tập · chưa deploy');
addSnippet('create-step','Equipment Create · export XML',sourceFiles[allowedPaths[10]],'xml','Export · 10.09.2026');
addSnippet('update-step','Equipment Update · export XML',sourceFiles[allowedPaths[11]],'xml','Export · 10.09.2026');

function rendererFor(path, collected = [], compactCsharp = false) {
  const marked = new Marked({gfm:true});
  marked.use({renderer:{
    html({text}) {return esc(text);},
    heading({tokens,depth}) {
      const text = this.parser.parseInline(tokens);
      return '<h'+depth+' id="'+slug(text.replace(/<[^>]*>/g,''))+'">'+text+'</h'+depth+'>';
    },
    link({href,tokens}) {
      const label = this.parser.parseInline(tokens);
      if (/^https:\/\//.test(href)) return '<a href="'+esc(href)+'" target="_blank" rel="noopener noreferrer">'+label+' ↗</a>';
      if (/^[a-z]+:/i.test(href)) return label;
      const [file,anchor] = href.split('#');
      const full = file ? relative(root,resolve(root,dirname(path),file)).replaceAll('\\','/') : path;
      if (full === guidePath && anchor) {
        const match = /^(\d+)-/.exec(anchor);
        if (match) return '<a href="#bai-'+match[1]+'">'+label+'</a>';
      }
      if (full.includes('/SdkMessageProcessingSteps') && !full.endsWith('.xml')) return '<button class="source-link" data-snippet="create-step">'+label+'</button>';
      if (sourceFiles[full]) return '<button class="source-link" data-resource="'+esc(full)+'" data-anchor="'+esc(anchor||'')+'">'+label+'</button>';
      return '<span class="repo-reference" title="Mở trong repo: '+esc(full)+'">'+label+'</span>';
    },
    code({text,lang}) {
      const language = (lang||'text').split(/\s/)[0];
      const existing = language==='csharp' && text.trimEnd()===snippets.plugin.code ? 'plugin' :
        language==='csharp' && text.trimEnd()===snippets.exercise.code ? 'exercise' : null;
      const key = existing || 'block-'+(++blockId);
      if(!existing)addSnippet(key,language==='powershell'?'PowerShell · lệnh trong bài':language==='csharp'?'C# · code trong bài':'Ví dụ · '+language,text,language);
      collected.push(key);
      if(compactCsharp && language==='csharp')return '<div class="learning-note"><strong>Source đầy đủ đang ở bên cạnh.</strong><p>Đọc giải thích bên dưới, chọn dòng tương ứng trong khung code. <button class="text-button" data-snippet="'+key+'">Mở code ↗</button></p></div>';
      const literal = highlight(text,language);
      return '<figure class="code-figure"><figcaption><span>'+esc(language==='text'?'MINH HỌA':language.toUpperCase())+'</span><span><button class="inline-code-action" data-snippet="'+key+'">Mở bên cạnh ↗</button><button class="inline-code-action" data-copy="'+key+'">Copy</button></span></figcaption><pre><code>'+literal+'</code></pre></figure>';
    }
  }});
  return marked;
}
const chapterPreferred = ['plugin','project','project','plugin','project','plugin','create-step','update-step','provisioner','api-test','provisioner','exercise','plugin'];
const chapters = chapterParts.map((m,i) => {
  const keys = [];
  const html = rendererFor(guidePath,keys,i===3||i===11).parse(m[3]);
  return {number:i+1,title:m[2],nav:titles[i][1],group:titles[i][0],headline:titles[i][2],lead:titles[i][3],html,
    searchText:m[3].replace(/<[^>]*>/g,''),
    snippets:[...new Set([chapterPreferred[i],...keys,...(i===6?['update-step']:[]),'plugin','api-test','exercise'])]};
});
const resources = Object.fromEntries(Object.entries(sourceFiles).map(([path,text]) => [path,{
  name:basename(path),text,
  html:path.endsWith('.md')?rendererFor(path).parse(text):'<figure class="code-figure"><pre><code>'+highlight(text,languageOf(path))+'</code></pre></figure>'
}]));
const explanationRows = guide.split('\n').filter(line => /^\| \d+(?:–\d+)? \|/.test(line));
const annotations = explanationRows.map(line => {
  const parts = line.split('|').map(p=>p.trim());
  const [from,to] = parts[1].split('–').map(Number);
  return {from,to:to||from,title:parts[2],html:rendererFor(guidePath).parseInline(parts[2]+' '+parts[3])};
});
const data = {
  version:'1.0.0',guidePath,planPath,sourcePath,chapters,snippets,resources,annotations,
  sourceHash:createHash('sha256').update(sourceFiles[sourcePath]).digest('hex'),
  guideHash:createHash('sha256').update(guide).digest('hex')
};
const output = '// Generated by npm run build from reviewed local sources. Do not hand-edit.\nwindow.PLUGIN_DOCS = '+
  JSON.stringify(data).replaceAll('<','\\u003c').replaceAll('\u2028','\\u2028').replaceAll('\u2029','\\u2029')+';\n';
await writeFile(resolve(here,'content.js'),output,'utf8');
console.log('Built '+chapters.length+' full chapters, '+Object.keys(snippets).length+' code examples, '+annotations.length+' line annotations.');
