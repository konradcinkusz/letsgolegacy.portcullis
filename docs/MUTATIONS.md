# Portcullis — Mutation pass (Track A, `track-a-engine`)

> **Names updated.** The runs recorded here predate the project's current name, and the
> diagnostic ids they printed carried the earlier prefix. This document gives every id
> as today's `PORTCULLIS_*`, because `CONTRIBUTING.md` requires every new rule to be
> documented here — it is a live reference, and a live reference carrying dead ids is
> worse than a lightly edited record. Nothing but the names changed.

Date: 2026-08-17. Written for the engine track's mutation pass, in the same honesty
discipline `docs/BOOTSTRAP.md` already carried into this repository: *"a suite that has
never failed is a suite nobody has tested."* This file also carries two things the
engine track's brief directed here rather than into `docs/SPEC.md` or
`src/Portcullis.Engine/Scanner.cs` directly, since both are frozen: a real problem found
in the frozen spec, and the mutation-test results themselves.

## 1. A real problem found in `docs/SPEC.md`, recorded rather than silently routed around

`docs/SPEC.md` section 1's rule-id convention is `PORTCULLIS-<PrincipleId>-<SLUG>`
(hyphenated), and section 2's worked example literally is
`"ruleId": "PORTCULLIS-P2-KERNEL-LOC-CEILING"`. That convention is **not reportable**
through the exact Roslyn pipeline `src/Portcullis.Engine/Scanner.cs` (frozen) uses.

Verified directly, not assumed — reproduced against `Microsoft.CodeAnalysis.CSharp`
4.11.0 in a throwaway harness before touching any rule code:

```csharp
var analyzer = new ProbeAnalyzer("PORTCULLIS-P2-KERNEL-LOC-CEILING");
var withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
await withAnalyzers.GetAnalyzerDiagnosticsAsync();
// -> AD0001: Analyzer 'ProbeAnalyzer' threw an exception of type 'System.ArgumentException'
//    with message 'Reported diagnostic has an ID 'PORTCULLIS-P2-KERNEL-LOC-CEILING',
//    which is not a valid identifier. (Parameter 'diagnostic')'
```

`Diagnostic.Create(descriptor, location)` itself accepts a hyphenated id without
complaint — the validation lives specifically in
`DiagnosticAnalysisContextHelpers.VerifyArguments`, invoked only inside
`CompilationAnalysisContext.ReportDiagnostic` / `SyntaxNodeAnalysisContext
.ReportDiagnostic`, i.e. exactly the call analyzer authors are required to make from
inside `Initialize`. A quick sweep of candidate ids against that exact call path:

| Id | Result |
|---|---|
| `PORTCULLIS-P2-KERNEL-LOC-CEILING` | `AD0001` (hyphen) |
| `PORTCULLIS.P2.KERNEL` | `AD0001` (dot) |
| `A-B` | `AD0001` (hyphen, minimal repro) |
| `PORTCULLIS_P2_KERNEL_LOC_CEILING` | OK |
| `PORTCULLISP2KERNELLOCCEILING` | OK |
| `AB_1` | OK |

