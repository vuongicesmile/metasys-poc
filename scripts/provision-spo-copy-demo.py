"""Render/deploy the single-file manual SPO copy demo using the existing dev login.

Run render first, validate/preflight with FlowAgent, then deploy and enable explicitly.
Cloud execution copies bytes connector-to-connector; this script never handles CSV data.
"""
import argparse
import json
import subprocess
import sys
import uuid
from pathlib import Path

import requests

ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS = ROOT / '.artifacts/spo-copy'
ORG = 'https://org06cbc9ec.crm5.dynamics.com'
ENV = '5abcb0e5-99b2-e51f-aa0e-90d84405798b'
ORG_ID = 'ab191700-b99e-f111-aaa0-000d3a80bb96'
NAME = 'FMC - Copy SPO Demo File (Manual)'
SOLUTION = 'FMCentralBms'
SP = 'shared_sharepointonline'
DV = 'shared_commondataserviceforapps'
SITE = 'https://titancorpvncom.sharepoint.com/sites/Powerplatform'
LIBRARY = '7d9282a4-c45d-4ef8-8719-18f78e71d0d0'
FILENAME = 'employee_bronze_profiling_demo_full.csv'
CONNECTIONS = {
    SP: ('fmc_sharedsharepointonline', 'shared-sharepointonl-c613b2d6'),
    DV: ('fmc_sharedcommondataserviceforapps', 'shared-commondataser-ffc44f95-e5ab-44b0-acdf-47752fa72135'),
}


def after(name=None, statuses=None):
    return {name: statuses or ['Succeeded']} if name else {}


def action(connector, operation, parameters, previous=None):
    return {'type': 'OpenApiConnection', 'runAfter': after(previous), 'inputs': {
        'host': {'apiId': '/providers/Microsoft.PowerApps/apis/' + connector,
                 'connectionName': connector, 'operationId': operation},
        'parameters': parameters}}


def compose(value, previous=None):
    return {'type': 'Compose', 'inputs': value, 'runAfter': after(previous)}


def stop(code, message):
    return {'type': 'Terminate', 'runAfter': {}, 'inputs': {
        'runStatus': 'Failed', 'runError': {'code': code, 'message': message}}}


