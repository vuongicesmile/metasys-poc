"""Verify one manual SPO copy run against Dataverse bytes, without printing CSV data.

Only SHA-256/metadata are saved. Access tokens and signed run-output URLs stay in memory.
"""
import argparse
import base64
import hashlib
import json
import subprocess
import sys
from pathlib import Path

import requests

ROOT = Path(__file__).resolve().parents[1]
ORG = 'https://org06cbc9ec.crm5.dynamics.com'
ENV = '5abcb0e5-99b2-e51f-aa0e-90d84405798b'
FLOW = '1c3eda7d-9bec-4a43-adc3-1426b89d2a84'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run-id', required=True)
    args = parser.parse_args()
    dv_token = subprocess.check_output([sys.executable, str(ROOT/'scripts/get-dataverse-token.py')], text=True).strip()
    result = subprocess.run([sys.executable, '-m', 'azure.cli', 'account', 'get-access-token',
        '--tenant', '31983a93-6f80-4356-a4e1-a65055e8327e',
        '--resource', 'https://service.flow.microsoft.com', '--output', 'json'],
        capture_output=True, text=True, timeout=60, check=True)
    flow_token = json.loads(result.stdout)['accessToken']

    def fetch(url, token=None, params=None):
        response = requests.get(url, headers={'Authorization':'Bearer '+token} if token else {},
                                params=params, timeout=90)
        if not response.ok:
            # Do not include signed output URLs in diagnostic exceptions.
            raise RuntimeError('Verification GET failed: HTTP '+str(response.status_code))
        return response

    who = fetch(ORG+'/api/data/v9.2/WhoAmI', dv_token).json()
    if who['OrganizationId'] != 'ab191700-b99e-f111-aaa0-000d3a80bb96':
        raise RuntimeError('Unexpected organization')
    run_url = 'https://api.flow.microsoft.com/providers/Microsoft.ProcessSimple/environments/'+ENV+'/flows/'+FLOW+'/runs/'+args.run_id
    run = fetch(run_url, flow_token, {'api-version':'2016-11-01'}).json()
    if run['properties']['status'] != 'Succeeded':
        raise RuntimeError('The selected run has not succeeded')

    def output(name):
        action = fetch(run_url+'/actions/'+name, flow_token, {'api-version':'2016-11-01'}).json()
        props = action['properties']
        if props['status'] != 'Succeeded':
            raise RuntimeError('Action did not succeed: '+name)
        link = props.get('outputsLink', {}).get('uri')
        if not link:
            raise RuntimeError('No output link for '+name)
        return fetch(link).json()

    content = output('Get_content')
    before = output('Metadata_before')['body']
    after = output('Metadata_after')['body']
    receipt = output('Copy_receipt')
    # Compose output links contain their output value directly.
    if 'rowId' not in receipt and isinstance(receipt.get('body'), dict):
        receipt = receipt['body']
    row_id = receipt['rowId']
    metadata = fetch(ORG+"/api/data/v9.2/EntityDefinitions(LogicalName='fmc_spofile')",
                     dv_token, {'$select':'EntitySetName'}).json()
    table = metadata['EntitySetName']
    records = fetch(ORG+'/api/data/v9.2/'+table, dv_token, {
        '$filter': "fmc_sourcekey eq '"+receipt['sourceKey'].replace("'", "''")+"'",
        '$select':'fmc_spofileid,fmc_name,fmc_filename,fmc_sourcekey,fmc_status,fmc_etag,fmc_filesize,fmc_runid,fmc_file_name,fmc_importstatus',
        '$top':2}).json()['value']
    if len(records) != 1 or records[0]['fmc_spofileid'] != row_id:
        raise RuntimeError('Expected exactly one matching source receipt')
    record = records[0]
    body = content.get('body')
    if not isinstance(body, dict) or '$content' not in body:
        raise RuntimeError('Expected binary envelope; refusing a lossy text conversion')
    source_bytes = base64.b64decode(body['$content'], validate=True)
    dv_bytes = fetch(ORG+'/api/data/v9.2/'+table+'('+row_id+')/fmc_file/$value', dv_token).content
    source_hash = hashlib.sha256(source_bytes).hexdigest()
    destination_hash = hashlib.sha256(dv_bytes).hexdigest()
    if not (source_hash == destination_hash and len(source_bytes) == before['Size'] == record['fmc_filesize']
            and before['ETag'] == after['ETag'] == record['fmc_etag']
            and record['fmc_status'] == 789110002 and record['fmc_runid'] == args.run_id):
        raise RuntimeError('Bytes, version, status or run receipt mismatch')
    verified = {'flowId':FLOW, 'runId':args.run_id, 'runStatus':run['properties']['status'],
        'rowId':row_id, 'fileName':record['fmc_filename'], 'bytes':len(dv_bytes),
        'sourceKey':record['fmc_sourcekey'], 'etag':record['fmc_etag'],
        'archiveStatus':'Archived', 'importStatus':'NotRequested', 'matchingSourceRows':len(records),
        'sourceSha256':source_hash, 'dataverseSha256':destination_hash, 'hashMatch':True,
        'recordUrl':ORG+'/main.aspx?appid=d19f4897-d227-4df3-8361-988f97c53e89&pagetype=entityrecord&etn=fmc_spofile&id='+row_id}
    path = ROOT/'.artifacts/spo-copy'/('verification-'+args.run_id+'.json')
    path.write_text(json.dumps(verified,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(verified,indent=2))


if __name__ == '__main__':
    main()
