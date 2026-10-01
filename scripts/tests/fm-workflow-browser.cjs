// Offline UI regression with a simulated Dataverse boundary; never connects to the live organization.
const {chromium}=require('../../dataverse/app-source/fmc-bms-demo/node_modules/@playwright/test');
const fs=require('node:fs');const path=require('node:path');const assert=require('node:assert/strict');
(async()=>{
  const browser=await chromium.launch({headless:true,channel:process.env.FMC_TEST_BROWSER||'msedge'});
  try{
    const page=await browser.newPage({viewport:{width:1280,height:960}}), errors=[];
    page.on('pageerror',e=>errors.push(e.message));
    const id='11111111-1111-1111-1111-111111111111',newId='44444444-4444-4444-4444-444444444444',user='22222222-2222-2222-2222-222222222222';
    const record={'@odata.etag':'W/"1"',fmc_fmrequestid:id,fmc_name:'Repair air handling unit – Library',fmc_description:'Airflow inspection and preventive maintenance.',fmc_requesttype:789140000,fmc_requeststatus:789141000,fmc_workflowrevision:0,_ownerid_value:user};
    const reading={fmc_bmsreadingsnapshotid:'33333333-3333-3333-3333-333333333333',fmc_objectid:'AHU-01-TEMP',fmc_equipmentcode:'AHU-01',fmc_readingtime:'2026-10-01T03:15:00Z',fmc_readingvalue:27.1234,fmc_unit:'C',fmc_sqlreadingid:'9223372036854775807'};
    const history=[];let posts=0;
    await page.exposeFunction('__mockSave',evidenceId=>{assert.equal(evidenceId,reading.fmc_bmsreadingsnapshotid);Object.assign(record,{'@odata.etag':'W/"10"',fmc_fmrequestid:newId,fmc_name:'New request with evidence',fmc_description:'Created and submitted in one UI flow.',fmc_requeststatus:789141000,fmc_workflowrevision:0,_ownerid_value:user,fmc_evidencereadingid:evidenceId,fmc_evidencesnapshot:'Value: 27.1234 C\nSQL reading ID: 9223372036854775807'});});
    await page.addInitScript(({user,newId})=>{
      const evidence={value:null,setValue(value){this.value=value;},getValue(){return this.value;},setSubmitMode(){}};let formId='';
      const saveHandlers=[],postSaveHandlers=[];
      const form={getAttribute:name=>name==='fmc_evidencereadingid'?evidence:null,data:{entity:{getEntityName:()=> 'fmc_fmrequest',getId:()=>formId,addOnSave:h=>saveHandlers.push(h),addOnPostSave:h=>postSaveHandlers.push(h)},save:async()=>{const context={getEventArgs:()=>({getSaveMode:()=>1})};for(const handler of saveHandlers)handler(context);await window.__mockSave(evidence.value);formId='{'+newId+'}';for(const handler of postSaveHandlers)await handler({});},refresh:async()=>{}}};
      window.Xrm={Page:form,Utility:{getGlobalContext:()=>({getClientUrl:()=>location.origin,userSettings:{userId:user}})},Navigation:{openForm:async()=>{},openConfirmDialog:async()=>({confirmed:true})}};
    },{user,newId});
    await page.route('https://workflow.test/**',async route=>{
      const u=new URL(route.request().url()), p=u.pathname;
      if(p.includes('ClientGlobalContext'))return route.fulfill({contentType:'application/javascript',body:'function GetGlobalContext(){return Xrm.Utility.getGlobalContext();}'});
      if(p.startsWith('/api/data/v9.2/')){
        let body={};
        if(p.includes('EntityDefinitions'))body={EntitySetName:'verified_readings'};
        else if(p.endsWith('verified_readings')){const filter=u.searchParams.get('$filter');assert.ok(filter.includes("AHU-01"));assert.ok(filter.includes("fmc_unit eq 'C'"));assert.ok(filter.includes('fmc_readingvalue gt 40'));body={value:[reading]};}
        else if(p.includes('fmc_fmrequests(')&&route.request().method()==='PATCH'){
          assert.equal(route.request().headers()['if-match'],record['@odata.etag']);
          assert.equal(route.request().postDataJSON().fmc_evidencereadingid,reading.fmc_bmsreadingsnapshotid);
          record.fmc_evidencereadingid=reading.fmc_bmsreadingsnapshotid;
          record.fmc_evidencesnapshot='Value: 27.1234 C\nSQL reading ID: 9223372036854775807\nPoint: <script>unsafe</script>';
          record.fmc_workflowrevision++;record['@odata.etag']='W/"2"';
        }
        else if(p.includes('systemusers'))body={internalemailaddress:'demo@example.com'};
        else if(p.endsWith('fmc_TransitionFmRequest')){
          const req=route.request().postDataJSON();posts++;assert.equal(req.ExpectedRevision,record.fmc_workflowrevision);assert.ok(req.OperationId);
          if(req.Action==='Submit'){record.fmc_requeststatus=789141002;record.fmc_currentstep=1;record.fmc_currentapproveremail='demo@example.com';record.fmc_stepdueon='2026-10-01T10:00:00Z';}
          else if(req.Action==='Approve'){record.fmc_requeststatus=789141003;record.fmc_currentapproveremail=null;}
          record.fmc_workflowrevision++;history.unshift({fmc_command:req.Action,fmc_actoremail:'demo@example.com',fmc_actedon:'2026-09-30T10:00:00Z',fmc_comments:req.Comment,fmc_stepno:1});body={Status:record.fmc_requeststatus,Revision:record.fmc_workflowrevision};
        }else if(p.includes('fmc_requesthistories'))body={value:history};
        else if(p.includes('fmc_fmrequests('))body=record;
        else body={value:[record]};
        return route.fulfill({contentType:'application/json',body:JSON.stringify(body)});
      }
      const source=path.resolve(__dirname,'../../dataverse/webresources',p.replace('/WebResources/',''));
      return route.fulfill({contentType:p.endsWith('.js')?'application/javascript':'text/html',body:fs.readFileSync(source)});
    });
    await page.goto('https://workflow.test/WebResources/fmc_/pages/FmWorkflow.html');
    await page.getByRole('button',{name:record.fmc_name}).click();
    await page.getByRole('textbox',{name:'Tìm reading',exact:true}).fill('AHU-01');
    await page.getByRole('textbox',{name:'Unit'}).fill('C');await page.getByRole('combobox').selectOption('gt');await page.getByRole('spinbutton',{name:'Ngưỡng giá trị'}).fill('40');
    await page.getByRole('button',{name:'Tìm readings',exact:true}).click();
    await page.getByRole('button',{name:'Chọn reading'}).waitFor();
    await page.setViewportSize({width:390,height:844});
    fs.mkdirSync('.artifacts/fm-reading-evidence',{recursive:true});
    await page.screenshot({path:'.artifacts/fm-reading-evidence/draft-picker-mobile.png',fullPage:true});
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Evidence picker must fit mobile viewport');
    await page.setViewportSize({width:1280,height:960});
    await page.getByRole('button',{name:'Chọn reading'}).click();
    await page.locator('pre').filter({hasText:'27.1234 C'}).waitFor();
    assert.equal(await page.locator('#content script').count(),0);
    await page.getByRole('button',{name:'Gửi phê duyệt'}).click();
    await page.getByRole('button',{name:'Duyệt bước này'}).waitFor();
    assert.equal(posts,1);
    assert.equal(await page.getByRole('button',{name:'Tìm readings',exact:true}).count(),0);
    assert.ok((await page.locator('pre').textContent()).includes('9223372036854775807'));
    fs.mkdirSync('.artifacts/epic3-workflow',{recursive:true});
    await page.screenshot({path:'.artifacts/epic3-workflow/ui-desktop.png',fullPage:true});
    await page.getByRole('textbox',{name:'Nhận xét quyết định'}).fill('<img src=x onerror=alert(1)> Approved for maintenance');
    await page.getByRole('button',{name:'Duyệt bước này'}).click();
    await page.getByRole('button',{name:'Đóng request'}).waitFor();assert.equal(posts,2);assert.equal(await page.locator('img').count(),0);
    await page.setViewportSize({width:390,height:844});
    await page.screenshot({path:'.artifacts/epic3-workflow/ui-mobile.png',fullPage:true});
    await page.setViewportSize({width:1280,height:960});
    await page.goto('https://workflow.test/WebResources/fmc_/pages/FmWorkflow.html?typename=fmc_fmrequest');
    await page.getByRole('heading',{name:'Chọn reading trước khi tạo request'}).waitFor();
    await page.getByRole('textbox',{name:'Tìm reading',exact:true}).fill('AHU-01');
    await page.getByRole('textbox',{name:'Unit'}).fill('C');await page.getByRole('combobox').selectOption('gt');await page.getByRole('spinbutton',{name:'Ngưỡng giá trị'}).fill('40');
    await page.getByRole('button',{name:'Tìm readings',exact:true}).click();
    await page.getByRole('button',{name:'Chọn reading'}).click();
    await page.getByText('Đã chọn reading làm bằng chứng').waitFor();
    await page.screenshot({path:'.artifacts/fm-reading-evidence/new-request-picker.png',fullPage:true});
    await page.getByRole('button',{name:'Lưu & gửi phê duyệt'}).click();
    await page.getByRole('button',{name:'Duyệt bước này'}).waitFor();
    assert.equal(record.fmc_fmrequestid,newId);assert.equal(record.fmc_requeststatus,789141002);assert.equal(posts,3);
    await page.goto('https://workflow.test/WebResources/fmc_/pages/FmWorkflow.html?typename=fmc_fmrequest');
    await page.getByRole('textbox',{name:'Tìm reading',exact:true}).fill('AHU-01');
    await page.getByRole('textbox',{name:'Unit'}).fill('C');await page.getByRole('combobox').selectOption('gt');await page.getByRole('spinbutton',{name:'Ngưỡng giá trị'}).fill('40');
    await page.getByRole('button',{name:'Tìm readings',exact:true}).click();
    await page.getByRole('button',{name:'Chọn reading'}).click();
    await page.evaluate(()=>window.Xrm.Page.data.save());
    await page.getByRole('button',{name:'Duyệt bước này'}).waitFor();
    assert.equal(record.fmc_requeststatus,789141002);assert.equal(posts,4);
    assert.deepEqual(errors,[]);console.log('PASS: list → find reading → attach with ETag → submit → approve; frozen evidence displayed and escaped; desktop/mobile rendered; no JS errors.');
  }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