def build(entity_set):
    def update(values, previous=None):
        return action(DV, 'UpdateOnlyRecord', {
            'entityName': entity_set, 'recordId': "@variables('RowId')",
            **{'item/' + k: v for k, v in values.items()}}, previous)

    def set_id(value, previous=None):
        return {'type': 'SetVariable', 'runAfter': after(previous),
                'inputs': {'name': 'RowId', 'value': value}}

    source = "@first(body('Find_source')?['value'])"
    identifier = "@outputs('Source_properties')?['{Identifier}']"
    filename = "@outputs('Source_properties')?['{FilenameWithExtension}']"
    etag = "@body('Metadata_before')?['ETag']"
    create = action(DV, 'CreateRecord', {
        'entityName': entity_set, 'item/fmc_name': filename, 'item/fmc_filename': filename,
        'item/fmc_sourcekey': "@outputs('Source_key')", 'item/fmc_status': 789110000,
        'item/fmc_receivedat': '@utcNow()', 'item/fmc_rowcount': 0,
        'item/fmc_importstatus': 789111000})
    create_scope = {'type': 'Scope', 'runAfter': {}, 'actions': {
        'Create_receipt': create,
        'Set_created_id': set_id("@body('Create_receipt')?['fmc_spofileid']", 'Create_receipt')}}
    recover = {'type': 'Scope', 'runAfter': after('Create_attempt', ['Failed', 'TimedOut']), 'actions': {
        'Find_after_create_fault': action(DV, 'ListRecords', {
            'entityName': entity_set, '$select': 'fmc_spofileid', '$top': 2,
            '$filter': "@concat('fmc_sourcekey eq ', decodeUriComponent('%27'), outputs('Source_key'), decodeUriComponent('%27'))"}),
        'Recover_only_one': {'type': 'If', 'runAfter': after('Find_after_create_fault'),
            'expression': {'equals': ["@length(body('Find_after_create_fault')?['value'])", 1]},
            'actions': {'Recover_id': set_id("@first(body('Find_after_create_fault')?['value'])?['fmc_spofileid']")},
            'else': {'actions': {'Create_unresolved': stop('SPO-CREATE', 'Create failed; no unique receipt recovered.')}}}}}
    resolve = {'type': 'If', 'runAfter': after('Find_receipt'),
        'expression': {'equals': ["@length(body('Find_receipt')?['value'])", 0]},
        'actions': {'Create_attempt': create_scope, 'Recover_create': recover},
        'else': {'actions': {'Require_unique_receipt': {'type': 'If', 'runAfter': {},
            'expression': {'equals': ["@length(body('Find_receipt')?['value'])", 1]},
            'actions': {'Set_existing_id': set_id("@first(body('Find_receipt')?['value'])?['fmc_spofileid']")},
            'else': {'actions': {'Duplicate_receipt': stop('SPO-001', 'Duplicate source key.')}}}}}}

    actions = {
        'Find_source': action(SP, 'GetFileItems', {
            'dataset': "@parameters('SpoSiteUrl')", 'table': "@parameters('SpoLibraryId')",
            'folderPath': "@parameters('SpoInboxPath')", 'viewScopeOption': 'Default',
            '$filter': "FileLeafRef eq '" + FILENAME + "'", '$top': 2}),
        'Require_one_source': {'type': 'If', 'runAfter': after('Find_source'),
            'expression': {'equals': ["@length(body('Find_source')?['value'])", 1]},
            'actions': {}, 'else': {'actions': {'Source_missing_or_ambiguous': stop('SPO-SOURCE', 'Expected exactly one demo file.')}}},
        'Source_properties': compose(source, 'Require_one_source'),
        'Source_key': compose("@concat(parameters('SpoSourceNamespace'), '_', toLower(parameters('SpoLibraryId')), '_', string(outputs('Source_properties')?['ID']))", 'Source_properties'),
        'Find_receipt': action(DV, 'ListRecords', {
            'entityName': entity_set, '$select': 'fmc_spofileid,fmc_status,fmc_etag', '$top': 2,
            '$filter': "@concat('fmc_sourcekey eq ', decodeUriComponent('%27'), outputs('Source_key'), decodeUriComponent('%27'))"}, 'Source_key'),
        'Resolve_receipt': resolve,
        'Metadata_before': action(SP, 'GetFileMetadata', {'dataset': "@parameters('SpoSiteUrl')", 'id': identifier}, 'Resolve_receipt'),
        'Require_valid_metadata': {'type': 'If', 'runAfter': after('Metadata_before'),
            'expression': {'and': [
                {'not': {'equals': [etag, '']}},
                {'not': {'equals': [etag, None]}},
                {'lessOrEquals': ["@int(body('Metadata_before')?['Size'])", 5242880]}]},
            'actions': {}, 'else': {'actions': {
                'Reject_metadata': update({'fmc_status': 789110003, 'fmc_errormessage': 'Missing ETag or file exceeds 5 MiB.'}),
                'Stop_invalid_metadata': {**stop('SPO-002', 'Missing ETag or file exceeds 5 MiB.'), 'runAfter': after('Reject_metadata')}}}},
        'Mark_processing': update({
            'fmc_status': 789110001, 'fmc_candidateetag': etag, 'fmc_name': filename,
            'fmc_filename': filename, 'fmc_sharepointidentifier': identifier,
            'fmc_sharepointpath': "@outputs('Source_properties')?['{FullPath}']",
            'fmc_sharepointurl': "@outputs('Source_properties')?['{Link}']",
            'fmc_filesize': "@int(body('Metadata_before')?['Size'])",
            'fmc_runid': "@workflow()?['run']?['name']"}, 'Require_valid_metadata'),
        'Get_content': action(SP, 'GetFileContent', {'dataset': "@parameters('SpoSiteUrl')", 'id': identifier, 'inferContentType': False}, 'Mark_processing'),
        'Metadata_after': action(SP, 'GetFileMetadata', {'dataset': "@parameters('SpoSiteUrl')", 'id': identifier}, 'Get_content'),
        'Require_stable_version': {'type': 'If', 'runAfter': after('Metadata_after'),
            'expression': {'equals': [etag, "@body('Metadata_after')?['ETag']"]},
            'actions': {
                'Upload_file': action(DV, 'UpdateEntityFileImageFieldContent', {
                    'entityName': entity_set, 'recordId': "@variables('RowId')", 'fileImageFieldName': 'fmc_file',
                    'item': "@body('Get_content')", 'x-ms-file-name': filename}),
                'Mark_archived': update({'fmc_status': 789110002, 'fmc_etag': etag,
                    'fmc_processedat': '@utcNow()', 'fmc_errormessage': None}, 'Upload_file')},
            'else': {'actions': {
                'Mark_changed': update({'fmc_status': 789110003, 'fmc_errormessage': 'SPO-003 Source changed during read.'}),
                'Stop_changed': {**stop('SPO-003', 'Source changed during read.'), 'runAfter': after('Mark_changed')}}}}
    }
    definition = {
        '$schema': 'https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#',
        'contentVersion': '1.0.0.0',
        'parameters': {'$authentication': {'defaultValue': {}, 'type': 'SecureObject'},
                       '$connections': {'defaultValue': {}, 'type': 'Object'}},
        'triggers': {'manual': {'type': 'Request', 'kind': 'Button',
            'inputs': {'schema': {'type': 'object', 'properties': {}, 'required': []}},
            'runtimeConfiguration': {'concurrency': {'runs': 1}}}},
        'actions': {
            'Initialize_row': {'type': 'InitializeVariable', 'runAfter': {},
                'inputs': {'variables': [{'name': 'RowId', 'type': 'string', 'value': ''}]}},
            'Try_copy': {'type': 'Scope', 'runAfter': after('Initialize_row'), 'actions': actions},
            'Catch_copy': {'type': 'Scope', 'runAfter': after('Try_copy', ['Failed', 'TimedOut']), 'actions': {
                'Has_receipt': {'type': 'If', 'runAfter': {},
                    'expression': {'not': {'equals': ["@variables('RowId')", '']}},
                    'actions': {'Mark_failed': update({'fmc_status': 789110003,
                        'fmc_runid': "@workflow()?['run']?['name']",
                        'fmc_errormessage': 'SPO-COPY: connector action failed; inspect flow run history.'})},
                    'else': {'actions': {}}},
                'Fail_run': {**stop('SPO-COPY', 'Copy failed; inspect action error in run history.'),
                    'runAfter': after('Has_receipt', ['Succeeded', 'Failed', 'TimedOut', 'Skipped'])}}},
            'Copy_receipt': compose({'rowId': "@variables('RowId')", 'status': 'Archived',
                'sourceKey': "@outputs('Source_key')", 'etag': etag,
                'size': "@body('Metadata_before')?['Size']"}, 'Try_copy')}
    }
    for name, schema, value in [('SpoSiteUrl', 'fmc_SpoSiteUrl', SITE),
            ('SpoLibraryId', 'fmc_SpoLibraryId', LIBRARY),
            ('SpoInboxPath', 'fmc_SpoInboxPath', '/Shared Documents'),
            ('SpoSourceNamespace', 'fmc_SpoSourceNamespace', 'bms_spo_dev01')]:
        definition['parameters'][name] = {'type': 'String', 'defaultValue': value,
                                         'metadata': {'schemaName': schema}}
    refs = {api: {'runtimeSource': 'embedded', 'connection': {'name': conn,
        'connectionReferenceLogicalName': logical}, 'api': {'name': api}}
        for api, (logical, conn) in CONNECTIONS.items()}
    return {'properties': {'definition': definition, 'connectionReferences': refs}, 'schemaVersion': '1.0.0.0'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['render', 'deploy', 'enable', 'disable'])
    args = parser.parse_args()
    ARTIFACTS.mkdir(parents=True, exist_ok=True)
    token = subprocess.check_output([sys.executable, str(ROOT/'scripts/get-dataverse-token.py')], text=True).strip()
    session = requests.Session()
    session.headers.update({'Authorization': 'Bearer '+token, 'Accept': 'application/json',
                            'OData-Version': '4.0', 'OData-MaxVersion': '4.0'})

    def api(method, path, **kwargs):
        response = session.request(method, ORG+'/api/data/v9.2/'+path, timeout=90, **kwargs)
        if not response.ok:
            raise RuntimeError(f'{method} {path}: {response.status_code} {response.text[:2000]}')
        return response.json() if response.content else {}

    if api('GET', 'WhoAmI')['OrganizationId'].lower() != ORG_ID:
        raise RuntimeError('Unexpected organization')
    solutions = api('GET', 'solutions', params={'$filter': "uniquename eq 'FMCentralBms'", '$select': 'solutionid'})['value']
    if len(solutions) != 1:
        raise RuntimeError('Expected one FMCentralBms solution')
    entity_set = api('GET', "EntityDefinitions(LogicalName='fmc_spofile')", params={'$select':'EntitySetName'})['EntitySetName']
    clientdata = build(entity_set)
    rendered = ARTIFACTS/'clientdata.json'
    if args.mode == 'render':
        rendered.write_text(json.dumps(clientdata, indent=2)+'\n', encoding='utf-8')
        refs = {api_name: {'id':'/providers/Microsoft.PowerApps/apis/'+api_name,
                         'connectionName': conn, 'source':'Embedded'}
                for api_name, (_, conn) in CONNECTIONS.items()}
        for tool in ['validate_flow', 'preflight_flow']:
            payload = {'definition': clientdata['properties']['definition'],
                ('connectionRefs' if tool == 'validate_flow' else 'connectionReferences'): refs}
            if tool == 'preflight_flow':
                payload['env'] = ENV
            (ARTIFACTS/(tool+'.args.json')).write_text(json.dumps(payload, indent=2), encoding='utf-8')
        print(json.dumps({'rendered': str(rendered), 'entitySet': entity_set}))
        return
    flows = api('GET', 'workflows', params={'$select':'workflowid,name,statecode',
        '$filter': "category eq 5 and type eq 1 and name eq '"+NAME+"'"})['value']
    if len(flows) > 1:
        raise RuntimeError('Duplicate flow names')
    if args.mode == 'deploy':
        if not rendered.exists() or json.loads(rendered.read_text(encoding='utf-8')) != clientdata:
            raise RuntimeError('Render and validate the current definition before deployment')
        relationships = api('GET', "EntityDefinitions(LogicalName='environmentvariablevalue')/ManyToOneRelationships",
            params={'$select': 'ReferencingAttribute,ReferencingEntityNavigationPropertyName',
                    '$filter': "ReferencingAttribute eq 'environmentvariabledefinitionid'"})['value']
        if len(relationships) != 1:
            raise RuntimeError('Environment variable navigation metadata is ambiguous')
        definition_navigation = relationships[0]['ReferencingEntityNavigationPropertyName']
        for schema, value in [('fmc_SpoSiteUrl', SITE), ('fmc_SpoLibraryId', LIBRARY),
                ('fmc_SpoInboxPath', '/Shared Documents'), ('fmc_SpoSourceNamespace', 'bms_spo_dev01')]:
            definitions = api('GET', 'environmentvariabledefinitions', params={
                '$select': 'environmentvariabledefinitionid', '$filter': "schemaname eq '"+schema+"'"})['value']
            if len(definitions) != 1:
                raise RuntimeError('Expected one environment variable definition: '+schema)
            definition_id = definitions[0]['environmentvariabledefinitionid']
            values = api('GET', 'environmentvariablevalues', params={
                '$select': 'environmentvariablevalueid,value',
                '$filter': '_environmentvariabledefinitionid_value eq '+definition_id})['value']
            if values:
                if len(values) != 1 or values[0]['value'] != value:
                    raise RuntimeError('Existing current value differs: '+schema)
            else:
                api('POST', 'environmentvariablevalues', json={'value': value,
                    definition_navigation+'@odata.bind': '/environmentvariabledefinitions('+definition_id+')'})
        for connector, (logical, conn) in CONNECTIONS.items():
            rows = api('GET', 'connectionreferences', params={
                '$filter': "connectionreferencelogicalname eq '"+logical+"'",
                '$select':'connectionreferenceid,connectionid,connectorid'})['value']
            if rows:
                if len(rows) != 1 or rows[0]['connectionid'] != conn:
                    raise RuntimeError('Existing connection reference differs; refusing to rebind')
            else:
                api('POST', 'connectionreferences', json={
                    'connectionreferencelogicalname': logical, 'connectionreferencedisplayname': 'FM Central SharePoint',
                    'connectorid': '/providers/Microsoft.PowerApps/apis/'+connector, 'connectionid': conn},
                    headers={'MSCRM.SolutionUniqueName': SOLUTION})
        flow_id = flows[0]['workflowid'] if flows else str(uuid.uuid4())
        if flows and flows[0]['statecode'] != 0:
            raise RuntimeError('Disable existing demo flow before updating definition')
        payload = {'name': NAME, 'category':5, 'type':1, 'primaryentity':'none',
            'description':'Manual single-file CSV archive demo. No employee-row parsing. Reuses source-key receipt on replay.',
            'clientdata':json.dumps(clientdata)}
        if flows:
            api('PATCH', f'workflows({flow_id})', json=payload)
        else:
            api('POST', 'workflows', json={'workflowid':flow_id, **payload}, headers={'MSCRM.SolutionUniqueName':SOLUTION})
        api('POST', 'AddSolutionComponent', json={'ComponentId':flow_id, 'ComponentType':29,
            'SolutionUniqueName':SOLUTION, 'AddRequiredComponents':True})
    else:
        if len(flows) != 1:
            raise RuntimeError('Deploy the flow first')
        flow_id = flows[0]['workflowid']
        api('PATCH', f'workflows({flow_id})', json={'statecode': 1 if args.mode == 'enable' else 0})
    receipt=api('GET', f'workflows({flow_id})', params={'$select':'workflowid,name,statecode'})
    (ARTIFACTS/'deployment.json').write_text(json.dumps(receipt,indent=2),encoding='utf-8')
    print(json.dumps(receipt))


if __name__ == '__main__':
    main()
