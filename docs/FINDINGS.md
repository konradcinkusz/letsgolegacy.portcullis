# Portcullis — FINDINGS (Milestone M5)

> **Names updated.** This record predates the project's current name, Portcullis,
> settled on 2026-09-13 before anything was published. Identifiers, commands and paths
> below are given with today's names — `PORTCULLIS_*` rule ids, the `portcullis`
> command, the `Portcullis.*` projects — so they match the code in this repository; the
> findings themselves are as recorded.

Written for milestone M5, whose brief was one line: the whole system deliberately
attacked end to end — what the tool actually caught, not what it should. Same
discipline `docs/MUTATIONS.md`/`docs/M4-INTEGRATION.md` already carry: every number below
is from a command that was actually run in this session, not inferred from reading the
code.

M3A (`docs/MUTATIONS.md`) already proved each rule's own diagnostic logic depends on the
mechanism it claims to check, in isolation, against synthetic and real `<consumer>`
fixtures. M5's job is different and narrower: prove the *whole pipeline* — rules,
provenance escalation, and the CI gate, wired together — produces the correct merge
decision for realistic PR scenarios. A rule that fires correctly in a unit test proves
nothing about whether a real PR carrying that violation actually gets blocked; that is
what this document checks.

## 1. Method

Two complementary sources of evidence, both real:

1. **A read-only snapshot of real `<consumer>`** (`<consumer-checkout>`, the same fixture
   repository `fixtures/consumer-violations.json` is built from), scanned with all 6
   rules now in `RuleRegistry.All` — grounding numbers, not a controlled experiment.
2. **A purpose-built scratch git repository** (not `<consumer>`, not committed or pushed
   anywhere — five commits, deliberately shaped to isolate one pipeline behavior each),
   scanned and piped through `Portcullis.CiComment` at each step. This is the actual
   "attack": each commit is a deliberate, realistic PR shape — a human-authored warning,
   an AI-authored warning, an unconditional error, and a completely clean, unrelated
   commit — and this document reports the literal process exit code and rendered
   comment the pipeline produced for each, not the expected one.

## 2. Real `<consumer>`, whole-system snapshot

`portcullis scan <consumer-checkout>/src`, 2026-08-17, all 6 rules registered:

```
filesScanned: 156, rulesEvaluated: 6
summary: { errorCount: 4, warningCount: 14, infoCount: 0 }
```

| Rule | Count | Severity |
|---|---|---|
| `PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT` | 4 | error |
| `PORTCULLIS_P9_ORPHAN_ENTITY` | 6 | warning |
| `PORTCULLIS_P10_CUSTOM_BASE_CLASS` | 5 | warning |
| `PORTCULLIS_P11_VENDOR_SDK_OUTSIDE_ADAPTER` | 3 | warning |
| `PORTCULLIS_P4_*` | 0 | — |
| `PORTCULLIS_P15_MISSING_SERVICE_DEFAULTS` | 0 | — |

The plain fact this table states, stated plainly: **if `portcullis-pr-check.yml` were
dropped onto `<consumer>` today, every single PR would fail the gate immediately** — not
because of anything the hypothetical PR touches, but because `<consumer>/src` already
carries 4 `error`-severity violations. This was already flagged as a risk in
`docs/M4-INTEGRATION.md` section 6; section 4 below reproduces it as a controlled,
minimal, deterministic repro rather than an inference from `<consumer>`' current state.

## 3. Scratch-repo attack scenarios

Five commits, one repo, each isolating one pipeline behavior. Full detail (file
contents, exact commands) is reproducible from this document's commit messages and
`--author`/trailer values; nothing here is hand-simplified from what was actually run.

```
A  Baseline: compliant Widget domain, DbContext, service, controller     (human)
B  Add Gadget entity — new orphan-entity WARNING                         (human)
C  Add Sprocket entity — new orphan-entity WARNING                       (AI: "Co-Authored-By: Claude <noreply@anthropic.com>")
D  Add BadController — new controller/DbContext ERROR                    (human)
E  Add HealthCheck helper — unrelated to D, zero new violations          (human)
```

| # | State scanned | Command | errorCount | warningCount | Exit code |
|---|---|---|---|---|---|
| 1 | Commit B | `scan` (no provenance) | 0 | 1 (Gadget) | **0** |
| 2a | Commit C | `scan` (no provenance) | 0 | 2 (Gadget, Sprocket) | **0** |
| 2b | Commit C | `scan --provenance-range B..C` | 1 (Sprocket) | 1 (Gadget) | **1** |
| 3 | Commit D | `scan` (no provenance) | 1 (BadController) | 2 | **1** |
| 4 | Commit E | `scan` (no provenance) | 1 (BadController — unchanged) | 2 | **1** |

Every exit code above is the literal process exit code the CLI returned, confirmed with
`echo $?` immediately after each run — not inferred from the JSON's `errorCount` field.

### 3.1 The provenance mechanism, reconfirmed with a fresh, controlled example

