const {test}=require('node:test');const assert=require('node:assert/strict');
const {allowed,heatmap,quote,serializeEvidenceIds,parseEvidenceIds}=require('../../dataverse/webresources/fmc_/scripts/FmWorkflow.js');
test('UI only offers owner submit and assigned decisions',()=>{
  assert.equal(allowed({fmc_requeststatus:789141000,_ownerid_value:'ABC'},'{abc}','a@x').Submit,true);
  assert.equal(allowed({fmc_requeststatus:789141002,fmc_currentapproveremail:'A@X'},'abc','a@x').Approve,true);
  assert.equal(allowed({fmc_requeststatus:789141002,fmc_currentapproveremail:'b@x'},'abc','a@x').Approve,false);
});
test('heatmap ignores incomplete risk scores',()=>{const c=heatmap([{fmc_likelihood:5,fmc_impact:4},{fmc_likelihood:0,fmc_impact:4},{}]);assert.equal(c[4][3],1);assert.equal(c.flat().reduce((a,b)=>a+b,0),1);});
test('OData literal escapes apostrophes',()=>assert.equal(quote("o'brien@x"),"o''brien@x"));
test('multiple evidence IDs use real newlines accepted by the server',()=>{
  const ids=['{11111111-1111-1111-1111-111111111111}','22222222-2222-2222-2222-222222222222'];
  const value=serializeEvidenceIds(ids);
  assert.equal(value,'11111111-1111-1111-1111-111111111111\n22222222-2222-2222-2222-222222222222');
  assert.equal(value.includes('\\n'),false);
  assert.deepEqual(parseEvidenceIds(value),ids.map(id=>id.replace(/[{}]/g,'')));
});
