[CmdletBinding()]
param([ValidateSet('Status','Deploy','Verify','Test')][string]$Mode = 'Status')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$config = Get-Content (Join-Path $repo 'DataverseSyncWorker/appsettings.json') -Raw | ConvertFrom-Json
$org = 'https://org06cbc9ec.crm5.dynamics.com'
$solution = 'FMCentralBms'
$apiName = 'fmc_RequestBmsSync'
$flowName = 'FMC - App Request BMS Sync Event'
$resourceName = 'fmc_/pages/BmsEventDemo.html'
if ($config.Dataverse.Url.TrimEnd('/') -ne $org) { throw 'Unexpected environment.' }
$token = (& $config.Dataverse.DeveloperTokenPython $config.Dataverse.DeveloperTokenScript | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $token.Split('.').Count -ne 3) { throw 'Authentication failed.' }
$headers = @{Authorization="Bearer $token"; Accept='application/json'; 'OData-Version'='4.0'; 'OData-MaxVersion'='4.0'}
function Invoke-DemoDv([string]$Path, [string]$Method='GET', $Body=$null) {
    $requestArgs = @{Uri="$org/api/data/v9.2/$Path"; Headers=$headers; Method=$Method; TimeoutSec=60}
    if ($null -ne $Body) {
        $requestArgs.ContentType='application/json; charset=utf-8'
        $requestArgs.Body=[Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 60 -Compress))
    }
    Invoke-RestMethod @requestArgs
}
function Ensure-DemoRow($Set, $Primary, $Filter, $Body) {
    $found = Invoke-DemoDv "${Set}?`$select=$Primary&`$filter=$Filter"
    if ($found.value.Count -gt 1) { throw "Ambiguous $Set target." }
    if ($found.value.Count -eq 1) { return $found.value[0].$Primary }
    $id = [guid]::NewGuid().ToString('D')
    $Body[$Primary]=$id
    $null = Invoke-DemoDv $Set POST $Body
    return $id
}
function Add-DemoComponent($Id, $Type) {
    $null=Invoke-DemoDv 'AddSolutionComponent' POST @{ComponentId=$Id;ComponentType=$Type;SolutionUniqueName=$solution;AddRequiredComponents=$true}
}
$who=Invoke-DemoDv 'WhoAmI'
if ($who.OrganizationId -ne 'ab191700-b99e-f111-aaa0-000d3a80bb96') { throw 'Wrong live organization.' }
$sol=Invoke-DemoDv 'solutions?$select=solutionid&$expand=publisherid($select=uniquename,customizationprefix)&$filter=uniquename eq ''FMCentralBms'''
if ($sol.value.Count -ne 1 -or $sol.value[0].publisherid.uniquename -ne 'FMCentralBmsPublisher' -or $sol.value[0].publisherid.customizationprefix -ne 'fmc') { throw 'Wrong solution/publisher.' }
$app=(Invoke-DemoDv 'appmodules?$select=appmoduleid,name&$filter=uniquename eq ''fmc_FMCBMSDemo''').value
if ($app.Count -ne 1) { throw 'Expected one FMC BMS Demo app.' }
$headers['MSCRM.SolutionUniqueName']=$solution
$resourceSet=(Invoke-DemoDv "EntityDefinitions(LogicalName='webresource')?`$select=EntitySetName").EntitySetName
if ($Mode -eq 'Deploy') {
    $registeredApi=(Invoke-DemoDv "customapis?`$select=customapiid,allowedcustomprocessingsteptype&`$filter=uniquename eq '$apiName'").value
    if ($registeredApi.Count -ne 1 -or $registeredApi[0].allowedcustomprocessingsteptype -ne 1) {
        throw 'Deploy the fmc_RequestBmsSync Custom API with Async Only processing before the app demo.'
    }
    $apiId=$registeredApi[0].customapiid
    $catalogId=Ensure-DemoRow 'catalogs' 'catalogid' "uniquename eq 'fmc_BmsDemoEvents'" @{name='FMC BMS Demo Events';displayname='FMC BMS Demo';description='Business events exposed by the FMC BMS Demo solution.';uniquename='fmc_BmsDemoEvents'}
    $categoryId=Ensure-DemoRow 'catalogs' 'catalogid' "uniquename eq 'fmc_BmsAppEvents'" @{name='FMC BMS App Events';displayname='App Events';description='Events raised by commands in the FMC BMS Demo app.';uniquename='fmc_BmsAppEvents';'ParentCatalogId@odata.bind'="/catalogs($catalogId)"}
    $null=Ensure-DemoRow 'catalogassignments' 'catalogassignmentid' "_catalogid_value eq $categoryId and _object_value eq $apiId" @{
        name='Request BMS Sync';'CatalogId@odata.bind'="/catalogs($categoryId)";'CustomAPIId@odata.bind'="/customapis($apiId)"
    }
    # Reuse the already configured connection reference, never embed credentials in the page.
    $connection=(Invoke-DemoDv "connectionreferences?`$select=connectionid,connectionreferencelogicalname&`$filter=connectionreferencelogicalname eq '$($config.Dataverse.PowerAutomateConnectionReference)'").value
    if ($connection.Count -ne 1 -or -not $connection[0].connectionid) { throw 'Missing Dataverse connection reference.' }
    $connector='/providers/Microsoft.PowerApps/apis/shared_commondataserviceforapps'
    $definition=@{
        '$schema'='https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#';contentVersion='1.0.0.0'
        parameters=@{'$connections'=@{defaultValue=@{};type='Object'};'$authentication'=@{defaultValue=@{};type='SecureObject'}}
        triggers=@{When_BMS_demo_requested=@{
            type='OpenApiConnectionWebhook'
            inputs=@{host=@{connectionName='shared_commondataserviceforapps';operationId='BusinessEventsTrigger';apiId=$connector};parameters=@{catalog=$catalogId;category=$categoryId;'subscriptionRequest/entityname'='none';'subscriptionRequest/sdkmessagename'=$apiName};authentication="@parameters('`$authentication')"}
        }}
        actions=@{Demo_received=@{type='Compose';runAfter=@{};inputs=@{message='Button -> Custom API -> Power Automate: OK';receivedAt='@utcNow()';event='@triggerBody()';demoOnly=$true}}}
    }
    $clientData=@{properties=@{connectionReferences=@{shared_commondataserviceforapps=@{runtimeSource='embedded';connection=@{name=$connection[0].connectionid;connectionReferenceLogicalName=$connection[0].connectionreferencelogicalname};api=@{name='shared_commondataserviceforapps'}}};definition=$definition};schemaVersion='1.0.0.0'} | ConvertTo-Json -Depth 60 -Compress
    $flowId=Ensure-DemoRow 'workflows' 'workflowid' "name eq '$flowName' and category eq 5 and type eq 1" @{category=5;type=1;name=$flowName;primaryentity='none';description='Records successful fmc_RequestBmsSync calls from the demo app. The Custom API queues work; the worker performs SQL synchronization.';clientdata=$clientData}
    $liveFlow=Invoke-DemoDv "workflows($flowId)?`$select=statecode,clientdata"
    if ($liveFlow.clientdata -ne $clientData) {
        if ($liveFlow.statecode -eq 1) { $null=Invoke-DemoDv "workflows($flowId)" PATCH @{statecode=0} }
        $null=Invoke-DemoDv "workflows($flowId)" PATCH @{clientdata=$clientData}
    }
    Add-DemoComponent $flowId 29
    $null=Invoke-DemoDv "workflows($flowId)" PATCH @{statecode=1}
    $html=Get-Content (Join-Path $repo 'dataverse/app-source/BmsEventDemo.html') -Raw -Encoding UTF8
    $html=$html.Replace('__FLOW_ID__',$flowId).Replace('__ENVIRONMENT_ID__','5abcb0e5-99b2-e51f-aa0e-90d84405798b')
    $resourceBody=@{name=$resourceName;displayname='BMS Power Automate Event Demo';webresourcetype=1;content=[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($html))}
    $resourceId=Ensure-DemoRow $resourceSet 'webresourceid' "name eq '$resourceName'" $resourceBody
    $null=Invoke-DemoDv "${resourceSet}($resourceId)" PATCH @{content=$resourceBody.content}
    Add-DemoComponent $resourceId 61
    $sites=(Invoke-DemoDv 'sitemaps?$select=sitemapid,sitemapxml&$filter=sitemapname eq ''fmc_FMCBMSDemo''').value
    if ($sites.Count -ne 1) { throw 'Expected one demo sitemap.' }
    [xml]$site=$sites[0].sitemapxml
    $group=$site.SelectSingleNode('//Group[@Id="fmc_catalog"]')
    if (-not $group) { throw 'Demo app catalog group missing.' }
    $area=$group.SelectSingleNode('SubArea[@Id="fmc_eventdemo"]')
    if (-not $area) { $area=$site.CreateElement('SubArea');$area.SetAttribute('Id','fmc_eventdemo');$null=$group.AppendChild($area) }
    $area.SetAttribute('Url',"`$webresource:$resourceName")
    $area.SetAttribute('Client','All')
    if (-not $area.SelectSingleNode('Titles')) {
        $titles=$site.CreateElement('Titles');$title=$site.CreateElement('Title');$title.SetAttribute('LCID','1033');$title.SetAttribute('Title','Power Automate Demo');$null=$titles.AppendChild($title);$null=$area.AppendChild($titles)
    }
    $null=Invoke-DemoDv "sitemaps($($sites[0].sitemapid))" PATCH @{sitemapxml=$site.OuterXml}
    Add-DemoComponent $sites[0].sitemapid 62
    $validation=Invoke-DemoDv "ValidateApp(AppModuleId=$($app[0].appmoduleid))"
    if (-not $validation.AppValidationResponse.ValidationSuccess) { throw ($validation | ConvertTo-Json -Depth 10) }
    $null=Invoke-DemoDv 'PublishXml' POST @{ParameterXml="<importexportxml><webresources><webresource>$resourceId</webresource></webresources><sitemaps><sitemap>$($sites[0].sitemapid)</sitemap></sitemaps><appmodules><appmodule>$($app[0].appmoduleid)</appmodule></appmodules></importexportxml>"}
}
$api=Invoke-DemoDv "customapis?`$select=customapiid,uniquename,allowedcustomprocessingsteptype,bindingtype,isfunction,isprivate,executeprivilegename&`$filter=uniquename eq '$apiName'"
$flow=Invoke-DemoDv "workflows?`$select=workflowid,name,statecode,clientdata&`$filter=name eq '$flowName' and category eq 5 and type eq 1"
$resource=Invoke-DemoDv "${resourceSet}?`$select=webresourceid,name,content&`$filter=name eq '$resourceName'"
$callbackId=$null
$keyStatus=$null
if ($Mode -ne 'Status') {
    if ($api.value.Count -ne 1 -or $flow.value.Count -ne 1 -or $resource.value.Count -ne 1) { throw 'Missing demo component.' }
    if ($api.value[0].allowedcustomprocessingsteptype -ne 1 -or $flow.value[0].statecode -ne 1) { throw 'Demo is not active.' }
    $flowId=$flow.value[0].workflowid
    $trigger=($flow.value[0].clientdata | ConvertFrom-Json).properties.definition.triggers.When_BMS_demo_requested
    if ($trigger.inputs.host.operationId -ne 'BusinessEventsTrigger' -or
        $trigger.inputs.parameters.'subscriptionRequest/sdkmessagename' -ne $apiName) { throw 'Flow trigger differs from the Custom API business event.' }
    $callbacks=(Invoke-DemoDv "callbackregistrations?`$select=callbackregistrationid,sdkmessagename,entityname,softdeletestatus&`$filter=sdkmessagename eq '$apiName'").value
    if ($callbacks.Count -ne 1 -or $callbacks[0].entityname -ne 'none' -or $callbacks[0].softdeletestatus -ne 0) { throw 'Active Power Automate callback registration missing.' }
    $callbackId=$callbacks[0].callbackregistrationid
    $activeKey=(Invoke-DemoDv "EntityDefinitions(LogicalName='fmc_syncrequest')/Keys?`$select=LogicalName,EntityKeyIndexStatus").value | Where-Object LogicalName -eq 'fmc_syncrequest_activekey'
    if ($null -eq $activeKey -or $activeKey.EntityKeyIndexStatus -ne 'Active') { throw 'Active sync-request alternate key missing.' }
    $keyStatus=$activeKey.EntityKeyIndexStatus
    $expected=(Get-Content (Join-Path $repo 'dataverse/app-source/BmsEventDemo.html') -Raw -Encoding UTF8).Replace('__FLOW_ID__',$flowId).Replace('__ENVIRONMENT_ID__','5abcb0e5-99b2-e51f-aa0e-90d84405798b')
    if ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($resource.value[0].content)) -ne $expected) { throw 'Page differs from source.' }
    $liveSite=(Invoke-DemoDv 'sitemaps?$select=sitemapxml&$filter=sitemapname eq ''fmc_FMCBMSDemo''').value
    if ($liveSite.Count -ne 1 -or -not $liveSite[0].sitemapxml.Contains('Id="fmc_eventdemo"')) { throw 'Demo sitemap entry missing.' }
}
if ($Mode -eq 'Test') {
    $testId=[guid]::NewGuid().ToString('D')
    $first=Invoke-DemoDv $apiName POST @{ClientRequestId=$testId}
    $retry=Invoke-DemoDv $apiName POST @{ClientRequestId=$testId}
    if ($first.RequestId -ne $retry.RequestId -or $retry.Created) { throw 'Custom API retry was not idempotent.' }
    Write-Output "DEMO_EVENT_ACCEPTED ClientRequestId=$testId RequestId=$($first.RequestId) FirstCreated=$($first.Created) RetryCreated=$($retry.Created) Status=$($retry.Status)"
}
[pscustomobject]@{Organization=$who.OrganizationId;API=$api.value.uniquename;Flow=$flow.value.name;FlowId=$flow.value.workflowid;FlowState=$flow.value.statecode;CallbackId=$callbackId;ActiveKey=$keyStatus;Page=$resource.value.name;AppUrl="$org/main.aspx?appid=$($app[0].appmoduleid)"} | Format-List
