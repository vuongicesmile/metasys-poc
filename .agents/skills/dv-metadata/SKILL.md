---
name: dv-metadata
description: Inspect or change Dataverse tables, columns, alternate keys, relationships, forms and views for the FMCentralBms solution.
license: MIT
---

# Dataverse schema and UI metadata

Read [dv-overview](../dv-overview/SKILL.md) and the relevant provisioner, mapper
and exported entity definitions before changing metadata.

## Inspect the actual model

Retrieve table type, ownership, logical/schema/entity-set names, column types,
lengths, precision, required levels, keys and relevant relationships. Compare
live metadata with dataverse/FMCentralBms when cloud state matters.

Keep fmc_bmspoint standard and fmc_bmsreading elastic unless the requested change
includes migration. Elastic history uses its primary GUID and partitionid;
do not apply the source skill's generic custom-alternate-key recipe to it.
Wait for the point alternate-key index to become Active before relying on it.

## Implement a schema change

1. Reuse FMCentralBms, FMCentralBmsPublisher and prefix fmc. Resolve a different
   target only if the task calls for one.
2. Prepare the schema change in DataverseProvisioner.cs and update dependent
   mapping, validation, queries and documentation as needed.
3. For authorized deployment, inspect existing components and create/update
   through supported .NET metadata requests or the maker portal. Pass
   SolutionUniqueName where supported and verify component membership.
4. Re-read metadata after success or timeout before retrying. Serialize dependent
   customizations and poll observable key/metadata readiness with bounded retries.
5. Publish affected components, verify the resulting model, and use
   [dv-solution](../dv-solution/SKILL.md) to retain the export.

A type/key mismatch needs migration analysis; do not drop/recreate a populated
table as a retry. Local source/XML edits alone do not prove an applied change.

## Forms and views

Use existing form/view metadata or a supported maker template. Form/control IDs
must be valid unique GUIDs; referenced view/relationship IDs must exist.
FetchXML attributes and layout cells must match, and the target entity and
query/form type must identify the intended component. Do not select by a common
display name alone when several tables may share it.

Publish and verify the requested columns/layout in the consuming app when
available. Report actual logical names and types for downstream code.
For Web API lookups use the metadata's case-sensitive navigation property;
entity-set names are not reliably obtained by adding an s.
