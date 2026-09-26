# Portcullis — SARIF output, changed-lines filter and baseline (ticket R3)

Date: 2026-09-26. Ticket R3 in [`WORKPLAN.md`](WORKPLAN.md): *SARIF 2.1 output with a
changed-lines filter and a baseline file*, done when *a pull request produces filtered
SARIF*. This document is the design and the evidence, in the same register as
[`DIFF-GATE.md`](DIFF-GATE.md), which this work builds on: what was actually run, with its
output, and what was not.

**Contents**

1. [What `--sarif` writes](#1-what---sarif-writes)
2. [Which findings it contains](#2-which-findings-it-contains)
3. [The baseline file](#3-the-baseline-file)
4. [A worked example, actually run](#4-a-worked-example-actually-run)
5. [On this repository's own pull requests](#5-on-this-repositorys-own-pull-requests)
6. [Verification](#6-verification)
7. [Limits](#7-limits)

## 1. What `--sarif` writes

```sh
portcullis scan ./src \
  --provenance-range "$BASE_SHA..$HEAD_SHA" \
  --provenance-repo . \
  --baseline portcullis-baseline.json \
  --sarif portcullis.sarif
```

`--sarif <file>` writes a SARIF 2.1.0 log (the OASIS standard, maintained at
[oasis-tcs/sarif-spec](https://github.com/oasis-tcs/sarif-spec)) alongside the usual JSON
on stdout; nothing about stdout or the exit code changes. The log is built by
`Portcullis.Engine.Sarif.SarifReport` and holds one run:

| Part | What Portcullis writes |
|---|---|
| `$schema`, `version` | The OASIS schema URI for 2.1.0 (errata 01), and `"2.1.0"`. |
| `tool.driver` | `name` `Portcullis`, `version` (the engine's informational version, including the commit it was built from), `informationUri`, and `rules`: every registered rule, sorted by id, with its title as short and full description, a `help` text and `helpUri` pointing at the rule family's documentation, `defaultConfiguration.level`, and its category as `properties.category` and a tag. |
| `invocations[0]` | `executionSuccessful: true`, plus a `toolExecutionNotifications` entry for each thing the reader of the log should know: the diff scope could not be applied (§2), the configuration file could not be read, or some baseline entries matched nothing (§3). |
| `originalUriBaseIds` | `%SRCROOT%`: the repository root as an absolute `file:` URI. |
| `results` | One per finding that counts against the change (§2). |
| `properties` | `scope` (`diff` or `all`), `findingsInScan`, `excludedOutsideChangedLines`, `excludedByBaseline` — so what the two filters removed is counted in the log itself rather than lost. |

Each result carries the fields the ticket asks for, and one more when a baseline is in use:

| Field | Value |
|---|---|
| `ruleId`, `ruleIndex` | The rule, and its index in `tool.driver.rules`. |
| `level` | The violation's severity after provenance escalation: `error` → `error`, `warning` → `warning`, `info` → `note`. |
| `message.text` | The violation's message. |
| `locations[0].physicalLocation` | `artifactLocation.uri` relative to the repository root with `uriBaseId: "%SRCROOT%"`, each path segment percent-encoded; `region.startLine`. |
| `partialFingerprints` | `{"portcullisFingerprint/v1": "<sha-256>"}` — the finding's identity across commits (§3.2). |
| `baselineState` | `"new"` when the scan was given a baseline: everything a baseline accepted has been left out, so what remains is new relative to it. Absent otherwise. |

**Locations are relative to the repository root, not the scanned directory.** A SARIF
consumer resolves them against its checkout: GitHub code scanning looks for
`src/Shop/Orders.cs`, not `Shop/Orders.cs`. The root is `--provenance-repo` when given and
the scanned path otherwise, so pass `--provenance-repo` whenever you scan a subdirectory —
the same rule the diff-scoped gate already had.

A repository-wide finding (the convention-coverage diagnostics report against the whole
compilation) has no file to point at and is written without `locations`, which SARIF
allows. It is never on a changed line, so it only appears in a log that is not
diff-scoped; GitHub code scanning does not display a result without a location.

## 2. Which findings it contains

The SARIF holds **the findings that count against the change**: the gate's own definition
of scope, applied to every severity rather than only to errors.

- **Changed lines.** With `--provenance-range`, only findings on the lines that range
  changed. The check is `Portcullis.Engine.Provenance.ChangedLines`, and it is the same
  object the gate asks: before this ticket the diff-scoped gate's range check lived in
  private helpers inside `Scanner`; it was lifted into one class that the gate, severity
  escalation and the SARIF filter all use, so a SARIF log can never disagree with the
  gate about which findings belong to a pull request.
- **The baseline.** With `--baseline`, findings the baseline accepts are left out.

Nothing is dropped silently: `properties.excludedOutsideChangedLines` and
`properties.excludedByBaseline` count what each filter removed, and the scan's JSON on
stdout still lists every finding, with `baselined: true` on the accepted ones.

**When the diff scope cannot be computed, the SARIF covers the whole scan and says why.**
If git cannot resolve the range, the gate falls back to the whole tree
([`DIFF-GATE.md` §3](DIFF-GATE.md)); the SARIF does the same, and carries the reason as a
`warning` tool notification. A filter that fell back to "nothing changed" would publish an
empty log for a pull request whose changes were never looked at — the SARIF form of the
fail-open gate that section closed. `ChangedLines` refuses to be built from a degraded
provenance report at all, so that fallback cannot be skipped by mistake.

## 3. The baseline file

### 3.1 What it is for

The diff-scoped gate already keeps pre-existing findings from blocking a pull request that
does not touch them. The baseline covers what the diff scope cannot:

- **Pre-existing findings on changed lines that did not really change.** Re-indenting a
  block, moving a method within its file, or normalising line endings puts old findings
  inside the diff.
  Under the diff scope alone they block, although the pull request introduced nothing;
  accepted in the baseline, they do not (§4 shows exactly this).
- **Whole-tree gates.** A scheduled audit, a CI system with no usable commit range, and the
  degraded fallback above all gate on the whole tree. Without a baseline that blocks on all
  existing debt at once; with one, only on what is new.
- **SARIF for code scanning** stays limited to what is new, instead of re-reporting the
  accepted debt on every upload.

A finding the baseline accepts is still **reported** — in the JSON, with
`baselined: true`, and in the pull-request comment, marked *(accepted by the baseline)*.
It only stops **blocking**. `gate.acceptedByBaselineCount` counts the errors in scope it
accepted; `gate.blockingErrorCount` plus that number is every error in scope.

### 3.2 Fingerprints

A baseline entry is a finding's fingerprint, `portcullisFingerprint/v1`: the SHA-256 of the
rule id, the file path (relative to the scanned directory), the text of the finding's line
with its whitespace collapsed, and an occurrence number — implemented in
`Portcullis.Engine.Findings.FindingFingerprint`, pinned by a test against a value computed
outside .NET.

| Change | Fingerprint |
|---|---|
| Lines added or removed elsewhere in the file | unchanged |
| The line re-indented, or its whitespace changed | unchanged |
| The rule's message reworded in a later version | unchanged, except for a repository-wide finding, whose message stands in for its line |
| The finding's severity changed (escalation, configuration) | unchanged |
| The line itself rewritten | **changes** — the finding is new, and a reviewer should look at it again |
| The file renamed or moved | **changes** |
| The scan run on a different directory | **changes**: write and read a baseline with the same scan path |

Line numbers are deliberately not an input. A baseline keyed on line numbers goes stale at
the first edit above a finding: every accepted finding below it moves, stops matching, and
comes back as new — so the baseline would block exactly the pull requests it was written to
let through. Two findings of one rule on identical text in one file are told apart by
occurrence, counted in file order. A repository-wide finding has no line, so its message
stands in for the line text.

### 3.3 The file

```sh
# accept every current finding, once, and commit the file
portcullis scan ./src --baseline portcullis-baseline.json --write-baseline
```

This repository's own [`portcullis-baseline.json`](../portcullis-baseline.json), written
exactly that way, holds the two findings `src/` had when this ticket was done:

```json
{
  "schemaVersion": "1.0.0",
  "fingerprintVersion": "portcullisFingerprint/v1",
  "about": "Findings accepted as pre-existing. …",
  "findings": [
    {
      "fingerprint": "3ded74b18cca77948520679cf45b3fb644383312223aadfe5141c50c9c84cd1c",
      "ruleId": "PORTCULLIS_MIG_SYNC_OVER_ASYNC",
      "filePath": "Portcullis.Engine/Provenance/GitCommandRunner.cs",
      "line": 74,
      "message": "'stderrTask.Result' blocks the calling thread until the task completes (Task<TResult>.Result). …"
    },
    …
  ]
}
```

Only `fingerprint` is ever matched. The rule, file, line and message sit beside it so that a
pull request which grows the baseline shows its reviewer what is being accepted, rather than
a column of hashes; `line` is where the finding was when the file was written. Entries are
sorted by file, line and rule, with `\n` line endings on every platform, so a rewritten
baseline diffs only where findings changed.

- **`--write-baseline`** writes every finding of the scan to the `--baseline` file,
  replacing it, and judges the run against the new file — so that run passes, and the next
  blocks only on what is new. It requires `--baseline`; on its own it is a usage error
  rather than a flag that quietly does nothing.
- **A file that cannot be used is an error, never an empty baseline.** A missing file,
  invalid JSON, a `schemaVersion` other than 1.x, a different `fingerprintVersion`, or an
  entry without a fingerprint stops the run with exit code 2 and a message naming the file
  and the fix. Reading it as "accepts nothing" would be safe for the gate, but it would turn
  a typo in a workflow into a gate that blocks on every pre-existing finding, with nothing
  saying why.
- **Stale entries are counted, not hidden.** The JSON's `baseline` object reports
  `entryCount` and `acceptedCount`; when entries matched nothing (the debt was paid, or the
  line was rewritten), the CLI says so on stderr and the SARIF carries a `note`
  notification. They never block anything; rewriting the baseline drops them.

## 4. A worked example, actually run

A scratch repository with two pre-existing findings, accepted in a baseline, and a pull
request that re-indents one of them and adds a new error. Run on 2026-09-26 with the CLI
built from this branch; the repository was created for the run and thrown away after it.

```sh
# commit A: Settings.cs reads ConfigurationManager (an error), Orders.cs blocks on .Result (a warning)
$ portcullis scan src --baseline portcullis-baseline.json --write-baseline
portcullis: wrote 3 finding(s) to the baseline 'portcullis-baseline.json'; this run is judged against it.

# commit B, the pull request: re-indents Settings.cs line 7, adds Pricing.cs with a new error
$ portcullis scan src --provenance-range A..B --provenance-repo . \
    --baseline portcullis-baseline.json --sarif pr.sarif
exit 1
summary:  { errorCount: 2, warningCount: 2, infoCount: 0 }
gate:     { blocked: true, scope: "diff", blockingErrorCount: 1, acceptedByBaselineCount: 1 }
baseline: { path: "portcullis-baseline.json", entryCount: 3, acceptedCount: 3 }
```

The third baseline entry is the repository-wide coverage warning a scratch repository with
no conventions gets. The re-indented line is part of the diff, and its error is still
there; the baseline recognises it, so only the new error blocks. The same range without
`--baseline` blocks on both (`blockingErrorCount: 2`). The SARIF holds exactly the new
finding:

```json
"results": [
  {
    "ruleId": "PORTCULLIS_MIG_CONFIGURATION_MANAGER",
    "ruleIndex": 1,
    "level": "error",
    "message": { "text": "'ConfigurationManager.AppSettings' reads settings through System.Configuration.ConfigurationManager. …" },
    "locations": [ { "physicalLocation": {
      "artifactLocation": { "uri": "src/Shop/Pricing.cs", "uriBaseId": "%SRCROOT%" },
      "region": { "startLine": 7 } } } ],
    "partialFingerprints": { "portcullisFingerprint/v1": "fac5d50ce7ade0e13f64cb9bb502ebed456ea2d3724a12e99769791332c7fbd5" },
    "baselineState": "new"
  }
],
"properties": { "scope": "diff", "findingsInScan": 4, "excludedOutsideChangedLines": 2, "excludedByBaseline": 1 }
```

and the pull-request comment (`portcullis-ci-comment`) for the same scan says:

> 🚫 **Blocking this PR** — 1 error within this PR's own changed lines. 1 more there is accepted by the baseline.

## 5. On this repository's own pull requests

[`.github/workflows/portcullis-sarif.yml`](../.github/workflows/portcullis-sarif.yml) runs
on every pull request here: it builds the CLI, scans `src/` with the pull request's range,
`--provenance-repo` set to the checkout and this repository's own
`portcullis-baseline.json`, and then

- writes a job summary of what each filter removed and what is left;
- keeps `portcullis.sarif` and the scan's JSON as the build artifact **`portcullis-sarif`**;
- uploads the SARIF with `github/codeql-action/upload-sarif@v4`, category `portcullis`,
  so the findings appear in GitHub code scanning. A pull request from a fork runs with a
  read-only token that the upload would be refused with, so it is skipped there; the
  artifact is still produced.

It reports and never blocks: exit code 1 is recorded, and only a tool failure (exit 2, or no
SARIF) fails the job. The gate stays in `portcullis-pr-check.yml`, which now passes the same
`--baseline`. The two are separate so that only the SARIF job holds
`security-events: write`.

The first pull request to run it was the one that added it; its run and artifact are
linked from that pull request's description.

## 6. Verification

- **Schema.** Every SARIF log the tests produce is validated against the official OASIS
  SARIF 2.1.0 schema, embedded in the test assembly
  ([`THIRD-PARTY-NOTICE.md`](../tests/Portcullis.Engine.Tests/Sarif/THIRD-PARTY-NOTICE.md)
  records where it comes from; a test pins its SHA-256) and evaluated with
  [JsonSchema.Net](https://github.com/json-everything/json-everything) with format
  assertions on. The schema declares draft-04, which JsonSchema.Net does not implement, so
  it is evaluated as draft-07; a test asserts that it uses only keywords whose meaning is the
  same in both drafts and only local references, which is what makes that faithful. Negative
  controls show the validator rejecting a wrong version, a result with no message, line 0,
  a tool with no driver, an unescaped space in a URI and an unknown level — without them, a
  validator that accepted everything would make every "valid SARIF" assertion vacuous.
- **Content** (`SarifReportTests`): real scans rendered as SARIF — the fields of each
  result, both filters and their counts, the degraded fallback and its notification, a
  stale-baseline note, an unreadable config file, escaping of a space, a `#` and a
  non-ASCII letter in paths, level mapping, a rule descriptor for every registered rule,
  deterministic output.
- **Baseline and fingerprints** (`FindingFingerprintTests`, `BaselineFileTests`,
  `ScannerBaselineTests`, `ChangedLinesTests`): the pinned fingerprint value; what changes
  it and what does not, through the scanner; the gate with a baseline in all three scopes
  (whole tree, diff, degraded); every unusable-file case; deterministic output and
  ordering, including two findings on one line, which used to come out in whichever order
  concurrent analyzers finished.
- **The CLI as a process** (`SarifAndBaselineEndToEndTests`): write a baseline, pass with
  it, block on a new error with only that error in the SARIF; a diff-scoped SARIF from a
  real two-commit git history; a missing baseline file and an unwritable SARIF path each
  exit 2.
- **The comment** (`CommentFormatterTests`): the baseline notes in each scope and the
  per-finding marker; and `ViolationDiff` no longer treats a finding whose fingerprint or
  baseline status changed as resolved and re-introduced.

## 7. Limits

- **Regions carry the start line only.** A violation records a line, not a column or an
  end, so that is what SARIF gets.
- **A baseline is tied to the scanned directory**, because fingerprints contain paths
  relative to it (§3.2). Scanning `src` in one workflow and the repository root in another
  needs two baselines.
- **Identical lines are told apart only by order.** If a copy of an accepted line is added
  above it, one finding is correctly reported as new, but on whichever of the identical
  lines comes second.
- **The composite GitHub Action does not expose `--sarif` or `--baseline` yet.** It
  installs the tools from NuGet, where nothing is published, so a change to it could not be
  exercised here; the workflow in §5 calls the CLI directly.
- **Code scanning's own view is out of this repository's hands.** Which alerts GitHub
  shows on a pull request, and how it tracks them between runs, is GitHub's processing of
  the uploaded log; Portcullis's part ends at a schema-valid, filtered file.
