<a name="readme-top"></a>

<p align="center">
  <img src="docs/assets/logo.svg" alt="" width="88" height="88">
</p>

# Portcullis

[![CI](https://github.com/konradcinkusz/letsgolegacy.portcullis/actions/workflows/ci.yml/badge.svg)](https://github.com/konradcinkusz/letsgolegacy.portcullis/actions/workflows/ci.yml "CI")
[![Portcullis PR Check](https://github.com/konradcinkusz/letsgolegacy.portcullis/actions/workflows/portcullis-pr-check.yml/badge.svg)](https://github.com/konradcinkusz/letsgolegacy.portcullis/actions/workflows/portcullis-pr-check.yml "Portcullis PR Check")
[![GitHub license](https://flat.badgen.net/github/license/konradcinkusz/letsgolegacy.portcullis?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/letsgolegacy.portcullis/blob/main/LICENSE "GitHub license")
[![Maintained](https://flat.badgen.net/static/Maintained/yes?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/letsgolegacy.portcullis/commits/main "Maintained")
[![GitHub issues](https://flat.badgen.net/github/issues/konradcinkusz/letsgolegacy.portcullis?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/letsgolegacy.portcullis/issues "GitHub issues")
[![GitHub pull requests](https://flat.badgen.net/github/prs/konradcinkusz/letsgolegacy.portcullis?icon=github&color=black&scale=1.01)](https://github.com/konradcinkusz/letsgolegacy.portcullis/pulls "GitHub pull requests")

**A CI gate that catches code which passes every test and still breaks your
architecture** — Roslyn analyzers run on every pull request, reported on changed lines
only, as SARIF with a baseline. Portcullis is component C5 of
[Second Key](https://github.com/konradcinkusz/letsgolegacy.secondkey), which produces
independent, deterministic evidence that a .NET system migrated by an AI agent still
keeps its business contract; Portcullis is the gate step of that chain.

> **Status:** the code base has been imported and is being extended; see
> [`docs/WORKPLAN.md`](docs/WORKPLAN.md) for the tickets and the criterion each one is
> done by.

AI coding agents now write a large share of committed code, and their characteristic
failure is not a bug — it is a change that compiles, passes review, and quietly violates a
boundary the team decided on months ago. Portcullis turns those decisions into Roslyn
analyzers, so the build fails at `file:line` instead of the drift being discovered a year
later.

Existing tools answer a different question. SonarQube grades code quality, Snyk finds
vulnerabilities, CodeClimate measures complexity — none of them know what *your*
architecture is. `NetArchTest` and `ArchUnitNET` do check a declared architecture, and
have for years, but as reflection-based test libraries you wire up yourself. The closest
comparison is a published tool that enforces team rules against AI-written code too, via
TypeScript check functions attached to ADRs, language-agnostic by design. This project
takes the opposite bet — .NET only, on the
Roslyn semantic model, so it resolves real symbols and can point at a line rather than
match text, and it ships as build-time compiler diagnostics plus a merge gate rather than
a CLI check step.

## Quick start

> **Not published yet.** `Portcullis.Analyzers`, `Portcullis.Cli`, the `@v0` Action and the
> container image below are built and verified locally, but no `v*` tag has shipped —
> every install command on this page currently 404s. The build-from-source path at the
> end of this section is what actually works today.

**Get the rules enforced on every build** — one package reference, nothing else:

```sh
dotnet add package Portcullis.Analyzers
```

Violations now appear as ordinary compiler diagnostics on every `dotnet build`, in every
IDE and every CI system. Severity is tuned the ordinary way, in `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.PORTCULLIS_P9_ORPHAN_ENTITY.severity = error   # promote
dotnet_diagnostic.PORTCULLIS_P10_CUSTOM_BASE_CLASS.severity = none # or silence while adopting
```

**Gate pull requests**, with a sticky comment and a diff-scoped verdict:

```yaml
# .github/workflows/architecture.yml
name: Architecture
on: pull_request
permissions:
  contents: read
  pull-requests: write
jobs:
  portcullis:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0   # required: the gate diffs against the PR's base commit
      - uses: konradcinkusz/letsgolegacy.portcullis@v0
        with:
          path: src
```

**Or run it anywhere** — as a .NET tool, or as a container for CI that has no .NET SDK:

```sh
dotnet tool install -g Portcullis.Cli
portcullis scan ./src                       # JSON to stdout; exit 1 when the gate blocks

docker run --rm -v "$PWD:/workspace" ghcr.io/konradcinkusz/letsgolegacy.portcullis:latest scan /workspace/src
```

**Adopting on a codebase that already has violations?** Read
[`docs/TUTORIAL.md` §4](docs/TUTORIAL.md) first — a gate that fails every PR on
pre-existing debt gets switched off within a week, and avoiding that is a setup
decision, not a later fix.

Nothing above needs this repository cloned. To build from source instead:
`git clone`, then `dotnet build portcullis.sln && dotnet test portcullis.sln`, then
`dotnet run --project src/Portcullis.Cli -- scan ./src`. The .NET 10 SDK is the only
prerequisite.

## What it checks today

Nine diagnostics across six of the fifteen principles in
[`architecture-standards`](https://github.com/konradcinkusz/architecture-standards), the
written architecture constitution Portcullis enforces. You do not have to adopt that
standard to use these rules, but it is where each rule's reasoning lives.

| Rule id | Default | What it catches |
|---|---|---|
| `PORTCULLIS_P2_KERNEL_LOC_CEILING` | error | A shared-kernel folder grown past an ~800-line ceiling |
| `PORTCULLIS_P2_KERNEL_ENTITY_REFERENCE` | error | The shared kernel referencing a business entity type |
| `PORTCULLIS_P4_ENSURE_CREATED_OUTSIDE_TEST` | error | `Database.EnsureCreated()` with no in-memory guard — freezes the schema at first-boot state while migrations pile up |
| `PORTCULLIS_P4_SEED_DATA_IN_MODEL` | warning | `HasData(...)` seeding inside `OnModelCreating` |
| `PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT` | error | A controller touching a `DbContext` directly instead of delegating |
| `PORTCULLIS_P9_ORPHAN_ENTITY` | warning | An EF Core entity nothing outside its own file ever references |
| `PORTCULLIS_P10_CUSTOM_BASE_CLASS` | warning | Extension by hand-rolled base class instead of an interface plus DI |
| `PORTCULLIS_P11_VENDOR_SDK_OUTSIDE_ADAPTER` | warning | A vendor SDK's types leaking past the adapter boundary |
| `PORTCULLIS_P15_MISSING_SERVICE_DEFAULTS` | warning | A service entry point that never wires shared observability |
| `PORTCULLIS_CONVENTION_UNMATCHED` | warning | A convention you configured matches no path — likely a typo or a renamed folder |
| `PORTCULLIS_NO_CONVENTION_MATCHED` | warning | Nothing was configured and no convention matched, so the convention-driven rules checked nothing |

The other nine principles are not implemented, and
[`docs/SPEC.md` §5](docs/SPEC.md) says which and why — five need cross-file or temporal
judgment a single-snapshot analyzer does not have, and four are not C# at all
(Dockerfiles, `fly.toml`, CI YAML, prose).

## The two things that make it more than a linter

**A gate scoped to the pull request's own diff.** Point a rule engine at a real codebase
and it finds pre-existing violations; a gate that fails on all of them fails *every* PR,
including one-line changes that touch nothing related, and gets switched off within a
week. Portcullis reports everything it finds but only blocks on violations inside the lines
your PR actually changed. [`docs/DIFF-GATE.md`](docs/DIFF-GATE.md) has the design and the
verification, including the failure it fixes reproduced as a two-commit repro.

**AI provenance as an input, not a product.** Lines attributable to an AI coding agent —
read from real git history, commit trailers and authorship, never from code style — get a
stricter severity threshold than the same violation written by hand. It is never a badge
or a score in the PR comment; the only thing it does is change which threshold applies.
[`docs/SPEC.md` §4](docs/SPEC.md) explains the design, including the
session-level-averaging failure it was built not to repeat.

## Documentation

| Document | What it is |
|---|---|
| [`docs/WORKPLAN.md`](docs/WORKPLAN.md) | The tickets this repository is working through, with the criterion each one is done by |
| [`docs/TUTORIAL.md`](docs/TUTORIAL.md) | **Start here to use it:** CI setup for GitHub/GitLab/Azure DevOps, and how to adopt it on a codebase that already has violations |
| [`docs/index.html`](docs/index.html) | The same guide as a styled page — the stranger-facing surface if GitHub Pages is enabled for `/docs` |
| [`docs/CONFIGURATION.md`](docs/CONFIGURATION.md) | Telling the rules where your kernel, domain, adapters and entry points live |
| [`docs/SPEC.md`](docs/SPEC.md) | The frozen contracts: rule format, violation schema, output JSON, provenance interface, and a verdict on all 15 principles |
| [`docs/DIFF-GATE.md`](docs/DIFF-GATE.md) | The diff-scoped merge gate |
| [`docs/DISTRIBUTION.md`](docs/DISTRIBUTION.md) | How each artifact ships and why — including why the GitHub Action is composite rather than a container |
| [`docs/MUTATIONS.md`](docs/MUTATIONS.md) | The mutation pass: every rule deliberately broken to prove its tests would notice |
| [`docs/FINDINGS.md`](docs/FINDINGS.md) | What the whole pipeline actually caught when attacked end to end, and what it did not |
| [`docs/BOOTSTRAP.md`](docs/BOOTSTRAP.md), [`docs/M4-INTEGRATION.md`](docs/M4-INTEGRATION.md) | Dated verification records from the skeleton and the integration milestones |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Build, test, and the bar a new rule has to clear |
| [`CHANGELOG.md`](CHANGELOG.md) | What has changed, and what has not been released |
| [`SECURITY.md`](SECURITY.md) | How to report a vulnerability, and what counts as one here |
| [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) | Contributor Covenant 2.1 |

## Honest limits

- **Single-compilation analysis.** Source is parsed directly rather than loaded through
  MSBuild, so a type from a referenced NuGet package or another project does not resolve.
  Several rules are written to rely on exactly that; others would be sharper without it.
- **Rules are still convention-based, but the conventions are yours.** "Kernel",
  "Domain", "adapter" are recognized by folder and file naming rather than by anything
  declared in the code. Which names those are is configurable per repository — see
  [`docs/CONFIGURATION.md`](docs/CONFIGURATION.md). What is *not* configurable is the
  shape of each rule: you can tell `PORTCULLIS_P11_VENDOR_SDK_OUTSIDE_ADAPTER` where your
  adapters live, not what "leaking a vendor type" means.
- **A convention that matches nothing is reported, not silent.**
  `PORTCULLIS_NO_CONVENTION_MATCHED` fires once when a scan matched none of them, and
  `PORTCULLIS_CONVENTION_UNMATCHED` fires per convention you configured that matches no
  path. Both are warnings by default, so neither blocks a build on its own.
- **Nine of the fifteen principles are unimplemented.** Five need cross-file, cross-repo
  or git-history context a single-snapshot C# pass does not have, and four are a
  different artifact type entirely (Dockerfiles, `fly.toml`, CI YAML, prose). P5 is split
  rather than cleanly in one bucket: its secret-literal half is mechanical and could be
  built today; its "exactly one service holds the signing key" half is a cross-repo
  invariant no single-repo analyzer can see. See
  [`docs/SPEC.md` §5](docs/SPEC.md) for the principle-by-principle verdict.

Every claim in this repository's documentation is meant to be something that was actually
run, with the command and its real output recorded. Where a thing is unbuilt or
unverified, the docs say so instead of rounding up.

## Licence

MIT; see [`LICENSE`](LICENSE). The engine, rules, CLI and Action are all MIT.

<p align="right">(<a href="#readme-top">back to top</a>)</p>
