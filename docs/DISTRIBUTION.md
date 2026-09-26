# How Portcullis ships, and why

Date: 2026-08-18. Written when portcullis was prepared for public release, replacing
earlier recommendation-only research with what was actually built and verified. Same
discipline as every other dated note here: the numbers and behaviours below are from
commands that were run, and the two problems found by running them are recorded rather
than smoothed over.

## The four channels

| Channel | Artifact | For |
|---|---|---|
| **NuGet (analyzer)** | `Portcullis.Analyzers` | The default. Rules as build diagnostics in any IDE and any CI. |
| **NuGet (tools)** | `Portcullis.Cli` → `portcullis`, `Portcullis.CiComment` → `portcullis-ci-comment` | Machine-readable scans, the diff-scoped gate, PR comments. |
| **GitHub Action** | `konradcinkusz/letsgolegacy.portcullis@v0` (composite) | The PR gate, wired end to end. |
| **GHCR container** | `ghcr.io/konradcinkusz/letsgolegacy.portcullis` | CI that is not GitHub Actions and would rather not install a .NET SDK. |

## Why the analyzer package is the primary channel

It is the only one that requires no CI plumbing at all. A consumer adds one
`PackageReference` and architecture violations become ordinary compiler warnings and
errors — in Visual Studio, in Rider, in `dotnet build`, in GitHub Actions, in Azure
DevOps, in Jenkins, on a developer's laptop before the code is ever pushed. Every other
channel needs something installed, configured, or run.

This mirrors what `Roslynator.Analyzers` actually does, and it is the reason the rules
were split into their own `netstandard2.0` project: an analyzer is loaded by the compiler
process, so it must load in .NET Framework MSBuild (Visual Studio) as well as in `dotnet
build`. `netstandard2.0` is the only target that satisfies both. An analyzer built for
a .NET target such as `net8.0` or `net10.0` does not error — it silently never runs in
Visual Studio, which is worse.

## Why the GitHub Action is composite, not a container

This is the one decision most likely to be revisited by someone who prefers containers
everywhere, so the constraint is written down: **Docker container actions run only on
Linux hosted runners.** Not Windows, not macOS, and not the slimmer Ubuntu runner images,
which have no Docker preinstalled. A composite action that installs a .NET tool runs
identically on all of them.

The precedent in the ecosystem points the same way: `trivy-action` migrated *away* from
Docker to composite, `golangci-lint-action` never used it, and both direct .NET analogues
(`ReportGenerator-GitHub-Action`, `GitTools/actions`) are JavaScript actions that download
a binary rather than container actions.

**The container image is still published**, because "not the default for the Action" is
not the same as "not useful": for GitLab CI, Jenkins, Woodpecker, or a `docker run` in a
Makefile, an image with git and the tool already in it is the least-friction option
available. It is a real channel with a real audience — just not the one that should
implement the Action.

Native AOT was considered and rejected as infeasible today, not as undesirable: Roslyn's
own trimming support (`dotnet/roslyn#68154`) has been open since 2023, analyzer loading
specifically breaks under AOT (`dotnet/runtime#83355`), and `CSharpier` — the closest
comparable Roslyn-based tool — has wanted it for years without getting it.

## Two problems found by actually consuming the packages

Both were found by packing the artifacts and installing them from a throwaway project,
not by reading the csproj files. Neither would have been visible any other way.

**1. `CS9057` in every consumer's build.** The analyzer was first built against
`Microsoft.CodeAnalysis.CSharp` 4.11.0, the newest available. Consuming it from a project
on the .NET 8 SDK produced:

```
warning CS9057: The analyzer assembly '...Portcullis.Rules.dll' references version
'4.11.0.0' of the compiler, which is newer than the currently running version '4.8.0.0'.
```

An analyzer must be built against the *oldest* Roslyn it intends to support, not the
newest one available. Pinned to 4.8.0 (what the .NET 8 SDK ships); the warning is gone
and the package loads on .NET 8 and every later SDK.

**2. An empty symbols package failed the pack.** `IncludeSymbols=true` combined with
`IncludeBuildOutput=false` — which an analyzer package requires, since its assembly goes
to `analyzers/dotnet/cs/` rather than `lib/` — produces a `.snupkg` with nothing in it,
and `dotnet pack` fails with `NU5017`. Resolved by embedding symbols in the analyzer
assembly instead (`DebugType=embedded`), which preserves debuggability without a separate
package.

## Verification

Run for real on 2026-08-18, against the packed `.nupkg` files, not the source projects.

**The analyzer package fires from a plain `PackageReference`.** A throwaway consumer
project with no knowledge of portcullis beyond the package reference:

```
Controllers/WidgetsController.cs(5,22): error PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT: ...
Domain/Widget.cs(2,14): warning PORTCULLIS_P9_ORPHAN_ENTITY: ...
Infrastructure/WidgetDbContext.cs(3,32): warning PORTCULLIS_P10_CUSTOM_BASE_CLASS: ...
Build FAILED.
```

The build failing is the point: an architecture violation stopped a build in a project
that never referenced this repository. This check is also wired into `release.yml` as a
gate, because an analyzer package whose DLL lands in the wrong folder packs cleanly,
installs cleanly, and does nothing at all — a failure mode that is invisible unless
something asserts a diagnostic actually appeared.

**Both tools install and run.** `dotnet tool install -g Portcullis.Cli` /
`Portcullis.CiComment`, then the full chain against a disposable git repository whose
working tree matched the head of the range being scanned:

```
$ portcullis scan <repo>/src --provenance-range <D>..<E> --provenance-repo <repo>
summary: { errorCount: 1, warningCount: 2, infoCount: 0 }
gate:    { blocked: false, scope: "diff", blockingErrorCount: 0 }
exit 0

$ portcullis-ci-comment --current scan.json
ℹ️ Not blocking this PR — the 1 error above is outside this PR's own changed lines.
exit 0
```

An error is present in the tree, is reported, and correctly does not block a pull request
that did not introduce it — through the installed packages, not the source tree.

## Not verified here

The GHCR image and the release workflow have not been executed — both need a real tag
push and registry credentials, which happens on the first release, not in the session
that wrote them. The Dockerfile builds a standard multi-stage .NET image and installs git
(a real runtime dependency of the provenance signal, not a convenience), but "it built
and ran" is not something this document can claim yet.

One gotcha is worth pre-empting, from `architecture-standards`'
[`OPEN-SOURCE-RELEASE.md` §5](https://github.com/konradcinkusz/architecture-standards/blob/main/docs/guides/OPEN-SOURCE-RELEASE.md):
**a GHCR package is created private on its first push**, regardless of the repository's
visibility, and nothing flips it automatically. The release workflow emits a notice
saying so; from outside, the symptom is "works for me, fails for everyone else."
