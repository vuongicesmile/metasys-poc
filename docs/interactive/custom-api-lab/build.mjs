import {readFile,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {createRequire} from 'node:module';
import {dirname,resolve,relative,basename} from 'node:path';
import {existsSync} from 'node:fs';
import {fileURLToPath} from 'node:url';

const here=dirname(fileURLToPath(import.meta.url));
const root=resolve(here,'../../..');
const dependencyAnchor=existsSync(resolve(here,'node_modules'))
  ? resolve(here,'package.json')
  : resolve(here,'../plugin-lab/package.json');
const require=createRequire(dependencyAnchor);
const {Marked}=require('marked');
const hljs=require('highlight.js');

const guidePath='docs/runbooks/custom-api-step-by-step.vi.md';
const receiptPath='docs/plans/custom-api-request-bms-sync.vi.md';
const sourcePath='plugins/FMCentralBms.Plugins/RequestBmsSync.cs';
const customApiXml='dataverse/FMCentralBms/customapis/fmc_RequestBmsSync/customapi.xml';
const inputXml='dataverse/FMCentralBms/customapis/fmc_RequestBmsSync/customapirequestparameters/ClientRequestId/customapirequestparameter.xml';
const flowPath='dataverse/FMCentralBms/Workflows/FMC-AppRequestBMSSyncEvent-D13BFE37-CA87-412B-9531-D3ED381B5B21.json';
const allowedPaths=[
  guidePath,receiptPath,sourcePath,
  'plugins/FMCentralBms.Plugins/FMCentralBms.Plugins.csproj',
  'DataverseSyncWorker/Program.cs',
  'DataverseSyncWorker/Services/DataversePluginProvisioner.cs',
  'DataverseSyncWorker/Services/DataverseProvisioner.cs',
  'DataverseSyncWorker/Services/SyncRequestStore.cs',
  'dataverse/app-source/BmsEventDemo.html',
  'scripts/Invoke-BmsEventDemo.ps1',
  customApiXml,inputXml,
  'dataverse/FMCentralBms/customapis/fmc_RequestBmsSync/customapiresponseproperties/RequestId/customapiresponseproperty.xml',
  'dataverse/FMCentralBms/customapis/fmc_RequestBmsSync/customapiresponseproperties/Created/customapiresponseproperty.xml',
  'dataverse/FMCentralBms/customapis/fmc_RequestBmsSync/customapiresponseproperties/Status/customapiresponseproperty.xml',
  'dataverse/FMCentralBms/customapis/fmc_RequestBmsSync/customapiresponseproperties/Message/customapiresponseproperty.xml',
  'dataverse/FMCentralBms/Assets/catalogassignments.xml',
  flowPath,
  'docs/runbooks/custom-api-bms-sync-demo.vi.md',
  'docs/reference/dataverse-deployment.md'
];
// Strict whitelist: never embed appsettings, token helpers, secrets or the .snk key.
const sourceFiles=Object.fromEntries(await Promise.all(allowedPaths.map(async path=>[
  path,(await readFile(resolve(root,path),'utf8')).replace(/^\uFEFF/,'').replace(/\r\n/g,'\n')
])));
const guide=sourceFiles[guidePath];
const plugin=sourceFiles[sourcePath];

const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const slug=s=>s.toLowerCase().replace(/<[^>]+>/g,'').replace(/[^\p{L}\p{N}_\-\s]/gu,'').trim().replace(/\s+/g,'-');
const languageOf=path=>path.endsWith('.cs')?'csharp':path.endsWith('.js')||path.endsWith('.html')?'javascript':path.endsWith('.json')?'json':/\.(xml|csproj)$/.test(path)?'xml':path.endsWith('.ps1')?'powershell':'text';
const highlight=(code,lang)=>hljs.getLanguage(lang)?hljs.highlight(code,{language:lang,ignoreIllegals:true}).value:esc(code);

const titles=[
  ['NỀN TẢNG','Bài toán & kiến trúc','Từ nút bấm<br>đến queue.','Nhìn toàn bộ tuyến UI → Custom API → plug-in → queue → worker trước khi chạm vào code.'],
  ['NỀN TẢNG','Đọc API contract','Một message.<br>Một hợp đồng rõ ràng.','Hiểu binding, Action, privilege, input và output của fmc_RequestBmsSync.'],
  ['NỀN TẢNG','File nào để làm gì?','Copy đúng thứ.<br>Đặt đúng chỗ.','Phân biệt source, project, signing key, DLL, provisioner, app page và solution export.'],
  ['HANDLER C#','Giải thích từng dòng C#','132 dòng.<br>Click để hiểu từng dòng.','Source thật được đánh số; mỗi dòng đều có giải thích riêng ngay trong code inspector.'],
  ['HANDLER C#','Idempotency & race','Bấm lại an toàn.<br>Hai caller vẫn an toàn.','Theo dõi hai lớp chống trùng và alternate key phân xử request đồng thời.'],
  ['BUILD & DEPLOY','Build đúng DLL','Release. net48.<br>Strong name.','Biết build ở đâu, upload file nào và xử lý lỗi assembly fullname unique.'],
  ['BUILD & DEPLOY','Schema queue & active key','Queue là contract.<br>Key là hàng rào.','Hiểu từng column mà handler ghi và vì sao active key không thể bỏ.'],
  ['BUILD & DEPLOY','Provision Custom API','DLL thành message<br>trên Dataverse.','Đọc helper SDK tạo assembly, plugin type, Custom API, input và output.'],
  ['CALLER & EVENT','UI gọi Web API','Một POST.<br>Bốn output.','Đọc fetch, session, timeout và cách UI hiển thị kết quả không gây request trùng.'],
  ['CALLER & EVENT','Business Event & Flow','API phát event.<br>Flow lắng nghe.','Hiểu điều kiện để Custom API xuất hiện trong Dataverse business event trigger.'],
  ['VERIFY','Deploy & test','Verify trước.<br>Test có chủ đích.','Phân biệt lệnh read-only, lệnh gọi API có side effect và lệnh deploy cloud.'],
  ['VERIFY','Tracing & lỗi thường gặp','Thấy đúng nhánh.<br>Sửa đúng chỗ.','Dùng trace marker và triệu chứng để phân biệt API, plug-in, Flow và worker.'],
  ['MỞ RỘNG','Tạo API tương tự','Đến lượt bạn.<br>Thiết kế từ contract.','Dùng checklist và contract designer để phác thảo một command API mới.']
];
const chapterParts=[...guide.matchAll(/^## (\d+)\. (.+)\n([\s\S]*?)(?=^## \d+\. |$(?![\s\S]))/gm)];
if(chapterParts.length!==13)throw new Error('Expected exactly 13 numbered chapters in '+guidePath+'.');

const snippets={};
let blockId=0;
function addSnippet(key,title,code,lang,origin){
  const clean=code.trimEnd();
  snippets[key]={key,title,code:clean,lang,origin,lines:clean.split('\n').map(line=>highlight(line,lang))};
  return key;
}
addSnippet('plugin','RequestBmsSync.cs',plugin,'csharp','Source thật · net48');
addSnippet('project','FMCentralBms.Plugins.csproj',sourceFiles[allowedPaths[3]],'xml','Project thật · version + signing');
addSnippet('provisioner','DataversePluginProvisioner.cs',sourceFiles[allowedPaths[5]],'csharp','SDK deployment helper thật');
addSnippet('ui','BmsEventDemo.html',sourceFiles[allowedPaths[8]],'javascript','Model-driven app web resource thật');
addSnippet('deploy-script','Invoke-BmsEventDemo.ps1',sourceFiles[allowedPaths[9]],'powershell','Deploy / Verify / Test script thật');
addSnippet('custom-api-xml','customapi.xml',sourceFiles[customApiXml],'xml','Solution export thật');
addSnippet('input-xml','ClientRequestId parameter.xml',sourceFiles[inputXml],'xml','Solution export thật');
addSnippet('flow','Power Automate business event.json',sourceFiles[flowPath],'json','Solution export thật');

function rendererFor(path,collected=[],compact=false){
  const marked=new Marked({gfm:true});
  marked.use({renderer:{
    html({text}){return esc(text);},
    heading({tokens,depth}){const label=this.parser.parseInline(tokens);return '<h'+depth+' id="'+slug(label)+'">'+label+'</h'+depth+'>';},
    link({href,tokens}){
      const label=this.parser.parseInline(tokens);
      if(/^https:\/\//.test(href))return '<a href="'+esc(href)+'" target="_blank" rel="noopener noreferrer">'+label+' ↗</a>';
      if(/^[a-z]+:/i.test(href))return label;
      const [file,anchor]=href.split('#');
      const full=file?relative(root,resolve(root,dirname(path),file)).replaceAll('\\','/'):path;
      if(full===guidePath&&anchor){const match=/^(\d+)-/.exec(anchor);if(match)return '<a href="#bai-'+match[1]+'">'+label+'</a>';}
      if(sourceFiles[full])return '<button class="source-link" data-resource="'+esc(full)+'" data-anchor="'+esc(anchor||'')+'">'+label+'</button>';
      return '<span class="repo-reference" title="Mở trong repo: '+esc(full)+'">'+label+'</span>';
    },
    code({text,lang}){
      const language=(lang||'text').split(/\s/)[0];
      const key='block-'+(++blockId);
      addSnippet(key,language.toUpperCase()+' · ví dụ trong bài',text,language,'Ví dụ trong '+basename(path));
      collected.push(key);
      if(compact)return '<div class="learning-note"><strong>Code đầy đủ ở khung bên cạnh.</strong><p><button class="text-button" data-snippet="'+key+'">Mở code example ↗</button></p></div>';
      return '<figure class="code-figure"><figcaption><span>'+esc(language.toUpperCase())+'</span><span><button class="inline-code-action" data-snippet="'+key+'">Mở bên cạnh ↗</button><button class="inline-code-action" data-copy="'+key+'">Copy</button></span></figcaption><pre><code>'+highlight(text,language)+'</code></pre></figure>';
    }
  }});
  return marked;
}

const preferred=['plugin','custom-api-xml','project','plugin','plugin','project','plugin','provisioner','ui','flow','deploy-script','plugin','plugin'];
const chapters=chapterParts.map((match,index)=>{
  const keys=[];
  const html=rendererFor(guidePath,keys,index===3).parse(match[3]);
  return {number:index+1,title:match[2],nav:titles[index][1],group:titles[index][0],headline:titles[index][2],lead:titles[index][3],html,searchText:match[3],snippets:[...new Set([preferred[index],...keys,'plugin','custom-api-xml','provisioner','ui','flow'])]};
});

function describeLine(line,index,lines){
  const n=index+1,t=line.trim(),previous=(lines[index-1]||'').trim();
  if(t==='')return 'Dòng trống tách các khối ý nghĩa để source dễ đọc; compiler bỏ qua dòng này.';
  if(t==='{'||t==='}')return 'Dấu ngoặc '+(t==='{'?'mở':'đóng')+' phạm vi của '+(previous.endsWith(')')?'method/khối điều kiện':'namespace, class, object initializer hoặc khối lệnh')+'.';
  if(t==='};')return 'Đóng object initializer và kết thúc statement bằng dấu chấm phẩy.';
  if(t.startsWith('using '))return 'Import namespace '+t.slice(6,-1)+' để dùng type ngắn gọn trong file.';
  if(t.startsWith('namespace '))return 'Đặt class trong namespace FMCentralBms.Plugins; plugin type fullname phụ thuộc namespace + class.';
  if(t.startsWith('///'))return 'XML documentation mô tả mục đích class; không thay đổi runtime behavior.';
  if(t.startsWith('public sealed class'))return '`sealed` ngăn kế thừa; `: IPlugin` làm class trở thành Dataverse plug-in handler.';
  if(t.includes('private const string Message'))return 'Tên message phải khớp Unique Name của Custom API: fmc_RequestBmsSync.';
  if(t.includes('private const string RequestTable'))return 'Logical name table queue mà Organization Service sẽ query và create.';
  if(t.includes('private const int Queued'))return 'Giá trị Choice Queued dùng khi tạo và trả status mặc định.';
  if(t.includes('private const int Running'))return 'Giá trị Choice Running được xem là active để không queue song song.';
  if(t.startsWith('public void Execute'))return 'Entry point Dataverse gọi cho main operation plug-in.';
  if(t.startsWith('private static string ReadClientRequestId'))return 'Khai báo helper đọc optional input và trả GUID string chuẩn hoặc null.';
  if(t.startsWith('private static Entity FindByCorrelation'))return 'Khai báo helper query tối đa một row theo fmc_correlationid.';
  if(t.startsWith('private static Entity FindActive'))return 'Khai báo helper tìm request đang Queued/Running của pipeline.';
  if(t.startsWith('private static Entity First'))return 'Khai báo helper nhỏ gom logic lấy phần tử đầu hoặc null.';
  if(t.startsWith('private static void Respond'))return 'Khai báo helper duy nhất gán output contract để các nhánh trả nhất quán.';
  if(t.includes('ITracingService'))return 'Lấy tracing service để ghi marker START/REUSE/CREATED vào Plug-in Trace Log.';
  if(t.includes('IPluginExecutionContext'))return 'Lấy execution context chứa message, organization, users, input và output parameters.';
  if(t.includes('IOrganizationServiceFactory'))return 'Lấy factory để tạo Organization Service trong security context phù hợp.';
  if(t.includes('string.Equals(context.MessageName'))return 'Guard: chỉ tiếp tục nếu message hiện tại đúng fmc_RequestBmsSync, so sánh ordinal chính xác.';
  if(t==='return;')return 'Thoát method ngay; các câu lệnh phía sau không chạy trong nhánh này.';
  if(t.includes('ReadClientRequestId'))return 'Đọc, validate và chuẩn hóa optional ClientRequestId từ input contract.';
  if(t.includes('Guid.NewGuid().ToString'))return 'Nếu caller không gửi ID, sinh GUID mới theo format D để làm correlation ID.';
  if(t.includes('context.OrganizationId')&&t.includes(':FMC'))return 'Tạo pipeline key ổn định theo organization; suffix FMC phân biệt pipeline này.';
  if(t.includes('CreateOrganizationService(context.UserId)'))return 'Tạo service chạy bằng calling user, nên quyền create/read của caller được tôn trọng.';
  if(t.startsWith('trace.Trace('))return 'Ghi trace marker và dữ liệu định danh không nhạy cảm để biết handler đi qua nhánh nào.';
  if(t==='pipeline, clientRequestId != null, correlationId);')return 'Truyền ba giá trị vào placeholder của trace START: pipeline, có client ID hay không và correlation ID.';
  if(t.includes('FindByCorrelation'))return 'Chỉ query correlation khi caller thật sự cung cấp ClientRequestId.';
  if(t==='if (existing != null)')return 'Nếu query tìm thấy row, vào nhánh reuse thay vì create.';
  if(t.includes('Respond(context, existing, false'))return 'Gán output từ row cũ; Created=false nói rõ không tạo row trong lần gọi này.';
  if(t.includes('FindActive(service, pipeline)'))return n<70?'Tìm request Queued/Running của cùng pipeline để chống hai sync đồng thời.':'Sau create fault, tìm row winner mà caller khác vừa tạo.';
  if(t==='try')return 'Bắt đầu vùng có thể phát sinh Dataverse fault khi tạo row.';
  if(t.includes('new Entity(RequestTable)'))return 'Khởi tạo late-bound Entity cho table fmc_syncrequest.';
  if(t.includes('["fmc_name"]'))return 'Đặt tên dễ đọc kèm timestamp UTC ISO-8601.';
  if(t.includes('["fmc_command"]'))return 'Ra lệnh DrainPending; worker hiểu command này để xử lý delivery ledger.';
  if(t.includes('["fmc_pipeline"]'))return 'Lưu pipeline để worker claim đúng phạm vi và để query active request.';
  if(t.includes('["fmc_activekey"]'))return 'Ghi alternate-key value; Dataverse dùng nó phân xử create đồng thời.';
  if(t.includes('["fmc_correlationid"]'))return 'Lưu idempotency/correlation ID để retry tìm lại cùng row.';
  if(t.includes('["fmc_requestedby"]'))return 'Lưu initiating user GUID dạng text cho audit nghiệp vụ.';
  if(t.includes('request.Id = service.Create'))return 'Gọi Dataverse Create; GUID trả về được gán lại vào Entity để đưa ra output.';
  if(t.includes('request["fmc_status"]'))return 'Bổ sung status vào Entity local vì Create không tự hydrate attributes.';
  if(t.includes('["fmc_status"] = new OptionSetValue'))return 'Ghi Choice Queued bằng OptionSetValue thay vì integer thô.';
  if(t.includes('Respond(context, request, true'))return 'Trả Created=true cùng ID/status/message của request vừa tạo.';
  if(t.startsWith('catch (FaultException'))return 'Bắt Organization Service fault có kiểu rõ ràng; chủ yếu xử lý collision của active key.';
  if(t.startsWith('//'))return 'Comment giải thích race condition; compiler không thực thi dòng này.';
  if(t.includes('if (winner == null) throw'))return 'Không tìm thấy winner nghĩa là fault không chắc do race; ném lại để không che lỗi thật.';
  if(t.includes('Respond(context, winner'))return 'Trả row thắng race với Created=false.';
  if(t.includes('InputParameters.Contains'))return 'Kiểm tra key tồn tại trước khi index dictionary để tránh KeyNotFound behavior.';
  if(t.includes('InputParameters["ClientRequestId"] == null'))return 'Coi explicit null giống như không truyền optional parameter.';
  if(t==='return null;')return 'Optional input không có giá trị: trả null để caller path sinh correlation GUID mới.';
  if(t.includes('as string'))return 'Cast an toàn sang string; type khác cho kết quả null và sẽ fail validation.';
  if(t==='Guid parsed;')return 'Khai báo biến nhận GUID sau khi Guid.TryParse thành công, tương thích C# của net48 project.';
  if(t.includes('string.IsNullOrWhiteSpace'))return 'Từ chối rỗng, quá 100 ký tự hoặc không parse được GUID.';
  if(t.includes('InvalidPluginExecutionException'))return 'Ném lỗi plug-in có chủ đích để caller nhận thông báo nghiệp vụ rõ ràng.';
  if(t.includes('BMS-SYNC-001'))return 'Mã lỗi ổn định giúp support tìm đúng validation rule.';
  if(t.includes('parsed.ToString("D")'))return 'Chuẩn hóa GUID về lowercase/hyphen format D để query nhất quán.';
  if(t.includes('new QueryExpression(RequestTable)'))return 'Tạo late-bound QueryExpression cho fmc_syncrequest.';
  if(t.includes('ColumnSet ='))return 'Chỉ lấy fmc_status vì Respond cần status; giảm dữ liệu trả về.';
  if(t.includes('TopCount = 1'))return 'Giới hạn một row vì caller chỉ cần biết request cần reuse.';
  if(t.includes('fmc_correlationid'))return 'Thêm điều kiện equality trên correlation ID đã chuẩn hóa.';
  if(t.includes('return First(service.RetrieveMultiple'))return 'Thực thi query đồng bộ trong transaction và chuyển collection thành Entity/null.';
  if(t.includes('"fmc_pipeline"'))return 'Lọc chính xác pipeline của organization hiện tại.';
  if(t.includes('ConditionOperator.In'))return 'Chỉ Queued và Running được coi là active; Completed/Failed không chặn request mới.';
  if(t.includes('OrderExpression'))return 'Nếu dữ liệu bất thường có nhiều row, ưu tiên row được tạo sớm nhất.';
  if(t.includes('rows.Entities.Count'))return 'Không có row thì null; có row thì lấy phần tử index 0.';
  if(t.includes('GetAttributeValue<OptionSetValue>'))return 'Đọc Choice status dưới dạng OptionSetValue; có thể null với Entity chưa hydrate.';
  if(t.includes('OutputParameters["RequestId"]'))return 'Gán Guid response property RequestId.';
  if(t.includes('OutputParameters["Created"]'))return 'Gán Boolean response property Created.';
  if(t.includes('OutputParameters["Status"]'))return 'Gán Integer status; fallback Queued khi Entity không có attribute.';
  if(t.includes('OutputParameters["Message"]'))return 'Gán String message mô tả nhánh create/reuse.';
  if(t.endsWith(');')||t.endsWith(';'))return 'Hoàn tất một statement C# trong khối '+n+'.';
  return 'Dòng '+n+' tổ chức cấu trúc C# của handler; xem các dòng liền kề để hiểu toàn bộ khối.';
}
const pluginLines=plugin.trimEnd().split('\n');
const annotations=pluginLines.map((line,index)=>({from:index+1,to:index+1,title:'Dòng '+(index+1),html:'<p>'+describeLine(line,index,pluginLines)+'</p>'}));

const resources=Object.fromEntries(Object.entries(sourceFiles).map(([path,text])=>[path,{name:basename(path),text,html:path.endsWith('.md')?rendererFor(path).parse(text):'<figure class="code-figure"><pre><code>'+highlight(text,languageOf(path))+'</code></pre></figure>'}]));
const data={version:'1.0.0',guidePath,receiptPath,sourcePath,chapters,snippets,resources,annotations,sourceHash:createHash('sha256').update(plugin).digest('hex'),guideHash:createHash('sha256').update(guide).digest('hex')};
const output='// Generated by npm run build from reviewed local sources. Do not hand-edit.\nwindow.CUSTOM_API_DOCS = '+JSON.stringify(data).replaceAll('<','\\u003c').replaceAll('\u2028','\\u2028').replaceAll('\u2029','\\u2029')+';\n';
await writeFile(resolve(here,'content.js'),output,'utf8');
console.log('Built '+chapters.length+' chapters, '+Object.keys(snippets).length+' code examples and '+annotations.length+' per-line explanations.');
