---
name: power-platform-mcp-connector-suite
description: Create, validate or troubleshoot a selected Power Platform custom connector or MCP integration for Copilot Studio.
---

# Power Platform MCP connector integration

Use this for an explicitly selected custom connector/MCP architecture.
A Dataverse query or BMS synchronization task does not by itself require a new
connector or Copilot Studio agent.

## Resolve the contract

Establish the requested server/tools, target environment, authentication,
endpoint, schemas and consuming agent. Reuse an existing endpoint/connector
when it meets the task. Verify current transport and schema requirements using
[Microsoft's MCP connection guide](https://learn.microsoft.com/en-us/microsoft-copilot-studio/mcp-add-existing-server-to-agent).

As checked on 2026-09-08, the guide supports Streamable transport and both an MCP
onboarding wizard and a custom-connector path. Its Swagger example uses
POST /mcp with x-ms-agentic-protocol: mcp-streamable-1.0.
The simulator's COV-over-SSE stream is a separate application protocol; it is
not an MCP server and does not establish Copilot Studio transport support.

## Prepare the selected integration

For a custom connector, create the OpenAPI definition and connector metadata/
authentication files needed by the chosen CLI or import path. Add script.csx
only when a documented transformation is required; a conforming MCP server does
not inherently need custom C# JSON-RPC rewriting.

Use the server's actual input/output schema and current Copilot support for
tools/resources. Validate unsupported references/type unions against the target
capability instead of mechanically removing every OpenAPI $ref.

Keep endpoints, tenant/app IDs and connection references configurable. Credentials
belong in the supported connection/secret mechanism. Use least required scopes,
proper token audience validation, HTTPS and the server's authorization boundary.

## Validate and deploy within scope

Validate JSON/OpenAPI and the selected CLI's package rules. Use available
validators; do not refer to a nonexistent ConnectorPackageValidator.ps1.
Exercise tool discovery and a representative read/error/authentication case.
Only test write-capable tools when the task authorizes those side effects.

Prepare the concrete connector/server/agent configuration before any missing
approval is requested. Import, publish, connection creation, consent and DLP
changes are separate effects; carry out those covered by the user's task.

Report which checks used local fixtures and which reached the actual connector.
The source skill's full certification package and OAuth-hardening mode apply
only when those deliverables are requested.
