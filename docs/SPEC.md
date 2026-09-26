# Portcullis — SPEC (Milestone M0)

This document covers exactly five things, and nothing else: the rule contract format,
the violation record schema, the engine output JSON schema (frozen, with a worked
example that was actually run), the provenance signal interface, and an
expressible/deferred/out-of-scope call on every principle P1–P15. Written before any
rule is implemented, per the project's own house rule: spec before code, always.

Everything downstream of this document treats it as frozen. In particular, Track B
(`track-b-ci`) builds and tests its GitHub Action against a hand-written mock file that
conforms exactly to section 3's schema, without waiting for Track A's real engine — that
only works if this schema does not move under it.

---

## 1. The rule contract format

### 1.1 What architecture-standards actually exports

`architecture-standards` exports rules as **Agent Plugins 1.0.0** — a vendor-neutral
packaging format (`https://agent-plugins.org/`), read natively by GitHub Copilot, VS
Code, Claude Code, Cursor and Codex. `architecture-standards` does not vendor a local
`.schema.json` file; the `$schema` field on every plugin manifest is a reference-only URL
(`https://agent-plugins.org/schemas/1.0.0/plugin.schema.json`). The practical, enforced
shape of the format — verified against `scripts/build-marketplace.mjs` (the generator),
`scripts/validate-marketplace.mjs` (the independent validator), and the real generated
artifacts — is:

**`marketplace.json`** (two byte-identical copies: `.claude-plugin/marketplace.json` and
`.github/plugin/marketplace.json`):

| Field | Type | Required |
|---|---|---|
| `name` | string | yes |
| `metadata.description` | string | yes |
| `metadata.version` | semver string | yes |
| `owner.name` | string | yes |
| `plugins[]` | array, non-empty | yes |
| `plugins[].name` | string | yes |
| `plugins[].description` | string | yes |
| `plugins[].version` | semver string | yes |
| `plugins[].source` | string, must start with `"./"` | yes |

**`plugin.json`** (two byte-identical copies per plugin: root and `.claude-plugin/`):

| Field | Type | Required |
|---|---|---|
| `$schema` | string, exact URL `https://agent-plugins.org/schemas/1.0.0/plugin.schema.json` | yes |
| `name` | string, must equal the marketplace entry's `name` | yes |
| `description` | string | yes |
| `version` | semver, must equal the marketplace entry's `version` | yes |
| `author` | object `{name, url}` | in practice |
| `keywords` | string array | in practice |

**`SKILL.md`** (YAML front matter): `name` (kebab-case, must equal the containing folder
name) and `description` (10–1024 chars, must contain the substring `"use when"`
case-insensitively — this is what makes automatic delegation work).

**`catalog/marketplace.catalog.json`** — the one hand-authored source file everything else
generates from. Each plugin entry carries `skills[]`, and each skill carries an optional
`principles` field: a flat array of bare-token strings, e.g. `"principles": ["P7",
"P12"]` or `["P1-P15"]`.

### 1.2 The gap this mapping has to cross honestly

**None of P1–P15 is individually machine-readable in `architecture-standards`.** They are
H3 prose headings inside one Markdown document
(`docs/architecture/00-REFERENCE-ARCHITECTURE.md`), referenced elsewhere only by the bare
token string (`"P7"`) inside `catalog/marketplace.catalog.json`'s `skills[].principles`
arrays. This is not portcullis's assumption — it is a gap `architecture-standards` names
about itself, in its own `docs/proposals/MARKETPLACE-PACKAGING.md`: *"P1–P15 still have no
stable anchors... a skill can cite `P7` but cannot link to it."* The Agent Plugins format
has no concept of a single, individually executable rule at all — it packages
skills/agents for an AI assistant to load, not machine-checkable static-analysis rules.

So the field-by-field mapping below is honest about which fields are actually inherited
and which are new, invented by portcullis because nothing upstream exists to inherit:

| portcullis rule field | Type | Inherited from architecture-standards? | Source |
|---|---|---|---|
| `ruleId` | string | **No — new.** | Convention: `PORTCULLIS-<PrincipleId>-<SLUG>`, e.g. `PORTCULLIS-P2-KERNEL-LOC-CEILING`. Becomes the Roslyn `DiagnosticDescriptor.Id`. |
| `principleIds` | string[] | **Yes.** | The same bare-token convention already used in `catalog/marketplace.catalog.json` → `plugins[].skills[].principles[]` (e.g. `["P2"]`). This is the sole join key back to architecture-standards; it is a string, not a link, for the same reason architecture-standards' own catalog uses a string — there is nothing to link to yet. |
| `sourcePlugin` | string | **Yes.** | `marketplace.json` → `plugins[].name` / `plugin.json` → `name` (e.g. `"architecture-core"`) — which installable package the principle text was read from. |
| `sourceDoc` | string (repo-relative path) | **Yes.** | `catalog/marketplace.catalog.json` → `skills[].source` (e.g. `docs/architecture/00-REFERENCE-ARCHITECTURE.md`) — the prose document, since the principle itself has no finer-grained anchor. |
| `title` | string | Loosely modeled on | `plugin.json`'s `description` field, but scoped to one rule instead of one whole plugin. |
| `description` | string | Loosely modeled on | `SKILL.md`'s `description` field — the same "when does this fire" phrasing convention, adapted from "when should an assistant load this skill" to "when does this rule fire." |
| `defaultSeverity` | `"error"` \| `"warning"` \| `"info"` | **No — new.** | No severity concept exists anywhere in Agent Plugins. |
| `roslynAnalyzer` | string (fully-qualified type name) | **No — new.** | The `DiagnosticAnalyzer` subclass implementing the rule, registered in `Portcullis.Rules.RuleRegistry.All`. |

A rule that cannot honestly be given a `principleIds` entry (nothing in P1–P15 fits) is
not forced into one — see section 5.

---

## 2. The violation record schema

Exactly the five fields fixed at bootstrap, implemented as
`Portcullis.Engine.Model.Violation` (`src/Portcullis.Engine/Model/Violation.cs`):

| JSON field | Type | Meaning |
|---|---|---|
| `ruleId` | string | The portcullis rule id that fired, e.g. `"PORTCULLIS-P2-KERNEL-LOC-CEILING"`. |
| `filePath` | string | POSIX-style (`/`-separated), relative to the scanned path. |
| `line` | integer | 1-based line number. |
| `message` | string | Human-readable description of the specific violation. |
| `severity` | `"error"` \| `"warning"` \| `"info"` | Maps 1:1 from Roslyn's `DiagnosticSeverity` (`Hidden` folds into `"info"`). |

```json
{
  "ruleId": "PORTCULLIS-P2-KERNEL-LOC-CEILING",
  "filePath": "src/ServiceDefaults/Extensions.cs",
  "line": 812,
  "message": "ServiceDefaults exceeds the 800-line kernel ceiling (currently 812 lines). See architecture-standards P2.",
  "severity": "error"
}
```

---

## 3. The engine output JSON schema

The exact shape `portcullis scan <path>` prints to stdout, implemented as
`Portcullis.Engine.Model.ScanResult` (`src/Portcullis.Engine/Model/ScanResult.cs`):

