// ---------------- Type Definitions which can be imported from ./RuntimeTypes -------------------------
export interface TableRegistrations extends BaseTableRegistrations {
    "cr3c8_silvernewbmspoint": cr3c8_silvernewbmspoint,
    "fmc_bmsbuilding": fmc_bmsbuilding,
    "fmc_bmsequipment": fmc_bmsequipment,
    "fmc_bmspoint": fmc_bmspoint,
    "fmc_bmsreading": fmc_bmsreading,
    "fmc_spofile": fmc_spofile,
    "fmc_spoimportrow": fmc_spoimportrow,
    "fmc_syncrequest": fmc_syncrequest,
}
export interface EnumRegistrations extends BaseEnumRegistrations {
    "cr3c8_silvernewbmspoint-statecode": cr3c8_silvernewbmspoint_statecode,
    "cr3c8_silvernewbmspoint-statuscode": cr3c8_silvernewbmspoint_statuscode,
    "fmc_bmsbuilding-statecode": fmc_bmsbuilding_statecode,
    "fmc_bmsbuilding-statuscode": fmc_bmsbuilding_statuscode,
    "fmc_bmsequipment-fmc_equipmenttype": fmc_bmsequipment_fmc_equipmenttype,
    "fmc_bmsequipment-statecode": fmc_bmsequipment_statecode,
    "fmc_bmsequipment-statuscode": fmc_bmsequipment_statuscode,
    "fmc_bmspoint-statecode": fmc_bmspoint_statecode,
    "fmc_bmspoint-statuscode": fmc_bmspoint_statuscode,
    "fmc_spofile-fmc_importstatus": fmc_spofile_fmc_importstatus,
    "fmc_spofile-fmc_status": fmc_spofile_fmc_status,
    "fmc_spofile-statecode": fmc_spofile_statecode,
    "fmc_spofile-statuscode": fmc_spofile_statuscode,
    "fmc_spoimportrow-statecode": fmc_spoimportrow_statecode,
    "fmc_spoimportrow-statuscode": fmc_spoimportrow_statuscode,
    "fmc_syncrequest-fmc_status": fmc_syncrequest_fmc_status,
    "fmc_syncrequest-statecode": fmc_syncrequest_statecode,
    "fmc_syncrequest-statuscode": fmc_syncrequest_statuscode,
}
export type cr3c8_silvernewbmspoint = TableRow<{
    // Primary Key Column
    readonly cr3c8_silvernewbmspointid: string,
    cr3c8_fmc_bmspointid: string,
    cr3c8_fmc_building: string,
    cr3c8_fmc_currentvalue: number,
    cr3c8_fmc_lastreadingtime: Date,
    cr3c8_fmc_lastsqlid: string,
    cr3c8_fmc_name: string,
    cr3c8_fmc_objectid: string,
    cr3c8_fmc_objecttype: string,
    cr3c8_fmc_sourcesystem: string,
    cr3c8_fmc_unit: string,
    cr3c8_fmc_unitconverted: string,
    cr3c8_fmc_valueconverted: number,
    cr3c8_fmc_warningflag: string,
    cr3c8_value_display: string,
    readonly createdbyname: string,
    readonly createdbyyominame: string,
    readonly createdonbehalfbyname: string,
    readonly createdonbehalfbyyominame: string,
    readonly modifiedbyname: string,
    readonly modifiedbyyominame: string,
    readonly modifiedonbehalfbyname: string,
    readonly modifiedonbehalfbyyominame: string,
    readonly owningbusinessunitname: string,
    statecode: cr3c8_silvernewbmspoint_statecode,
    statuscode: cr3c8_silvernewbmspoint_statuscode,
}>

export type fmc_bmsbuilding = TableRow<{
    // Primary Key Column
    readonly fmc_bmsbuildingid: string,
    readonly createdbyname: string,
    readonly createdbyyominame: string,
    readonly createdonbehalfbyname: string,
    readonly createdonbehalfbyyominame: string,
    fmc_buildingcode: string,
    fmc_description: string,
    fmc_name: string,
    fmc_sourcebuilding: string,
    readonly modifiedbyname: string,
    readonly modifiedbyyominame: string,
    readonly modifiedonbehalfbyname: string,
    readonly modifiedonbehalfbyyominame: string,
    // Foreign Key Column
    readonly _organizationid_value: `/organization(${string})`,
    readonly organizationidname: string,
    statecode: fmc_bmsbuilding_statecode,
    statuscode: fmc_bmsbuilding_statuscode,
}>

