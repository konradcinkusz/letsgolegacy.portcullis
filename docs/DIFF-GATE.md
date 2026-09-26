# Portcullis — diff-scoped merge gate (Milestone M5A)

Date: 2026-08-17. Written the same session as `docs/FINDINGS.md`, immediately after it —
FINDINGS.md's own §5 named this exact gap as "unchanged by this pass... sharper evidence
for a decision, not the decision itself." This document is that decision, made and
verified, in the same honesty register as every other dated note in this repo: what was
actually run, not what the design intends.

## 1. The problem this closes

Every milestone through M5 flagged the same thing without fixing it: `Portcullis.Cli` and
`Portcullis.CiComment` both computed their exit code as `errorCount > 0` over the *entire*
scanned tree, not the PR's own diff. `docs/M4-INTEGRATION.md` §6 first named it;
`docs/FINDINGS.md` §3.2 reproduced it as a deterministic, two-commit repro. The practical
consequence, stated plainly: `<consumer>/src` carries 4 pre-existing
`PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT` errors today, so dropping `portcullis-pr-check.yml`
onto it would fail *every* PR immediately — not just ones introducing new violations.

## 2. Design

**`ScanResult` gains one new, additive field: `Gate`** (`src/Portcullis.Engine/Model/ScanResult.cs`):

```csharp
public sealed record GateResult(bool Blocked, string Scope, int BlockingErrorCount);
```

- `Scope` is `"all"` — the original, unconditional M0–M5 behavior — when `Scanner.ScanAsync`
  is called with no provenance range, or `"diff"` when one is given.
- In `"diff"` scope, `Blocked`/`BlockingErrorCount` only count error-severity violations
  whose location falls inside the given range's changed lines. A pre-existing error
  elsewhere in the tree no longer fails a PR that never touches it — but it is still
  reported in `Violations`/`Summary` exactly as before, for visibility. Nothing about
  what gets *shown* changed, only what gets to *block*.
- Nullable and defaulted (`GateResult? Gate = null`) so every existing positional
  `ScanResult` construction, every pre-existing JSON fixture (Track B's
  `fixtures/mock-engine-output/*.json`), and every consumer that doesn't know this field
  exists keeps working unchanged. `Scanner.ScanAsync` always populates it now; only
  foreign or historical JSON lacking the field deserializes it as `null`.

