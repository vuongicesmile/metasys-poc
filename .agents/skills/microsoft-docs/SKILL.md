---
name: microsoft-docs
description: Verify current official Microsoft guidance for Power Platform, Dataverse, Power Apps, Power Automate, Entra and .NET used by metasys-poc.
---

# Microsoft documentation

Use official documentation to resolve capability, API, version, configuration,
limit and licensing questions relevant to the task.

1. Inspect the repo's package/configuration and installed CLI help to establish
   which version and host the project actually uses.
2. If Microsoft Learn MCP tools are callable, search for the precise product,
   feature and task. Otherwise use web search restricted to learn.microsoft.com
   or official Microsoft repositories.
3. Fetch the relevant page when the excerpt omits a prerequisite, limitation or
   complete procedure. Match standard versus elastic tables, app type,
   development versus production and SDK/CLI version.
4. Link the exact source supporting the conclusion. Distinguish documented
   capability, observed local behavior and inference.

Start points:

- [Dataverse developer documentation](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/)
- [PAC CLI reference](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/)
- [Code Apps overview](https://learn.microsoft.com/en-us/power-apps/developer/code-apps/overview)
- [Copilot Studio MCP connection](https://learn.microsoft.com/en-us/microsoft-copilot-studio/mcp-add-existing-server-to-agent)

Search narrowly, for example: Dataverse elastic UpsertMultiple partial failure,
Power Apps code apps solution deployment, or PAC solution unpack options.

The RMIT source skill offered a Microsoft Learn CLI fallback. It is optional:
use available read tools first and install tooling only when needed for the task.
Do not require MCP or a global npm installation just to consult documentation.
