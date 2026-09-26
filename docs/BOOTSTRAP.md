# Bootstrap verification note

> **Names updated.** This record predates the project's current name, Portcullis,
> settled on 2026-09-13 before anything was published. Identifiers, commands and paths
> below are given with today's names — `PORTCULLIS_*` rule ids, the `portcullis`
> command, the `Portcullis.*` projects — so they match the code in this repository; the
> findings themselves are as recorded.

Date: 2026-08-17. Written at the end of the bootstrap session (M0, M1D, M2), before the
three parallel tracks begin. The discipline: say plainly what was actually run and
confirmed, and what a clean scaffold does not prove. Nothing below is inferred from code existing — everything is
either a command that was run in this session, or is explicitly marked unbuilt.

## What was actually run and confirmed working

- `dotnet build` on the full solution (`portcullis.sln`: `Portcullis.Engine`, `Portcullis.Cli`,
  `Portcullis.Engine.Tests`) — clean, 0 warnings, 0 errors.
- `dotnet test` — 3/3 passing:
  `ScanAsync_WithZeroRulesRegistered_ReturnsValidEmptyResult`,
  `ScanAsync_ExcludesBinAndObjDirectories`,
  `ScanAsync_OnMissingPath_ReturnsEmptyResultRatherThanThrowing`.
- `dotnet run --project src/Portcullis.Cli -- scan <consumer-checkout>/src` — ran for
  real against `<consumer>`' full `src/` tree, exit code `0`, produced the literal JSON
  reproduced in `docs/SPEC.md` section 3 (156 files scanned, 0 rules, 0 violations,
  schema-valid). Also run against portcullis's own `src/` (6 files) as a smaller sanity
  check before the `<consumer>` run; same shape, different counts.
- The JSON output was diffed by eye against the schema table in `docs/SPEC.md` section
  3 field-by-field — every field present, every type matches, `violations: []` and
  `rulesEvaluated: 0` are truthful (`RuleRegistry.All` is genuinely
  `ImmutableArray<DiagnosticAnalyzer>.Empty`, not a rule that happens not to fire).
- `fixtures/consumer-violations.json` — every one of its 11 entries was independently
  re-verified against `<consumer>`' actual source in this session (`Read`/`Grep`, not
  copy-pasted from `<consumer>`' own docs). See the finding immediately below — two entries
  differ substantially from what the bootstrap brief assumed going in, and that
  difference was chased down to a specific commit before being written into the fixture.

## A finding from this session worth stating plainly

The bootstrap brief named three "at minimum" violation categories for the M1D fixture,
sourced from an earlier audit of `<consumer>`: endpoints an admin client called that did
not exist, a server-side handler that always returned `false`, and orphan EF Core
entities. On independent verification against current `<consumer>` source (the fixture's
`verifiedAgainstCommit`), **two of those three no longer reproduced.** Both had been fixed
by a `<consumer>` commit that predates the audit the brief's claims traced back to; the
application's own `README.md` was never updated to match, so the stale claims persisted
into this session's briefing.

This was not silently corrected by swapping in different examples and moving on.
`fixtures/consumer-violations.json` keeps both original categories, reframed to what is
actually true today:

- **`CONSUMER-001`** is now the README staleness itself — a real, current, verifiable
  documentation-drift violation, and a clean match for architecture-standards' P14 (*"a
  stale README describing a system that no longer exists is itself a review finding"*).
- **`CONSUMER-002`** is the real, current shape of the second gap: not a hardcoded
  `false` on the server (verified false — the server does a real lookup), but a web
  client button that never calls the real, correctly implemented endpoint at all. The
  backend is fine in isolation; the wiring between client and server for this one
  feature is not — which is a better example of "passes review because each piece looks
  correct on its own" than the originally assumed bug, not a weaker one.

Both are marked `roslynCheckable: false` in the fixture, for two different reasons
(prose-vs-git-history for one, wrong language entirely for the other — TypeScript, not
C#). The one category that reproduced as originally described and is Roslyn-checkable —
orphan EF Core entities with no controller — was expanded from one example to nine
confirmed instances (`CONSUMER-003` through `CONSUMER-011`) across two of `<consumer>`'
services, specifically because it is the only one of the three categories a single-repo
C# static analyzer can plausibly reach, and M2A only needs one entry it can catch.

## What remains entirely unbuilt

Everything past the skeleton. Concretely:

- **Every rule.** `RuleRegistry.All` is empty. Zero of P1–P15's six "Expressible"
  principles (`docs/SPEC.md` section 5) have an implementation. Nothing in this session
  has been run against `fixtures/consumer-violations.json` and caught anything — that
  fixture is unconsumed reference data until Track A writes a rule against it.
- **The mutation pass.** No deliberately broken rule variant exists, because no rule
  exists yet to break. `MUTATIONS.md` does not exist. The claim "portcullis detects
  architectural drift" has no evidence behind it at all yet — it is exactly the kind of
  claim this project's own house style exists to prevent stating prematurely.
- **The CI action.** No GitHub Action, no PR comment formatter, nothing under
  `.github/workflows/`. Track B builds this against a hand-written mock conforming to
  `docs/SPEC.md` section 3 — that mock does not exist yet either.
- **The provenance implementation.** `ProvenanceModels.cs` defines the contract types
  only (`ProvenanceRange`, `ProvenanceReport`, `IProvenanceProvider`) and is not called
  from anywhere. No git-blame or commit-trailer parsing exists. Track C starts from
  nothing.
- **Cross-project/cross-assembly semantic analysis.** The M2 scanner parses `*.cs` files
  directly into one `CSharpCompilation`, referencing only `System.Private.CoreLib`. It
  has no project-reference or NuGet-dependency resolution (no `MSBuildWorkspace`). Any
  rule that needs to resolve a type from another project or package will not work against
  this skeleton without that being added first — noted here rather than discovered as a
  surprise mid-Track-A.
- **Integration (M4) and the end-to-end mutation pass (M5).** Both require all three
  tracks to exist first and are out of scope for every session before that point, this
  one included.

## What this session does not claim

This session does not claim portcullis catches anything. It claims: the schema is frozen
and was validated against a real run, not a hand-written guess; the fixture data was
independently re-verified rather than trusted from `<consumer>`' own possibly-stale docs;
and the skeleton compiles, tests green, and runs for real. That is the complete list.
