(function(){
  'use strict';
  const D=window.CUSTOM_API_DOCS;
  const $=id=>document.getElementById(id);
  if(!D){$('article').textContent='Chưa có content.js. Chạy npm run build trong docs/interactive/custom-api-lab.';return;}
  const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const normalize=s=>String(s).toLocaleLowerCase('vi').normalize('NFD').replace(/[\u0300-\u036f]/g,'').replaceAll('đ','d');
  const validGuid='11111111-1111-4111-8111-111111111111';
  let current=1,currentCode='plugin',currentResource=null,toastTimer;

  function toast(message){clearTimeout(toastTimer);$('toast').textContent=message;$('toast').hidden=false;toastTimer=setTimeout(()=>$('toast').hidden=true,2200);}
  async function copy(text){
    try{await navigator.clipboard.writeText(text);toast('Đã copy vào clipboard.');}
    catch{const field=document.createElement('textarea');field.value=text;field.style.position='fixed';field.style.left='-9999px';document.body.appendChild(field);field.select();const ok=document.execCommand('copy');field.remove();toast(ok?'Đã copy.':'Hãy chọn code và copy thủ công.');}
  }
  function download(name,text){const url=URL.createObjectURL(new Blob([text],{type:'text/plain;charset=utf-8'}));const a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}

  function renderNavigation(search=''){
    const query=normalize(search.trim()),groups=new Map();
    for(const chapter of D.chapters){
      if(query&&!normalize(chapter.nav+' '+chapter.title+' '+chapter.searchText).includes(query))continue;
      if(!groups.has(chapter.group))groups.set(chapter.group,[]);
      groups.get(chapter.group).push(chapter);
    }
    $('navigation').innerHTML=[...groups].map(([group,chapters])=>'<div class="nav-group"><div class="nav-group-title">'+esc(group)+'</div>'+chapters.map(ch=>'<a class="nav-link '+(ch.number===current?'active':'')+'" href="#bai-'+ch.number+'" '+(ch.number===current?'aria-current="page"':'')+'><span class="num">'+String(ch.number).padStart(2,'0')+'</span><span>'+esc(ch.nav)+'</span></a>').join('')+'</div>').join('');
    $('no-results').hidden=groups.size!==0;
  }
  function setTab(tab){for(const name of ['code','lab','contract']){$('tab-'+name).setAttribute('aria-selected',String(name===tab));$('panel-'+name).hidden=name!==tab;}}
  function explain(line){
    const snippet=D.snippets[currentCode];
    const note=currentCode==='plugin'?D.annotations[line-1]:null;
    const raw=snippet.code.split('\n')[line-1]||'';
    let html;
    if(note)html=note.html;
    else if(snippet.lang==='powershell')html='<p>'+(raw.trim().startsWith('#')?'Comment hướng dẫn; PowerShell không chạy dòng này.':'Dòng thuộc script deploy/verify/test. Đọc mode và target trước khi chạy vì một số nhánh có thể ghi cloud.')+'</p>';
    else if(snippet.lang==='xml'||snippet.lang==='json')html='<p>Đây là metadata solution export. File local mô tả contract đã export; tự sửa file không thay đổi Dataverse live.</p>';
    else if(currentCode==='ui')html='<p>Đây là JavaScript của web resource chạy trong Model-driven app. Nó dùng session hiện tại để gọi Dataverse Web API.</p>';
    else if(currentCode==='provisioner')html='<p>Deployment helper chạy từ máy developer để tạo/update metadata. Nó không phải handler chạy mỗi lần người dùng bấm nút.</p>';
    else html='<p>Dòng '+line+' của '+esc(snippet.title)+'. Đọc block đầy đủ và phần bài học bên trái để hiểu bối cảnh.</p>';
    $('line-explanation').innerHTML='<span class="tiny-label">DÒNG '+line+(currentCode==='plugin'?' / '+D.annotations.length:'')+'</span>'+html;
    document.querySelectorAll('.code-line').forEach(el=>el.classList.toggle('selected',Number(el.dataset.line)===line));
  }
  function showCode(key){
    const snippet=D.snippets[key];if(!snippet)return;
    currentCode=key;
    if(![...$('code-file').options].some(option=>option.value===key))$('code-file').add(new Option(snippet.title,key));
    $('code-file').value=key;$('code-language').textContent={csharp:'C#',javascript:'JS',powershell:'PS',json:'JSON',xml:'XML',text:'TEXT'}[snippet.lang]||snippet.lang.toUpperCase();
    $('code-origin').textContent=snippet.origin;$('line-count').textContent=snippet.lines.length+' dòng';
    $('code-lines').innerHTML=snippet.lines.map((html,index)=>'<button class="code-line" data-line="'+(index+1)+'" aria-label="Giải thích dòng '+(index+1)+'"><span class="line-num" aria-hidden="true">'+(index+1)+'</span><code>'+html+'</code></button>').join('');
    $('code-lines').scrollTop=0;explain(currentCode==='plugin'?19:1);
  }
  function progress(){const total=document.documentElement.scrollHeight-innerHeight;$('reading-progress').style.width=(total>0?Math.min(100,scrollY/total*100):100)+'%';}
  function loadChapter(){
    const match=/^#bai-(\d+)$/.exec(location.hash),number=match?Number(match[1]):1;
    current=number>=1&&number<=13?number:1;
    const chapter=D.chapters[current-1];
    $('lesson-title').innerHTML=chapter.headline;$('lesson-lead').textContent=chapter.lead;$('lesson-category').textContent=chapter.group;$('chapter-count').textContent='BÀI '+String(current).padStart(2,'0')+' / 13';$('breadcrumb-current').textContent=chapter.nav;document.title=chapter.nav+' · Custom API Handbook';
    $('lesson-intro').innerHTML=current===1?'<div class="quick-facts"><span><b>13</b> bài thực hành</span><span><b>'+D.annotations.length+'</b> dòng C# có giải thích</span><span><b>6</b> request scenarios</span></div><div class="roadmap" aria-label="Luồng Custom API"><div><em>UI</em><strong>Nút bấm</strong><small>POST action</small></div><div><em>API</em><strong>Contract</strong><small>Input/output</small></div><div><em>C#</em><strong>Handler</strong><small>Create/reuse</small></div><div><em>Q</em><strong>Worker</strong><small>SQL sync</small></div></div><div class="learning-note"><strong>Không gọi cloud.</strong><p>Simulator, code inspector và contract designer chạy hoàn toàn trong browser.</p></div>':current===4?'<div class="learning-note"><strong>Click bất kỳ dòng nào.</strong><p>Lab giải thích đủ source hiện tại, kể cả import, dấu ngoặc, query, trace và output.</p></div>':current===13?'<div class="learning-note"><strong>Thiết kế contract trước khi deploy.</strong><p>Mở tab “Thiết kế contract”, thử đổi Action thành Function hoặc bỏ output để thấy lỗi thiết kế.</p></div>':'';
    $('article').innerHTML=chapter.html;
    $('code-file').innerHTML=chapter.snippets.map(key=>'<option value="'+esc(key)+'">'+esc(D.snippets[key].title)+'</option>').join('');
    showCode(chapter.snippets[0]);setTab(current===5?'lab':current===13?'contract':'code');
    $('previous').disabled=current===1;$('next').disabled=current===13;$('previous-title').textContent=current===1?'Bạn đang ở bài đầu':D.chapters[current-2].nav;$('next-title').textContent=current===13?'Đã đến cuối hướng dẫn':D.chapters[current].nav;
    $('sidebar').classList.remove('open');$('menu-toggle').setAttribute('aria-expanded','false');renderNavigation($('search').value);window.scrollTo({top:0,behavior:'instant'});progress();
  }

  const scenarios={
    new:{id:validGuid,same:false,active:false,race:false},
    retry:{id:validGuid,same:true,active:false,race:false},
    active:{id:'22222222-2222-4222-8222-222222222222',same:false,active:true,race:false},
    race:{id:'33333333-3333-4333-8333-333333333333',same:false,active:false,race:true},
    invalid:{id:'not-a-guid',same:false,active:false,race:false},
    optional:{id:'',same:false,active:false,race:false}
  };
  function loadScenario(){const item=scenarios[$('scenario').value];$('client-request-id').value=item.id;$('same-correlation').value=String(item.same);$('active-request').value=String(item.active);$('race').value=String(item.race);runRequest();}
  function runRequest(){
    const answer=CustomApiModel.simulate({clientRequestId:$('client-request-id').value||null,sameCorrelation:$('same-correlation').value==='true',activeRequest:$('active-request').value==='true',race:$('race').value==='true'});
    const css=answer.status==='invalid'?'invalid':answer.status==='created'?'pass':'skip';
    $('lab-result').className='lab-result '+css;
    $('lab-result').innerHTML='<div class="result-label">HTTP '+answer.code+' · '+esc(answer.status.toUpperCase())+'</div><h3>'+esc(answer.title)+'</h3><p>'+esc(answer.detail)+'</p>'+(answer.requestId?'<p><b>RequestId:</b> <code>'+esc(answer.requestId)+'</code><br><b>Created:</b> '+answer.created+'<br><b>Status:</b> '+CustomApiModel.QUEUED+'</p>':'')+'<div class="result-path">'+answer.trace.map(marker=>'<span>'+esc(marker)+'</span>').join('<b>→</b>')+'</div><button class="text-button" data-show-line="'+answer.lines[0]+'" style="margin-top:10px">Xem dòng '+answer.lines.join(', ')+' trong handler ↗</button>';
  }
  function contractValues(){return {name:$('api-name').value.trim(),kind:$('api-kind').value,binding:$('api-binding').value,processing:$('api-processing').value,privilege:$('api-privilege').value.trim(),input:$('api-input').value,output:$('api-output').value};}
  function renderContract(){
    const value=contractValues(),issues=CustomApiModel.checkContract(value);
    $('contract-result').className='step-result'+(issues.length?' warning':'');$('contract-result').innerHTML=issues.length?'<strong>Contract chưa khớp demo</strong><ul>'+issues.map(issue=>'<li>'+esc(issue)+'</li>').join('')+'</ul>':'<strong>✓ Contract nhất quán với fmc_RequestBmsSync</strong><br>Đây là bản nháp local; chưa tạo metadata trên Dataverse.';
    $('contract-preview').textContent=['Unique Name: '+value.name,'Binding: '+({0:'Global',1:'Entity',2:'Entity Collection'}[value.binding]),'Operation: '+(value.kind==='action'?'Action · POST':'Function · GET'),'Allowed processing: '+({0:'None',1:'Async Only',2:'Sync and Async'}[value.processing]),'Workflow enabled: true','Private: false','Execute privilege: '+value.privilege,'Input: '+value.input,'Outputs:',...value.output.split('\n').map(line=>'  - '+line)].join('\n');
  }
  function openResource(path,anchor=''){const resource=D.resources[path];if(!resource)return;currentResource=resource;$('dialog-title').textContent=resource.name;$('dialog-content').innerHTML=resource.html;if(!$('source-dialog').open)$('source-dialog').showModal();$('dialog-content').scrollTop=0;if(anchor){const target=[...$('dialog-content').querySelectorAll('[id]')].find(node=>node.id===anchor);if(target)target.scrollIntoView({block:'start'});}}

  document.addEventListener('click',event=>{
    const button=event.target.closest('button');if(!button)return;
    if(button.dataset.tab)setTab(button.dataset.tab);
    if(button.dataset.line)explain(Number(button.dataset.line));
    if(button.dataset.copy)copy(D.snippets[button.dataset.copy].code);
    if(button.dataset.snippet){if($('source-dialog').open)$('source-dialog').close();showCode(button.dataset.snippet);setTab('code');if(innerWidth<=1020)document.querySelector('.inspector').scrollIntoView({block:'start',behavior:'smooth'});}
    if(button.dataset.resource)openResource(button.dataset.resource,button.dataset.anchor);
    if(button.dataset.showLine){showCode('plugin');setTab('code');const number=Number(button.dataset.showLine);explain(number);const line=$('code-lines').querySelector('[data-line="'+number+'"]');if(line)$('code-lines').scrollTop=line.offsetTop-$('code-lines').offsetTop-40;}
  });
  $('code-file').addEventListener('change',()=>showCode($('code-file').value));$('copy-code').addEventListener('click',()=>copy(D.snippets[currentCode].code));
  $('download-code').addEventListener('click',()=>{const snippet=D.snippets[currentCode],extension={csharp:'cs',javascript:'js',powershell:'ps1',json:'json',xml:'xml',text:'txt'}[snippet.lang]||'txt';download(snippet.title.replace(/[^A-Za-z0-9_.-]/g,'-')||('example.'+extension),(snippet.lang==='powershell'?'\uFEFF':'')+snippet.code+'\n');});
  $('open-lab').addEventListener('click',()=>setTab('lab'));$('scenario').addEventListener('change',loadScenario);$('run-request').addEventListener('click',runRequest);
  for(const id of ['client-request-id','same-correlation','active-request','race'])$(id).addEventListener('input',runRequest);
  for(const id of ['api-name','api-kind','api-binding','api-processing','api-privilege','api-input','api-output'])$(id).addEventListener('input',renderContract);
  $('copy-contract').addEventListener('click',()=>copy($('contract-preview').textContent));
  $('previous').addEventListener('click',()=>{if(current>1)location.hash='bai-'+(current-1);});$('next').addEventListener('click',()=>{if(current<13)location.hash='bai-'+(current+1);});$('search').addEventListener('input',()=>renderNavigation($('search').value));
  $('menu-toggle').addEventListener('click',()=>{const open=$('sidebar').classList.toggle('open');$('menu-toggle').setAttribute('aria-expanded',String(open));});$('theme-toggle').addEventListener('click',()=>{const dark=document.body.classList.toggle('dark');$('theme-toggle').setAttribute('aria-label',dark?'Chuyển giao diện sáng':'Chuyển giao diện tối');});
  $('download-guide').addEventListener('click',()=>download('custom-api-step-by-step.vi.md',D.resources[D.guidePath].text));$('view-receipt').addEventListener('click',()=>openResource(D.receiptPath));$('download-resource').addEventListener('click',()=>{if(currentResource)download(currentResource.name,currentResource.text);});$('close-dialog').addEventListener('click',()=>$('source-dialog').close());$('close-dialog-bottom').addEventListener('click',()=>$('source-dialog').close());$('source-dialog').addEventListener('click',event=>{if(event.target===$('source-dialog'))$('source-dialog').close();});
  $('jump-lab').addEventListener('click',()=>document.querySelector('.inspector').scrollIntoView({block:'start',behavior:'smooth'}));$('back-to-lesson').addEventListener('click',()=>window.scrollTo({top:0,behavior:'smooth'}));
  document.querySelector('.tabs').addEventListener('keydown',event=>{const tabs=['code','lab','contract'],index=tabs.indexOf(event.target.dataset.tab);if(index<0||!['ArrowLeft','ArrowRight','Home','End'].includes(event.key))return;event.preventDefault();const next=event.key==='Home'?0:event.key==='End'?2:(index+(event.key==='ArrowRight'?1:2))%3;setTab(tabs[next]);$('tab-'+tabs[next]).focus();});
  document.addEventListener('keydown',event=>{if(event.key==='/'&&!/INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName)){event.preventDefault();if(innerWidth<=680)$('sidebar').classList.add('open');$('search').focus();}});
  window.addEventListener('hashchange',loadChapter);window.addEventListener('scroll',progress,{passive:true});
  loadChapter();loadScenario();renderContract();
})();
