(function(global) {
  'use strict';
  const equipment = 'fmc_bmsequipment';
  const field = 'fmc_buildingid';
  const own = (value,key) => Object.prototype.hasOwnProperty.call(value,key);
  function simulate({message,table,attributes,stage=10,mode=0,filter=true,enabled=true}) {
    const msg = String(message).toLowerCase();
    const answer = (status,title,detail,lines,path) => ({status,title,detail,lines,path});
    if (!attributes || typeof attributes !== 'object' || Array.isArray(attributes))
      return answer('invalid','Payload chưa đúng định dạng','Target.Attributes phải là một JSON object, không phải mảng hoặc null.',[],['JSON','Sai định dạng']);
    if (!enabled) return answer('skip','Step đang tắt','Dataverse sẽ không gọi handler của step này.',[],['Request','Step disabled']);
    if (!['create','update'].includes(msg) || table!==equipment)
      return answer('skip','Không khớp step','Hai steps của bài chỉ nhận Create/Update trên fmc_bmsequipment.',[],['Request','Không có step phù hợp']);
    if (msg==='update' && filter && !own(attributes,field))
      return answer('skip','Giữ nguyên Building','Update không gửi fmc_buildingid nên không khớp Filtering Attributes. Plugin không được gọi.',[31,32],['Update','Filter không khớp','Giữ lookup']);
    if (stage!==10 || mode!==0)
      return answer('skip','Handler thoát ở guard','Code yêu cầu Stage 10 và Mode 0. Cấu hình hiện tại không khớp.',[21,22],['Context','Guard','return']);
    if (msg==='update' && !own(attributes,field))
      return answer('skip','Bỏ qua cột không được gửi','Kể cả khi bỏ filter, guard trong code vẫn cho Update không gửi Building đi tiếp.',[31,32],['Update','Target thiếu key','return']);
    const building=attributes[field];
    if (building==null)
      return answer('block','Request bị chặn','BMS-EQUIPMENT-001: Equipment phai thuoc mot Building. Hay chon Building truoc khi luu.',[34,41,42,43],['Target','Building = null','throw']);
    if (typeof building!=='object' || Array.isArray(building) ||
      typeof building.logicalName!=='string' ||
      typeof building.id!=='string' || !/^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/i.test(building.id))
      return answer('invalid','Lookup chưa đúng kiểu','Trong mô phỏng SDK, Building cần object { logicalName, id } với id là GUID. Không dùng chuỗi hoặc @odata.bind tại đây.',[34],['JSON','EntityReference chưa hợp lệ']);
    return answer('pass','Rule cho request tiếp tục','Building reference khác null nên plugin không ném exception. Dataverse thật vẫn kiểm tra quyền, schema và record được tham chiếu.',[34,44,45],['Target','Có Building','Tiếp tục']);
  }
  function checkStep({message,table,filter,stage,mode,rank}) {
    const issues=[];
    if (!['Create','Update'].includes(message)) issues.push('Message phải là Create hoặc Update.');
    if (table!==equipment) issues.push('Primary Entity phải là fmc_bmsequipment để khớp rule.');
    if (stage!==10) issues.push('Stage cần là PreValidation (10); code sẽ return ở stage khác.');
    if (mode!==0) issues.push('Rule chặn lưu cần Synchronous (0).');
    if (mode===1 && stage!==40) issues.push('Dataverse chỉ hỗ trợ asynchronous step tại PostOperation.');
    if (message==='Update' && filter.trim()!==field) issues.push('Update filter của bài chỉ gồm fmc_buildingid.');
    if (message==='Create' && filter.trim()) issues.push('Để trống Filtering Attributes cho Create.');
    if (!Number.isInteger(rank)||rank<1) issues.push('Execution Order cần số nguyên dương.');
    if (Number.isInteger(rank)&&rank>0&&rank!==10) issues.push('Plan hiện dùng Execution Order 10; số khác đổi thứ tự giữa các steps.');
    return issues;
  }
  global.PluginRuleModel = {simulate,checkStep};
})(globalThis);