| JSON field | Type | Meaning |
|---|---|---|
| `schemaVersion` | string | This schema's own version. `"1.0.0"` today. Bump on any breaking field change. |
| `engineVersion` | string | The running engine's assembly informational version (includes the source commit when built from git, e.g. `"1.0.0+<sha>"`). |
| `scannedPath` | string | Absolute path that was scanned. |
| `scanStartedUtc` | string (ISO 8601, UTC) | Wall-clock start time of the scan. |
| `scanDurationMs` | number | Wall-clock scan duration in milliseconds. |
| `filesScanned` | integer | Count of `*.cs` files parsed (excludes `bin/`, `obj/`, `.git/`). |
| `rulesEvaluated` | integer | Count of rules in `RuleRegistry.All` that ran. `0` until Track A adds rules. |
| `violations` | `Violation[]` | See section 2. Sorted by `filePath` then `line`. |
| `summary.errorCount` / `.warningCount` / `.infoCount` | integer | Counts of `violations` by severity. |
| `gate` | object \| null | **Added post-M0, milestone M5A (docs/DIFF-GATE.md) — not part of the original frozen shape above.** `{blocked: bool, scope: "all" \| "diff", blockingErrorCount: int}`. `null` only for JSON predating this field; every scan since M5A populates it. `scope: "all"` is the original, unconditional M0–M5 gate (`blocked = errorCount > 0`); `scope: "diff"` (when `portcullis scan` is given `--provenance-range`) counts only error-severity violations inside that range's changed lines. Full rationale, including why this is additive rather than a `schemaVersion` bump, in docs/DIFF-GATE.md section 2. |

### Worked example — actually run, not hand-written

An example that has never been run is exactly the kind of unverified claim this
project's own house style rejects. This is the literal output of:

```
$ dotnet run --project src/Portcullis.Cli -- scan <consumer-checkout>/src
```

— run against `<consumer>`' full `src/` tree (156 `*.cs` files, the fixture repository this
whole project is oriented around), on 2026-08-17, immediately after the M2 skeleton
built clean and its 3-test suite passed:

```json
{
  "schemaVersion": "1.0.0",
  "engineVersion": "1.0.0+<commit>",
  "scannedPath": "<consumer-checkout>/src",
  "scanStartedUtc": "2026-08-17T12:45:56.0460086Z",
  "scanDurationMs": 522.9423,
  "filesScanned": 156,
  "rulesEvaluated": 0,
  "violations": [],
  "summary": {
    "errorCount": 0,
    "warningCount": 0,
    "infoCount": 0
  }
}
```

Zero rules, zero violations, exit code `0` — a valid, schema-conforming, honestly empty
result. `filesScanned: 156` and `scanDurationMs` are the actual numbers from that run,
not placeholders; only the private checkout path and the build commit in
`engineVersion` are shown as placeholders here. The numbers will differ (and the
`engineVersion` commit suffix always will, since it tracks whatever commit HEAD was at
build time) on any later run, but the shape will not.

### Scope limit, stated here rather than discovered later

This milestone parses target source directly (`CSharpSyntaxTree.ParseText` over every
`*.cs` file, then one `CSharpCompilation`) rather than loading it through
`MSBuildWorkspace`/real project references. That means single-file syntax and
single-compilation semantic analysis work; anything requiring real project/assembly
references (resolving a type from a referenced NuGet package or another project) does
not, yet. Recorded in `docs/BOOTSTRAP.md`, not hidden here.

---

## 4. The provenance signal interface

Contract only — implemented by Track C (`track-c-provenance`), consumed as an optional
input to the rule engine at integration (M4), not wired into `Scanner.ScanAsync` in this
milestone. Types live in `src/Portcullis.Engine/Provenance/ProvenanceModels.cs`.

```json
{
  "schemaVersion": "1.0.0",
  "commit": "<git sha>",
  "ranges": [
    {
      "filePath": "src/Foo/Bar.cs",
      "startLine": 10,
      "endLine": 25,
      "source": "ai",
      "confidence": 0.82,
      "attribution": {
        "tool": "claude-code",
        "method": "commit-trailer",
        "commitSha": "<git sha>"
      }
    }
  ]
}
```

| Field | Type | Meaning |
|---|---|---|
| `schemaVersion` | string | This interface's own version. |
| `commit` | string | The commit or diff range this report was computed against. |
| `ranges[].filePath` | string | POSIX-style, repo-relative. |
| `ranges[].startLine` / `.endLine` | integer | 1-based, inclusive line span — **not** a whole file, **not** a whole session. |
| `ranges[].source` | `"human"` \| `"ai"` \| `"mixed"` \| `"unknown"` | |
| `ranges[].confidence` | number, 0.0–1.0 | |
| `ranges[].attribution.tool` | string or `null` | e.g. `"claude-code"`, `"github-copilot"`, `"cursor"` — `null` if not determinable. |
| `ranges[].attribution.method` | string | e.g. `"commit-trailer"`, `"git-blame-authorship"`, `"heuristic"`. |
| `ranges[].attribution.commitSha` | string or `null` | |