`docs/M4-INTEGRATION.md` proved provenance escalation works against one real,
already-existing pair of `<consumer>` commits (a web client file, spot-checked by
hand). Scenario 2 here is the same mechanism exercised against a commit built for
exactly this purpose: **two structurally identical violations — same rule, same default
severity — one authored by a human, one by "Claude", diverge in outcome.** Gadget
(commit B, outside the B..C provenance range) stays a warning and does not block.
Sprocket (commit C, inside the range, `Co-Authored-By: Claude`) escalates to an error
and blocks the merge — piped through the full `Portcullis.CiComment` chain, not just the
scanner:

```
❌ 1 error, ⚠️ 1 warning — 6 rules evaluated across 6 files in 768ms.
...
### Sprocket.cs
- ❌ line 3 — PORTCULLIS_P9_ORPHAN_ENTITY: ...
```

exiting `1`, confirmed literally. The `--previous` diff also correctly labels Sprocket
`🆕 new` at its *escalated* severity (❌, not ⚠️) — the diff keys on rule+location, not
severity, exactly as `docs/M4-INTEGRATION.md` section 5 already established, now
reconfirmed on a controlled fixture instead of `<consumer>`' own history.

### 3.2 The absolute-vs-diff gate gap, reconfirmed with a fresh, controlled example

Scenario 4 is the actual "attack" on the gate design itself. Commit E's own diff is one
new file, six lines, touching nothing BadController or `WidgetDbContext` related:

```
$ git show --stat <commit E>
 src/HealthCheck.cs | 6 ++++++
 1 file changed, 6 insertions(+)
```

Scanning the resulting tree still exits `1`, with the identical `errorCount: 1` as
commit D — because the gate is `errorCount > 0` over the *entire scanned tree*, not over
commit E's own diff. `docs/M4-INTEGRATION.md` section 6 already stated this in prose,
citing `<consumer>`' own pre-existing errors as evidence; this scenario is the same claim
demonstrated as a minimal, deterministic, two-commit repro that isolates the exact
mechanism (an unrelated prior error, a genuinely clean follow-up commit) rather than
relying on `<consumer>`' more tangled real history to make the point.

## 4. What this proves

- The provenance-escalation mechanism (`Scanner.ApplyProvenanceEscalation`) generalizes
  beyond the one historical `<consumer>` example M4 checked by hand — confirmed on a
  fresh, purpose-built fixture where every variable (author, file, range) was
  controlled, not just observed.
- The full chain — scan → provenance escalation → new/resolved diff → Markdown
  rendering → process exit code — produces a *consistent* answer across all five
  scenarios: the rendered comment, the summary counts, and the literal exit code always
  agree with each other. No scenario found a case where the comment said one thing and
  the exit code did another.
- The absolute-vs-diff gate gap is not a theoretical reading of `docs/M4-INTEGRATION.md`
  section 6 — it reproduces on demand, in two commits, every time.
- The escalation and gate mechanisms are rule-agnostic by construction
  (`Scanner.ApplyProvenanceEscalation` operates purely on `filePath`/`line`, never on
  `ruleId`), so exercising them through `PORTCULLIS_P9_ORPHAN_ENTITY`/
  `PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT` is evidence about the mechanism itself, not
  something that needs separately repeating for each of the other 4 rules.

## 5. What this does not prove

- **The gate design question remains exactly as open as `docs/M4-INTEGRATION.md`
  section 6 left it.** This document adds a sharper repro, not a fix or a
  recommendation. Whether the gate should become diff-based is still a real product
  decision for whoever picks up M6, not something this pass resolves.
- **The GitHub-integration surface itself (sticky comment create/update against a real
  PR, `GITHUB_TOKEN`/Checks API) was not re-exercised here.** That was already confirmed
  against real GitHub Actions infrastructure in the M4 session (a pull request's own
  workflow run in the predecessor repository, `conclusion: success`) and is Track B's
  scope, unchanged by this pass. Every run
  in this document is a local CLI invocation, correctly degrading to "skipping comment
  publish" with no `GITHUB_TOKEN`/`--pr` — the same designed local-run behavior M4
  already established, not a new gap.
- **This is not a claim that all 6 rules are individually false-positive-free on
  arbitrary codebases.** `docs/MUTATIONS.md` section 7.3 already records one real false
  positive this same development pass found and fixed (`Program.cs`'s composition-root
  role, for P11) before it ever reached this document — evidence that grounding a rule
  in one real example, as every rule in this engine is, does not by itself guarantee no
  further gaps exist elsewhere.
- **Nothing here touches M6** (dogfooding — fixing `<consumer>`'s own open violations
  under Portcullis's live gate). That remains sequenced after this document, per the
  original sequencing caveat: fixing `<consumer>` now would destroy the fixture this and
  every prior milestone actively consume.

## 6. Summary

M0–M4 built and integrated the pipeline. M5's own bar — attack the whole thing, not each
piece's own tests — is met: five deliberate scenarios, real commands, real exit codes,
two mechanisms (provenance escalation, the merge gate) each shown doing exactly what
`docs/SPEC.md`/`docs/M4-INTEGRATION.md` said they would, plus one honest limitation
(section 5) reproduced on demand rather than left as a prose claim. The single largest
open question — absolute vs. diff-based gating — is unchanged by this pass; it is
sharper evidence for a decision this document does not make.
