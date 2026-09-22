[CmdletBinding()]
param([ValidateSet('Deploy','Verify','Remove')][string]$Mode = 'Verify')

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$org = 'https://org06cbc9ec.crm5.dynamics.com'
$expectedOrganizationId = 'ab191700-b99e-f111-aaa0-000d3a80bb96'
$solutionName = 'FMCentralBms'
$formId = '47330992-406e-4c8c-896e-ac796e53c55f'
$formName = 'BMS Point - Demo'
$appId = 'd19f4897-d227-4df3-8361-988f97c53e89'
$controlName = 'fmc_FMC.Bms.ScrollDownButton'
$anchorControlId = 'fmc_scroll_down_anchor'
$anchorUniqueId = '{cb1b74b4-3a9d-4cba-a811-6c711f217f17}'
$anchorCellId = '{2586f69f-50d9-4e69-9577-0297cd353d4e}'

$config = Get-Content (Join-Path $repo 'Dataverse.SyncWorker/Dataverse.SyncWorker.App/appsettings.json') -Raw | ConvertFrom-Json
if ($config.Dataverse.Url.TrimEnd('/') -ne $org) { throw 'Unexpected configured Dataverse URL.' }
$token = (& $config.Dataverse.DeveloperTokenPython $config.Dataverse.DeveloperTokenScript | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $token.Split('.').Count -ne 3) { throw 'Developer authentication failed.' }
$headers = @{ Authorization = "Bearer $token"; Accept = 'application/json'; 'OData-Version' = '4.0'; 'OData-MaxVersion' = '4.0' }

function Invoke-Dv([string]$Path, [string]$Method = 'GET', $Body = $null) {
    $request = @{ Uri = "$org/api/data/v9.2/$Path"; Headers = $headers; Method = $Method; TimeoutSec = 120 }
    if ($null -ne $Body) {
        $request.ContentType = 'application/json; charset=utf-8'
        $request.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 30 -Compress))
    }
    Invoke-RestMethod @request
}

$who = Invoke-Dv 'WhoAmI'
if ($who.OrganizationId -ne $expectedOrganizationId) { throw 'Unexpected live organization; no form write performed.' }
$solution = Invoke-Dv "solutions?`$select=solutionid,uniquename&`$expand=publisherid(`$select=uniquename,customizationprefix)&`$filter=uniquename eq '$solutionName'"
if ($solution.value.Count -ne 1 -or $solution.value[0].publisherid.uniquename -ne 'FMCentralBmsPublisher' -or $solution.value[0].publisherid.customizationprefix -ne 'fmc') {
    throw 'Solution/publisher mismatch.'
}
$headers['MSCRM.SolutionUniqueName'] = $solutionName

$controls = Invoke-Dv "customcontrols?`$select=customcontrolid,name,version&`$filter=name eq '$controlName'"
if ($controls.value.Count -ne 1 -or $controls.value[0].version -ne '1.0.0') { throw 'Expected ScrollDownButton 1.0.0 was not found.' }
$controlId = $controls.value[0].customcontrolid
$membership = Invoke-Dv "solutioncomponents?`$select=objectid,componenttype&`$filter=_solutionid_value eq $($solution.value[0].solutionid) and componenttype eq 66 and objectid eq $controlId"
if ($membership.value.Count -ne 1) { throw 'ScrollDownButton is not a root component of FMCentralBms.' }

$form = Invoke-Dv "systemforms($formId)?`$select=formid,name,objecttypecode,type,formactivationstate,formxml"
if ($form.name -ne $formName -or $form.objecttypecode -ne 'fmc_bmspoint' -or $form.type -ne 2 -or $form.formactivationstate -ne 1) {
    throw 'Unexpected target form.'
}

