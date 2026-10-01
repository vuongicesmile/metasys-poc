(function (root) {
  'use strict';
  const statuses = {789141000:'Draft',789141001:'Submitted (legacy)',789141002:'In Approval',789141003:'Approved',789141004:'Rejected',789141005:'Closed'};
  const types = {789140000:'CIWG',789140001:'Risk',789140002:'Project'};
  const cleanId = value => String(value || '').replace(/[{}]/g, '').toLowerCase();
  const quote = value => String(value).replace(/'/g, "''");
  const serializeEvidenceIds = ids => (ids || []).map(cleanId).filter(Boolean).join('\n');
  const parseEvidenceIds = value => String(value || '').split(/[\s,;]+/).map(cleanId).filter(Boolean);
  const allowed = (r, user, email) => ({Submit:r.fmc_requeststatus===789141000 && cleanId(r._ownerid_value)===cleanId(user),
    Approve:r.fmc_requeststatus===789141002 && String(r.fmc_currentapproveremail||'').toLowerCase()===email.toLowerCase(),
    Reject:r.fmc_requeststatus===789141002 && String(r.fmc_currentapproveremail||'').toLowerCase()===email.toLowerCase(),
    Close:[789141003,789141004].includes(r.fmc_requeststatus) && cleanId(r._ownerid_value)===cleanId(user)});
  function heatmap(rows) {
    const cells = Array.from({length:5},()=>Array(5).fill(0));
    for (const r of rows) if (r.fmc_likelihood>=1 && r.fmc_likelihood<=5 && r.fmc_impact>=1 && r.fmc_impact<=5) cells[r.fmc_likelihood-1][r.fmc_impact-1]++;
    return cells;
  }
  if (typeof module !== 'undefined') module.exports = {allowed, heatmap, quote, cleanId, serializeEvidenceIds, parseEvidenceIds};
  if (!root.document) return;
  const $ = id => document.getElementById(id);
  const element = (tag, text, cls) => {const e=document.createElement(tag);if(text!==undefined)e.textContent=text;if(cls)e.className=cls;return e;};
  let xrm, context, user, email, selected, rows=[], next=null, view='mine', busy=false, embedded=false;
  const fields='fmc_fmrequestid,fmc_name,fmc_description,fmc_requesttype,fmc_requeststatus,fmc_department,fmc_currentapproveremail,fmc_currentstep,fmc_stepdueon,fmc_workflowrevision,_ownerid_value,fmc_likelihood,fmc_impact,fmc_mitigation,fmc_reviewdate,fmc_riskseverity,fmc_evidencereadingid,fmc_evidencereadingids,fmc_evidencesnapshot';
  const date = value => value ? new Date(value).toLocaleString() : '—';
  function notice(message,error=false){$('notice').hidden=!message;$('notice').textContent=message;$('notice').className=error?'error':'';}
  async function api(path,body,method,headers={}){
    const url = new URL(path.startsWith('http')?path:context.getClientUrl()+'/api/data/v9.2/'+path);
    if(url.origin!==new URL(context.getClientUrl()).origin || !url.pathname.startsWith('/api/data/v9.2/'))throw Error('Unexpected Dataverse URL');
    const response=await fetch(url,{method:method||(body?'POST':'GET'),credentials:'same-origin',headers:{Accept:'application/json','Content-Type':'application/json','OData-Version':'4.0',Prefer:'odata.maxpagesize=50',...headers},body:body?JSON.stringify(body):undefined});
    const text=await response.text();let result={};try{result=text?JSON.parse(text):{};}catch{throw Error('Dataverse returned an unreadable response. Refresh and retry.');}
    if(!response.ok)throw Error(result.error?.message||'Dataverse request failed ('+response.status+')');return result;
  }
  async function safe(action){if(busy)return;busy=true;notice('Đang xử lý…');document.querySelectorAll('button').forEach(b=>b.disabled=true);try{await action();notice('');}catch(e){notice(e.message,true);}finally{busy=false;document.querySelectorAll('button').forEach(b=>b.disabled=false);}}
  function openForm(id){
    if(xrm?.Navigation)return xrm.Navigation.openForm({entityName:'fmc_fmrequest',entityId:id});
    // Sitemap HTML resources do not always expose parent.Xrm. GlobalContext is
    // supported here; retain the current app when opening a native form.
    const url=new URL(context.getCurrentAppUrl());url.searchParams.set('pagetype','entityrecord');url.searchParams.set('etn','fmc_fmrequest');
    url.searchParams.delete('webresourceName');url.searchParams.delete('data');if(id)url.searchParams.set('id',id);else url.searchParams.delete('id');
    root.top.location.assign(url.href);
  }
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
      const text=`${label}: ${r.fmc_name}?`;
      const answer=xrm?.Navigation?await xrm.Navigation.openConfirmDialog({text,title:'Xác nhận quyết định'}):{confirmed:root.confirm(text)};if(!answer.confirmed)return;
      const key='fmc-workflow:'+user+':'+r.fmc_fmrequestid;const signature=JSON.stringify([action,r.fmc_workflowrevision||0,comment.value]);let saved;try{saved=JSON.parse(sessionStorage.getItem(key));}catch{/* old value */}
      if(!saved||saved.signature!==signature){saved={signature,id:crypto.randomUUID()};sessionStorage.setItem(key,JSON.stringify(saved));}
      await api('fmc_TransitionFmRequest',{RequestId:r.fmc_fmrequestid,Action:action,Comment:comment.value,ExpectedRevision:r.fmc_workflowrevision||0,OperationId:saved.id});
      sessionStorage.removeItem(key);await loadRecord(r.fmc_fmrequestid);
    });buttons.append(b);}card.append(buttons);if(!Object.values(actions).some(Boolean))card.append(element('p','Không có quyết định dành cho bạn ở trạng thái hiện tại.','muted'));main.append(card);
    evidencePanel(main,r,actions.Submit);
    const history=element('section',undefined,'card');history.append(element('h2','Lịch sử xử lý'));main.append(history);
    let historyNext=`fmc_requesthistories?$select=fmc_command,fmc_actoremail,fmc_actedon,fmc_comments,fmc_stepno&$filter=_fmc_requestid_value eq ${r.fmc_fmrequestid}&$orderby=fmc_actedon desc`;
    const timeline=element('ol',undefined,'timeline'),more=element('button','Xem thêm lịch sử');history.append(timeline,more);
    async function loadHistory(){const page=await api(historyNext);for(const event of page.value){const li=element('li');li.append(element('strong',`${event.fmc_command||'Event'} · Bước ${event.fmc_stepno||'—'}`),element('p',`${event.fmc_actoremail||'—'} · ${date(event.fmc_actedon)}`,'muted'),element('p',event.fmc_comments||'—'));timeline.append(li);}historyNext=page['@odata.nextLink'];more.hidden=!historyNext;if(!timeline.children.length)timeline.append(element('li','Chưa có lịch sử.'));}
    more.onclick=()=>safe(loadHistory);await loadHistory();
  }
  let evidenceMetadata;
  async function readingMetadata(){
    if(evidenceMetadata)return evidenceMetadata;
    const table=await api("EntityDefinitions(LogicalName='fmc_bmsreadingsnapshot')?$select=EntitySetName");
    if(!table.EntitySetName)throw Error('Chưa có cấu hình bảng reading bằng chứng.');
    evidenceMetadata={set:table.EntitySetName};return evidenceMetadata;
  }
  function evidenceFrame(main,title,description){
    const panel=element('section',undefined,'card evidence-card'),head=element('div',undefined,'evidence-head'),icon=element('div','▥','evidence-icon'),copy=element('div');
    copy.append(element('div','BMS · STANDARD READING','eyebrow'),element('h2',title),element('p',description,'muted'));head.append(icon,copy);
    const body=element('div',undefined,'evidence-body');panel.append(head,body);main.append(panel);return body;
  }
  function selectedReading(container,row){
    container.replaceChildren();if(!row)return;
    const selected=element('div',undefined,'selected-reading'),copy=element('div');copy.append(element('strong','✓ Đã chọn reading làm bằng chứng'),element('p',`${row.fmc_objectid||'—'} · ${row.fmc_equipmentcode||'—'} · ${date(row.fmc_readingtime)} · SQL ${row.fmc_sqlreadingid||'—'}`,'muted'));
    selected.append(copy,element('div',(row.fmc_readingvalue==null?'—':Number(row.fmc_readingvalue).toFixed(4))+' '+(row.fmc_unit||''),'reading-value'));container.append(selected);
  }
  function readingPickerSingle(body,onSelect){
    const selectedRows=new Map();
    const picked=element('div');body.append(picked);
    const search=element('input');search.placeholder='Ví dụ: AHU-01, TEMP hoặc 82710';search.setAttribute('aria-label','Tìm reading');search.maxLength=200;
    const from=element('input'),to=element('input');from.type=to.type='datetime-local';from.setAttribute('aria-label','Từ thời điểm');to.setAttribute('aria-label','Đến thời điểm');
    const filters=element('div',undefined,'filter-grid');
    for(const [title,input,cls]of [['Point, Equipment hoặc SQL ID',search,'search-field'],['Từ thời điểm',from,''],['Đến thời điểm',to,'']]){const label=element('label',title,'field-label '+cls);label.append(input);filters.append(label);}
    const find=element('button','Tìm readings','primary search-button');filters.append(find);body.append(filters);
    const unit=element('input');unit.placeholder='C, %, m3';unit.setAttribute('aria-label','Unit');unit.maxLength=50;
    const compare=element('select');for(const [value,label]of [['all','Tất cả giá trị'],['gt','Cao hơn ngưỡng'],['lt','Thấp hơn ngưỡng'],['outside','Ngoài khoảng']]){const option=element('option',label);option.value=value;compare.append(option);}
    const threshold=element('input');threshold.type='number';threshold.step='any';threshold.placeholder='40';threshold.setAttribute('aria-label','Ngưỡng giá trị');
    const thresholdMax=element('input');thresholdMax.type='number';thresholdMax.step='any';thresholdMax.placeholder='80';thresholdMax.setAttribute('aria-label','Ngưỡng tối đa');thresholdMax.hidden=true;
    const rules=element('div',undefined,'filter-grid rules');for(const [title,input]of [['Unit',unit],['Điều kiện bất thường',compare],['Ngưỡng từ',threshold],['Ngưỡng đến',thresholdMax]]){const label=element('label',title,'field-label anomaly-label');label.append(input);rules.append(label);}body.append(rules);
    compare.onchange=()=>{thresholdMax.hidden=compare.value!=='outside';threshold.placeholder=compare.value==='all'?'Không cần':'40';};
    const help=element('div',undefined,'reading-help');help.append(element('span','Tìm trên dữ liệu Standard đã đồng bộ · thời gian hiển thị theo giờ máy.'),element('span','Tối đa 50 dòng / trang','step-badge'));body.append(help);
    const results=element('div',undefined,'reading-results'),more=element('button','Xem thêm readings');more.hidden=true;body.append(results,more);let readingNext;
    async function list(append){
      const meta=await readingMetadata(),filter=[],term=search.value.trim(),unitTerm=unit.value.trim(),mode=compare.value;
      if(term)filter.push(`(contains(fmc_objectid,'${quote(term)}') or contains(fmc_equipmentcode,'${quote(term)}') or fmc_sqlreadingid eq '${quote(term)}')`);
      if(unitTerm)filter.push(`fmc_unit eq '${quote(unitTerm)}'`);
      if(mode!=='all'){const low=Number(threshold.value);if(!Number.isFinite(low))throw Error('Nhập ngưỡng giá trị để lọc bất thường.');if(mode==='gt')filter.push(`fmc_readingvalue gt ${low}`);if(mode==='lt')filter.push(`fmc_readingvalue lt ${low}`);if(mode==='outside'){const high=Number(thresholdMax.value);if(!Number.isFinite(high)||low>=high)throw Error('Ngưỡng từ phải nhỏ hơn ngưỡng đến.');filter.push(`(fmc_readingvalue lt ${low} or fmc_readingvalue gt ${high})`);}}
      if(from.value)filter.push('fmc_readingtime ge '+new Date(from.value).toISOString());
      if(to.value)filter.push('fmc_readingtime le '+new Date(to.value).toISOString());
      if(from.value&&to.value&&new Date(from.value)>new Date(to.value))throw Error('Thời điểm bắt đầu phải trước thời điểm kết thúc.');
      const select='fmc_bmsreadingsnapshotid,fmc_objectid,fmc_objectname,fmc_equipmentcode,fmc_readingtime,fmc_readingvalue,fmc_unit,fmc_sqlreadingid';
      const page=await api(append?readingNext:`${meta.set}?$select=${select}&$orderby=fmc_readingtime desc,fmc_bmsreadingsnapshotid asc${filter.length?'&$filter='+encodeURIComponent(filter.join(' and ')):''}`);
      if(!append)results.replaceChildren();
      const table=element('table'),thead=element('thead'),head=element('tr'),tbody=element('tbody');for(const title of ['Point / Equipment','Thời gian đo','Giá trị','SQL ID',''])head.append(element('th',title));thead.append(head);table.append(thead,tbody);
      for(const row of page.value){const tr=element('tr'),identity=element('td'),identityName=element('strong',row.fmc_objectid||'—'),identitySub=element('span',`${row.fmc_objectname||'Chưa có tên'} · ${row.fmc_equipmentcode||'—'}`),action=element('td'),button=element('button','Chọn reading','primary');
        identity.append(identityName,identitySub);const id=cleanId(row.fmc_bmsreadingsnapshotid);button.textContent=selectedRows.has(id)?'Đã chọn':'Chọn reading';button.onclick=()=>safe(async()=>{if(selectedRows.has(id))selectedRows.delete(id);else{if(selectedRows.size>=10)throw Error('Tối đa 10 readings cho một report.');selectedRows.set(id,row);}button.textContent=selectedRows.has(id)?'Đã chọn':'Chọn reading';await onSelect([...selectedRows.values()]);selectedReading(picked,row);});action.append(button);
        tr.append(identity,element('td',date(row.fmc_readingtime)),element('td',(row.fmc_readingvalue==null?'—':Number(row.fmc_readingvalue).toFixed(4))+' '+(row.fmc_unit||'')),element('td',row.fmc_sqlreadingid||'—'),action);tbody.append(tr);
      }
      results.append(table);if(!page.value.length&&!append)results.append(element('div','Không tìm thấy reading phù hợp. Hãy thử Point/Equipment khác hoặc bỏ khoảng thời gian.','empty'));
      readingNext=page['@odata.nextLink'];more.hidden=!readingNext;
    }
    find.onclick=()=>safe(()=>list(false));more.onclick=()=>safe(()=>list(true));search.onkeydown=e=>{if(e.key==='Enter'){e.preventDefault();find.click();}};
    return {selected:()=>[...selectedRows.values()]};
  }
  function readingPicker(body,onSelect,initialIds=[]){
    const selectedIds=new Set(initialIds.map(cleanId));
    return readingPickerSingle(body,async value=>{const list=Array.isArray(value)?value:[];if(list.length>10)throw Error('Tối đa 10 readings cho một report.');selectedIds.clear();for(const row of list)selectedIds.add(cleanId(row.fmc_bmsreadingsnapshotid));root.__selectedEvidenceIds=[...selectedIds];const payload=[...selectedIds].map(x=>({fmc_bmsreadingsnapshotid:x}));payload.fmc_bmsreadingsnapshotid=payload[0]?.fmc_bmsreadingsnapshotid;await onSelect(payload);});
  }
  function evidencePanel(main,r,editable){
    const body=evidenceFrame(main,'Reading bằng chứng',editable?'Tìm và chọn phép đo đại diện trước khi gửi duyệt.':'Bản chụp đã khóa theo trạng thái của request.');
    if(r.fmc_evidencesnapshot)body.append(element('pre',r.fmc_evidencesnapshot,'snapshot'));
    else body.append(element('p','Chưa đính kèm reading bằng chứng.','muted'));
    body.append(element('p','Snapshot được server tạo khi đính kèm; giá trị đo mới không thay đổi bằng chứng đã lưu.','muted'));
    if(!editable)return;
    async function attach(readings){
      if(!r['@odata.etag'])throw Error('Thiếu phiên bản request. Refresh trước khi đính kèm.');
      const ids=readings.map(row=>cleanId(row.fmc_bmsreadingsnapshotid));
      await api(`fmc_fmrequests(${cleanId(r.fmc_fmrequestid)})`,{fmc_evidencereadingid:ids[0]||null,fmc_evidencereadingids:serializeEvidenceIds(ids)||null},'PATCH',{'If-Match':r['@odata.etag']});
      await loadRecord(r.fmc_fmrequestid);
    }
    readingPicker(body,attach,parseEvidenceIds(r.fmc_evidencereadingids||r.fmc_evidencereadingid));
    if(r.fmc_evidencereadingids){const clear=element('button','Gỡ toàn bộ bằng chứng');clear.onclick=()=>safe(async()=>{await api(`fmc_fmrequests(${cleanId(r.fmc_fmrequestid)})`,{fmc_evidencereadingid:null,fmc_evidencereadingids:null},'PATCH',{'If-Match':r['@odata.etag']});await loadRecord(r.fmc_fmrequestid);});body.append(clear);}
    if(r.fmc_evidencereadingid){const remove=element('button','Gỡ bằng chứng');remove.onclick=()=>safe(async()=>{await api(`fmc_fmrequests(${cleanId(r.fmc_fmrequestid)})`,{fmc_evidencereadingid:null},'PATCH',{'If-Match':r['@odata.etag']});await loadRecord(r.fmc_fmrequestid);});body.append(remove);}
  }
  function parentForm(){
    const form=root.parent?.Xrm?.Page;
    return form?.data?.entity?.getEntityName?.()==='fmc_fmrequest'?form:null;
  }
  function newRequestEvidence(main){
    const form=parentForm(),intro=element('section',undefined,'card create-callout');intro.append(element('div','TẠO REQUEST · MỘT LUỒNG','eyebrow'),element('h2','Chọn bằng chứng rồi gửi duyệt ngay'));
    intro.append(element('p','Điền thông tin request ở các tab phía trên, chọn reading bên dưới, sau đó bấm Save chuẩn của form hoặc Lưu & gửi phê duyệt. Hệ thống sẽ lưu Draft, chụp evidence trên server và Submit liên tiếp — bạn không cần vào My Requests để gửi lần nữa.','muted'));main.append(intro);
    if(!form){intro.append(element('p','Không lấy được form context. Hãy mở tab này từ form New FM Request trong FMC BMS Demo.','error'));return;}
    const body=evidenceFrame(main,'Chọn reading trước khi tạo request','Bộ lọc hoạt động ngay cả khi request chưa có ID. Reading được đưa vào lần Save đầu tiên để plugin chụp snapshot.');
    const attribute=form.getAttribute('fmc_evidencereadingid');if(!attribute){body.append(element('p','Form chưa chứa trường evidence reading. Refresh app và thử lại.','muted'));return;}
    let chosen=null,autoSubmit=false,suppressAuto=false;
    readingPicker(body,async row=>{chosen=row;attribute.setValue(cleanId(row.fmc_bmsreadingsnapshotid));attribute.setSubmitMode('always');});
    const actions=element('div',undefined,'create-actions'),save=element('button','Lưu Draft'),submit=element('button','Lưu & gửi phê duyệt','primary');actions.append(save,submit);body.append(actions);
    const multi=form.getAttribute('fmc_evidencereadingids');if(multi){setInterval(()=>{const ids=root.__selectedEvidenceIds;if(ids){multi.setValue(serializeEvidenceIds(ids));multi.setSubmitMode('always');}},250);}
    async function submitSavedRecord(){
      const id=cleanId(form.data.entity.getId());if(!id)throw Error('Request chưa được tạo. Kiểm tra các trường bắt buộc trên form.');
      const r=await api(`fmc_fmrequests(${id})?$select=${fields}`);
      if(r.fmc_evidencereadingid&&!r.fmc_evidencesnapshot)throw Error('Draft đã lưu nhưng server chưa tạo evidence snapshot. Refresh và chọn lại reading trước khi Submit.');
      await api('fmc_TransitionFmRequest',{RequestId:id,Action:'Submit',Comment:'',ExpectedRevision:r.fmc_workflowrevision||0,OperationId:crypto.randomUUID()});
      try{await form.data.refresh(false);}catch{/* The form can reload this embedded resource after first save. */}
      await loadRecord(id);
    }
    const entity=form.data.entity;
    if(entity.addOnSave&&entity.addOnPostSave){
      entity.addOnSave(executionContext=>{const args=executionContext?.getEventArgs?.(),mode=args?.getSaveMode?.();if(!suppressAuto&&[1,2].includes(mode))autoSubmit=true;});
      entity.addOnPostSave(()=>{if(!autoSubmit)return;autoSubmit=false;submitSavedRecord().catch(error=>notice(error.message,true));});
    }
    save.onclick=()=>safe(async()=>{suppressAuto=true;try{await form.data.save();}finally{suppressAuto=false;}const id=cleanId(form.data.entity.getId());if(id)await loadRecord(id);});
    submit.onclick=()=>safe(async()=>{suppressAuto=true;try{await form.data.save();}finally{suppressAuto=false;}await submitSavedRecord();});
  }
  async function init(){
    xrm=root.parent?.Xrm||root.Xrm;context=typeof GetGlobalContext==='function'?GetGlobalContext():xrm?.Utility.getGlobalContext();
    if(!context)throw Error('Mở trang này trong FMC BMS Demo để sử dụng tài khoản Power Apps.');
    user=cleanId(context.userSettings.userId);const profile=await api(`systemusers(${user})?$select=internalemailaddress`);email=profile.internalemailaddress||'';if(!email)throw Error('Tài khoản chưa có email trong Dataverse.');
    const query=new URLSearchParams(location.search);const id=query.get('id');embedded=!!id||query.has('typename');document.body.classList.toggle('embedded',embedded);
    $('views').hidden=embedded;$('refresh').onclick=()=>safe(()=>selected?loadRecord(selected.fmc_fmrequestid):embedded?loadNewRequest():loadList());
    $('back').onclick=()=>safe(()=>loadList());$('more').onclick=()=>safe(()=>loadList(true));$('create').onclick=()=>safe(()=>openForm());
    document.querySelectorAll('[data-view]').forEach(b=>b.onclick=()=>safe(async()=>{view=b.dataset.view;document.querySelectorAll('[data-view]').forEach(x=>x.classList.toggle('primary',x===b));await loadList();}));
    if(id)await loadRecord(id);else if(embedded)loadNewRequest();else await loadList();
  }
  function loadNewRequest(){const main=$('content');main.replaceChildren();newRequestEvidence(main);}
  safe(init);
})(typeof window==='undefined'?globalThis:window);
