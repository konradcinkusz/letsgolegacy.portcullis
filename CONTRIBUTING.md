# Contributing to Portcullis

Thanks for looking. This project has one unusual house rule, and it is worth reading
before anything else. By taking part you agree to the
[Code of Conduct](CODE_OF_CONDUCT.md).

## The house rule: claims are things that were run

Every documented behaviour in this repository is supposed to be traceable to a command
somebody actually executed and the output it actually produced. Not "the code does X" —
"here is X being done, here is the output." Where something is unbuilt, unverified, or
only partly working, the docs say so plainly rather than rounding up
([`docs/FINDINGS.md`](docs/FINDINGS.md) and [`docs/MUTATIONS.md`](docs/MUTATIONS.md) are
the reference examples, including the parts where a rule is recorded as *not* catching
something).

A tool whose job is to tell teams the truth about their code has no business overstating
what it does. If you find a claim here that is not true, that is a bug — please file it.

## Getting set up

Requires the .NET 10 SDK and nothing else.

```sh
git clone https://github.com/konradcinkusz/letsgolegacy.portcullis
cd letsgolegacy.portcullis
dotnet build portcullis.sln
dotnet test portcullis.sln
dotnet run --project src/Portcullis.Cli -- scan ./src
```

## Project layout

| Project | Target | Why |
|---|---|---|
| `src/Portcullis.Rules` | **netstandard2.0** | The analyzers. Packaged as `Portcullis.Analyzers`. |
| `src/Portcullis.Engine` | net10.0 | Scan harness, output model, git provenance. |
| `src/Portcullis.Cli` | net10.0 | The `portcullis` tool. |
| `src/Portcullis.CiComment` | net10.0 | The `portcullis-ci-comment` tool. |

**`Portcullis.Rules` must stay on `netstandard2.0`.** That is not a style preference: a
Roslyn analyzer is loaded by the compiler, which may be running on .NET Framework MSBuild
inside Visual Studio, and `netstandard2.0` is the only target that loads everywhere. An
analyzer retargeted to a .NET target such as `net10.0` does not error — it silently never runs in VS. Two
consequences you will hit:

- No `Split(params char[], StringSplitOptions)` overload; use the `PathSeparators` field
  pattern already in the codebase.
- No `[NotNullWhen]` on `string.IsNullOrEmpty`; write `x is null || x.Length == 0` so
  nullable flow analysis works.

Its `Microsoft.CodeAnalysis.CSharp` reference is also pinned deliberately **low** (4.8.0,
what the .NET 8 SDK ships). Building against a newer Roslyn makes every consumer's build
emit `CS9057`. Do not bump it to match the newest available.

## Adding a rule

A new rule is not done when it fires. It is done when there is evidence it fires *for the
reason you think*:

1. **Implement it** in `src/Portcullis.Rules/`, following an existing analyzer's shape.
   Rule ids use underscores (`PORTCULLIS_P9_ORPHAN_ENTITY`) — Roslyn rejects hyphenated
   diagnostic ids at `ReportDiagnostic`, which is recorded in
   [`docs/MUTATIONS.md` §1](docs/MUTATIONS.md).
2. **Register it** in `RuleRegistry.All` and update `RuleRegistryTests`.
3. **Unit-test both directions** — the case that fires and the case that must not. False
   positives are the failure mode that gets a gate switched off; treat a
   does-not-fire test as the more important one.
4. **Write a mutant.** Add a deliberately broken variant under
   `tests/.../Rules/Mutants/` and a `MutationTests` case asserting the real rule catches
   the violation *and* the mutant misses it. A rule with no mutant is a rule nobody has
   tested. Break the specific mechanism the rule depends on, not something incidental.
   For the migration rules, also run Stryker.NET (`dotnet tool restore`, then
   `dotnet stryker` in `tests/Portcullis.Engine.Tests`) and add a new rule's file to the
   `mutate` list in `stryker-config.json`; CI fails below the configured threshold.
   [`docs/MUTATIONS.md` §8](docs/MUTATIONS.md) explains why the rules avoid pattern
   variables in conditions.
5. **Run it against real code**, not only fixtures, and record what happened —
   including anything it flagged that it should not have.
6. **Document it** in `docs/MUTATIONS.md` and the README's rule table (and, for a
   migration rule, in `docs/rules/MIGRATION.md`).

Prefer a small rule with high confidence over a broad one that needs tuning. A noisy
gate loses the team's trust faster than a narrow one earns it.

## Pull requests

- `dotnet build` clean (zero warnings — the bar is zero, not "few") and `dotnet test`
  green before pushing.
- Say what you actually verified and how. If you could not run something, say that
  instead of implying you did.
- Flag limitations you know about rather than leaving them to be discovered. Several
  documents here exist purely to record a gap that was found and deliberately not fixed;
  that is a normal and welcome outcome.
- CI (`.github/workflows/ci.yml`) builds, runs the full suite, and fails if the
  project's former name reappears anywhere in the tree (job *Former name absent*). The
  name is Portcullis everywhere — code, docs, and notes about the past alike.
- Portcullis gates its own pull requests (`portcullis-pr-check.yml`) and publishes their
  findings as SARIF (`portcullis-sarif.yml`), both against the committed
  `portcullis-baseline.json`. A new error on a line you changed blocks. Fix it rather than
  growing the baseline; if accepting it really is the right call, regenerate the file with
  `dotnet run --project src/Portcullis.Cli -- scan src --baseline portcullis-baseline.json
  --write-baseline` and say why in the pull request, where the reviewer sees the entry.

## Releasing

Releases are tag-driven: pushing a `v*` tag builds, tests, packs, publishes all three
NuGet packages, pushes the container to GHCR, creates the GitHub release, and force-moves
the major tag (`v0`) to that commit.

**The version lives in two files and they must agree before you tag**, because the moving
`v0` tag points at the tagged commit — so `uses: konradcinkusz/letsgolegacy.portcullis@v0` runs the
`action.yml` from that commit, and if its default tools version does not exist on NuGet,
every consumer's workflow breaks. `release.yml` fails the build rather than let that
happen, but the fix is to get it right first:

1. Bump `<Version>` in `Directory.Build.props`.
2. Bump the `version` input default in `action.yml` to the same value.
3. Update any pinned versions in `docs/TUTORIAL.md`, `docs/index.html` and the
   `PACKAGE-README.md` files.
4. Commit, then tag: `git tag v0.2.0 && git push origin v0.2.0`.

First release only, one-time setup: a `NUGET_API_KEY` repository secret (an API key from
nuget.org, scoped with the glob pattern `Portcullis.*` so it works for packages that do not
exist yet), and — after the first container push — flipping the GHCR package from private
to public in the package's own settings, which repository visibility does not do for you.

## Reporting a bug

The most useful report contains the code that triggered it. A minimal C# snippet, the
rule id, what you expected, and what happened. False positives are high-priority — a rule
that cries wolf is worse than a missing rule.

There are issue templates for the three common shapes — a false positive, any other
bug, and a rule proposal — and each asks for the things a fix actually needs. A
**vulnerability** goes through [`SECURITY.md`](SECURITY.md) instead, privately, not into a
public issue.

## License

MIT. Contributions are accepted under the same license.