if ($Mode -eq 'Deploy') {
    [xml]$xml = $form.formxml
    $root = $xml.DocumentElement
    $existingAnchor = $root.SelectSingleNode(".//control[@id='$anchorControlId']")
    if (-not $existingAnchor) {
        $section = $root.SelectSingleNode(".//section[@name='section_0_0']")
        if (-not $section) { throw 'Target section section_0_0 was not found.' }
        $rows = $section.SelectSingleNode('rows')
        if (-not $rows) { throw 'Target section has no rows node.' }
        $row = $xml.CreateElement('row')
        $cell = $xml.CreateElement('cell')
        foreach ($pair in @{ id=$anchorCellId; showlabel='false'; visible='true'; colspan='1'; rowspan='1' }.GetEnumerator()) { $cell.SetAttribute($pair.Key, $pair.Value) }
        $labels = $xml.CreateElement('labels')
        $labelEn = $xml.CreateElement('label'); $labelEn.SetAttribute('description','Scroll down'); $labelEn.SetAttribute('languagecode','1033'); $null = $labels.AppendChild($labelEn)
        $labelVi = $xml.CreateElement('label'); $labelVi.SetAttribute('description','Scroll down'); $labelVi.SetAttribute('languagecode','1066'); $null = $labels.AppendChild($labelVi)
        $anchor = $xml.CreateElement('control')
        foreach ($pair in @{ id=$anchorControlId; uniqueid=$anchorUniqueId; classid='{4273EDBD-AC1D-40D3-9FB2-095C621B552D}'; datafieldname='fmc_name'; disabled='true' }.GetEnumerator()) { $anchor.SetAttribute($pair.Key, $pair.Value) }
        $null = $cell.AppendChild($labels); $null = $cell.AppendChild($anchor); $null = $row.AppendChild($cell); $null = $rows.AppendChild($row)
    }

    $descriptions = $root.SelectSingleNode('controlDescriptions')
    if (-not $descriptions) { $descriptions = $xml.CreateElement('controlDescriptions'); $null = $root.AppendChild($descriptions) }
    $description = $descriptions.SelectSingleNode("controlDescription[@forControl='$anchorUniqueId']")
    if (-not $description) {
        $description = $xml.CreateElement('controlDescription'); $description.SetAttribute('forControl', $anchorUniqueId); $null = $descriptions.AppendChild($description)
        $default = $xml.CreateElement('customControl'); $default.SetAttribute('id','{4273EDBD-AC1D-40D3-9FB2-095C621B552D}')
        $defaultParameters = $xml.CreateElement('parameters'); $datafield = $xml.CreateElement('datafieldname'); $datafield.InnerText = 'fmc_name'
        $null = $defaultParameters.AppendChild($datafield); $null = $default.AppendChild($defaultParameters); $null = $description.AppendChild($default)
        foreach ($factor in 0,1,2) {
            $custom = $xml.CreateElement('customControl'); $custom.SetAttribute('name',$controlName); $custom.SetAttribute('formFactor',[string]$factor)
            $parameters = $xml.CreateElement('parameters'); $bound = $xml.CreateElement('anchorValue'); $bound.SetAttribute('type','SingleLine.Text'); $bound.InnerText = 'fmc_name'
            $null = $parameters.AppendChild($bound); $null = $custom.AppendChild($parameters); $null = $description.AppendChild($custom)
        }
    }

    $null = Invoke-Dv "systemforms($formId)" PATCH @{ formxml = $xml.OuterXml }
    $null = Invoke-Dv 'PublishXml' POST @{ ParameterXml = "<importexportxml><entities><entity>fmc_bmspoint</entity></entities><appmodules><appmodule>$appId</appmodule></appmodules></importexportxml>" }
}

if ($Mode -eq 'Remove') {
    [xml]$xml = $form.formxml
    $root = $xml.DocumentElement
    $anchor = $root.SelectSingleNode(".//control[@id='$anchorControlId']")
    if ($anchor) {
        $cell = $anchor.ParentNode
        $row = $cell.ParentNode
        $null = $row.ParentNode.RemoveChild($row)
    }

    $description = $root.SelectSingleNode("controlDescriptions/controlDescription[@forControl='$anchorUniqueId']")
    if ($description) { $null = $description.ParentNode.RemoveChild($description) }

    $null = Invoke-Dv "systemforms($formId)" PATCH @{ formxml = $xml.OuterXml }
    $null = Invoke-Dv 'PublishXml' POST @{ ParameterXml = "<importexportxml><entities><entity>fmc_bmspoint</entity></entities><appmodules><appmodule>$appId</appmodule></appmodules></importexportxml>" }
}

$liveForm = Invoke-Dv "systemforms($formId)?`$select=formid,name,formactivationstate,formxml"
[xml]$liveXml = $liveForm.formxml
$liveAnchor = $liveXml.DocumentElement.SelectSingleNode(".//control[@id='$anchorControlId' and @datafieldname='fmc_name' and @uniqueid='$anchorUniqueId']")
$liveDescription = $liveXml.DocumentElement.SelectSingleNode("controlDescriptions/controlDescription[@forControl='$anchorUniqueId']")
$customFactors = @()
$boundValues = @()
if ($liveDescription) {
    $customFactors = @($liveDescription.SelectNodes("customControl[@name='$controlName']") | ForEach-Object { [int]$_.formFactor } | Sort-Object)
    $boundValues = @($liveDescription.SelectNodes("customControl[@name='$controlName']/parameters/anchorValue") | ForEach-Object { $_.InnerText } | Select-Object -Unique)
}
$bindingPresent = $null -ne $liveAnchor -or $null -ne $liveDescription
if ($Mode -eq 'Deploy' -and (-not $liveAnchor -or $customFactors.Count -ne 3 -or ($customFactors -join ',') -ne '0,1,2' -or $boundValues.Count -ne 1 -or $boundValues[0] -ne 'fmc_name')) {
    throw 'Live form binding deployment verification failed.'
}
if ($Mode -eq 'Remove' -and $bindingPresent) {
    throw 'Live form binding removal verification failed.'
}

$validation = Invoke-Dv "ValidateApp(AppModuleId=$appId)"
if (-not $validation.AppValidationResponse.ValidationSuccess) { throw 'App validation failed after form publish.' }
$app = Invoke-Dv "appmodules($appId)?`$select=appmoduleid,name,uniquename,publishedon,statecode,statuscode"
if (-not $app.publishedon -or $app.uniquename -ne 'fmc_FMCBMSDemo') { throw 'Published app verification failed.' }

[pscustomobject]@{
    OrganizationId = $who.OrganizationId
    Solution = $solutionName
    Control = $controlName
    ControlId = $controlId
    Form = $liveForm.name
    FormId = $formId
    FormBindingPresent = $bindingPresent
    FormFactors = $customFactors -join ','
    App = $app.name
    AppId = $appId
    PublishedOn = $app.publishedon
    ValidationSuccess = $validation.AppValidationResponse.ValidationSuccess
} | ConvertTo-Json -Depth 5