So the hyphenated convention is unreportable by **any** rule built against this
milestone's harness — not something specific to one of my three rules. This was never
exercised before this session: `docs/BOOTSTRAP.md` records `RuleRegistry.All` as
genuinely empty at the end of M2, so no rule had ever actually called
`ReportDiagnostic` with a real `PORTCULLIS-...` id until this session's first `dotnet
test` run, which failed with exactly the `AD0001` above on all three rules
simultaneously.

**Resolution, since `docs/SPEC.md` is frozen and this is exactly the case its own
section 4 anticipates ("if you find a real problem with it, record the problem in
`docs/MUTATIONS.md` rather than editing `docs/SPEC.md` directly"):** all three rules in
this track use underscores instead of hyphens — `PORTCULLIS_P2_KERNEL_LOC_CEILING`,
`PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT`, `PORTCULLIS_P9_ORPHAN_ENTITY`,
`PORTCULLIS_P10_CUSTOM_BASE_CLASS`, `PORTCULLIS_P2_KERNEL_ENTITY_REFERENCE`. The
`principleIds`/`Category` join key (`"P2"`, `"P9"`, `"P10"`) is untouched — only the id
separator changes. `RuleRegistry.cs`'s own doc comment records this deviation and this
file's exact reasoning; `tests/Portcullis.Engine.Tests/Rules/RuleRegistryTests.cs`
asserts every registered rule id is actually a valid Roslyn identifier, so a future rule
that reintroduces a hyphen fails fast in CI rather than crashing every scan with
`AD0001`. **This needs a decision at M4 integration** (Tracks B/C's mocked/real
consumers of the `ruleId` field should not assume hyphens), which is exactly the kind
of cross-track call that belongs to the integration session, not this one.

## 2. Mutation-pass methodology

The method, as the engine track's brief put it: each variant "disables or weakens
exactly one rule, swapped in place at the same registration point... do not add any
flag, comment, or naming convention that self-announces the variant is broken." One deliberate implementation choice, made
explicit here rather than left implicit:

**Mutants live in test-only code** (`tests/Portcullis.Engine.Tests/Rules/Mutants/`), not
by literally editing `RuleRegistry.All` to a broken analyzer and reverting it
afterward. `RuleRegistry.All` (production) is never made to hold broken code at any
point in this session's history — each mutant is instead swapped in at the equivalent
position in a `Compilation.WithAnalyzers(ImmutableArray.Create(analyzer))` call inside
its own test, the same "registration point" role `RuleRegistry.All` plays for
`Scanner.ScanAsync`, just constructed directly rather than by reading the shared
registry. This is a safety trade a real CI/production codebase should make (a
mistakenly-committed broken analyzer landing on `main`, even briefly, is a real risk
this repo's own house style — `docs/BOOTSTRAP.md`'s honesty about what's unbuilt —
argues against taking casually); it does not weaken the
test itself, since the mutant still runs through the identical
`compilation.WithAnalyzers().GetAnalyzerDiagnosticsAsync()` path `Scanner.cs` uses, on
the identical source fixture, differing from the real analyzer by exactly the one
change each row below names. Each mutant reuses the real rule's own
`DiagnosticDescriptor` (same id, same category), so a mutant's failure to fire is a
pure detection gap, not a differently-shaped diagnostic slipping past a naive id check.

For each variant, in order: first assert the **real** analyzer
catches the target violation against the fixture (the sanity check — without it, "the
mutant missed it" would be meaningless, since the fixture itself might not actually
trigger the real rule either), then assert the **mutant** does not.

## 3. Results

All 5 commands below are `dotnet test -p:UseAppHost=false` runs against this branch,
run for real in this session (`tests/Portcullis.Engine.Tests/Rules/MutationTests.cs`),
not hand-typed as an assumed outcome. `-p:UseAppHost=false` is a local build workaround
for this sandbox only (no RID-specific `Microsoft.NETCore.App.Host` package available
offline) — it does not change what code executes.

| Variant | File | Breaks | Test | Result |
|---|---|---|---|---|
| `KernelLocCeilingMutant` | `Rules/Mutants/KernelLocCeilingMutant.cs` | P2 LOC-ceiling folder detection: kernel-folder name list uses singular `"ServiceDefault"` instead of `"ServiceDefaults"`, so no real `*.ServiceDefaults` project is ever classified as kernel | `MutationTests.KernelLocCeiling_RealCatchesButMutantMisses` | Real catches; mutant misses. **Caught.** |
| `KernelEntityReferenceMutant` | `Rules/Mutants/KernelEntityReferenceMutant.cs` | P2 entity-folder detection: entity-folder name list uses plural `"Domains"` instead of `"Domain"`, so no real `Domain/` folder is ever recognized as holding entity types | `MutationTests.KernelEntityReference_RealCatchesButMutantMisses` | Real catches; mutant misses. **Caught.** |
| `ControllerNoDbContextMutant` | `Rules/Mutants/ControllerNoDbContextMutant.cs` | P9 controller check: requires the referenced type name to equal `"DbContext"` exactly instead of merely ending with it, so any concrete derived context (`AdvertsDbContext`, `FooDbContext`, ...) — which is what every real controller actually references — is missed | `MutationTests.ControllerNoDbContext_RealCatchesButMutantMisses` | Real catches; mutant misses. **Caught.** |
| `OrphanEntityMutant` | `Rules/Mutants/OrphanEntityMutant.cs` | P9 orphan-entity check: forgets to exclude the `DbSet<T>` declaration's own span when counting references to `T`, so every entity's own registration line counts as a reference to itself and no entity can ever be flagged as orphan | `MutationTests.OrphanEntity_RealCatchesButMutantMisses` | Real catches; mutant misses. **Caught.** |
| `CustomBaseClassMutant` | `Rules/Mutants/CustomBaseClassMutant.cs` | P10 check: tests `TypeKind.Struct` instead of `TypeKind.Class` on the resolved base-type symbol — a base-list entry is never a struct in valid C#, so the condition can never be true | `MutationTests.CustomBaseClass_RealCatchesButMutantMisses` | Real catches; mutant misses. **Caught.** |

```
$ dotnet test -p:UseAppHost=false
...
Passed Portcullis.Engine.Tests.Rules.MutationTests.CustomBaseClass_RealCatchesButMutantMisses [782 ms]
Passed Portcullis.Engine.Tests.Rules.MutationTests.KernelEntityReference_RealCatchesButMutantMisses [541 ms]
Passed Portcullis.Engine.Tests.Rules.MutationTests.OrphanEntity_RealCatchesButMutantMisses [289 ms]
Passed Portcullis.Engine.Tests.Rules.MutationTests.KernelLocCeiling_RealCatchesButMutantMisses [292 ms]
Passed Portcullis.Engine.Tests.Rules.MutationTests.ControllerNoDbContext_RealCatchesButMutantMisses [258 ms]

