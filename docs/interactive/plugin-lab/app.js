(function() {
  'use strict';
  const D=window.PLUGIN_DOCS;
  const $=id=>document.getElementById(id);
  if(!D){$('article').textContent='Chưa có nội dung. Chạy npm run build trong docs/interactive/plugin-lab.';return;}
  const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const normalize=s=>s.toLocaleLowerCase('vi').normalize('NFD').replace(/[\u0300-\u036f]/g,'').replaceAll('đ','d');
  let current=1, currentCode='plugin', currentResource=null, toastTimer;
  const guid='11111111-1111-4111-8111-111111111111';
  const ref=()=>({logicalName:'fmc_bmsbuilding',id:guid});
  const scenarios={
    'create-valid':{message:'Create',table:'fmc_bmsequipment',data:{fmc_name:'Equipment demo',fmc_buildingid:ref()}},
    'create-missing':{message:'Create',table:'fmc_bmsequipment',data:{fmc_name:'Equipment demo'}},
    'create-null':{message:'Create',table:'fmc_bmsequipment',data:{fmc_name:'Equipment demo',fmc_buildingid:null}},
    'update-name':{message:'Update',table:'fmc_bmsequipment',data:{fmc_name:'Equipment renamed'}},
    'update-clear':{message:'Update',table:'fmc_bmsequipment',data:{fmc_buildingid:null}},
    'update-valid':{message:'Update',table:'fmc_bmsequipment',data:{fmc_buildingid:ref()}},
    'other-table':{message:'Create',table:'fmc_bmspoint',data:{fmc_name:'Point demo'}}
  };
  function toast(text){clearTimeout(toastTimer);$('toast').textContent=text;$('toast').hidden=false;toastTimer=setTimeout(()=>$('toast').hidden=true,2400);}
  async function copy(text){
    try{await navigator.clipboard.writeText(text);toast('Đã copy. Bạn có thể paste vào editor hoặc PowerShell.');}
    catch {
      const field=document.createElement('textarea');field.value=text;field.style.position='fixed';field.style.left='-9999px';document.body.appendChild(field);field.select();
      const success=document.execCommand('copy');field.remove();toast(success?'Đã copy.':'Trình duyệt không cho copy tự động. Hãy chọn code và copy thủ công.');
    }
  }
  function download(name,text){
    const url=URL.createObjectURL(new Blob([text],{type:'text/plain;charset=utf-8'}));
    const a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
  }
  function nav(search=''){
    const query=normalize(search.trim()), groups=new Map();
    for(const chapter of D.chapters){
      if(query&&!normalize(chapter.nav+' '+chapter.title+' '+chapter.searchText).includes(query))continue;
      if(!groups.has(chapter.group))groups.set(chapter.group,[]);
      groups.get(chapter.group).push(chapter);
    }
    $('navigation').innerHTML=[...groups].map(([group,chapters])=>'<div class="nav-group"><div class="nav-group-title">'+group+'</div>'+chapters.map(c=>'<a class="nav-link '+(c.number===current?'active':'')+'" href="#bai-'+c.number+'" '+(c.number===current?'aria-current="page"':'')+'><span class="num">'+String(c.number).padStart(2,'0')+'</span><span>'+c.nav+'</span></a>').join('')+'</div>').join('');
    $('no-results').hidden=groups.size!==0;
  }
  function setTab(tab){
    for(const name of ['code','lab','step']){
      $('tab-'+name).setAttribute('aria-selected',String(tab===name));$('panel-'+name).hidden=tab!==name;
    }
  }
  function explain(line){
    const found=currentCode==='plugin'?D.annotations.find(a=>line>=a.from&&line<=a.to):null;
    const snippet=D.snippets[currentCode];
    const raw=snippet.code.split('\n')[line-1]||'';
    let text;
    if(found)text=found.html;
    else if(snippet.lang==='powershell')text=raw.trim().startsWith('#')?'Comment để hướng dẫn người đọc; PowerShell không thực thi dòng này.':
      /--register-plugin|--enqueue|--process-command-once|--provision|POST|PATCH|DELETE|Invoke-BmsPluginDemoApi/.test(raw)?'Dòng này thuộc thao tác ghi hoặc gọi API. Đọc toàn bộ bước và xác nhận đúng target trước khi chạy trong PowerShell. Khung docs không tự thực thi lệnh.':
      'Dòng nằm trong một khối PowerShell. Copy cả block để giữ đủ biến, functions và kiểm tra lỗi; chỉ chạy trên máy khi đến bước tương ứng.';
    else if(snippet.lang==='xml')text='Cấu hình XML của project hoặc step đã export. Nội dung này mô tả cấu hình; sửa/copy XML local không tự thay đổi registration trên cloud.';
    else if(currentCode==='exercise')text='Bài tập RequireBuildingName: đọc fmc_name dưới dạng string và chặn null, chuỗi rỗng hoặc chỉ có khoảng trắng. Cần đăng ký steps trên fmc_bmsbuilding cho class mới.';
    else if(currentCode==='provisioner')text='Deployment helper chạy ngoài Dataverse. Nó dùng SDK để đọc/tạo/update assembly, plugin type, steps và solution membership; không phải business handler chạy trên cloud.';
    else text='Dòng '+line+' của '+esc(snippet.title)+'. Đọc phần hướng dẫn bên trái để hiểu bối cảnh; dòng trống và dấu ngoặc giúp tổ chức code.';
    $('line-explanation').innerHTML='<span class="tiny-label">DÒNG '+line+(found&&found.to!==found.from?' · NHÓM '+found.from+'–'+found.to:'')+'</span><p>'+text+'</p>';
    document.querySelectorAll('.code-line').forEach(el=>el.classList.toggle('selected',Number(el.dataset.line)===line));
  }
  function showCode(key){
    const snippet=D.snippets[key];if(!snippet)return;
    currentCode=key;
    if(![...$('code-file').options].some(o=>o.value===key))$('code-file').add(new Option(snippet.title,key));
    $('code-file').value=key;
    $('code-language').textContent=snippet.lang==='csharp'?'C#':snippet.lang.toUpperCase();
    $('code-origin').textContent=snippet.origin;
    $('line-count').textContent=snippet.lines.length+' dòng';
    $('code-lines').innerHTML=snippet.lines.map((html,i)=>'<button class="code-line" data-line="'+(i+1)+'" aria-label="Giải thích dòng '+(i+1)+'"><span class="line-num" aria-hidden="true">'+(i+1)+'</span><code>'+html+'</code></button>').join('');
    $('code-lines').scrollTop=0;
    if(key==='plugin')explain(9);
    else $('line-explanation').innerHTML='<span class="tiny-label">CODE CẠNH BÀI HỌC</span><p>'+esc(snippet.origin)+'. Chọn dòng để xem lưu ý, hoặc copy toàn bộ ví dụ.</p>';
  }
  function loadChapter(){
    const match=/^#bai-(\d+)$/.exec(location.hash);
    const n=match?Number(match[1]):1;
    current=n>=1&&n<=13?n:1;
    const chapter=D.chapters[current-1];
    $('lesson-title').innerHTML=chapter.headline;
    $('lesson-lead').textContent=chapter.lead;
    $('lesson-category').textContent=chapter.group;
    $('chapter-count').textContent='BÀI '+String(current).padStart(2,'0')+' / 13';
    $('breadcrumb-current').textContent=chapter.nav;
    document.title=chapter.nav+' · Plugin Handbook';
    $('lesson-intro').innerHTML=current===1?
      '<div class="quick-facts"><span><b>13</b> bài thực hành</span><span><b>58</b> dòng C# được giải thích</span><span><b>7</b> tình huống để thử</span></div>'+
      '<div class="roadmap" aria-label="Quy trình: viết code, build DLL, đăng ký steps, kiểm thử"><div><em>&lt;/&gt;</em><strong>Viết code</strong><small>Local .cs</small></div><div><em>◇</em><strong>Build DLL</strong><small>net48</small></div><div><em>↗</em><strong>Đăng ký</strong><small>Assembly + steps</small></div><div><em>✓</em><strong>Kiểm thử</strong><small>API + worker</small></div></div>'+
      '<div class="learning-note"><strong>Bài học dùng source thật của dự án.</strong><p>Rule mẫu: Equipment cần có Building. Đổi tên vẫn được; chủ động xóa Building sẽ bị chặn.</p></div>':
      current===4?'<div class="learning-note"><strong>Đọc code có người dẫn đường.</strong><p>Click dòng 9 để hiểu IPlugin, dòng 40 để hiểu Update, hoặc dòng 51 để hiểu cách chặn request.</p></div>':
      current===12?'<div class="learning-note"><strong>Bài tập mở rộng · chưa deploy.</strong><p>Code mới được build để kiểm tra cú pháp. Việc đăng ký và bật rule thật là bước thực hành riêng.</p></div>':'';
    $('article').innerHTML=chapter.html;
    $('code-file').innerHTML=chapter.snippets.map(key=>'<option value="'+key+'">'+esc(D.snippets[key].title)+'</option>').join('');
    showCode(chapter.snippets[0]);
    setTab(current===7?'step':current===10?'lab':'code');
    $('previous').disabled=current===1;$('next').disabled=current===13;
    $('previous-title').textContent=current===1?'Bạn đang ở bài đầu':D.chapters[current-2].nav;
    $('next-title').textContent=current===13?'Đã đến cuối hướng dẫn':D.chapters[current].nav;
    $('sidebar').classList.remove('open');$('menu-toggle').setAttribute('aria-expanded','false');
    nav($('search').value);
    window.scrollTo({top:0,behavior:'instant'});
    progress();
  }
  function progress(){
    const total=document.documentElement.scrollHeight-innerHeight;
    $('reading-progress').style.width=(total>0?Math.min(100,scrollY/total*100):100)+'%';
  }
  function result(answer){
    $('lab-result').className='lab-result '+answer.status;
    const labels={pass:'RULE PASS',block:'RULE REJECTED',skip:'KHÔNG GỌI / BỎ QUA',invalid:'KIỂM TRA INPUT'};
    $('lab-result').innerHTML='<div class="result-label">'+labels[answer.status]+'</div><h3>'+esc(answer.title)+'</h3><p>'+esc(answer.detail)+'</p>'+
      '<div class="result-path">'+answer.path.map(s=>'<span>'+esc(s)+'</span>').join('<b>→</b>')+'</div>'+
      (answer.lines.length?'<button class="text-button" data-show-line="'+answer.lines[0]+'" style="margin-top:10px">Xem dòng '+answer.lines.join(', ')+' trong code ↗</button>':'');
  }
  function runPayload(){
    let attributes;
    try{attributes=JSON.parse($('payload').value);}
    catch{result({status:'invalid',title:'JSON chưa hợp lệ',detail:'Kiểm tra dấu phẩy, dấu ngoặc và dấu nháy kép. JSON không nhận comment.',lines:[],path:['Editor','JSON parse error']});return;}
    result(PluginRuleModel.simulate({message:$('message').value,table:$('table').value,attributes}));
  }
  function loadScenario(){
    const s=scenarios[$('scenario').value];
    $('message').value=s.message;$('table').value=s.table;$('payload').value=JSON.stringify(s.data,null,2);runPayload();
  }
  function stepValues(){return {message:$('step-message').value,table:$('step-table').value.trim(),filter:$('step-filter').value,stage:Number($('step-stage').value),mode:Number($('step-mode').value),rank:Number($('step-order').value)};}
  function renderStep(){
    const v=stepValues(),issues=PluginRuleModel.checkStep(v);
    const stageName={10:'PreValidation',20:'PreOperation',40:'PostOperation'}[v.stage];
    $('step-result').className='step-result'+(issues.length?' warning':'');
    $('step-result').innerHTML=issues.length?'<strong>Cấu hình chưa khớp rule</strong><ul>'+issues.map(x=>'<li>'+esc(x)+'</li>').join('')+'</ul>':'<strong>✓ Cấu hình khớp rule Equipment → Building</strong><br>Đây là bản nháp để điền vào PRT; chưa đăng ký lên cloud.';
    $('step-preview').textContent=[
      'Name: BMS: Require Building on Equipment '+v.message,
      'Event Handler: FMCentralBms.Plugins.RequireEquipmentBuilding',
      'Message: '+v.message,'Primary Entity: '+v.table,'Filtering Attributes: '+(v.filter||'(trống)'),
      'Stage: '+stageName+' ('+v.stage+')','Mode: '+(v.mode===0?'Synchronous':'Asynchronous')+' ('+v.mode+')',
      'Execution Order: '+v.rank,'Context: Calling User','Deployment: Server','Images / Configuration: (trống)'
    ].join('\n');
  }
  function openResource(path,anchor=''){
    const resource=D.resources[path];if(!resource)return;
    currentResource=resource;
    $('dialog-title').textContent=resource.name;
    $('dialog-content').innerHTML=resource.html;
    if(!$('source-dialog').open)$('source-dialog').showModal();
    $('dialog-content').scrollTop=0;
    if(anchor){
      const target=[...$('dialog-content').querySelectorAll('[id]')].find(el=>el.id===anchor);
      if(target)target.scrollIntoView({block:'start'});
    }
  }
  document.addEventListener('click',event=>{
    const target=event.target.closest('button');if(!target)return;
    if(target.dataset.tab)setTab(target.dataset.tab);
    if(target.dataset.line)explain(Number(target.dataset.line));
    if(target.dataset.copy)copy(D.snippets[target.dataset.copy].code);
    if(target.dataset.snippet){
      if($('source-dialog').open)$('source-dialog').close();
      showCode(target.dataset.snippet);setTab('code');
      if(innerWidth<=1020)document.querySelector('.inspector').scrollIntoView({block:'start',behavior:'smooth'});
    }
    if(target.dataset.resource)openResource(target.dataset.resource,target.dataset.anchor);
    if(target.dataset.showLine){
      showCode('plugin');setTab('code');explain(Number(target.dataset.showLine));
      const line=$('code-lines').querySelector('[data-line="'+target.dataset.showLine+'"]');
      if(line)$('code-lines').scrollTop=line.offsetTop-$('code-lines').offsetTop-40;
    }
  });
  $('code-file').addEventListener('change',()=>showCode($('code-file').value));
  $('copy-code').addEventListener('click',()=>copy(D.snippets[currentCode].code));
  $('download-code').addEventListener('click',()=>{
    const snippet=D.snippets[currentCode];
    const names={'plugin':'RequireEquipmentBuilding.cs','project':'FMCentralBms.Plugins.csproj','exercise':'RequireBuildingName.cs','api-test':'Test-EquipmentBuildingPlugin.ps1','provisioner':'DataversePluginProvisioner.cs','program':'Program.cs'};
    const ext={powershell:'ps1',csharp:'cs',xml:'xml',text:'txt'}[snippet.lang]||'txt';
    // BOM makes downloaded PowerShell readable by Windows PowerShell 5.1.
    download(names[currentCode]||'example.'+ext,(snippet.lang==='powershell'?'\uFEFF':'')+snippet.code+'\n');
  });
  $('jump-lab').addEventListener('click',()=>document.querySelector('.inspector').scrollIntoView({block:'start',behavior:'smooth'}));
  $('back-to-lesson').addEventListener('click',()=>window.scrollTo({top:0,behavior:'smooth'}));
  document.querySelector('.tabs').addEventListener('keydown',event=>{
    const tabs=['code','lab','step'];
    const index=tabs.indexOf(event.target.dataset.tab);
    if(index<0||!['ArrowLeft','ArrowRight','Home','End'].includes(event.key))return;
    event.preventDefault();
    const next=event.key==='Home'?0:event.key==='End'?2:(index+(event.key==='ArrowRight'?1:2))%3;
    setTab(tabs[next]);$('tab-'+tabs[next]).focus();
  });
  $('open-lab').addEventListener('click',()=>setTab('lab'));
  $('scenario').addEventListener('change',loadScenario);
  $('reset-payload').addEventListener('click',loadScenario);
  $('run-payload').addEventListener('click',runPayload);
  $('message').addEventListener('change',runPayload);$('table').addEventListener('change',runPayload);
  $('payload').addEventListener('keydown',event=>{if((event.ctrlKey||event.metaKey)&&event.key==='Enter'){event.preventDefault();runPayload();}});
  for(const id of ['step-table','step-filter','step-stage','step-mode','step-order'])$(id).addEventListener('input',renderStep);
  $('step-message').addEventListener('change',()=>{$('step-filter').value=$('step-message').value==='Update'?'fmc_buildingid':'';renderStep();});
  $('copy-step').addEventListener('click',()=>copy($('step-preview').textContent));
  $('previous').addEventListener('click',()=>{if(current>1)location.hash='bai-'+(current-1);});
  $('next').addEventListener('click',()=>{if(current<13)location.hash='bai-'+(current+1);});
  $('search').addEventListener('input',()=>nav($('search').value));
  $('menu-toggle').addEventListener('click',()=>{const open=$('sidebar').classList.toggle('open');$('menu-toggle').setAttribute('aria-expanded',String(open));});
  $('theme-toggle').addEventListener('click',()=>{const dark=document.body.classList.toggle('dark');$('theme-toggle').setAttribute('aria-label',dark?'Chuyển giao diện sáng':'Chuyển giao diện tối');$('theme-toggle').title=dark?'Chuyển giao diện sáng':'Chuyển giao diện tối';});
  $('download-guide').addEventListener('click',()=>download('dataverse-plugin-step-by-step.vi.md',D.resources[D.guidePath].text));
  $('view-receipt').addEventListener('click',()=>openResource(D.planPath,'11-deployment-receipt--developer-environment-2026-09-10'));
  $('download-resource').addEventListener('click',()=>{if(currentResource)download(currentResource.name,currentResource.text);});
  $('close-dialog').addEventListener('click',()=>$('source-dialog').close());
  $('close-dialog-bottom').addEventListener('click',()=>$('source-dialog').close());
  document.addEventListener('keydown',event=>{
    if(event.key==='/'&&!/INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName)){
      event.preventDefault();if(innerWidth<=680){$('sidebar').classList.add('open');$('menu-toggle').setAttribute('aria-expanded','true');}$('search').focus();
    }
  });
  $('source-dialog').addEventListener('click',event=>{if(event.target===$('source-dialog'))$('source-dialog').close();});
  window.addEventListener('hashchange',loadChapter);
  window.addEventListener('scroll',progress,{passive:true});
  loadChapter();loadScenario();renderStep();
})();
