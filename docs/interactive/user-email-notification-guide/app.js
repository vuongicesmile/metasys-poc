(function(){
  'use strict';
  const D=window.EMAIL_NOTIFICATION_GUIDE;
  const $=id=>document.getElementById(id);
  const esc=value=>String(value).replace(/[&<>"']/g,char=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[char]));
  const normalize=value=>String(value).toLocaleLowerCase('vi').normalize('NFD').replace(/[\u0300-\u036f]/g,'').replaceAll('đ','d');
  const storageKey='fmc.email-notification-guide.checklist';
  let current=1,toastTimer;

  function toast(message){clearTimeout(toastTimer);$('toast').textContent=message;$('toast').hidden=false;toastTimer=setTimeout(()=>$('toast').hidden=true,1800);}
  async function copy(text){try{await navigator.clipboard.writeText(text);toast('Đã copy expression.');}catch{const input=document.createElement('textarea');input.value=text;input.style.position='fixed';input.style.left='-9999px';document.body.appendChild(input);input.select();document.execCommand('copy');input.remove();toast('Đã copy.');}}

  function renderNavigation(query=''){
    const value=normalize(query.trim());
    const groups=new Map();
    D.chapters.forEach(chapter=>{
      if(value&&!normalize(chapter.nav+' '+chapter.headline+' '+chapter.keywords).includes(value))return;
      if(!groups.has(chapter.group))groups.set(chapter.group,[]);
      groups.get(chapter.group).push(chapter);
    });
    $('navigation').innerHTML=[...groups].map(([group,items])=>'<div class="nav-group"><div class="nav-group-title">'+esc(group)+'</div>'+items.map(item=>'<a class="nav-link '+(item.number===current?'active':'')+'" href="#buoc-'+item.number+'" '+(item.number===current?'aria-current="page"':'')+'><span class="num">'+String(item.number).padStart(2,'0')+'</span><span>'+esc(item.nav)+'</span></a>').join('')+'</div>').join('');
    $('no-results').hidden=groups.size>0;
  }

  function loadChapter(){
    const match=/^#buoc-(\d+)$/.exec(location.hash);
    const requested=match?Number(match[1]):1;
    current=requested>=1&&requested<=D.chapters.length?requested:1;
    const chapter=D.chapters[current-1];
    $('lesson-category').textContent=chapter.category;
    $('chapter-count').textContent='BƯỚC '+String(current).padStart(2,'0')+' / '+String(D.chapters.length).padStart(2,'0');
    $('lesson-title').innerHTML=chapter.headline;
    $('lesson-lead').textContent=chapter.lead;
    $('breadcrumb-current').textContent=chapter.nav;
    $('article').innerHTML=chapter.html;
    $('lesson-intro').innerHTML=current===1?'<div class="quick-facts"><span><b>8</b> bước triển khai</span><span><b>15</b> business columns</span><span><b>4</b> delivery states</span></div><div class="roadmap" aria-label="Luồng notification"><div><em>01</em><strong>Sync</strong><small>Terminal state</small></div><div><em>02</em><strong>Outbox</strong><small>Pending row</small></div><div><em>03</em><strong>Flow</strong><small>SendEmailV2</small></div><div><em>04</em><strong>Receipt</strong><small>Sent / Failed</small></div></div>':'';
    $('previous').disabled=current===1;
    $('next').disabled=current===D.chapters.length;
    $('previous-title').textContent=current===1?'Bạn đang ở bước đầu':D.chapters[current-2].nav;
    $('next-title').textContent=current===D.chapters.length?'Hoàn tất hướng dẫn':D.chapters[current].nav;
    document.title=chapter.nav+' · Email Notifications';
    $('sidebar').classList.remove('open');$('menu-toggle').setAttribute('aria-expanded','false');
    renderNavigation($('search').value);
    window.scrollTo({top:0,behavior:'instant'});
    updateReadingProgress();
  }

  function setTab(name){
    ['flow','schema','checklist'].forEach(tab=>{
      $('tab-'+tab).setAttribute('aria-selected',String(tab===name));
      $('panel-'+tab).hidden=tab!==name;
    });
  }

  function renderFlow(){
    $('flow-stack').innerHTML=D.flowNodes.map((node,index)=>'<button type="button" class="flow-node '+(index===0?'active':'')+'" data-flow="'+esc(node.id)+'"><span class="flow-index">'+String(index+1).padStart(2,'0')+'</span><span><strong>'+esc(node.label)+'</strong><small>'+esc(node.meta)+'</small></span></button>'+(index<D.flowNodes.length-1?'<span class="flow-arrow" aria-hidden="true">↓</span>':'')).join('');
    showFlow(D.flowNodes[0].id);
  }
  function showFlow(id){
    const node=D.flowNodes.find(item=>item.id===id);if(!node)return;
    document.querySelectorAll('.flow-node').forEach(button=>button.classList.toggle('active',button.dataset.flow===id));
    $('flow-detail').innerHTML='<span class="tiny-label">'+esc(node.meta)+'</span><p>'+esc(node.detail)+'</p>';
  }

  function renderSchema(query=''){
    const value=normalize(query.trim());
    const rows=D.schema.filter(row=>!value||normalize(row.join(' ')).includes(value));
    $('schema-list').innerHTML=rows.map(row=>'<div class="schema-row"><code>'+esc(row[0])+'</code><span>'+esc(row[1])+'</span><small>'+esc(row[2])+'</small></div>').join('');
    $('schema-count').textContent=rows.length+' / '+D.schema.length+' columns';
  }

  function checklistState(){try{const value=JSON.parse(localStorage.getItem(storageKey)||'[]');return Array.isArray(value)?value:[];}catch{return [];}}
  function renderChecklist(){
    const selected=new Set(checklistState());
    $('checklist').innerHTML=D.checklist.map((item,index)=>'<label class="check-row"><input type="checkbox" value="'+index+'" '+(selected.has(index)?'checked':'')+'><span><b>'+String(index+1).padStart(2,'0')+'</b>'+esc(item)+'</span></label>').join('');
    updateChecklist();
  }
  function updateChecklist(){
    const checked=[...$('checklist').querySelectorAll('input:checked')].map(input=>Number(input.value));
    try{localStorage.setItem(storageKey,JSON.stringify(checked));}catch{}
    $('check-count').textContent=checked.length+' / '+D.checklist.length;
    $('check-progress-bar').style.width=(checked.length/D.checklist.length*100)+'%';
  }

  function updateReadingProgress(){const total=document.documentElement.scrollHeight-innerHeight;$('reading-progress').style.width=(total>0?Math.min(100,scrollY/total*100):100)+'%';}

  document.addEventListener('click',event=>{
    const button=event.target.closest('button');if(!button)return;
    if(button.dataset.tab)setTab(button.dataset.tab);
    if(button.dataset.flow)showFlow(button.dataset.flow);
    if(button.dataset.copy)copy(decodeURIComponent(button.dataset.copy));
  });
  $('previous').addEventListener('click',()=>{if(current>1)location.hash='buoc-'+(current-1);});
  $('next').addEventListener('click',()=>{if(current<D.chapters.length)location.hash='buoc-'+(current+1);});
  $('search').addEventListener('input',()=>renderNavigation($('search').value));
  $('schema-search').addEventListener('input',()=>renderSchema($('schema-search').value));
  $('checklist').addEventListener('change',updateChecklist);
  $('reset-checklist').addEventListener('click',()=>{try{localStorage.removeItem(storageKey);}catch{}renderChecklist();});
  $('menu-toggle').addEventListener('click',()=>{const open=$('sidebar').classList.toggle('open');$('menu-toggle').setAttribute('aria-expanded',String(open));});
  $('theme-toggle').addEventListener('click',()=>{const dark=document.body.classList.toggle('dark');try{localStorage.setItem('fmc.email-guide.theme',dark?'dark':'light');}catch{}$('theme-toggle').setAttribute('aria-label',dark?'Chuyển giao diện sáng':'Chuyển giao diện tối');});
  document.querySelector('.tabs').addEventListener('keydown',event=>{const names=['flow','schema','checklist'],index=names.indexOf(event.target.dataset.tab);if(index<0||!['ArrowLeft','ArrowRight','Home','End'].includes(event.key))return;event.preventDefault();const next=event.key==='Home'?0:event.key==='End'?2:(index+(event.key==='ArrowRight'?1:2))%3;setTab(names[next]);$('tab-'+names[next]).focus();});
  document.addEventListener('keydown',event=>{if(event.key==='/'&&!/INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName)){event.preventDefault();if(innerWidth<=680)$('sidebar').classList.add('open');$('search').focus();}});
  window.addEventListener('hashchange',loadChapter);
  window.addEventListener('scroll',updateReadingProgress,{passive:true});

  try{if(localStorage.getItem('fmc.email-guide.theme')==='dark')document.body.classList.add('dark');}catch{}
  renderFlow();renderSchema();renderChecklist();loadChapter();
})();
