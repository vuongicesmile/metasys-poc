---
name: dv-connect
description: Set up or diagnose Dataverse authentication, target organization and optional MCP connectivity for metasys-poc, reusing its Windows and WSL developer tooling.
license: MIT
---

# Connect to the existing Dataverse environment

Read [dv-overview](../dv-overview/SKILL.md) and its project reference.

## Inspect before changing setup

1. Determine which surface the task needs: the worker, PAC, MCP or another
   documented API client. Inspect that surface's configuration.
2. Discover callable Dataverse tools. Check local commands and configured paths;
   do not reinstall the source project's entire toolchain.
3. Compare the requested target with the configured URL and organization ID.
   Use existing profiles when they match. Inspect profile metadata through CLI
   commands, never raw credential caches.
4. Diagnose the failing layer: executable/path, expired identity, tenant mismatch,
   DNS/network, organization mismatch, privileges, or MCP registration.

For worker authentication, use the token helper's --probe only, followed by a
read-only data-plane check. Its normal output is a credential.
PAC org who with the explicit target URL verifies PAC's identity path.
A live --verify also tests worker access, but needs the SQL integration schema;
a SQL failure does not by itself establish an authentication failure.

## Repair only what the task requires

Reuse the existing developer flow for this POC. Missing .env or scripts/auth.py
does not mean setup is incomplete. Avoid duplicating credentials or changing the
shared RMIT workspace to repair a local path.

If credentials expired, use the relevant CLI's supported interactive sign-in.
If the correct tenant/account requires the user's participation or admin consent,
report the concrete error and required action. Stop repeated attempts when the
cause will not change; an auth failure does not justify changing security policy.

For service-principal work use [dv-security](../dv-security/SKILL.md), including
the effective configuration precedence check.

Configure MCP only when requested or needed for an explicitly selected workflow.
Inspect the actual host's config and current official instructions first.
Preserve unrelated server entries; do not copy Claude/Copilot app IDs or plugin
version placeholders into Codex configuration. A saved config is not proof
that tools are callable in this session.

## Verify the relevant connection

Report the surface tested, URL, organization identity and result of an actual
read. For MCP, verify tool discovery plus a successful read through MCP itself.
An SDK/PAC fallback can complete an ordinary data task, but cannot be reported
as an MCP test. Creating new environments or granting roles requires that scope
in the user's request.
