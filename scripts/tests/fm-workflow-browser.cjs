// Offline UI regression with a simulated Dataverse boundary; never connects to the live organization.
const {chromium}=require('../../dataverse/app-source/fmc-bms-demo/node_modules/@playwright/test');
const fs=require('node:fs');const path=require('node:path');const assert=require('node:assert/strict');
(async()=>{
  const browser=await chromium.launch({headless:true,channel:process.env.FMC_TEST_BROWSER||'msedge'});
  try{
    const page=await browser.newPage({viewport:{width:1280,height:960}}), errors=[];
    page.on('pageerror',e=>errors.push(e.message));
    const id='11111111-1111-1111-1111-111111111111',user='22222222-2222-2222-2222-222222222222';
    const record={fmc_fmrequestid:id,fmc_name:'Repair air handling unit – Library',fmc_description:'Airflow inspection and preventive maintenance.',fmc_requesttype:789140000,fmc_requeststatus:789141000,fmc_workflowrevision:0,_ownerid_value:user};
    const history=[];let posts=0;
    await page.addInitScript(({user})=>{window.Xrm={Utility:{getGlobalContext:()=>({getClientUrl:()=>location.origin,userSettings:{userId:user}})},Navigation:{openForm:async()=>{},openConfirmDialog:async()=>({confirmed:true})}};},{user});
    await page.route('https://workflow.test/**',async route=>{
      const u=new URL(route.request().url()), p=u.pathname;
      if(p.includes('ClientGlobalContext'))return route.fulfill({contentType:'application/javascript',body:'function GetGlobalContext(){return Xrm.Utility.getGlobalContext();}'});
      if(p.startsWith('/api/data/v9.2/')){
        let body={};
        if(p.includes('systemusers'))body={internalemailaddress:'demo@example.com'};
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
    await page.getByRole('button',{name:'Gửi phê duyệt'}).click();
    await page.getByRole('button',{name:'Duyệt bước này'}).waitFor();
    assert.equal(posts,1);
    fs.mkdirSync('.artifacts/epic3-workflow',{recursive:true});
    await page.screenshot({path:'.artifacts/epic3-workflow/ui-desktop.png',fullPage:true});
    await page.getByRole('textbox',{name:'Nhận xét quyết định'}).fill('<img src=x onerror=alert(1)> Approved for maintenance');
    await page.getByRole('button',{name:'Duyệt bước này'}).click();
    await page.getByRole('button',{name:'Đóng request'}).waitFor();assert.equal(posts,2);assert.equal(await page.locator('img').count(),0);
    await page.setViewportSize({width:390,height:844});
    await page.screenshot({path:'.artifacts/epic3-workflow/ui-mobile.png',fullPage:true});
    assert.deepEqual(errors,[]);console.log('PASS: list → detail → submit → approve; comments escaped; desktop/mobile rendered; no JS errors.');
  }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