Test Run Successful.
Total tests: 22
     Passed: 22
```

## 4. Survivors

**None, in this pass.** Every mutant above was caught by its corresponding test on the
first run — no test needed a second, weaker assertion added after the fact to make it
pass, and no mutant accidentally reproduced the real rule's behavior. This is stated
plainly rather than treated as an assumed/default outcome, per this repository's own
house style, which is specifically about not hiding a survivor when one occurs, not
about survivors being expected on every run. Section 1 above is this session's actual example of the same
non-defensive-writing discipline applied one level up — a real gap was found (the
frozen spec's rule-id convention is unreportable through the real harness), it
surfaced by actually running the code rather than by inspection, and it is recorded
here rather than quietly worked around with no trace.

## 5. What this does and does not prove

This proves: the 5 diagnostics registered in `RuleRegistry.All` each depend on the
specific logic the mutation was designed to remove — none of them would still fire, by
accident, on a codebase where that specific mechanism was broken. It does not prove the
rules have no false positives, that every one of `fixtures/consumer-violations.json`'s
`roslynCheckable: true` entries is caught (only `CONSUMER-003`, `CONSUMER-005`, and
`CONSUMER-006` are, out of 9 — see section 6), or that the rules generalize to
codebases shaped differently from `<consumer>` and this session's own fixtures.

## 6. Verification against `fixtures/consumer-violations.json`

Run for real against a local `<consumer>` checkout, at the same commit the fixture itself
was verified against (its `verifiedAgainstCommit`):

```
$ dotnet exec src/Portcullis.Cli/bin/Debug/net8.0/Portcullis.Cli.dll scan /path/to/<consumer>/src
```

`filesScanned: 156` (matches `docs/SPEC.md` section 3's own baseline exactly),
`rulesEvaluated: 3`, 15 violations: 4 `PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT`, 6
`PORTCULLIS_P9_ORPHAN_ENTITY`, 5 `PORTCULLIS_P10_CUSTOM_BASE_CLASS`. Zero
`PORTCULLIS_P2_KERNEL_LOC_CEILING` or `PORTCULLIS_P2_KERNEL_ENTITY_REFERENCE` — correctly:
`<consumer>.ServiceDefaults` totals 542 lines, under the 800-line ceiling, and it does
not reference any `Domain`-folder type; `<consumer>` is P2-compliant here, and the rule
says nothing when there is nothing to say.

Three of the six `PORTCULLIS_P9_ORPHAN_ENTITY` hits are exact matches against the
fixture's `roslynCheckable: true` entries:

| Fixture id | Entity | `filePath:line` | Matches this scan |
|---|---|---|---|
| `CONSUMER-003` | `QrCode` | `<consumer>.AdvertsService/Domain/QrCode.cs:11` | Yes — the fixture's own "strongest candidate" for a first rule |
| `CONSUMER-005` | `SystemMessage` | `<consumer>.AdvertsService/Domain/SystemMessage.cs:11` | Yes |
| `CONSUMER-006` | `AdvertMessage` | `<consumer>.AdvertsService/Domain/AdvertMessage.cs:11` | Yes |

This satisfies the milestone's bar (at least one `roslynCheckable: true` fixture entry
caught) three times over, honestly. The other three `PORTCULLIS_P9_ORPHAN_ENTITY` hits
(`AddressItem`, `Consent`, `AdvertBalance`) are real findings the fixture never
enumerated — plausible new orphan entities, not verified against `<consumer>` with the
same rigor `docs/BOOTSTRAP.md` applied to the fixture's own entries, so not claimed as
confirmed fixture matches here.

The rule does **not** catch `CONSUMER-004` (`StaticMap`), `CONSUMER-008` through
`CONSUMER-011` (the `IdentityService` entities), or `CONSUMER-007` (`AdvertTraffic`),
and this is a real scope limit, not a bug:

- `StaticMap` is referenced as `Address.MiniMap`, a real C# symbol reference living
  outside its own declaring file — `PORTCULLIS_P9_ORPHAN_ENTITY`'s check is "zero
  references anywhere outside the entity's declaring file", which is coarser than the
  fixture's own "zero references **from a controller specifically**". A navigation
  property reference from a sibling domain class correctly reads as "used" under this
  rule's definition, even though it is still unreachable from any controller under the
  fixture's finer-grained one.
- `CompanyAddress` (`CONSUMER-009`) is referenced the same way, from
  `CompanyAddressItem.CompanyAddress` — same reason, confirmed directly, not assumed.
- `AdvertTraffic` (`CONSUMER-007`) is write-only (`AdvertInteractions.RecordTrafficAsync`
  constructs one), which is itself a real C# reference to the type — this rule detects
  zero-reference orphans, not "written but never read back", which the fixture itself
  already documents as needing a different check (`roslynCheckableReason`: "a variant of
  the same pattern... detectable by checking whether a `DbSet<T>` has any LINQ read...
  not just an Add/insert").
- `UserAddressItem` and `UserMessage` (`CONSUMER-008`, `CONSUMER-011`) were not
  independently re-verified this session as to why this rule misses them; recorded here
  as an honest gap rather than a claimed explanation, in the same spirit as this
  document's other sections — a real difference between "not checked" and "checked and
  found compliant."

Catching one fixture entry was the milestone's bar; catching three, plus
finding entities beyond the fixture, is this session's actual result — stated as what it
is, not inflated into "catches the orphan-entity pattern" as a blanket claim the scan
above does not support.

## 7. Follow-up pass — P4/P11/P15 rules

Per `docs/SPEC.md` section 5's own tally, 6 of P1–P15 are rated "Expressible": P2, P4, P9,
P10, P11, P15. Track A shipped 3 of the 6 (P2, P9, P10) — deliberately, per that
section's own closing note ("Track A's first 2–3 rules should come from the Expressible
column"), not because the other 3 were harder. This pass adds the remaining 3, bringing
the engine to parity with everything `docs/SPEC.md` rated reachable, using the same
mutation-pass discipline section 2 establishes.

### 7.1 Rules added

| Rule | File | Diagnostics |
|---|---|---|
| P4 — persistence portability | `src/Portcullis.Rules/PersistencePortabilityAnalyzer.cs` | `PORTCULLIS_P4_ENSURE_CREATED_OUTSIDE_TEST` (error), `PORTCULLIS_P4_SEED_DATA_IN_MODEL` (warning) |
| P11 — anti-corruption at the edge | `src/Portcullis.Rules/AntiCorruptionEdgeAnalyzer.cs` | `PORTCULLIS_P11_VENDOR_SDK_OUTSIDE_ADAPTER` (warning) |
| P15 — observability is build-time | `src/Portcullis.Rules/ObservabilityBuildTimeAnalyzer.cs` | `PORTCULLIS_P15_MISSING_SERVICE_DEFAULTS` (warning) |

All three are purely syntactic, for the same reason every existing rule is (section 5's
own scope note, `docs/BOOTSTRAP.md`): the scanner's single-compilation, corelib-only
harness never resolves EF Core, ASP.NET Core, or any third-party SDK's real types, so
there is nothing to bind a semantic check against — only invocation/using-directive
shape.

### 7.2 Grounded in `<consumer>`, not synthetic guesses

Before writing any rule, this session read real `<consumer>` source rather than assuming
what a plausible violation would look like:

- **P4**: `<consumer>.ServiceDefaults/DatabaseExtensions.cs` already implements the
  corrected pattern this rule checks for, with its own doc comment citing P4 by name:
  *"Always MigrateAsync for a real provider — never EnsureCreated."* This became the
  rule's true-negative case (a guarded `if (context.Database.IsInMemory()) {
  EnsureCreatedAsync }` is not flagged), not a synthetic fixture.
- **P11**: `<consumer>.AgentService.Application` (`AgentTools.cs`, `AgentOrchestrator.cs`)
  imports `Anthropic.Models.Messages` directly, despite the project already having a
  dedicated `Infrastructure/Claude/` folder — a real, current instance of exactly the
  pattern P11 exists to catch, not a hypothetical vendor SDK.
- **P15**: every one of `<consumer>`' 7 services (`AdvertsService`, `AgentService`,
  `BillingService`, `BlazorWeb`, `IdentityService`, `Jobs.PromotionExpiry`,
  `MediaService`) already calls `AddServiceDefaults()` from `Program.cs` — confirmed by
  grep before writing the rule, not assumed.

### 7.3 A real design gap this session's own scan surfaced, not fixed silently

The first live `portcullis scan` run against `<consumer>/src` with the new P11 rule flagged
four locations, not the three predicted from source reading — the fourth being
`<consumer>.AgentService/Program.cs` itself, for its own `using Anthropic;`. Reading the
file showed a completely idiomatic composition-root pattern:

```csharp
builder.Services.AddSingleton(sp =>
{
    var claudeOptions = sp.GetRequiredService<IOptions<ClaudeOptions>>().Value;
    return string.IsNullOrWhiteSpace(claudeOptions.ApiKey)
        ? new AnthropicClient()
        : new AnthropicClient { ApiKey = claudeOptions.ApiKey };
});
```

This is the textbook composition-root role — the one place allowed to construct a
concrete vendor type before handing it out through DI — not the violation P11 targets.
Rather than leave this as an undocumented rule limitation, `AntiCorruptionEdgeAnalyzer`
was changed to also recognize `Program.cs` (matched by filename, the same convention
`ObservabilityBuildTimeAnalyzer` uses) as a second, legitimate adapter location alongside
the `Infrastructure`/`Adapters`/`Integrations` folder convention. Re-running the scan
after the fix produced exactly the three predicted violations — see 7.5. Recorded here
per this document's own section 1 precedent (the frozen SPEC's hyphen convention): a real
gap found by actually running the tool, not routed around silently.

### 7.4 Mutation pass — 4 new variants, no survivors

Same methodology as sections 2–3: for each new diagnostic, assert the real rule catches
a target violation, then assert a deliberately broken variant
(`tests/Portcullis.Engine.Tests/Rules/Mutants/`) does not, reusing the real
`DiagnosticDescriptor` so a mutant's miss is a pure detection gap.

| Variant | File | Breaks | Result |
|---|---|---|---|
| `EnsureCreatedAsyncVariantDroppedMutant` | `EnsureCreatedAsyncVariantDroppedMutant.cs` | Matches only "EnsureCreated", not "EnsureCreatedAsync" — the variant real code (`<consumer>`' own helper) actually calls | Real catches; mutant misses. **Caught.** |
| `OnModelCreatingTypoMutant` | `OnModelCreatingTypoMutant.cs` | Checks the containing method name against "OnModelCreated" (missing "ing") instead of "OnModelCreating" | Real catches; mutant misses. **Caught.** |
| `AdapterSegmentOverbroadMutant` | `AdapterSegmentOverbroadMutant.cs` | Adapter allowlist includes "Application" instead of "Integrations" — exempts the exact layer the real `<consumer>` violation lives in | Real catches; mutant misses. **Caught.** |
| `HostBuilderFactoryNarrowedMutant` | `HostBuilderFactoryNarrowedMutant.cs` | Recognizes only "CreateBuilder", not "CreateApplicationBuilder" — a Host-based worker/job Program.cs is never recognized as a service entry point at all | Real catches; mutant misses. **Caught.** |

```
$ dotnet test portcullis.sln
...
Passed!  - Failed: 0, Passed: 23, Skipped: 0, Total: 23 - Portcullis.CiComment.Tests.dll (net8.0)
Passed!  - Failed: 0, Passed: 63, Skipped: 0, Total: 63 - Portcullis.Engine.Tests.dll (net8.0)
```

No survivors on this pass either, for the same reason section 4 states about the
original five: every mutant was caught on first run, no assertion needed weakening after
the fact.

### 7.5 Live verification against `<consumer>`, after the Program.cs fix

`portcullis scan <consumer-checkout>/src` (156 files, matching every prior baseline in this
document exactly):

```
rulesEvaluated: 6
summary: { errorCount: 4, warningCount: 14, infoCount: 0 }
```

`errorCount` is unchanged from section 6's baseline (still the same 4 pre-existing
`PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT` errors — P4/P11/P15 added nothing to the error
class). `warningCount` rose from 11 to 14 — exactly the 3 real
`PORTCULLIS_P11_VENDOR_SDK_OUTSIDE_ADAPTER` hits predicted in 7.2 and confirmed live:

| File | Line | Vendor namespace |
|---|---|---|
| `<consumer>.AgentService/Application/AgentOrchestrator.cs` | 3 | `Anthropic` |
| `<consumer>.AgentService/Application/AgentOrchestrator.cs` | 4 | `Anthropic.Models.Messages` |
| `<consumer>.AgentService/Application/AgentTools.cs` | 2 | `Anthropic.Models.Messages` |

Zero `PORTCULLIS_P4_ENSURE_CREATED_OUTSIDE_TEST`, `PORTCULLIS_P4_SEED_DATA_IN_MODEL`, or
`PORTCULLIS_P15_MISSING_SERVICE_DEFAULTS` hits — correctly: `<consumer>` is compliant with all
three of these patterns (section 7.2), and, per this document's own section 6 precedent,
the rule says nothing when there is nothing to say. This is a compliant result, not a
coverage gap — distinguishing the two is the entire point of grounding these rules in
real source before writing them, rather than trusting a rule's own unit tests to be
evidence of what it catches in practice.

### 7.6 What this does and does not prove

This proves the same thing section 5 states about the original five diagnostics, now
extended to all 6 `RuleRegistry.All` entries: each of the 4 new diagnostics depends on
the specific logic its mutation removed, and each was verified against real, current
`<consumer>` source — not just its own synthetic unit-test fixtures. It does not prove
these rules have no false positives on codebases shaped differently than `<consumer>` (the
`Program.cs` composition-root case in 7.3 is direct evidence that a rule *can* still have
a real gap even when grounded in a real example — it took an actual scan, not just
reasoning about the design, to surface it), and it does not extend any claim to the 9
principles this pass leaves untouched (5 Deferred, 4 Out of scope per `docs/SPEC.md`
section 5's own tally).