**`--provenance-range` now does double duty** — it drives both severity escalation (M4,
unchanged: only `Ai`-sourced ranges) and gate scope (new: *any* range, regardless of
source, since "was this line part of the diff at all" doesn't care who wrote it). One
flag, not two, and this was a deliberate choice, not an oversight: escalation can only
make a violation's severity *stricter*, never more lenient, so reusing the same flag for
gating scope never produces a worse outcome than a hypothetical separate flag would —
there is no scenario where a team would want diff-scoped gating without also getting
(at most) stricter treatment of AI-authored lines within that same diff.

**`Portcullis.Cli` and `Portcullis.CiComment`** both changed their exit-code line from reading
`Summary.ErrorCount` directly to `Gate?.Blocked ?? Summary.ErrorCount > 0` — which is
also, as a side effect, the first time this repository's gate decision has been computed
in exactly one place (`Scanner.ComputeAbsoluteGate`/`ComputeDiffScopedGate`) rather than
independently in both CLI entry points. That duplication was already latent before this
change; fixing it wasn't the goal, but it falls out of the same edit.

**`Portcullis.CiComment`'s rendered comment** gains one new, conditional line
(`CommentFormatter.RenderGateNote`), present only when `Gate.Scope == "diff"`: which
tells the reader whether the shown violations are why the build is red, or whether
they're pre-existing and not what failed it. The `"all"`-scope / no-`Gate` rendering is
completely unchanged — every existing `CommentFormatterTests.cs` assertion still passes
untouched.

**`portcullis-pr-check.yml`** is wired to actually pass `--provenance-range`/
`--provenance-repo` for the first time. This is a real, separate finding worth stating
plainly: the provenance escalation feature has existed and been tested since M4, but the
real, deployed CI workflow never once passed it a range — meaning escalation has never
actually run in production, only in manual verification sessions. Fixed here alongside
gating, since both need the same PR base/head range. Also added `fetch-depth: 0` to the
checkout step — `actions/checkout`'s default shallow clone does not contain the PR's
base commit, so `git diff`/`git blame` against it would otherwise fail.

## 3. The fail-open risk, and how it was closed

**Status: fixed.** Recorded here as it was originally written, then what changed, because
the failure mode is worth keeping visible.

### 3.1 What was wrong

`GitProvenanceProvider.GetProvenance` caught `GitCommandException` and degraded to an
*empty* `ProvenanceReport` rather than propagating the failure — a deliberate M4-era
design for graceful degradation on an unresolvable ref. Combined with diff-scoped gating,
that had a sharp edge: if the git commands failed for *any* reason (the shallow-clone case
§2 fixes, but also a corrupted checkout, a force-pushed base branch, a `provenanceRoot`
that isn't a git repository, or a missing git binary), the gate did not fall back to the
safer absolute-count behaviour — it failed **open**, reporting zero blocking violations,
because an empty ranges list matches nothing.

Reproduced before fixing, on a directory holding one error-severity violation and no git
repository at all:

```
$ portcullis scan "$SB" --provenance-range HEAD~1..HEAD --provenance-repo "$SB"
CLI exit code = 0
gate: {'blocked': False, 'scope': 'diff', 'blockingErrorCount': 0}
summary: {'errorCount': 1, 'warningCount': 0, 'infoCount': 0}
stderr:
```

One error present, gate green, exit 0, nothing on stderr.

### 3.2 The fix

`ProvenanceReport` gained a `ProvenanceStatus` — `Complete` or `Degraded` — which is
precisely the distinction the contract could not previously express: "this diff genuinely
changed nothing" and "git failed, so I don't know what it changed" were the same value.
`Scanner.ScanAsync` treats a degraded report as no range at all, falling back to
`scope: "all"` and recording `Gate.DegradedReason`.

The fallback is **never more lenient** than the diff scope it replaces: diff scope over
zero ranges blocks on 0 violations, absolute blocks on N ≥ 0. So the failure mode is now a
gate that is too strict and says why, rather than one that is silently vacuous.

Same command, after:

```
$ portcullis scan "$SB" --provenance-range HEAD~1..HEAD --provenance-repo "$SB"
CLI exit code = 1
gate: {'blocked': True, 'scope': 'all', 'blockingErrorCount': 1,
       'degradedReason': "git could not resolve 'HEAD~1..HEAD' in '...': ... Could not access 'HEAD~1'"}
stderr:
portcullis: warning: could not scope the gate to 'HEAD~1..HEAD' — git could not resolve ...
portcullis: warning: falling back to gating on the whole scan (Gate.Scope "all").
```

### 3.3 Degradation is about the range set, not attribution

Deliberately, a `git blame` or commit-classification failure does **not** degrade the
report. At that point the changed-line set is already known and correct, so the gate can
still scope to it honestly; all that is lost is *who* wrote those lines, which only feeds
severity escalation — and escalation can only make a violation stricter, so losing it
never turns a blocking violation into a passing one. Degrading there would flip the whole
scan to absolute gating over one unreadable commit, re-blocking PRs on pre-existing debt,
which is the exact regression M5A's diff-scoped gate existed to remove.

### 3.4 Two adjacent faults closed with it

- `ClassifyCommit`'s `git log` ran outside both of the span loop's try blocks, so a
  failure there propagated out of `GetProvenance` and killed the process — while its
  sibling `git diff` failing went silently green. Same fault class, two opposite outcomes,
  neither of them the defined one. It now returns an unattributed classification.
- `GitOutputParsers.ParseAddedSpans` silently dropped every hunk whose `+++` header it
  could not parse. git exits 0, nothing throws, and the only symptom is a smaller span
  list — which under a diff-scoped gate means a quietly weaker gate. It now reports
  `SawUnattributedHunk`, which degrades the report. Note the naive version of this check
  is wrong: a pure-deletion commit and a binary-only commit both legitimately produce a
  non-empty patch with zero spans, so "patch text but no spans" is *not* a failure signal.
  Deletions are distinguished by a successfully-parsed `+++ /dev/null` header.

## 4. Verification

### 4.1 Unit tests (`tests/Portcullis.Engine.Tests/ScannerGateTests.cs`)

Six tests against the real registered rules, mirroring `ScannerProvenanceTests.cs`'s own
style: no-provenance parity with the old absolute behavior; an error outside the given
range does not block despite being reported; an error inside the range blocks; an
AI-escalated warning composes correctly into a block; a human-authored error inside the
range still blocks (proving gating is source-agnostic, unlike escalation); a resolved-but-
empty range (a real, clean PR diff) does not block. Plus three new
`CommentFormatterTests.cs` cases for the new rendering, and both pre-existing test
suites still pass unchanged.

```
$ dotnet test portcullis.sln
...
Passed!  - Failed: 0, Passed: 26, Skipped: 0, Total: 26 - Portcullis.CiComment.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 69, Skipped: 0, Total: 69 - Portcullis.Engine.Tests.dll (net8.0)
```

### 4.2 The exact M5 scenario 4 repro, re-run with diff-scoped gating

`docs/FINDINGS.md` §3.2's scenario 4 (a clean, unrelated one-file commit E on top of an
earlier error-introducing commit D, same scratch repo, never committed/pushed anywhere)
re-scanned with `--provenance-range D..E` (i.e., treating "just commit E" as the PR):

```
summary: { errorCount: 1, warningCount: 2, infoCount: 0 }
gate:    { blocked: false, scope: "diff", blockingErrorCount: 0 }
```

Exit code `0` (was `1` under the old absolute gate) — the BadController error is still
reported, still visible, but no longer fails a PR that never touches it. The AI-escalation
scenario (2b) was re-run the same way and still blocks (`gate.blocked: true`,
`blockingErrorCount: 1`) — escalation and gating compose correctly.

### 4.3 Real `<consumer>` history — the strongest evidence, not a controlled fixture

`<consumer>`' actual most recent merge at the time — a pull request that added a web
client, merged at the commit `fixtures/consumer-violations.json` was verified against —
scanned diff-scoped against its real first parent as the PR base:

```
summary: { errorCount: 9, warningCount: 9, infoCount: 0 }
gate:    { blocked: true, scope: "diff", blockingErrorCount: 5 }
```

This is not a synthetic scenario — it is what the diff-scoped gate would have actually
done to a real, already-merged `<consumer>` PR. `errorCount: 9` (vs. the 4-error baseline
this document's own §1 cites) reflects the 5 `PORTCULLIS_P10_CUSTOM_BASE_CLASS` warnings on
the new client classes escalating to errors — the same 5 violations
`docs/M4-INTEGRATION.md` §5 already spot-checked as genuinely Claude-authored via
`git blame`. `blockingErrorCount: 5` confirms the gate correctly attributes exactly those
5 to this PR's own diff, and correctly does **not** blame it for the 4 unrelated,
pre-existing `CONTROLLER_NO_DBCONTEXT` errors sitting elsewhere in the tree — piped
through the full `Portcullis.CiComment` chain, rendering the new
"🚫 **Blocking this PR** — 5 errors within this PR's own changed lines." line and exiting
`1`, confirmed as the literal process exit code, not inferred from the JSON.

## 5. What this does and does not resolve

This resolves the specific, repeatedly-flagged gap: the gate now distinguishes what a PR
introduces from what already existed. It was verified on both a controlled, disposable
fixture and real, historical `<consumer>` data — not one or the other.

It does not: give `portcullis-pr-check.yml` a live end-to-end run in this session (that
happens naturally the next time a PR is opened against this repository, since the workflow
now dogfoods its own fix); or change anything about which 6 rules exist or what they catch.
Dogfooding the gate on `<consumer>`' own repair PRs was sequenced after this milestone
specifically because an active gate there needed this fix to be workable at all.

**The fail-open risk in §3 is fixed.** As first written, this list also said the milestone
left that risk open at the contract level. It has since been closed there: a
`ProvenanceReport` carries a `ProvenanceStatus`, and a degraded one makes the scan fall back
to the whole-tree gate with `Gate.DegradedReason` set, as §3.2 records.

## 6. Since then: SARIF, the baseline, and one range check (ticket R3, 2026-09-26)

Recorded here because it changes where §2's logic lives, not what it does.

- **The range check is one class now.** The two private helpers that decided whether a
  violation sat inside the diff moved, unchanged in behaviour, into
  `Portcullis.Engine.Provenance.ChangedLines`. The gate, severity escalation and the new
  SARIF changed-lines filter all ask it, so SARIF cannot disagree with the gate about which
  findings belong to a pull request. It refuses to be built from a degraded provenance
  report, which puts §3's fail-safe rule into the type rather than into each caller.
- **The gate can be given a baseline.** Errors the baseline accepts are still reported but
  no longer count toward `blockingErrorCount`, in either scope and in the degraded
  fallback; `gate.acceptedByBaselineCount` says how many that was. Without a baseline every
  number here is what it was.

[`SARIF.md`](SARIF.md) has the design, the fingerprint the baseline is keyed on, and the
verification.