Consumption contract (for M4, not this milestone): a rule may read a
`ProvenanceReport` alongside the compilation and apply a stricter `defaultSeverity`
threshold when a violation falls inside an `"ai"`-sourced range. Provenance is never a
standalone report or a separate CLI verb — one of the decisions fixed before any code
was written: "this line was written by an AI" with no action following from it is a
curiosity, not a product, so the signal only ever changes which threshold applies.

### Why this shape, specifically — the failure it is designed not to repeat

[`copilot-scope`](https://github.com/konradcinkusz/copilot-scope) already tried an
AI-provenance signal ("edit survival") and it did not pass verification in an earlier
audit. Confirmed directly in `copilot-scope` source, not taken on faith from its own
docs:

- **It is a session-level average, not a line-level attribution.** The ingestion path
  (`CopilotScope.Collector/Domain/SessionStore.cs`, `IngestMetric`) stores
  `point.Value / point.Count` — an already-editor-averaged histogram mean — as one more
  sample in a flat `List<double>` on the session object
  (`CopilotSession.SurvivalFourGram` / `.SurvivalNoRevert`,
  `CopilotScope.Collector/Domain/CopilotSession.cs`). The analyzer then averages that
  list again per session (`Quality/Analyzers.cs`, `EditSurvivalAnalyzer`). No field
  anywhere in that path — not the wire format (`OtlpMetricPoint`), not the session model,
  not the persisted record (`PersistedSession`) — carries a file path, a line number, or a
  diff-hunk identity. Every sample's only identity is the containing `session.id`.
- **It does not work for the Claude family at all.** `ClaudeCode.cs`'s
  `TryApplyMetric` switch has no case for any survival signal — GitHub Copilot is the
  only emitter of `copilot_chat.edit.survival.*`. Documented as a real product finding in
  `copilot-scope`'s own `docs/architecture/PRODUCT-REVIEW-2026-08.md` (finding B2): *"an
  80 for a Claude Code session and an 80 for a Copilot session are different numbers"* —
  because the composite score silently renormalizes its weights when the signal is
  absent, rather than flagging the gap.

Every field in the schema above exists specifically to not reproduce that shape: a
`filePath` + `startLine`/`endLine` pair is mandatory on every range (no session-only
identity is representable), and `attribution.tool`/`.method` make the signal's source and
computation method explicit and inspectable rather than an opaque pre-averaged number
from one vendor's editor telemetry.

---

## 5. P1–P15: expressible, deferred, or out of scope

One line each, per this project's requirement to say so explicitly rather
than silently skip a principle Roslyn can't reach. "Expressible" means a first M2A rule
could plausibly implement it now; "Deferred" means partially mechanical but needs either
cross-file/temporal/behavioral judgment beyond a first rule; "Out of scope" means the
artifact type isn't C# at all, so no Roslyn analyzer — however sophisticated — can reach
it.

| ID | Principle | Verdict | Reason |
|---|---|---|---|
| P1 | AppHost is the composition root | **Deferred** | Resource-declaration shape (`WithReference`, `IsProxied` on pinned ports) is checkable; "the AppHost is not the production topology" is a judgment call across the whole repo, not a single-file rule. |
| P2 | Shared kernel, not shared domain | **Expressible** | Explicitly designed to be mechanical by architecture-standards itself: an ~800-line ceiling plus a dependency-direction rule (kernel project must not reference any entity-tagged type). The P2a corollary (`AddServiceDefaults()` called by every service) is a call-presence check, same technique as P15. |
| P3 | Service/database per bounded context | **Deferred** | "No second service opens a connection to it" is a dependency-graph rule; judging *logical* vs. merely *physical* co-location (the documented CopilotScope monolith exception) needs architectural judgment a static pass doesn't have. |
| P4 | Persistence is provider-portable and migrated | **Expressible** | Concrete, greppable patterns: `EnsureCreated()` outside InMemory/test context, presence/absence of `MigrateAsync`, `HasData` inside `OnModelCreating`. |
| P5 | Config via environment, secrets via platform | **Deferred** | Secret-literal scanning is mechanical (architecture-standards' own words: *"enforced by a scanner, not by review"*) and expressible today. But "exactly one service holds the signing key, no shared symmetric secret" is a cross-repo invariant a single-repo analyzer structurally cannot see. |
| P6 | One container per service, multi-stage Dockerfile | **Out of scope** | Dockerfile validation (stage order, base-image version, `EXPOSE`, non-root user) — a different artifact type and tool class entirely, not C#/Roslyn. |
| P7 | Fly.io cost-shaped topology | **Out of scope** | `fly.toml` (TOML) field validation — not C# source. |
| P8 | Optional dependencies degrade | **Deferred** | Conditional DI registration without an else-branch is pattern-detectable; judging whether the fallback actually works (vs. a stub that throws) needs semantic/behavioral understanding a shape-matching rule doesn't have. |
| P9 | `Program.cs` is a manifest; strict layering | **Expressible** | High confidence: a line-count/complexity ceiling on `Program.cs`, plus the canonical dependency-direction rule — controllers/minimal-APIs must not reference `DbContext`/repositories directly, only orchestrators may. This is the textbook Roslyn/ArchUnitNET-style rule. |
| P10 | Extensibility via interface + registration, not inheritance | **Expressible** | Inheritance-depth and base-class-intended-for-derivation detection is a standard Roslyn pattern. |
| P11 | Anti-corruption at the edge | **Expressible** | Forbidden-namespace/forbidden-type-reference rule: vendor SDK types must not appear outside a designated adapter namespace — same dependency-direction technique as P2/P9. |
| P12 | Tag-driven CI/CD with ordered deploy | **Out of scope** | GitHub Actions YAML (job graph, path filters, gating conditions) — not C# source. |
| P13 | Test at the layer that holds the logic | **Deferred** | Test-project existence and basic counts are mechanical. Whether assertions are real rather than placeholders, and whether characterization tests were written *before* a move (a temporal, git-history question), are not detectable from a single-snapshot static pass. |
| P14 | Documentation records reasoning, not just steps | **Out of scope** | Irreducibly prose/process-oriented — whether a doc "records reasoning" or a README is stale relative to the system requires natural-language understanding of content, not code structure. No C# analyzer, however sophisticated, evaluates this. (See `fixtures/consumer-violations.json` entry `CONSUMER-001` for a real, current instance of exactly this failure mode, caught only by direct verification against git history — not by any form of static analysis.) |
| P15 | Observability is a build-time decision | **Expressible** | Call-presence pattern: `AddServiceDefaults()`/OTel package references, health-check trace-filtering code. |

**Tally: 6 Expressible (P2, P4, P9, P10, P11, P15), 5 Deferred (P1, P3, P5, P8, P13),
3 Out of scope as a different artifact type (P6, P7, P12), 1 Out of scope as
irreducibly prose (P14). 15 total.**

Note on the tally: P5 and P1 are each split (partially Expressible, partially not) rather
than cleanly one bucket; the table above states the split rather than forcing a single
verdict where none fits honestly. Track A's first 2–3 rules should come from the
Expressible column — P2's line-ceiling-plus-kernel-dependency rule and P9's
controller-must-not-reference-DbContext rule are the two strongest starting candidates,
being both high-confidence and directly aligned with `fixtures/consumer-violations.json`'s
one Roslyn-checkable category (`CONSUMER-003` through `CONSUMER-011`, the orphan-EF-Core-entity
pattern — closest in spirit to P9's layering discipline, though not a literal P9 citation
in any audit doc; see that fixture's own honesty note on this point).
