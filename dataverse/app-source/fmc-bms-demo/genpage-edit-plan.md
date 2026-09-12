# Genpage Edit Plan

## File Being Edited
- **Absolute path:** D:/Coder/metasys-poc/dataverse/app-source/fmc-bms-demo/trung-tam-van-hanh.tsx
- **App ID:** d19f4897-d227-4df3-8361-988f97c53e89
- **Page ID:** 9d05b7f9-f4c4-4572-b6b8-0220b51812f3
- **Environment:** https://org06cbc9ec.crm5.dynamics.com/
- **Solution / Publisher Prefix:** FMCentralBms / fmc

## Working Directory
D:/Coder/metasys-poc/dataverse/app-source/fmc-bms-demo

## Plugin Root
C:/Users/Admin/.codex/plugins/cache/power-platform-skills/model-apps/2.7.0

## Original Page Context
- **Original prompt (from prompt.txt):** Generative page Trung tâm vận hành BMS
- **Original data sources (from config.json):** fmc_bmsbuilding, fmc_bmsequipment, fmc_bmspoint, fmc_bmsreading, cr3c8_silvernewbmspoint, fmc_syncrequest, fmc_spofile, fmc_spoimportrow
- **Current purpose:** Show bounded Bronze/Silver/SharePoint metrics, latest activity, navigation and catalog-entry actions in the BMS operations center.
- **Live snapshot:** D:/Coder/metasys-poc/.artifacts/model-apps/language-edit-before/9d05b7f9-f4c4-4572-b6b8-0220b51812f3/
- **Source drift:** The downloaded source and checked-in source differ only by a UTF-8 BOM and one final blank line. No implementation drift.

## Entities Used
fmc_bmsbuilding, fmc_bmsequipment, fmc_bmspoint, fmc_bmsreading, cr3c8_silvernewbmspoint, fmc_syncrequest, fmc_spofile, fmc_spoimportrow

## Requested Changes
1. Add an accessible English / Tiếng Việt language selector to the header. Default to English when no valid saved choice exists; remember an explicit selection using a guarded localStorage read/write. Browser language must not override this default.
2. Move all authored page copy into a typed English/Vietnamese dictionary, including headings, KPI captions, navigation buttons, status fallback labels, search text, empty/loading/error states, tooltips and ARIA labels.
3. Format dates and numbers using the selected locale (`en-US` or `vi-VN`). Switch sorting locale consistently. Preserve source record names, paths, IDs and business data verbatim.
4. Make language changes update existing visible errors and nested components immediately. Prefer error codes/translation keys in state to already translated strings. Keep language state independent of data loading so switching language does not trigger extra Dataverse reads or discard filters.
5. Set the authored page container's `lang` attribute. Keep native Microsoft Entra login/logout and navigation behavior. The selector applies to this authored page; standard Power Apps chrome/forms follow the user's enabled Dataverse language settings.

## Connector Changes
No connector bindings. Preserve by omitting --connectors during update.

## Custom API Changes
No custom API bindings. Preserve by omitting --actions during update. Existing navigation to the separate BMS Event Demo web resource remains intact.

## Preservation Constraints
- Keep all eight table queries, selected fields, limits, sort keys and Dataverse logical names intact.
- Preserve shared dashboard cache, in-flight de-duplication, generation guard and explicit refresh.
- Keep all quick actions and row links pointing to existing tables, records and web resource.
- Keep search, client grid sorting, responsive layout and bounded-count explanations.
- Retain actual record content and backend exception details; localize only authored UI and supported status presentation.
- Do not create new entities, records, identities, connectors or pages for this change.

## Design Notes
Use existing Fluent UI V9 and makeStyles/tokens. An explicitly labelled native select or Fluent Select provides clear keyboard interaction. Translate both options' surrounding label and accessibility description. Persisting a language choice is optional when browser storage is unavailable; switching must still work in memory.

The original page was Vietnamese only. English default is an explicit user-requested change, with Vietnamese retained as a selectable language. User authorization to implement and publish already exists; no repeated approval is needed.

## Validation and Deployment
Validate English default, both switch directions, persistence/remount, invalid stored preferences, unavailable storage, translated errors after a switch, locale-aware formatting, and unchanged query counts/navigation.

PAC 2.11.2 upload automatically transpiles and publishes the updated page. Use the existing page ID to avoid duplication:

```powershell
pac model genpage upload --environment https://org06cbc9ec.crm5.dynamics.com/ --app-id d19f4897-d227-4df3-8361-988f97c53e89 --page-id 9d05b7f9-f4c4-4572-b6b8-0220b51812f3 --code-file D:/Coder/metasys-poc/dataverse/app-source/fmc-bms-demo/trung-tam-van-hanh.tsx --name "BMS Operations Center" --prompt "BMS operations dashboard with English and Vietnamese language selection; English is the default." --data-sources "fmc_bmsbuilding,fmc_bmsequipment,fmc_bmspoint,fmc_bmsreading,cr3c8_silvernewbmspoint,fmc_syncrequest,fmc_spofile,fmc_spoimportrow"
```

After successful upload, download to a new artifact directory and compare source (normalizing BOM/trailing whitespace) to verify the intended code reached Dataverse. Export/unpack the affected solution and merge its reviewed generated source. Do not claim browser interaction testing without exercising the interface.

## Relevant Samples
No new visual pattern requires an additional sample; retain the existing dashboard structure.