export type fmc_bmsequipment = TableRow<{
    // Primary Key Column
    readonly fmc_bmsequipmentid: string,
    readonly createdbyname: string,
    readonly createdbyyominame: string,
    readonly createdonbehalfbyname: string,
    readonly createdonbehalfbyyominame: string,
    // Foreign Key Column
    _fmc_buildingid_value: `/fmc_bmsbuilding(${string})`,
    readonly fmc_buildingidname: string,
    fmc_description: string,
    fmc_equipmentcode: string,
    fmc_equipmenttype: fmc_bmsequipment_fmc_equipmenttype,
    fmc_name: string,
    readonly modifiedbyname: string,
    readonly modifiedbyyominame: string,
    readonly modifiedonbehalfbyname: string,
    readonly modifiedonbehalfbyyominame: string,
    // Foreign Key Column
    readonly _organizationid_value: `/organization(${string})`,
    readonly organizationidname: string,
    statecode: fmc_bmsequipment_statecode,
    statuscode: fmc_bmsequipment_statuscode,
}>

export type fmc_bmspoint = TableRow<{
    // Primary Key Column
    readonly fmc_bmspointid: string,
    readonly createdbyname: string,
    readonly createdbyyominame: string,
    readonly createdonbehalfbyname: string,
    readonly createdonbehalfbyyominame: string,
    fmc_building: string,
    fmc_currentvalue: number,
    // Foreign Key Column
    _fmc_equipmentid_value: `/fmc_bmsequipment(${string})`,
    readonly fmc_equipmentidname: string,
    fmc_lastreadingtime: Date,
    fmc_lastsqlid: string,
    fmc_name: string,
    fmc_objectid: string,
    fmc_objecttype: string,
    fmc_sourcesystem: string,
    fmc_unit: string,
    readonly modifiedbyname: string,
    readonly modifiedbyyominame: string,
    readonly modifiedonbehalfbyname: string,
    readonly modifiedonbehalfbyyominame: string,
    // Foreign Key Column
    readonly _organizationid_value: `/organization(${string})`,
    readonly organizationidname: string,
    statecode: fmc_bmspoint_statecode,
    statuscode: fmc_bmspoint_statuscode,
}>

export type fmc_bmsreading = TableRow<{
    // Primary Key Column
    readonly fmc_bmsreadingid: string,
    readonly createdbyname: string,
    readonly createdbyyominame: string,
    readonly createdonbehalfbyname: string,
    readonly createdonbehalfbyyominame: string,
    fmc_building: string,
    fmc_externalkey: string,
    fmc_name: string,
    fmc_objectid: string,
    fmc_objectname: string,
    fmc_objecttype: string,
    fmc_readingtime: Date,
    fmc_readingvalue: number,
    fmc_sourcesystem: string,
    fmc_sqlingestedat: Date,
    fmc_sqlreadingid: string,
    fmc_unit: string,
    readonly modifiedbyname: string,
    readonly modifiedbyyominame: string,
    readonly modifiedonbehalfbyname: string,
    readonly modifiedonbehalfbyyominame: string,
    readonly owningbusinessunitname: string,
    partitionid: string,
    ttlinseconds: number,
}>

export type fmc_spofile = TableRow<{
    // Primary Key Column
    readonly fmc_spofileid: string,
    readonly createdbyname: string,
    readonly createdbyyominame: string,
    readonly createdonbehalfbyname: string,
    readonly createdonbehalfbyyominame: string,
    fmc_candidateetag: string,
    fmc_errormessage: string,
    fmc_etag: string,
    readonly fmc_file_name: string,
    fmc_filename: string,
    fmc_filesize: number,
    fmc_importedetag: string,
    fmc_importstatus: fmc_spofile_fmc_importstatus,
    fmc_name: string,
    fmc_processedat: Date,
    fmc_receivedat: Date,
    fmc_rowcount: number,
    fmc_runid: string,
    fmc_sharepointidentifier: string,
    fmc_sharepointpath: string,
    fmc_sharepointurl: string,
    fmc_sourcekey: string,
    fmc_status: fmc_spofile_fmc_status,
    readonly modifiedbyname: string,
    readonly modifiedbyyominame: string,
    readonly modifiedonbehalfbyname: string,
    readonly modifiedonbehalfbyyominame: string,
    // Foreign Key Column
    readonly _organizationid_value: `/organization(${string})`,
    readonly organizationidname: string,
    statecode: fmc_spofile_statecode,
    statuscode: fmc_spofile_statuscode,
}>

