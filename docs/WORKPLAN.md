# Work plan — `letsgolegacy.portcullis` (C5 gate)

Portcullis is the Second Key gate: deterministic Roslyn analyzers run on every pull
request, reported only on changed lines, as SARIF with a baseline. It continues a
private predecessor repository, renamed to Portcullis on 2026-09-13 because another
established product already used its earlier name; this repository is where the rename
becomes complete and the migration rule set is added.

Ticket IDs match the cross-repository backlog. One ticket is one pull request.

## Phase 01 — demo

| ID | Deliverable | Done when | Status |
|---|---|---|---|
| R0 | Import the Portcullis code base from the private predecessor repository (source, tests, fixtures, user-facing docs; not internal strategy notes) with CI green | Build and the full test suite pass in this repository's CI | in review (#1) |
| R1 | Rename finished: repository, package and namespaces | The old name appears nowhere in the tree, enforced by a CI check | in review (#2) |
| R2 | Migration rule set: `System.Web`, `HttpContext.Current`, `.Result` / `.Wait()`, `ConfigurationManager` instead of `IOptions` | Four analyzers ship with mutation-tested tests | in review (#3) |
| R3 | SARIF 2.1 output with a changed-lines filter and a baseline file | A pull request produces filtered SARIF | in review (#4) |
| P8 support | The rules run on the migrated candidate's PR in the nopCommerce bench | SARIF is attached to the evidence pack (tracked in the bench) | planned |

## Later phases

| Work | Phase |
|---|---|
| ArchUnitNET layer rules; design partners' rules | 02 |
| Rule packs per estate | 03 |
| Java analyzers (modernize-java) | 04 |
| Public vs licensed distribution decision | open |
