---
name: solution-architect
description: Turn requirements and the Metasys POC implementation into a business overview, technical architecture or implementation plan with traceable decisions.
---

# Solution architecture documentation

Adapt the source skill's overview, architecture and implementation perspectives
to the deliverable requested. A full architecture pack can use three linked
documents; a narrow request does not require generating all three.

## Establish the baseline

Read [AGENTS.md](../../../AGENTS.md), the deployment notes, relevant code and
provided requirements. Plan 3.0 has superseded details: use the implementation
amendments when describing current behavior.

Keep the current boundaries visible: simulator, SSE ingestion, raw/full SQL
history, independent .NET sync, standard current-point table and elastic history.
Reusing RMIT's Power Platform tooling does not transfer its wider business scope
or Dataverse Bronze/Silver/Gold storage decision into this POC.

## Write for the audience

- Business overview: problem, users, scope, outcome, current capability and
  measurable acceptance criteria supported by requirements.
- Technical architecture: system boundaries, data flow, storage ownership,
  identity, integration contract, failure/recovery behavior and key decisions.
- Implementation plan: concrete deliverables, dependencies, validation,
  deployment/rollback and unresolved choices.

Keep dates, budgets, volume estimates and SLAs evidence-based. Label proposed
assumptions with their rationale and consequence; do not present them as
stakeholder decisions. Link requirement -> decision -> artifact -> evidence
where useful, without inventing RMIT FR/NFR IDs for this repository.

Use [mermaid-diagrams](../mermaid-diagrams/SKILL.md) for relationships that benefit
from a diagram. Show TTL, receipt acknowledgement and system-of-record boundaries
accurately. Distinguish the fake API contract from a verified vendor interface.

## Validate the deliverable

Check consistency with code and deployment notes, source links, diagrams,
dependencies and acceptance criteria. A past successful run is not a current
production readiness claim. Provide the requested artifact and identify
decisions actually needed to proceed. SOP/runbook writing can be done directly;
the source skill's separate sop-builder dependency is not installed here.
