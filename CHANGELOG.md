# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

**Nothing has been released.** No `v*` tag has been pushed, no package is on nuget.org,
and no container has been published — so every entry below sits under `[Unreleased]`, and
`0.1.0` in `Directory.Build.props` is the version the first release *would* carry, not a
version anyone can install. Same house rule as the rest of this repository: what has not
happened is not written as though it had.

## [Unreleased]

### Added

- **Rule engine on the Roslyn semantic model** — scans C# sources and reports each
  violation at `file:line` as a deterministic, machine-readable verdict.
- **Nine diagnostics across six principles** (P2, P4, P9, P10, P11, P15 of
  [`architecture-standards`](https://github.com/konradcinkusz/architecture-standards)),
  each with unit tests in both directions and a mutant proving the tests would notice if
  the rule broke — see [`docs/MUTATIONS.md`](docs/MUTATIONS.md).
- **Two convention-coverage diagnostics** — `PORTCULLIS_CONVENTION_UNMATCHED` and
  `PORTCULLIS_NO_CONVENTION_MATCHED`, so a gate that matched nothing is loud instead of
  vacuously green.
- **Configurable conventions** — `portcullis.json` for the CLI/Action/container, a
  `.globalconfig` for the analyzer package, both flattened onto one convention model.
  See [`docs/CONFIGURATION.md`](docs/CONFIGURATION.md).
- **Diff-scoped merge gate** — everything found is reported, but only violations inside
  the lines a pull request actually changed block it.
  See [`docs/DIFF-GATE.md`](docs/DIFF-GATE.md).
- **AI provenance as a severity input** — line ranges attributed to an AI agent, read
  from real git history (commit trailers and authorship, never code style), get a
  stricter threshold. Never a badge or a score.
- **Four distribution channels** — the `Portcullis.Analyzers` package, the `portcullis` CLI
  and `portcullis-ci-comment` tools, a composite GitHub Action, and a GHCR container.
  Built and verified locally; none of them published. See
  [`docs/DISTRIBUTION.md`](docs/DISTRIBUTION.md).
- **Sticky pull-request comment** — one comment, updated in place, carrying the diff of
  violations rather than a link to a dashboard.
- **Tag-driven release pipeline** with a consistency gate that fails the build if
  `action.yml`'s default tools version disagrees with the tag being released.
- **Project documentation for outside readers** — `CONTRIBUTING.md`, `SECURITY.md`,
  `CODE_OF_CONDUCT.md`, issue and pull-request templates, and this changelog.
- **Migration rule set** (ticket R2) — four semantic analyzers for .NET Framework idioms
  that survive a migration to modern .NET: `PORTCULLIS_MIG_SYSTEM_WEB`,
  `PORTCULLIS_MIG_HTTPCONTEXT_CURRENT`, `PORTCULLIS_MIG_SYNC_OVER_ASYNC` and
  `PORTCULLIS_MIG_CONFIGURATION_MANAGER`, category `Migration`, configurable through
  `migrationExemptFolders` and `systemWebAllowedTypes`. Tested in both directions, with
  hand-written mutants and a Stryker.NET score of 100 % (214 of 214 mutants detected),
  enforced in CI. See [`docs/rules/MIGRATION.md`](docs/rules/MIGRATION.md) and
  [`docs/MUTATIONS.md`](docs/MUTATIONS.md) §8.
- **SARIF 2.1.0 output with a changed-lines filter and a baseline file** (ticket R3).
  `portcullis scan --sarif <file>` writes the findings that count against the change — on
  the changed lines of `--provenance-range`, less those a baseline accepts — with rule id
  and index, level, message, a location relative to the repository root with its start
  line, and a partial fingerprint; the test suite validates it against the official OASIS
  schema. `--baseline <file>` accepts the findings a team recorded as pre-existing: still
  reported, never blocking; `--write-baseline` records them. A workflow runs this on the
  repository's own pull requests, keeps the SARIF as a build artifact and uploads it to
  GitHub code scanning, and the pull-request gate uses the same committed baseline. See
  [`docs/SARIF.md`](docs/SARIF.md).

### Changed

- **Imported into `konradcinkusz/letsgolegacy.portcullis` as component C5 of
  [Second Key](https://github.com/konradcinkusz/letsgolegacy.secondkey)** (ticket R0 in
  [`docs/WORKPLAN.md`](docs/WORKPLAN.md)). Source, tests, fixtures and the user-facing
  documentation were brought over without the private predecessor's git history;
  internal planning notes were left behind, and the documents that cited them were
  edited so no link dangles. The `net8.0` projects now target `net10.0`; the analyzers
  stay on `netstandard2.0`. CI builds and runs the full suite on every pull request and
  every push to `main`.
- **Name settled as Portcullis (2026-09-13), before anything was published.** The three
  package ids (`Portcullis.Analyzers`, `Portcullis.Cli`, `Portcullis.CiComment`), every
  diagnostic id (`PORTCULLIS_*`), assembly and namespace names, project directories, the
  solution file, the CLI tool commands (`portcullis`, `portcullis-ci-comment`), the config
  file (`portcullis.json`), the container tag, the Action reference, the pull-request
  workflow and the sticky comment's marker all carry it. The earlier name collided with
  an established product in the same problem space; nothing had been published under
  it, so nothing breaks for anyone.
- **Rename finished** (ticket R1 in [`docs/WORKPLAN.md`](docs/WORKPLAN.md)): the earlier
  name appears nowhere in this repository's tree — the dated records now use today's
  identifiers, each with a note saying so — and CI fails if it reappears.

- **The CLI scan compiles against the runtime's framework assemblies** and against
  declarations of the .NET Framework APIs the migration rules look for, instead of
  `System.Private.CoreLib` alone, so a semantic rule binds the same symbols in a scan as in
  a real build. No existing rule's output changed (the full suite passed unmodified).
- **The scan result carries fingerprints and the baseline** (ticket R3, additive): each
  violation gains `fingerprint` and `baselined`, the result a `baseline` object, and the
  gate `acceptedByBaselineCount`. Without `--baseline` every verdict is what it was. The
  pull-request comment marks accepted findings and says when a baseline, rather than the
  diff scope, is why an error did not block; it still compares two scans on the five
  original violation fields, so a cached scan from before fingerprints diffs cleanly.
- **One range check for the diff scope.** Whether a violation is on a changed line is
  decided by `ChangedLines`, shared by the gate, severity escalation and the SARIF filter,
  instead of private helpers in the scanner.

### Fixed

- **The gate no longer fails open when git fails.** `ProvenanceStatus` distinguishes
  "the diff is genuinely empty" from "the git command failed", and the scanner falls back
  to absolute counting with `Gate.DegradedReason` set instead of silently passing.
  This closes the limitation recorded in [`docs/DIFF-GATE.md`](docs/DIFF-GATE.md) §3.
- **The CLI honours configuration.** It previously passed `options: null` to
  `CompilationWithAnalyzers`, discarding analyzer configuration entirely — so the
  analyzer package could be configured and the CLI could not, from the same rules.
- **The pull-request comment path no longer crashes** on malformed or partial scan
  output.
- **Two findings on one line come out in the same order on every run.** Violations were
  sorted by file and line only, and analyzers run concurrently, so the order of findings
  sharing a line — and with it the JSON — could differ between two scans of the same tree.
- **Two sentences left over from the rename**: the no-convention diagnostic suggested
  declaring conventions "in an portcullis.json", and the comment tool's package description
  began "Renders an Portcullis scan result".

### Known limitations

Recorded here because they are load-bearing for anyone evaluating this, and stated at
length in the README's *Honest limits*:

- Single-compilation analysis in the CLI path: types from referenced packages or other
  projects do not resolve. The analyzer package does not have this limitation. The
  migration rules' consequences of it are listed per rule in `docs/rules/MIGRATION.md`.
- Nine of the fifteen principles are unimplemented — five need cross-file, cross-repo or
  historical context, four are not C# at all.
- The project is unpublished: no package, no container and no `v*` tag exist yet.

[Unreleased]: https://github.com/konradcinkusz/letsgolegacy.portcullis/commits/main