export type fmc_spoimportrow = TableRow<{
    // Primary Key Column
    readonly fmc_spoimportrowid: string,
    readonly createdbyname: string,
    readonly createdbyyominame: string,
    readonly createdonbehalfbyname: string,
    readonly createdonbehalfbyyominame: string,
    fmc_buildingname: string,
    fmc_code: string,
    // Foreign Key Column
    _fmc_fileid_value: `/fmc_spofile(${string})`,
    readonly fmc_fileidname: string,
    fmc_floorcount: number,
    fmc_name: string,
    fmc_ordinal: number,
    fmc_sourceetag: string,
    readonly modifiedbyname: string,
    readonly modifiedbyyominame: string,
    readonly modifiedonbehalfbyname: string,
    readonly modifiedonbehalfbyyominame: string,
    // Foreign Key Column
    readonly _organizationid_value: `/organization(${string})`,
    readonly organizationidname: string,
    statecode: fmc_spoimportrow_statecode,
    statuscode: fmc_spoimportrow_statuscode,
}>

export type fmc_syncrequest = TableRow<{
    // Primary Key Column
    readonly fmc_syncrequestid: string,
    readonly createdbyname: string,
    readonly createdbyyominame: string,
    readonly createdonbehalfbyname: string,
    readonly createdonbehalfbyyominame: string,
    fmc_activekey: string,
    fmc_attempts: number,
    fmc_baselinedeadletters: number,
    fmc_baselinedelivered: number,
    fmc_batches: number,
    fmc_command: string,
    fmc_completedat: Date,
    fmc_correlationid: string,
    fmc_deadletterafter: number,
    fmc_deliveredrows: number,
    fmc_errormessage: string,
    fmc_heartbeat: Date,
    fmc_leaseexpiresat: Date,
    fmc_name: string,
    fmc_pendingafter: number,
    fmc_pipeline: string,
    fmc_quarantinedrows: number,
    fmc_requestedby: string,
    fmc_requestedcutoffid: string,
    fmc_startedat: Date,
    fmc_status: fmc_syncrequest_fmc_status,
    fmc_workerowner: string,
    readonly modifiedbyname: string,
    readonly modifiedbyyominame: string,
    readonly modifiedonbehalfbyname: string,
    readonly modifiedonbehalfbyyominame: string,
    // Foreign Key Column
    readonly _organizationid_value: `/organization(${string})`,
    readonly organizationidname: string,
    statecode: fmc_syncrequest_statecode,
    statuscode: fmc_syncrequest_statuscode,
}>

const enum cr3c8_silvernewbmspoint_statecode {
"Active" = 0,
"Inactive" = 1,
}
const enum cr3c8_silvernewbmspoint_statuscode {
"Active" = 1,
"Inactive" = 2,
}
const enum fmc_bmsbuilding_statecode {
"Active" = 0,
"Inactive" = 1,
}
const enum fmc_bmsbuilding_statuscode {
"Active" = 1,
"Inactive" = 2,
}
const enum fmc_bmsequipment_fmc_equipmenttype {
"Water Meter" = 789100000,
"Temperature Sensor" = 789100001,
"Test Rig" = 789100002,
}
const enum fmc_bmsequipment_statecode {
"Active" = 0,
"Inactive" = 1,
}
const enum fmc_bmsequipment_statuscode {
"Active" = 1,
"Inactive" = 2,
}
const enum fmc_bmspoint_statecode {
"Active" = 0,
"Inactive" = 1,
}
const enum fmc_bmspoint_statuscode {
"Active" = 1,
"Inactive" = 2,
}
const enum fmc_spofile_fmc_importstatus {
"Not Requested" = 789111000,
"Processing" = 789111001,
"Imported" = 789111002,
"Failed" = 789111003,
}
const enum fmc_spofile_fmc_status {
"Received" = 789110000,
"Processing" = 789110001,
"Archived" = 789110002,
"Failed" = 789110003,
"Ignored" = 789110004,
}
const enum fmc_spofile_statecode {
"Active" = 0,
"Inactive" = 1,
}
const enum fmc_spofile_statuscode {
"Active" = 1,
"Inactive" = 2,
}
const enum fmc_spoimportrow_statecode {
"Active" = 0,
"Inactive" = 1,
}
const enum fmc_spoimportrow_statuscode {
"Active" = 1,
"Inactive" = 2,
}
const enum fmc_syncrequest_fmc_status {
"Queued" = 789100000,
"Running" = 789100001,
"Succeeded" = 789100002,
"Completed with issues" = 789100003,
"Failed" = 789100004,
}
const enum fmc_syncrequest_statecode {
"Active" = 0,
"Inactive" = 1,
}
const enum fmc_syncrequest_statuscode {
"Active" = 1,
"Inactive" = 2,
}

export interface UxAgentDataApi extends BaseUxAgentDataApi<TableRegistrations, EnumRegistrations> {}

export interface GeneratedComponentProps {
    dataApi: UxAgentDataApi;
}
