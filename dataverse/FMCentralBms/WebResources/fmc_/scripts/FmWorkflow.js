(function (root) {
  'use strict';
  const statuses = {789141000:'Draft',789141001:'Submitted (legacy)',789141002:'In Approval',789141003:'Approved',789141004:'Rejected',789141005:'Closed'};
  const types = {789140000:'CIWG',789140001:'Risk',789140002:'Project'};
  const cleanId = value => String(value || '').replace(/[{}]/g, '').toLowerCase();
  const quote = value => String(value).replace(/'/g, "''");
  const allowed = (r, user, email) => ({Submit:r.fmc_requeststatus===789141000 && cleanId(r._ownerid_value)===cleanId(user),
    Approve:r.fmc_requeststatus===789141002 && String(r.fmc_currentapproveremail||'').toLowerCase()===email.toLowerCase(),
    Reject:r.fmc_requeststatus===789141002 && String(r.fmc_currentapproveremail||'').toLowerCase()===email.toLowerCase(),
    Close:[789141003,789141004].includes(r.fmc_requeststatus) && cleanId(r._ownerid_value)===cleanId(user)});
  function heatmap(rows) {
    const cells = Array.from({length:5},()=>Array(5).fill(0));
    for (const r of rows) if (r.fmc_likelihood>=1 && r.fmc_likelihood<=5 && r.fmc_impact>=1 && r.fmc_impact<=5) cells[r.fmc_likelihood-1][r.fmc_impact-1]++;
    return cells;
  }
  if (typeof module !== 'undefined') module.exports = {allowed, heatmap, quote, cleanId};
  if (!root.document) return;
  const $ = id => document.getElementById(id);
  const element = (tag, text, cls) => {const e=document.createElement(tag);if(text!==undefined)e.textContent=text;if(cls)e.className=cls;return e;};
  let xrm, context, user, email, selected, rows=[], next=null, view='mine', busy=false, embedded=false;
  const fields='fmc_fmrequestid,fmc_name,fmc_description,fmc_requesttype,fmc_requeststatus,fmc_department,fmc_currentapproveremail,fmc_currentstep,fmc_stepdueon,fmc_workflowrevision,_ownerid_value,fmc_likelihood,fmc_impact,fmc_mitigation,fmc_reviewdate,fmc_riskseverity';
  const date = value => value ? new Date(value).toLocaleString() : '—';
  function notice(message,error=false){$('notice').hidden=!message;$('notice').textContent=message;$('notice').className=error?'error':'';}
  async function api(path,body){
    const url = new URL(path.startsWith('http')?path:context.getClientUrl()+'/api/data/v9.2/'+path);
    if(url.origin!==new URL(context.getClientUrl()).origin || !url.pathname.startsWith('/api/data/v9.2/'))throw Error('Unexpected Dataverse URL');
    const response=await fetch(url,{method:body?'POST':'GET',credentials:'same-origin',headers:{Accept:'application/json','Content-Type':'application/json','OData-Version':'4.0',Prefer:'odata.maxpagesize=50'},body:body?JSON.stringify(body):undefined});
    const text=await response.text();let result={};try{result=text?JSON.parse(text):{};}catch{throw Error('Dataverse returned an unreadable response. Refresh and retry.');}
    if(!response.ok)throw Error(result.error?.message||'Dataverse request failed ('+response.status+')');return result;
  }
  async function safe(action){if(busy)return;busy=true;notice('Đang xử lý…');document.querySelectorAll('button').forEach(b=>b.disabled=true);try{await action();notice('');}catch(e){notice(e.message,true);}finally{busy=false;document.querySelectorAll('button').forEach(b=>b.disabled=false);}}
  function openForm(id){return xrm.Navigation.openForm({entityName:'fmc_fmrequest',entityId:id});}
  async function loadList(append=false){
    selected=null;$('back').hidden=true;
    let filter=view==='mine'?`_ownerid_value eq ${user}`:view==='pending'?`fmc_requeststatus eq 789141002 and fmc_currentapproveremail eq '${quote(email)}'`:'fmc_requesttype eq 789140001 and fmc_requeststatus ne 789141005';
    const result=await api(append?next:`fmc_fmrequests?$select=${fields}&$filter=${encodeURIComponent(filter)}&$orderby=createdon desc`);
    rows=append?rows.concat(result.value):result.value;next=result['@odata.nextLink']||null;$('more').hidden=!next;
    const main=$('content');main.replaceChildren();
    if(view==='risk'){
      const card=element('section',undefined,'card');card.append(element('h2','Risk heatmap'));
      card.append(element('p',`Likelihood × Impact · ${rows.length} request đã tải${next?' (chưa phải toàn bộ)':''}. Số trong ô = số request.`, 'muted'));
      const grid=element('div',undefined,'heatmap'), cells=heatmap(rows);
      for(let l=5;l>=1;l--){grid.append(element('span',String(l),'axis'));for(let i=1;i<=5;i++){const cell=element('span',String(cells[l-1][i-1]));cell.style.background=l*i>=16?'#f9b9ba':l*i>=9?'#f9dcac':'#d6eee2';cell.title=`Likelihood ${l}, Impact ${i}`;grid.append(cell);}}
      grid.append(element('span','L / I','axis'));for(let i=1;i<=5;i++)grid.append(element('span',String(i),'axis'));card.append(grid);main.append(card);
    }
    const card=element('section',undefined,'card table-wrap'),table=element('table'),thead=element('thead'),tr=element('tr');
    for(const text of ['Request','Loại','Trạng thái','Bước / Người duyệt','Hạn'])tr.append(element('th',text));thead.append(tr);table.append(thead);
    const tbody=element('tbody');for(const r of rows){const row=element('tr'),name=element('td'),b=element('button',r.fmc_name,'link');b.onclick=()=>safe(()=>loadRecord(r.fmc_fmrequestid));name.append(b);row.append(name,element('td',types[r.fmc_requesttype]||'—'),element('td',statuses[r.fmc_requeststatus]||'Chưa có trạng thái'),element('td',(r.fmc_currentstep||'—')+' / '+(r.fmc_currentapproveremail||'—')),element('td',date(r.fmc_stepdueon)));tbody.append(row);}table.append(tbody);card.append(table);if(!rows.length)card.append(element('div','Không có request trong danh sách này.','empty'));main.append(card);
  }
  async function loadRecord(id){
    const r=await api(`fmc_fmrequests(${cleanId(id)})?$select=${fields}`);selected=r;$('more').hidden=true;$('back').hidden=embedded;
    const main=$('content');main.replaceChildren();const card=element('section',undefined,'card');card.append(element('h2',r.fmc_name),element('p',r.fmc_description||'Chưa có mô tả.'));
    const meta=element('div',undefined,'meta');for(const [label,value]of [['Trạng thái',statuses[r.fmc_requeststatus]],['Bước',r.fmc_currentstep||'—'],['Người duyệt',r.fmc_currentapproveremail||'—'],['Hạn',date(r.fmc_stepdueon)]]){const item=element('div',label,'muted');item.append(element('b',value));meta.append(item);}card.append(meta);
    const toolbar=element('div',undefined,'toolbar'),edit=element('button','Mở form / sửa Draft');edit.onclick=()=>safe(()=>openForm(r.fmc_fmrequestid));toolbar.append(edit);card.append(toolbar);
    const actions=allowed(r,user,email),comment=element('textarea');comment.placeholder='Nhận xét quyết định (bắt buộc khi approve, reject, close)';comment.maxLength=4000;comment.setAttribute('aria-label','Nhận xét quyết định');card.append(comment);
    const buttons=element('div',undefined,'toolbar');for(const [action,label]of [['Submit','Gửi phê duyệt'],['Approve','Duyệt bước này'],['Reject','Từ chối'],['Close','Đóng request']])if(actions[action]){const b=element('button',label,action==='Reject'?'danger':'primary');b.onclick=()=>safe(async()=>{
      if(action!=='Submit'&&!comment.value.trim())throw Error('Vui lòng nhập nhận xét.');
      const answer=await xrm.Navigation.openConfirmDialog({text:`${label}: ${r.fmc_name}?`,title:'Xác nhận quyết định'});if(!answer.confirmed)return;
      const key='fmc-workflow:'+user+':'+r.fmc_fmrequestid;const signature=JSON.stringify([action,r.fmc_workflowrevision||0,comment.value]);let saved;try{saved=JSON.parse(sessionStorage.getItem(key));}catch{/* old value */}
      if(!saved||saved.signature!==signature){saved={signature,id:crypto.randomUUID()};sessionStorage.setItem(key,JSON.stringify(saved));}
      await api('fmc_TransitionFmRequest',{RequestId:r.fmc_fmrequestid,Action:action,Comment:comment.value,ExpectedRevision:r.fmc_workflowrevision||0,OperationId:saved.id});
      sessionStorage.removeItem(key);await loadRecord(r.fmc_fmrequestid);
    });buttons.append(b);}card.append(buttons);if(!Object.values(actions).some(Boolean))card.append(element('p','Không có quyết định dành cho bạn ở trạng thái hiện tại.','muted'));main.append(card);
    const history=element('section',undefined,'card');history.append(element('h2','Lịch sử xử lý'));main.append(history);
    let historyNext=`fmc_requesthistories?$select=fmc_command,fmc_actoremail,fmc_actedon,fmc_comments,fmc_stepno&$filter=_fmc_requestid_value eq ${r.fmc_fmrequestid}&$orderby=fmc_actedon desc`;
    const timeline=element('ol',undefined,'timeline'),more=element('button','Xem thêm lịch sử');history.append(timeline,more);
    async function loadHistory(){const page=await api(historyNext);for(const event of page.value){const li=element('li');li.append(element('strong',`${event.fmc_command||'Event'} · Bước ${event.fmc_stepno||'—'}`),element('p',`${event.fmc_actoremail||'—'} · ${date(event.fmc_actedon)}`,'muted'),element('p',event.fmc_comments||'—'));timeline.append(li);}historyNext=page['@odata.nextLink'];more.hidden=!historyNext;if(!timeline.children.length)timeline.append(element('li','Chưa có lịch sử.'));}
    more.onclick=()=>safe(loadHistory);await loadHistory();
  }
  async function init(){
    xrm=root.parent?.Xrm||root.Xrm;context=typeof GetGlobalContext==='function'?GetGlobalContext():xrm?.Utility.getGlobalContext();
    if(!xrm||!context)throw Error('Mở trang này trong FMC BMS Demo để sử dụng tài khoản Power Apps.');
    user=cleanId(context.userSettings.userId);const profile=await api(`systemusers(${user})?$select=internalemailaddress`);email=profile.internalemailaddress||'';if(!email)throw Error('Tài khoản chưa có email trong Dataverse.');
    const query=new URLSearchParams(location.search);const id=query.get('id');embedded=!!id||query.has('typename');
    $('views').hidden=embedded;$('refresh').onclick=()=>safe(()=>selected?loadRecord(selected.fmc_fmrequestid):loadList());
    $('back').onclick=()=>safe(()=>loadList());$('more').onclick=()=>safe(()=>loadList(true));$('create').onclick=()=>safe(()=>openForm());
    document.querySelectorAll('[data-view]').forEach(b=>b.onclick=()=>safe(async()=>{view=b.dataset.view;document.querySelectorAll('[data-view]').forEach(x=>x.classList.toggle('primary',x===b));await loadList();}));
    if(id)await loadRecord(id);else if(embedded){$('content').append(element('section','Lưu Draft trước, sau đó mở lại tab để gửi phê duyệt.','card'));}else await loadList();
  }
  safe(init);
})(typeof window==='undefined'?globalThis:window);
