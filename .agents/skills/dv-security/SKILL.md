---
name: dv-security
description: Inspect or configure Dataverse roles, user access and application identities for FMCentralBms deployment and worker runtime.
license: MIT
---

# Dataverse identities and permissions

Read [dv-overview](../dv-overview/SKILL.md), the deployment notes and the existing
identity/provisioning scripts before identity work.

## Establish the current caller and access

Resolve the exact environment, principal/application ID, business unit and role.
Inspect actual role privileges and membership; a role name is not a permission
test. Use PAC, supported SDK messages or available admin tools with sufficient
existing rights.

The provisioner defines FM Central BMS Integration with organization-level
Create, Read and Write on fmc_bmspoint and fmc_bmsreading. Schema provisioning
needs different privileges from continuous runtime. Do not give the steady-state
worker an admin role to resolve an unexplained access failure.

## Configure the requested identity

Reuse the task's chosen identity and role. Creating an Entra app, issuing a
secret/certificate, registering a Dataverse application user and assigning roles
are separate actions; execute those included in the requested deployment scope.

Inspect New-DataverseIdentity.ps1 and Start-DataverseSync.ps1 before invoking
them. Keep secrets in the supported hidden prompt/environment/secret store.
Do not copy the RMIT .env or credential cache.

The checked-in developer token configuration takes precedence over client
credentials in DataverseConnection. For a service-principal run, disable both
developer-token options in effective configuration and verify the actual caller;
merely supplying ClientId/ClientSecret does not establish the runtime identity.

## Verify changes

After an authorized role/application-user change, read back the exact principal,
business unit and memberships from the same organization. Check needed table
privileges and an appropriate operation using the intended runtime identity.
A zero CLI exit code or successful admin query is insufficient evidence.

Role elevation, self-elevation and grants across other environments are distinct
scope. Do not use them automatically after a failed grant. If new authority is
needed, first identify the missing privilege and prepare the exact proposed
principal/role/target change for the user's decision.
