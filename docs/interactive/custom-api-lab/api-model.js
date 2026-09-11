(function(root){
  'use strict';
  const QUEUED=789100000;
  const RUNNING=789100001;
  const guid=/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

  function simulate(input){
    const supplied=input.clientRequestId!==null&&input.clientRequestId!==undefined&&input.clientRequestId!=='';
    if(supplied&&(!guid.test(String(input.clientRequestId))||String(input.clientRequestId).length>100)){
      return {status:'invalid',title:'BMS-SYNC-001',detail:'ClientRequestId phải là GUID string khi được truyền vào.',created:null,requestId:null,code:400,trace:['START','VALIDATION_ERROR'],lines:[80,88,89]};
    }
    if(input.sameCorrelation){
      return {status:'reuse',title:'Retry được nhận diện',detail:'Tìm thấy row có cùng correlation ID nên trả lại request cũ.',created:false,requestId:'671d5a8a-b2ad-f111-aaad-00224819a344',code:200,trace:['START','REUSE_BY_ID'],lines:[36,39,40]};
    }
    if(input.activeRequest){
      return {status:'reuse',title:'Pipeline đang có request active',detail:'Queued/Running request được dùng lại để tránh chạy đồng thời.',created:false,requestId:'e2fd9b60-b2ad-f111-aaad-00224819a344',code:200,trace:['START','REUSE_ACTIVE'],lines:[44,47,48]};
    }
    if(input.race){
      return {status:'reuse',title:'Caller khác thắng active key',detail:'Create bị fault; handler tìm được winner và trả Created=false.',created:false,requestId:'48a65cba-b0ad-f111-aaad-00224819a344',code:200,trace:['START','RACE_REUSE'],lines:[69,73,75,76]};
    }
    return {status:'created',title:'Đã queue request mới',detail:'Handler tạo fmc_syncrequest ở trạng thái Queued.',created:true,requestId:'11111111-1111-4111-8111-111111111111',code:200,trace:['START','CREATED'],lines:[54,64,66,67]};
  }

  function checkContract(value){
    const issues=[];
    if(value.binding!=='0')issues.push('Demo này là global unbound API: Binding phải là Global (0).');
    if(value.kind!=='action')issues.push('Có side effect tạo queue row nên phải là Action, không phải Function.');
    if(value.processing!=='1')issues.push('Business Event cần Allowed Custom Processing Step Type = Async Only (1).');
    if(!/^fmc_[A-Za-z][A-Za-z0-9]*$/.test(value.name))issues.push('Unique Name cần prefix fmc_ và không chứa khoảng trắng.');
    if(value.privilege!=='prvCreatefmc_syncrequest')issues.push('Contract demo bảo vệ bằng prvCreatefmc_syncrequest.');
    if(!value.input.includes('ClientRequestId:String?'))issues.push('Thiếu input optional ClientRequestId:String?.');
    for(const output of ['RequestId:Guid','Created:Boolean','Status:Integer','Message:String'])if(!value.output.includes(output))issues.push('Thiếu output '+output+'.');
    return issues;
  }

  root.CustomApiModel={QUEUED,RUNNING,simulate,checkContract};
})(typeof window==='undefined'?globalThis:window);
