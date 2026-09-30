# Epic 3 — FM workflow implementation

Target: Developer `org06cbc9ec`, solution `FMCentralBms`, app `FMC BMS Demo`.
Implemented in Developer on 2026-09-30. This document describes the design;
see [runbook and verification receipt](../runbooks/epic-3-fm-workflow.vi.md) for observed results and remaining limits.

## Contract

- Reuse live `fmc_fmrequest`, `fmc_approvalroute`, `fmc_requesthistory` and the existing email outbox/Power Automate dispatcher.
- `fmc_requeststatus` is authoritative (Draft/Submitted/In Approval/Approved/Rejected/Closed). Keep legacy `fmc_status` synchronized for existing consumers. Do not delete existing rows or fields.
- Native model-driven forms handle Draft CRUD. Dataverse synchronous guard protects edits and state transitions regardless of client.
- `fmc_TransitionFmRequest` validates caller, expected revision and operation ID. All request updates, immutable history and email-outbox writes share the Custom API transaction.
- Submit selects configured routes by type, department, minimum value and severity. Snapshot approver identity, role and timing at submission so later route edits do not change a running approval.
- One assigned active user per step; optional role constraint is checked on assignment and decision. Direct and team-derived roles are supported. Ambiguous/missing routes or recipients fail visibly.
- Power Automate sends actionable email links through the existing outbox flow. A new scheduled flow invokes the deadline API for reminders and escalation; decisions are authenticated in the app.
- Due dates use UTC. Each route configures SLA days, reminder hours before due and escalation hours after due. Only one reminder, overdue event and escalation per step. Zero-delay escalation combines overdue and escalation in one notification. Escalation transfers the pending decision to the configured escalation user and records history.
- FR-3.7 uses existing risk fields, owner, validation and a 5×5 risk heat-map in an embedded workflow page. The page also shows My Requests, Pending Approvals and history with paging.

## Acceptance

FR-3.1 create/edit Draft, submit, approve, reject, close; FR-3.2 two-step routing with role checks; FR-3.3 email receipt for each event; FR-3.4 reminder/escalation without duplicate events; FR-3.5 append-only actor/time/comment history; FR-3.6 authenticated app actions and scoped lists; FR-3.7 risk fields and heat-map.

Tests: server-side state/authorization/concurrency/idempotency/route policy tests, solution build, flow-definition tests, app action tests, and scoped Developer smoke tests. No new organizational approval policy is inferred: demo route configuration is isolated under department `EPIC3-DEMO` and uses only the existing demo user.
