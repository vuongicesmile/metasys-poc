# FMC BMS Demo model-driven app workflow log

## Phase 1 — authoring

`node --version`
Result: v24.20.0.

`pac help`
Result: PAC CLI Version 2.11.2 (>= 2.7.0).

`pac auth list`
Result: one active profile `ai-agent-platform`, user `vuong.nguyenq@titancorpvn.com`.

`pac org who --environment https://org06cbc9ec.crm5.dynamics.com/`
Result: organization `ab191700-b99e-f111-aaa0-000d3a80bb96`, environment `5abcb0e5-99b2-e51f-aa0e-90d84405798b`.

`pac model list`
Result: existing app `FMC BMS Demo`, app ID `d19f4897-d227-4df3-8361-988f97c53e89`, unique name `fmc_FMCBMSDemo`.

`node "C:\Users\Admin\.codex\plugins\cache\power-platform-skills\model-apps\2.7.0\scripts\download-model-app.js" --env https://org06cbc9ec.crm5.dynamics.com/ --app fmc_FMCBMSDemo --out D:\Coder\metasys-poc\.artifacts\model-apps\fmc-bms-demo`
Result: live snapshot downloaded; five app tables, one web resource and no generative page. Existing forms and views remain deployed.

`pac model list-tables --search "fmc_bmsbuilding,fmc_bmsequipment,fmc_bmspoint,fmc_bmsreading,fmc_syncrequest,fmc_spofile,fmc_spoimportrow,fmc_local_silver_bmspoint,cr3c8_silvernewbmspoint"`
Result: exact live metadata was read through Dataverse Web API because the installed PAC list-tables surface does not return the required column detail. All nine tables exist.

AskUserQuestion unavailable in Default mode. Persona decision was inferred from the user's explicit request: `FMC BMS Demo Operator` and `FMC BMS Demo Viewer`.

AskUserQuestion unavailable in Default mode. Data-model decision: reuse `FMCentralBms`, `fmc_FMCBMSDemo` and the live tables; do not create sample rows.

AskUserQuestion unavailable in Default mode. Surface decision: a generative operations center, Vietnamese navigation, focused main/quick-create forms, paged views and the existing Custom API sync web resource.

AskUserQuestion unavailable in Default mode. Access decision: Operator can create/update Building and Equipment and create/read Sync Requests; synchronized Bronze/Silver and SharePoint data are read-oriented. Viewer has read-only access.

Live read-only review: 6 Buildings, 9 Equipment, 112 current Bronze Points, 81,773 retained Bronze Readings, 107 Silver rows, 21 Sync Requests, 1 SPO File and 0 parsed SPO Import Rows.

Plan approved: yes — user response on 2026-09-12: `implement theo plan`.

`node "C:\Users\Admin\.codex\plugins\cache\power-platform-skills\model-apps\2.7.0\scripts\build-model-app.js" --env https://org06cbc9ec.crm5.dynamics.com/ --spec @D:\Coder\metasys-poc\dataverse\app-source\fmc-bms-demo\app-spec.json --stage data --apply`
Result: 3 applied, 67 skipped, 0 failed. All eight tables, declared columns and three relationships were reused; Quick Create was enabled for Building and Equipment.

`pac model genpage generate-types --environment https://org06cbc9ec.crm5.dynamics.com/ --data-sources "fmc_bmsbuilding,fmc_bmsequipment,fmc_bmspoint,fmc_bmsreading,cr3c8_silvernewbmspoint,fmc_syncrequest,fmc_spofile,fmc_spoimportrow" --output-file D:/Coder/metasys-poc/dataverse/app-source/fmc-bms-demo/RuntimeTypes.ts`
Result: `RuntimeTypes.ts` generated successfully for eight live Dataverse data sources.

`node "C:\Users\Admin\.codex\plugins\cache\power-platform-skills\model-apps\2.7.0\scripts\dataverse-request.js" https://org06cbc9ec.crm5.dynamics.com/ GET RetrieveProvisionedLanguages`
Result: LCID 1033 only. Vietnamese copy is authored in the page and sitemap; Dataverse base labels remain English.

`node "C:\Users\Admin\.codex\plugins\cache\power-platform-skills\model-apps\2.7.0\scripts\write-page-plan.js" --spec @D:\Coder\metasys-poc\dataverse\app-source\fmc-bms-demo\app-spec.json --working-dir D:\Coder\metasys-poc\dataverse\app-source\fmc-bms-demo --env https://org06cbc9ec.crm5.dynamics.com/ --app "FMC BMS Demo" --languages "English (1033) only; Vietnamese UI copy in authored page"`
Result: one Dataverse intent page projected to `trung-tam-van-hanh.tsx`.

`node -e "const{lintAppSpec}=require('C:\\Users\\Admin\\.codex\\plugins\\cache\\power-platform-skills\\model-apps\\2.7.0\\scripts\\lib\\spec-lint.js');const s=require('D:\\Coder\\metasys-poc\\dataverse\\app-source\\fmc-bms-demo\\app-spec.json');const r=lintAppSpec(s);console.log(JSON.stringify(r,null,2));"`
Result: `ok=true`, errors=0, warnings=1. The warning is expected because the reused Dataflow output table has publisher prefix `cr3c8_` while this solution uses `fmc_`.

`node "C:\Users\Admin\.codex\plugins\cache\power-platform-skills\model-apps\2.7.0\scripts\preview-app.js" @D:\Coder\metasys-poc\dataverse\app-source\fmc-bms-demo\app-spec.json`
Result: whole-app preview rendered successfully.

`node "C:\Users\Admin\.codex\plugins\cache\power-platform-skills\model-apps\2.7.0\scripts\build-model-app.js" --env https://org06cbc9ec.crm5.dynamics.com/ --spec @D:\Coder\metasys-poc\dataverse\app-source\fmc-bms-demo\app-spec.json`
Result: dry-run succeeded with 96 planned operations and no Dataverse writes.

EnterPlanMode unavailable in Default mode. The concrete preview and engine dry-run are presented in chat for the required single build approval.

## Phase 2 — build, publish and verification

`node build-model-app.js --env https://org06cbc9ec.crm5.dynamics.com/ --spec @D:\Coder\metasys-poc\dataverse\app-source\fmc-bms-demo\app-spec.json --apply --verify --publish --allow-destructive`

Result: build completed with 29 created, 68 reused/skipped and 0 failed across 97 steps. Ten views, eight forms, the app icon, app module, `Trung tâm vận hành BMS` page, finalized sitemap and two demo security roles were published. Verification passed 109/109 components.

`node download-model-app.js --env https://org06cbc9ec.crm5.dynamics.com/ --app fmc_FMCBMSDemo --out D:\Coder\metasys-poc\.artifacts\model-apps\fmc-bms-demo-verify`

Result: independent live readback confirmed eight app tables, one deployed generative page and no dropped sitemap subareas. The downloader also inventoried the live forms and views. Its cross-environment reconstruction warning is covered by the complete PAC solution export below.

`pac solution export --name FMCentralBms --path .artifacts\model-apps\fmc-bms-demo-live.zip --managed false --environment https://org06cbc9ec.crm5.dynamics.com/ --overwrite`

`pac solution unpack --zipfile .artifacts\model-apps\fmc-bms-demo-live.zip --folder .artifacts\model-apps\fmc-bms-demo-solution-20260912 --packagetype Unmanaged`

Result: live unmanaged solution exported and unpacked successfully, then merged into `dataverse/FMCentralBms`. This preserves the exact app module, sitemap, forms, views, roles, web resources and existing integration components in source control.
