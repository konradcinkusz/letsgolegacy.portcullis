# Portcullis — M4 integration verification note

> **Names updated.** This record predates the project's current name, Portcullis,
> settled on 2026-09-13 before anything was published. Identifiers, commands and paths
> below are given with today's names — `PORTCULLIS_*` rule ids, the `portcullis`
> command, the `Portcullis.*` projects — so they match the code in this repository; the
> findings themselves are as recorded.

Date: 2026-08-17. Written after merging Track A (P2/P9/P10 rules), Track B (CI/PR
comment), and Track C (git-based provenance), each as its own pull request in the
private predecessor repository — the point the roadmap called M4: "everything tied
together, run against `<consumer>`."
Same honesty discipline as `docs/BOOTSTRAP.md` and `docs/MUTATIONS.md`: what follows is
what was actually run and its actual output, not what the code is assumed to do.

## 1. What M4 found already broken: the three tracks were never actually together

`main` carried Track A and Track B (both pull requests merged to `main` directly), but
Track C's real `GitProvenanceProvider` implementation only ever merged into the
`track-c-provenance` branch (its pull request's base) — `main` still had only the M2
contract stub, `ProvenanceModels.cs`. `track-a-engine` and `track-b-ci` themselves were
never updated either; they still sat at the bootstrap commit. Verified directly by
diffing `src/Portcullis.Engine/Provenance/` between `main` and `track-c-provenance` before
touching anything — `main` had one file, `track-c-provenance` had six.

This repository was never actually in the state M4 requires until this session: three
real implementations, coexisting, buildable, testable together. Fixed by merging
`track-c-provenance` into this integration branch (`git merge origin/track-c-provenance
--no-ff`) — a clean, conflict-free merge (both share the bootstrap commit as merge
base; Track C's commits only ever touched `src/Portcullis.Engine/Provenance/` and its own
tests, disjoint from Tracks A/B's files), bringing in `GitCommandRunner.cs`,
`GitOutputParsers.cs`, `AiToolClassifier.cs`, `GitProvenanceProvider.cs`, and their 14
tests unchanged.

## 2. What was actually run and confirmed working

Unlike Track B's pull request ("no dotnet SDK is reachable in this execution environment"),
this session had a working .NET 8 SDK — `dotnet-sdk-8.0` installed via `apt-get` from
Ubuntu's own package archive, which the session's egress policy allows even though
`builds.dotnet.microsoft.com` (the path `dotnet-install.sh`/`setup-dotnet` use) is
blocked. Everything below is a real, executed command, not static review.

- `dotnet build portcullis.sln` after the Track C merge, before any new code — clean, 0
  warnings, 0 errors, all five projects.
- `dotnet test portcullis.sln` after the merge, before any new code — 59/59 passing (23
  `Portcullis.CiComment.Tests` + 36 `Portcullis.Engine.Tests`, the latter being the 22
  pre-existing Track A tests plus Track C's 14 `GitProvenanceProviderTests`).
- After adding the provenance-wiring code (section 3) and its own tests (section 4):
  `dotnet build portcullis.sln` — still 0 warnings, 0 errors. `dotnet test portcullis.sln` —
  67/67 passing (23 CiComment + 44 Engine, the 8 new tests being
  `tests/Portcullis.Engine.Tests/ScannerProvenanceTests.cs`).

## 3. Wiring provenance into the engine (docs/SPEC.md section 4, "consumption contract")

`docs/SPEC.md` section 4 specifies the shape of a `ProvenanceReport` and states the
consumption contract only in prose: *"a rule may read a `ProvenanceReport` alongside the
compilation and apply a stricter `defaultSeverity` threshold when a violation falls
inside an `"ai"`-sourced range."* That contract had no implementation anywhere before
this session — Track C built the provider, but nothing called it, and nothing in
`Scanner.cs` or the `Rules/` analyzers accepted a `ProvenanceReport` as input.

**Design choice: escalation happens once, in `Scanner.ScanAsync`, not inside each
`DiagnosticAnalyzer`.** The literal SPEC wording ("a rule may read...") reads as
per-rule, but implementing that literally would mean plumbing a `ProvenanceReport`
through Roslyn's `AnalyzerOptions`/`AdditionalFiles` into all three (and every future)
analyzer, duplicating the same file/line-overlap check in each one. Doing it once, as a
post-processing pass over the collected `Violation` list, is one tested policy point
instead of three-and-growing, keeps every existing rule's own tests
(`ExtensibilityInheritanceAnalyzerTests`, etc.) provenance-agnostic and still valid
unchanged, and produces an identical observable result — a violation in an AI-authored
range ends up at a stricter severity either way. Recorded here as a deliberate deviation
from the SPEC text's literal phrasing, not a silent one.

`Scanner.ScanAsync` gained two new optional parameters:

```csharp
public static async Task<ScanResult> ScanAsync(
    string path,
    ProvenanceReport? provenance = null,
    string? provenanceRoot = null,
    CancellationToken cancellationToken = default)
```

- `provenance` is caller-supplied, not computed by `Scanner` itself — the engine stays
  git-agnostic, matching the SPEC's own framing of provenance as "an optional input to
  the rule engine", not something `Scanner` reaches out and fetches.
- `provenanceRoot` exists because `ProvenanceRange.FilePath` values are relative to the
  git repository's top-level directory (how `git diff`/`git blame` always report paths,
  confirmed by reading `GitCommandRunner`/`GitOutputParsers`), which is **not** always
  the scanned `path` — e.g. Track B's own CI workflow scans `"$GITHUB_WORKSPACE/src"`, a
  subdirectory of the repository root. Both a violation's and a provenance range's
  `FilePath` are resolved back to absolute paths (`scanRoot`/`provenanceRoot`
  respectively) before comparison, so this reconciles correctly regardless of how deep
  the scanned path sits inside the repository. Defaults to `path` when omitted, for the
  common case of scanning a repository's own root. Covered by
  `ScanAsync_ScannedPathIsSubdirectoryOfProvenanceRoot_StillMatchesCorrectly`.
- **Escalation policy, stated explicitly rather than left implicit:** only
  `ProvenanceSource.Ai` escalates (`info`→`warning`, `warning`→`error`, `error` stays
  `error`) — the literal wording of the SPEC ("an `'ai'`-sourced range").
  `ProvenanceSource.Mixed` (an AI co-author trailer alongside a distinct human one, per
  `AiToolClassifier`) deliberately does **not** escalate: a human co-author on the commit
  is treated as the equivalent of review having happened, not as unattended AI output.
  This is a judgment call, open to revisiting, not a fact derived from the SPEC text.

`src/Portcullis.Cli/Program.cs` gained two new flags so this is reachable from the CLI
(and, later, from `portcullis-pr-check.yml`) without changing the existing `portcullis scan
<path>` contract when they are omitted:

```
portcullis scan <path> [--provenance-range <commitOrRange>] [--provenance-repo <path>]
```

`--provenance-range` triggers `new GitProvenanceProvider(repo).GetProvenance(range)`
before the scan; `--provenance-repo` defaults to `<path>` and only needs setting when
`<path>` is a subdirectory of the actual git repository.

## 4. New tests (`tests/Portcullis.Engine.Tests/ScannerProvenanceTests.cs`)

Exercised against the real, registered rules (`RuleRegistry.All`), not a synthetic
diagnostic — proves the wiring works on the actual scan path, not just the escalation
function in isolation:

| Test | Proves |
|---|---|
| `ScanAsync_WarningViolationInsideAiRange_EscalatesToError` | The P10 (`warning`) rule escalates to `error` when its location is in an `Ai` range. |
| `ScanAsync_ErrorViolationInsideAiRange_StaysError` | The P9 controller rule (already `error`) is unaffected — no invalid "past error" state. |
| `ScanAsync_ProvenanceRangeOnADifferentLine_DoesNotEscalate` | Line-level, not file-level, matching. |
| `ScanAsync_HumanSourcedRange_DoesNotEscalate` | `Human` source is a no-op. |
| `ScanAsync_MixedSourcedRange_DoesNotEscalate` | The `Mixed`-does-not-escalate policy call (section 3) actually holds. |
| `ScanAsync_NoProvenanceGiven_LeavesSeverityUnchanged` | Omitting `provenance` is exactly the pre-M4 behavior — no regression for existing callers. |
| `ScanAsync_ProvenanceRootOmitted_DefaultsToScannedPath` | The default-to-`path` behavior. |
| `ScanAsync_ScannedPathIsSubdirectoryOfProvenanceRoot_StillMatchesCorrectly` | The path-reconciliation case Track B's CI workflow actually needs. |

## 5. End-to-end run against real `<consumer>` (not a fixture, not a mock)

`<consumer>` is the fixture repository `fixtures/consumer-violations.json` is built from,
checked out at `<consumer-checkout>` (HEAD at the fixture's own `verifiedAgainstCommit`,
64 real commits).

**Baseline — `portcullis scan <consumer-checkout>/src`, no provenance:**

```
filesScanned: 156, rulesEvaluated: 3, violations: 15
summary: { errorCount: 4, warningCount: 11, infoCount: 0 }
```

Three of the 15 are exact matches against `fixtures/consumer-violations.json`'s
Roslyn-checkable entries — the same three Track A's pull request already established
(`QrCode.cs:11`/`CONSUMER-003`, `SystemMessage.cs:11`/`CONSUMER-005`,
`AdvertMessage.cs:11`/`CONSUMER-006`), independently reconfirmed live in this session's
own run rather than trusted from that PR's description. All are
`PORTCULLIS_P9_ORPHAN_ENTITY`, default severity `warning`. The 4 pre-existing errors are
all `PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT` (a different, real violation class also
present in `<consumer>` today, not itself a fixture entry).

**Same scan, with real provenance —
`--provenance-repo <consumer-checkout> --provenance-range <root-commit>..HEAD` (the repo's
root commit to `HEAD`, i.e. blame-attribution for every currently-live line):**

```
summary: { errorCount: 9, warningCount: 6, infoCount: 0 }
```

5 of the 5 `PORTCULLIS_P10_CUSTOM_BASE_CLASS` warnings (the web client classes)
escalated to `error`. Spot-checked two, directly against `git blame`/`git log`, not
taken on the tool's word:

- One escalated web client class blames to a commit whose author is
  `Claude <noreply@anthropic.com>` and which carries a `Co-Authored-By: Claude …
  <noreply@anthropic.com>` trailer — genuinely AI-authored, correctly escalated.
- `QrCode.cs:11` (still `warning`, one of the three fixture matches above) blames to a
  commit by a human author with no AI trailer — genuinely human-authored, correctly left
  alone.

This is the concrete proof docs/SPEC.md section 4 asked for: the same architectural
defect gets a stricter gate when it was AI-authored, driven by real git history, not a
synthetic example.

**Full chain — piping that scan into `Portcullis.CiComment`:** renders correctly (grouped
by file, `🆕 new` / `✅ resolved` sections correctly identify the 5 severity-escalated
P10 violations as both "resolved" at their old `warning` severity and "new" at `error`,
since the diff keys on rule+location, not severity), and exits `1` — confirmed as the
literal process exit code, not inferred from the rendered text. `dotnet run --project
src/Portcullis.CiComment -- --current <scan.json>` with no `--pr`/`GITHUB_TOKEN` degrades
to "skipping comment publish" rather than crashing, exactly as designed for a local run.

## 6. An honest gap this run surfaced, not fixed here

The CLI's and `Portcullis.CiComment`'s exit-code gate is **absolute**: `errorCount > 0`
over the whole scan, not `errorCount > 0` restricted to what a given PR's diff
introduced (`portcullis-pr-check.yml`'s own comment already says as much: *"exits non-zero
when the scan has any error-severity violation... so it doubles as the merge gate"*).
Concretely, this means: because `<consumer>`' `src/` already carries 4 pre-existing
`PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT` errors today, dropping `portcullis-pr-check.yml`
onto `<consumer>` right now would fail **every** PR's gate immediately — including one
that touches nothing near those controllers — not specifically a PR that introduces a
new violation.

Separately, and more specific to `fixtures/consumer-violations.json`: 9 of its 11
entries are `PORTCULLIS_P9_ORPHAN_ENTITY`-shaped, whose **default** severity is `warning`,
not `error`. A PR literally reintroducing one of them would not, on its own, fail the
absolute-count gate today — only provenance escalation (section 3, when the offending
lines are AI-authored) or an unrelated pre-existing error in the same tree would make it
fail, as this run's own numbers show. So the roadmap's M4 wording — "[Portcullis]
genuinely blocks a PR carrying one of the documented violations" — holds unconditionally
for the `error`-severity fixture-adjacent class (`CONTROLLER_NO_DBCONTEXT`) and
conditionally for the `warning`-severity orphan-entity class (holds when AI-authored,
via section 3's mechanism; does not hold on its own for a human-authored reintroduction
under the current absolute-count gate).

Not fixed in this session: whether the gate should switch to diff-based ("only new
violations block", using the same before/after comparison `Portcullis.CiComment` already
computes for its `🆕`/`✅` sections) is a real product decision with consequences for
how `<consumer>` dogfooding (M6) would actually work, not something to change as a side
effect of wiring provenance in. Flagged here, deliberately not decided, for whoever
picks up M5/M6.

## 7. What M4 leaves for M5

Per the roadmap, M5 is a full end-to-end mutation pass across the integrated system plus
`FINDINGS.md`: what the *whole* pipeline (rules + provenance escalation + CI gate together) actually catches when
attacked, not what each track's own unit tests already proved in isolation. Section 6
above is exactly the kind of finding `FINDINGS.md` should be built to surface honestly,
not the kind M5 should paper over.
