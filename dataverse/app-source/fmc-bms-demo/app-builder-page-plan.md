# Genpage Plan

## User Requirements
English-first BMS demo with an English/Vietnamese page selector, catalog entry, Bronze-to-Silver data, SQL sync and SharePoint file ingestion.

## Working Directory
D:/Coder/metasys-poc/dataverse/app-source/fmc-bms-demo

## Plugin Root
C:/Users/Admin/.codex/plugins/cache/power-platform-skills/model-apps/2.7.0

## Environment
- URL: https://org06cbc9ec.crm5.dynamics.com/
- App: FMC BMS Demo
- Languages: English (1033); English and Vietnamese in authored pages, English default
- Solution: FMCentralBms
- Publisher Prefix: fmc
- Mode: app-builder

## Pages
| Page | Key | File | Purpose | Entities |
|------|-----|------|---------|----------|
| BMS Operations Center | trung-tam-van-hanh | trung-tam-van-hanh.tsx | English-first landing page with an English/Vietnamese selector. Persist the choice in localStorage key fmc.bms.language, fall back to English if unavailable, and localize static copy, statuses, date and number formats without changing business data. Show the signed-in Microsoft Entra user and explain that sign-out is available from the Power Apps profile menu. Present KPI cards for Buildings, Equipment, current Bronze Points, retained Bronze Readings, Silver rows, Sync Requests and SharePoint files. Show only the newest five Bronze points, newest five sync requests and newest SharePoint receipt. Add a Bronze-to-Silver pipeline strip and quick actions that open create forms for fmc_bmsbuilding and fmc_bmsequipment, open fmc_/pages/BmsEventDemo.html, and open the entity lists. Query only counts and the first small page of large tables; never enumerate all elastic readings. Use Fluent UI V9 with responsive cards, loading, empty, error and refresh states. | fmc_bmsbuilding, fmc_bmsequipment, fmc_bmspoint, fmc_bmsreading, cr3c8_silvernewbmspoint, fmc_syncrequest, fmc_spofile, fmc_spoimportrow |

## Entity Creation Required
No entity creation required — all entities already exist.

## Existing Entities
fmc_bmsbuilding, fmc_bmsequipment, fmc_bmspoint, fmc_bmsreading, cr3c8_silvernewbmspoint, fmc_syncrequest, fmc_spofile, fmc_spoimportrow

## Connector Bindings
No connector bindings.

## Design Preferences
- Styling: Fluent UI V9 tokens — Accent color: #0f6cbd; Density: comfortable; Corner radius: medium; Dark mode: system
- Layout: cards layout, responsive
- Features: Search, sorting and filtering where the page lists records
- Accessibility: WCAG AA (ARIA labels, keyboard navigation, semantic HTML)

## Relevant Samples
| Page | Sample | Reason |
|------|--------|--------|
| BMS Operations Center | 9-list-with-caching.tsx | Dataverse-bound page: queryTable + DataTable rows with the on-mount de-dupe cache |

## Per-Page Specifications

### BMS Operations Center
- **Key:** trung-tam-van-hanh
- **File:** trung-tam-van-hanh.tsx
- **Purpose:** English-first landing page with an English/Vietnamese selector. Persist the choice in localStorage key fmc.bms.language, fall back to English if unavailable, and localize static copy, statuses, date and number formats without changing business data. Show the signed-in Microsoft Entra user and explain that sign-out is available from the Power Apps profile menu. Present KPI cards for Buildings, Equipment, current Bronze Points, retained Bronze Readings, Silver rows, Sync Requests and SharePoint files. Show only the newest five Bronze points, newest five sync requests and newest SharePoint receipt. Add a Bronze-to-Silver pipeline strip and quick actions that open create forms for fmc_bmsbuilding and fmc_bmsequipment, open fmc_/pages/BmsEventDemo.html, and open the entity lists. Query only counts and the first small page of large tables; never enumerate all elastic readings. Use Fluent UI V9 with responsive cards, loading, empty, error and refresh states.
- **Entities:** fmc_bmsbuilding, fmc_bmsequipment, fmc_bmspoint, fmc_bmsreading, cr3c8_silvernewbmspoint, fmc_syncrequest, fmc_spofile, fmc_spoimportrow
- **Needs caching:** true
- **Key Features:** English-first landing page with an English/Vietnamese selector. Persist the choice in localStorage key fmc.bms.language, fall back to English if unavailable, and localize static copy, statuses, date and number formats without changing business data. Show the signed-in Microsoft Entra user and explain that sign-out is available from the Power Apps profile menu. Present KPI cards for Buildings, Equipment, current Bronze Points, retained Bronze Readings, Silver rows, Sync Requests and SharePoint files. Show only the newest five Bronze points, newest five sync requests and newest SharePoint receipt. Add a Bronze-to-Silver pipeline strip and quick actions that open create forms for fmc_bmsbuilding and fmc_bmsequipment, open fmc_/pages/BmsEventDemo.html, and open the entity lists. Query only counts and the first small page of large tables; never enumerate all elastic readings. Use Fluent UI V9 with responsive cards, loading, empty, error and refresh states.
- **Components:** Fluent UI V9 (unsized Regular/Filled icons only)
- **Layout:** cards layout, responsive — responsive flexbox/grid with relative units (never 100vh/100vw)
- **Data Binding:** dataApi.queryTable / retrieveRow over the entities above
- **Interactions:** In-page interactions only (no cross-page navigation)
