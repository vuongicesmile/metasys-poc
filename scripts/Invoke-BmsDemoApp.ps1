[CmdletBinding()]
param([ValidateSet('Status','Deploy','Verify')][string]$Mode = 'Status')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$config = Get-Content (Join-Path $repo 'DataverseSyncWorker/appsettings.json') -Raw | ConvertFrom-Json
$org = 'https://org06cbc9ec.crm5.dynamics.com'
$solution = 'FMCentralBms'
if ($config.Dataverse.Url.TrimEnd('/') -ne $org) { throw 'Unexpected configured environment.' }
$token = (& $config.Dataverse.DeveloperTokenPython $config.Dataverse.DeveloperTokenScript | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $token.Split('.').Count -ne 3) { throw 'Developer authentication failed.' }
$headers = @{ Authorization = "Bearer $token"; Accept = 'application/json'; 'OData-Version' = '4.0'; 'OData-MaxVersion' = '4.0' }
function Invoke-Dv([string]$Path, [string]$Method = 'GET', $Body = $null) {
    $args = @{ Uri = "$org/api/data/v9.2/$Path"; Headers = $headers; Method = $Method; TimeoutSec = 120 }
    if ($null -ne $Body) {
        $args.ContentType = 'application/json; charset=utf-8'
        $args.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 30 -Compress))
    }
    Invoke-RestMethod @args
}
$who = Invoke-Dv 'WhoAmI'
if ($who.OrganizationId -ne 'ab191700-b99e-f111-aaa0-000d3a80bb96') { throw 'Unexpected live organization; no writes made.' }
Write-Host "Verified organization: $($who.OrganizationId); caller: $($who.UserId)"
$sol = Invoke-Dv 'solutions?$select=solutionid,uniquename&$expand=publisherid($select=uniquename,customizationprefix)&$filter=uniquename eq ''FMCentralBms'''
if ($sol.value.Count -ne 1 -or $sol.value[0].publisherid.uniquename -ne 'FMCentralBmsPublisher' -or $sol.value[0].publisherid.customizationprefix -ne 'fmc') { throw 'Solution/publisher mismatch.' }
$headers['MSCRM.SolutionUniqueName'] = $solution
$webResourceMetadata = Invoke-Dv "EntityDefinitions(LogicalName='webresource')?`$select=EntitySetName"
$webResourceSet = $webResourceMetadata.EntitySetName
$apps = Invoke-Dv 'appmodules?$select=appmoduleid,name,uniquename,statecode,statuscode&$filter=uniquename eq ''fmc_FMCBMSDemo'''
$forms = Invoke-Dv 'systemforms?$select=formid,name,type,objecttypecode,formactivationstate&$filter=objecttypecode eq ''fmc_bmsequipment'' and type eq 2'
$meta = Invoke-Dv "EntityDefinitions(LogicalName='fmc_bmsequipment')/Attributes(LogicalName='fmc_buildingid')?`$select=LogicalName,RequiredLevel"
if ($Mode -eq 'Status') {
    $apps.value | ConvertTo-Json -Depth 6
    $forms.value | ConvertTo-Json -Depth 6
    $meta | Select-Object LogicalName,RequiredLevel | ConvertTo-Json -Depth 6
    Invoke-Dv 'sdkmessageprocessingsteps?$select=name,stage,mode,statecode,filteringattributes&$filter=sdkmessageprocessingstepid eq 00029f22-bcac-f111-aaad-00224819a344 or sdkmessageprocessingstepid eq 27894c29-bcac-f111-aaad-00224819a344' | ConvertTo-Json -Depth 6
    return
}
if ($apps.value.Count -gt 1) { throw 'Ambiguous app.' }
$appId = if ($apps.value.Count -eq 1) { $apps.value[0].appmoduleid } else { 'd19f4897-d227-4df3-8361-988f97c53e89' }
$testName = 'Equipment - Plugin Test'
$testForms = @($forms.value | Where-Object name -eq $testName)
if ($testForms.Count -gt 1) { throw 'Ambiguous test form.' }
$testId = if ($testForms.Count -eq 1) { $testForms[0].formid } else { '4e2cf907-5c8b-4fa5-9bfb-55faf81fd889' }
$libraryName = 'fmc_/scripts/EquipmentPluginTest.js'
$resources = Invoke-Dv "${webResourceSet}?`$select=webresourceid,name&`$filter=name eq '$libraryName'"
$resourceId = if ($resources.value.Count -eq 1) { $resources.value[0].webresourceid } else { '4d664e98-1a0d-42c8-9014-d8df1a16e5f7' }
$siteName = 'fmc_FMCBMSDemo'
$sites = Invoke-Dv "sitemaps?`$select=sitemapid,sitemapname&`$filter=sitemapname eq '$siteName'"
$siteId = if ($sites.value.Count -eq 1) { $sites.value[0].sitemapid } else { 'ae38d1aa-bf67-4ae7-ad9f-f1263a6b56f0' }
if ($resources.value.Count -gt 1 -or $sites.value.Count -gt 1) { throw 'Ambiguous owned components.' }
function Add-Solution([string]$Id, [int]$Type) {
    $null = Invoke-Dv 'AddSolutionComponent' POST @{ ComponentId = $Id; ComponentType = $Type; SolutionUniqueName = $solution; AddRequiredComponents = $false }
}
if ($Mode -eq 'Deploy') {
    if ($meta.RequiredLevel.Value -ne 'ApplicationRequired') { throw 'Expected ApplicationRequired Building; inspect before deploying test form.' }
    $js = Get-Content (Join-Path $repo 'dataverse/app-source/EquipmentPluginTest.js') -Raw -Encoding UTF8
    $resourceBody = @{ name = $libraryName; displayname = 'FMC Equipment Plugin Test (Developer only)'; webresourcetype = 3; content = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($js)) }
    if ($resources.value.Count -eq 0) { $resourceBody.webresourceid = $resourceId; $null = Invoke-Dv $webResourceSet POST $resourceBody }
    else { $null = Invoke-Dv "$webResourceSet($resourceId)" PATCH $resourceBody }
    Add-Solution $resourceId 61
    if ($testForms.Count -eq 0) {
        $null = Invoke-Dv 'systemforms(16116cbe-739c-40c0-820f-985ae43ca22d)/Microsoft.Dynamics.CRM.CopySystemForm' POST @{ Target = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.systemform'; formid = $testId; name = $testName; description = 'Developer-only UI test for RequireEquipmentBuilding. Does not change table requirements.' } }
    }
    $form = Invoke-Dv "systemforms($testId)?`$select=formid,formxml,objecttypecode,name"
    if ($form.objecttypecode -ne 'fmc_bmsequipment' -or $form.name -ne $testName) { throw 'Unexpected test form target.' }
    [xml]$xml = $form.formxml
    $root = $xml.DocumentElement
    $libraries = $root.SelectSingleNode('formLibraries')
    if (-not $libraries) { $libraries = $xml.CreateElement('formLibraries'); $null = $root.AppendChild($libraries) }
    if (-not $libraries.SelectSingleNode("Library[@name='$libraryName']")) {
        $library = $xml.CreateElement('Library'); $library.SetAttribute('name',$libraryName); $library.SetAttribute('libraryUniqueId',"{$resourceId}"); $null = $libraries.AppendChild($library)
    }
    $events = $root.SelectSingleNode('events')
    if (-not $events) { $events = $xml.CreateElement('events'); $null = $root.AppendChild($events) }
    $onload = $events.SelectSingleNode("event[@name='onload']")
    if (-not $onload) { $onload = $xml.CreateElement('event'); $onload.SetAttribute('name','onload'); $onload.SetAttribute('application','false'); $onload.SetAttribute('active','false'); $null = $events.AppendChild($onload) }
    $handlers = $onload.SelectSingleNode('Handlers')
    if (-not $handlers) { $handlers = $xml.CreateElement('Handlers'); $null = $onload.AppendChild($handlers) }
    if (-not $handlers.SelectSingleNode("Handler[@functionName='FMC.EquipmentPluginTest.onLoad']")) {
        $handler = $xml.CreateElement('Handler')
        foreach ($pair in @{ functionName='FMC.EquipmentPluginTest.onLoad'; libraryName=$libraryName; handlerUniqueId='{c7c8a982-5128-4c14-a239-85e922edb60d}'; enabled='true'; parameters=''; passExecutionContext='true' }.GetEnumerator()) { $handler.SetAttribute($pair.Key,$pair.Value) }
        $null = $handlers.AppendChild($handler)
    }
    $null = Invoke-Dv "systemforms($testId)" PATCH @{ formxml=$xml.OuterXml; formactivationstate=1 }
    Add-Solution $testId 60
    $sitemap = '<SiteMap><Area Id="fmc_bms" ShowGroups="true"><Titles><Title LCID="1033" Title="FMC BMS Demo" /></Titles><Group Id="fmc_catalog"><Titles><Title LCID="1033" Title="BMS Catalog" /></Titles><SubArea Id="fmc_buildings" Entity="fmc_bmsbuilding"><Titles><Title LCID="1033" Title="BMS Buildings" /></Titles></SubArea><SubArea Id="fmc_equipment" Entity="fmc_bmsequipment"><Titles><Title LCID="1033" Title="BMS Equipment" /></Titles></SubArea></Group></Area></SiteMap>'
    if ($sites.value.Count -eq 0) { $null = Invoke-Dv 'sitemaps' POST @{ sitemapid=$siteId; sitemapname=$siteName; sitemapnameunique=$siteName; sitemapxml=$sitemap } }
    else { $null = Invoke-Dv "sitemaps($siteId)" PATCH @{ sitemapxml=$sitemap } }
    Add-Solution $siteId 62
    if ($apps.value.Count -eq 0) {
        $null = Invoke-Dv 'appmodules' POST @{ appmoduleid=$appId; name='FMC BMS Demo'; uniquename='fmc_FMCBMSDemo'; description='Developer BMS catalog and UI plugin testing. No SQL sync is triggered by this app.'; webresourceid='953b9fac-1e5e-e611-80d6-00155ded156f'; clienttype=4 }
    }
    Add-Solution $appId 80
    $components = @(@{ '@odata.type'='Microsoft.Dynamics.CRM.sitemap'; sitemapid=$siteId })
    foreach ($table in @('fmc_bmsbuilding','fmc_bmsequipment','fmc_bmspoint')) {
        $tableMeta = Invoke-Dv "EntityDefinitions(LogicalName='$table')?`$select=MetadataId,LogicalName,PrimaryIdAttribute"
        $entityComponent = @{ '@odata.type'="Microsoft.Dynamics.CRM.$table" }
        $entityComponent[$tableMeta.PrimaryIdAttribute] = $tableMeta.MetadataId
        $components += $entityComponent
        $views = Invoke-Dv "savedqueries?`$select=savedqueryid&`$filter=returnedtypecode eq '$table' and statecode eq 0 and (querytype eq 0 or querytype eq 2 or querytype eq 64)"
        foreach ($view in $views.value) { $components += @{ '@odata.type'='Microsoft.Dynamics.CRM.savedquery'; savedqueryid=$view.savedqueryid } }
        if ($table -eq 'fmc_bmspoint') {
            $pointForms = Invoke-Dv 'systemforms?$select=formid&$filter=objecttypecode eq ''fmc_bmspoint'' and type eq 2 and formactivationstate eq 1'
            foreach ($pointForm in $pointForms.value) { $components += @{ '@odata.type'='Microsoft.Dynamics.CRM.systemform'; formid=$pointForm.formid } }
        }
    }
    foreach ($id in @('2b09585b-c7e9-410b-891e-f38551a9968e','16116cbe-739c-40c0-820f-985ae43ca22d',$testId)) { $components += @{ '@odata.type'='Microsoft.Dynamics.CRM.systemform'; formid=$id } }
    $existingComponents = Invoke-Dv "RetrieveAppComponents(AppModuleId=$appId)"
    # Remove only the incorrect metadata-table reference introduced by an early draft of this deployment.
    if (@($existingComponents.value | Where-Object { $_.componenttype -eq 1 -and $_.objectid -eq '9d0f025b-11ce-40f1-a7f4-a8088f4985aa' }).Count -gt 0) {
        $null = Invoke-Dv 'RemoveAppComponents' POST @{ AppId=$appId; Components=@(@{ '@odata.type'='Microsoft.Dynamics.CRM.entity'; entityid='9d0f025b-11ce-40f1-a7f4-a8088f4985aa' }) }
    }
    $missingComponents = @($components | Where-Object { $componentId = @($_.GetEnumerator() | Where-Object Key -ne '@odata.type')[0].Value; $componentId -notin @($existingComponents.value.objectid) })
    if ($missingComponents.Count -gt 0) { $null = Invoke-Dv 'AddAppComponents' POST @{ AppId=$appId; Components=$missingComponents } }
    $validation = Invoke-Dv "ValidateApp(AppModuleId=$appId)"
    $validation.AppValidationResponse | ConvertTo-Json -Depth 12 -Compress
    if (-not $validation.AppValidationResponse.ValidationSuccess) { throw 'App validation failed; not publishing.' }
    $null = Invoke-Dv 'PublishXml' POST @{ ParameterXml="<importexportxml><entities><entity>fmc_bmsequipment</entity></entities><webresources><webresource>$resourceId</webresource></webresources><sitemaps><sitemap>$siteId</sitemap></sitemaps><appmodules><appmodule>$appId</appmodule></appmodules></importexportxml>" }
}
$published = Invoke-Dv "appmodules($appId)?`$select=appmoduleid,name,uniquename,statecode,statuscode,publishedon"
$validation = Invoke-Dv "ValidateApp(AppModuleId=$appId)"
$form = Invoke-Dv "systemforms($testId)?`$select=formid,name,formxml,formactivationstate"
$liveJs = Invoke-Dv "$webResourceSet($resourceId)?`$select=content,name"
$metaAfter = Invoke-Dv "EntityDefinitions(LogicalName='fmc_bmsequipment')/Attributes(LogicalName='fmc_buildingid')?`$select=RequiredLevel"
if ($metaAfter.RequiredLevel.Value -ne 'ApplicationRequired' -or -not $form.formxml.Contains('FMC.EquipmentPluginTest.onLoad') -or -not $validation.AppValidationResponse.ValidationSuccess) { throw 'Verification failed.' }
$expectedJs = Get-Content (Join-Path $repo 'dataverse/app-source/EquipmentPluginTest.js') -Raw -Encoding UTF8
if ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($liveJs.content)) -ne $expectedJs) { throw 'Published webresource content mismatch.' }
$normalForm = Invoke-Dv 'systemforms(16116cbe-739c-40c0-820f-985ae43ca22d)?$select=formxml'
if ($normalForm.formxml.Contains($libraryName)) { throw 'Test script must not be attached to the normal form.' }
$steps = Invoke-Dv 'sdkmessageprocessingsteps?$select=name,stage,mode,statecode,filteringattributes&$filter=sdkmessageprocessingstepid eq 00029f22-bcac-f111-aaad-00224819a344 or sdkmessageprocessingstepid eq 27894c29-bcac-f111-aaad-00224819a344'
if ($steps.value.Count -ne 2 -or @($steps.value | Where-Object { $_.stage -ne 10 -or $_.mode -ne 0 -or $_.statecode -ne 0 }).Count -gt 0) { throw 'Expected two enabled synchronous PreValidation plugin steps.' }
$membership = Invoke-Dv "solutioncomponents?`$select=objectid,componenttype,rootcomponentbehavior&`$filter=_solutionid_value eq $($sol.value[0].solutionid) and (componenttype eq 80 or componenttype eq 62 or componenttype eq 61 or componenttype eq 60 or componenttype eq 1)"
foreach ($id in @($appId,$siteId,$resourceId)) { if ($id -notin @($membership.value.objectid)) { throw "Missing solution component: $id" } }
# A full-table root includes its forms without a separate root-component row.
if ($testId -notin @($membership.value.objectid)) {
    $equipmentRoot = @($membership.value | Where-Object { $_.componenttype -eq 1 -and $_.objectid -eq '729bc6da-02ac-f111-aaad-00224819a344' -and $_.rootcomponentbehavior -eq 0 })
    if ($equipmentRoot.Count -ne 1) { throw 'Test form lacks explicit membership or a full Equipment table root.' }
}
if (-not $published.publishedon) { throw 'App has not been published.' }
$published | ConvertTo-Json -Depth 6
$validation.AppValidationResponse | ConvertTo-Json -Depth 12 -Compress
(Invoke-Dv "RetrieveAppComponents(AppModuleId=$appId)").value | Select-Object componenttype,objectid | Format-Table
Write-Host "APP_URL=$org/main.aspx?appid=$appId"
Write-Host "TEST_URL=$org/main.aspx?appid=$appId&pagetype=entityrecord&etn=fmc_bmsequipment&formid=$testId"
Write-Host 'Verified: live app, validation, test form handler, script content, unchanged ApplicationRequired column. No data sync or security role changes.'
