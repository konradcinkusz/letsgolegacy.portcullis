# Using Portcullis in your CI

A practical guide for a team that has a C# repository and wants architecture violations
to stop merging. Start at §1 if you want the short version; §4 is the section that
actually matters if your codebase is more than a few months old.

> **Status, stated up front.** The packages this guide installs
> (`Portcullis.Analyzers`, `Portcullis.Cli`, `Portcullis.CiComment`) and the container image
> are **built and verified locally but not yet published** — publishing happens on the
> first `v*` tag. Until then, every `dotnet add package` / `dotnet tool install` below
> will fail with "package not found", and §7 has the build-from-source path that works
> today. Everything else in this guide has been run for real.

---

## 1. Pick your path

| You want | Use | Effort |
|---|---|---|
| Violations to fail the build, everywhere, including on developers' machines | **`Portcullis.Analyzers`** NuGet package | one line |
| A pull-request gate with a comment, on GitHub | **The Action** | one workflow file |
| The same on GitLab / Azure DevOps / Jenkins | **`portcullis` tool** or the **container** | a few lines |
| To look before you commit to anything | `portcullis scan` locally | one command |

These compose. Most teams end up with the analyzer package for fast local feedback *and*
the Action for the gate — the analyzer catches it before you push, the gate catches it if
you push anyway.

---

## 2. The fast path: the analyzer package

```sh
dotnet add package Portcullis.Analyzers
```

That is the whole installation. On the next `dotnet build`, architecture violations
appear as ordinary compiler diagnostics:

```
Controllers/WidgetsController.cs(5,22): error PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT:
    Controller 'WidgetsController' references 'WidgetDbContext' directly. Controllers are
    transport only — bind, authorize, delegate — route through an orchestrator or
    repository instead (architecture-standards P9).
Build FAILED.
```

Because they are ordinary diagnostics, they work everywhere a compiler runs — Visual
Studio, Rider, VS Code, `dotnet build`, and every CI system that builds your code. There
is nothing to install on the runner and no Portcullis-specific CI step at all.

Add it to one project, or to every project at once via `Directory.Build.props`:

```xml
<Project>
  <ItemGroup>
    <PackageReference Include="Portcullis.Analyzers" Version="0.1.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

`PrivateAssets="all"` matters: it stops Portcullis appearing as a dependency of *your*
package if you publish one. Pin the version — `Version="*"` means a rule added in a later
release can fail your build without anyone changing a line of code.

**This path has no diff scoping.** Every violation in every file is reported on every
build, which is the right behaviour for a compiler diagnostic and the wrong behaviour for
a first encounter with a legacy codebase. See §4.

---

## 3. The pull-request gate

### 3.1 GitHub Actions

```yaml
# .github/workflows/architecture.yml
name: Architecture
on: pull_request

permissions:
  contents: read
  pull-requests: write        # required for the sticky comment

jobs:
  portcullis:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0      # required — see the warning below
      - uses: konradcinkusz/letsgolegacy.portcullis@v0
        with:
          path: src
```

That is a complete, working gate. It scans `src/`, posts one sticky comment that updates
in place on each push rather than spamming a new one, and fails the job when the gate
blocks.

> **`fetch-depth: 0` is not optional.** `actions/checkout` defaults to a shallow clone
> that does not contain your PR's base commit. Without it, Portcullis cannot work out which
> lines your PR changed, so diff scoping silently cannot happen. The action detects this
> and prints a `::warning::` telling you, then falls back to whole-tree scope — it does
> not pretend to have scoped when it hasn't. But the fix is this line, not the warning.

**Inputs:**

| Input | Default | What it does |
|---|---|---|
| `path` | `src` | Directory to scan, relative to the repo root |
| `version` | pinned | Version of the tools to install |
| `diff-scoped` | `true` | Only violations in the PR's own changed lines can block (§4) |
| `comment` | `true` | Post/update the sticky PR comment |
| `fail-on-block` | `true` | Set `false` to report without enforcing |
| `dotnet-version` | `10.0.x` | Set `''` to use whatever the runner already has |

**Code scanning.** The Action does not take `--sarif` or `--baseline` yet. To publish SARIF
to GitHub code scanning today, run the CLI and `github/codeql-action/upload-sarif` in your
own workflow, as this repository does for its own pull requests — the workflow is in
[`SARIF.md` §5](SARIF.md).

**Outputs** — `blocked`, `error-count`, `blocking-error-count`, `result-path` — so a
later step can react:

```yaml
      - uses: konradcinkusz/letsgolegacy.portcullis@v0
        id: portcullis
        with: { path: src, fail-on-block: false }
      - if: steps.portcullis.outputs.blocked == 'true'
        run: echo "would have blocked on ${{ steps.portcullis.outputs.blocking-error-count }} violation(s)"
```

### 3.2 GitLab CI

The container image needs no .NET SDK on the runner. One wrinkle worth knowing: GitLab
overrides a container's `ENTRYPOINT`, so call the DLL explicitly rather than relying on
the image's entrypoint.

```yaml
portcullis:
  image: ghcr.io/konradcinkusz/letsgolegacy.portcullis:0.1.0
  rules:
    - if: $CI_PIPELINE_SOURCE == "merge_request_event"
  variables:
    GIT_DEPTH: 0              # same reason as fetch-depth: 0 above
  script:
    - dotnet /app/Portcullis.Cli.dll scan "$CI_PROJECT_DIR/src"
        --provenance-range "$CI_MERGE_REQUEST_DIFF_BASE_SHA..$CI_COMMIT_SHA"
        --provenance-repo "$CI_PROJECT_DIR"
        | tee portcullis.json
  artifacts:
    when: always
    paths: [portcullis.json]
```

The job fails when the CLI exits `1`, which is exactly when the gate blocks.

### 3.3 Azure DevOps

```yaml
- task: UseDotNet@2
  inputs: { version: '10.0.x' }

- script: |
    dotnet tool install -g Portcullis.Cli --version 0.1.0
    export PATH="$PATH:$HOME/.dotnet/tools"
    BASE=$(git merge-base "origin/$(System.PullRequest.TargetBranch)" HEAD)
    portcullis scan "$(Build.SourcesDirectory)/src" \
      --provenance-range "$BASE..HEAD" \
      --provenance-repo "$(Build.SourcesDirectory)"
  displayName: Portcullis
```

### 3.4 Anything else (Jenkins, Woodpecker, a Makefile)

The portable form — `git merge-base` works on every platform and needs no CI-specific
variables:

```sh
BASE=$(git merge-base origin/main HEAD)
portcullis scan ./src --provenance-range "$BASE..HEAD" --provenance-repo .
```

Or with the container, no .NET required:

```sh
docker run --rm -v "$PWD:/workspace" ghcr.io/konradcinkusz/letsgolegacy.portcullis:0.1.0 \
  scan /workspace/src --provenance-range "$BASE..HEAD" --provenance-repo /workspace
```

---

## 4. Adopting on a codebase that already has violations

**Read this one.** It is where most architecture-gate rollouts die.

Point any rule engine at a real codebase and it finds pre-existing violations — that is
the point of it. But a gate that fails on all of them fails *every* pull request,
including one-line changes touching nothing related. Developers cannot merge, so within a
week somebody switches the gate off, and it never comes back on.

Portcullis has three independent answers, and they compose.

### 4.1 Diff scoping (on by default in the Action)

When given a commit range, Portcullis still **reports** every violation it finds, but only
**blocks** on ones inside the lines your PR actually changed.

This is not theoretical. Here is a real run against a repository carrying pre-existing
errors, where the PR under test changed one unrelated file:

```
summary: { errorCount: 1, warningCount: 2, infoCount: 0 }
gate:    { blocked: false, scope: "diff", blockingErrorCount: 0 }
exit 0
```

and the comment says so plainly rather than leaving you to guess:

> ℹ️ Not blocking this PR — the 1 error above is outside this PR's own changed lines.

The effect: existing debt is visible on every PR without being anyone's emergency, while
anything *new* is stopped at the boundary. Debt shrinks as files get touched, instead of
requiring a big-bang cleanup before the gate can be switched on at all.

### 4.2 Severity ratcheting via `.editorconfig`

Every rule is an ordinary Roslyn diagnostic, so it is tuned the ordinary way — per rule,
and per path if you want:

```ini
# .editorconfig
root = true

[*.cs]
# start noisy rules as suggestions while you pay down existing violations
dotnet_diagnostic.PORTCULLIS_P10_CUSTOM_BASE_CLASS.severity = suggestion

# turn one all the way off for now
dotnet_diagnostic.PORTCULLIS_P4_SEED_DATA_IN_MODEL.severity = none

# and promote one you are already clean on, so it can never regress
dotnet_diagnostic.PORTCULLIS_P9_ORPHAN_ENTITY.severity = error

# legacy code is exempt; new code is not
[src/Legacy/**.cs]
dotnet_diagnostic.PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT.severity = none
```

Verified working, not assumed: promoting `PORTCULLIS_P9_ORPHAN_ENTITY` from its default
`warning` to `error` failed a build that previously passed, and setting
`PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT` to `none` removed a build-failing error entirely.

**A sane rollout:** everything to `suggestion` on day one → fix one rule's violations →
raise that rule to `warning`, then `error` → repeat. Each ratchet is one line, and no
step ever blocks work that is unrelated to it.

### 4.3 A baseline file

Record the findings that exist today, once, and commit the file:

```sh
portcullis scan ./src --baseline portcullis-baseline.json --write-baseline
```

From then on, pass `--baseline portcullis-baseline.json` to every scan. A finding the file
accepts is still **reported** — marked *(accepted by the baseline)* in the PR comment and
`baselined: true` in the JSON — but it never **blocks**, even on a line the pull request
touched. Findings are matched by a fingerprint of rule, file and line text rather than by
line number, so edits elsewhere in a file, or re-indenting the line itself, do not turn
accepted debt back into "new"; rewriting the line does, which is when it deserves a second
look anyway.

Diff scoping already keeps untouched debt from blocking; the baseline adds the cases it
cannot cover — reformatting inside a diff, and whole-tree gates such as a scheduled audit or
the fallback when git cannot resolve the range. The file lists the rule, file and message
beside each fingerprint, so a pull request that grows it shows its reviewer exactly what is
being accepted. Details, including what changes a fingerprint: [`SARIF.md` §3](SARIF.md).

---

## 5. Reading the output

`portcullis scan` prints one JSON document (full schema in [`SPEC.md`](SPEC.md)):

```json
{
  "filesScanned": 156,
  "rulesEvaluated": 7,
  "violations": [
    { "ruleId": "PORTCULLIS_P9_ORPHAN_ENTITY", "filePath": "Domain/QrCode.cs",
      "line": 11, "message": "...", "severity": "warning" }
  ],
  "summary": { "errorCount": 4, "warningCount": 14, "infoCount": 0 },
  "gate": { "blocked": true, "scope": "diff", "blockingErrorCount": 5 }
}
```

`summary` is what was **found**. `gate` is what **blocks** — they are deliberately
different numbers, and the gap between them is your pre-existing debt.

Each violation also carries a `fingerprint` (its identity across commits) and `baselined`
(true when the scan's baseline accepted it). With `--baseline`, the result also has a
`baseline` object — the file's `entryCount`, and the `acceptedCount` of this scan's
findings — and `gate.acceptedByBaselineCount` counts the errors it kept from blocking.
`--sarif <file>` writes the findings that count against the change as SARIF 2.1.0 — see
[`SARIF.md`](SARIF.md).

**Exit codes:** `0` not blocked · `1` blocked · `2` bad usage. Only `2` means Portcullis
itself failed; `1` is a verdict, not an error.

---

## 6. Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `gate.scope` is `"all"` when you expected `"diff"` | The base commit is not in the checkout. Add `fetch-depth: 0` (GitHub) or `GIT_DEPTH: 0` (GitLab). The Action warns about this explicitly. |
| Gate passes when you are sure it should block | Check `gate.scope`. If it is `"diff"`, the violation is outside this PR's own changed lines — that is the gate working as designed. If it is `"all"` with a `degradedReason`, git could not resolve your range and the scan fell back to whole-tree gating; the reason says what failed. A git failure no longer passes silently — see [`DIFF-GATE.md` §3](DIFF-GATE.md). |
| The package installs but no diagnostics ever appear | Check the DLL is at `analyzers/dotnet/cs/` inside the `.nupkg`. An analyzer packed to `lib/` installs cleanly and does nothing at all. |
| `warning CS9057: ... references version 4.x of the compiler, which is newer` | Your SDK's Roslyn is older than the one Portcullis was built against. `Portcullis.Analyzers` targets Roslyn 4.8 (the .NET 8 SDK's version) precisely to avoid this — if you see it, you are on an older SDK than .NET 8. |
| `docker pull` of the GHCR image says unauthorized | A GHCR package is created **private** on its first push regardless of repository visibility, and nothing flips it automatically. It has to be made public once in the package's own settings. |
| Rules do not fire on code you know is wrong | Portcullis parses source directly rather than through MSBuild, so a type from a NuGet package or another project does not resolve. Some rules depend on that; others are blunted by it. See the "Honest limits" section of the [README](../README.md). |
| A rule fires on code that is correct | That is a bug worth reporting, not something to work around — a noisy gate is worse than a missing rule. Silence it in `.editorconfig` (§4.2) and open an issue with the snippet. |

---

## 7. Before the first release: running from source

Until the packages are published, this works today and needs only the .NET 10 SDK:

```sh
git clone https://github.com/konradcinkusz/letsgolegacy.portcullis
cd letsgolegacy.portcullis
dotnet build portcullis.sln

# scan any repository on your machine
dotnet run --project src/Portcullis.Cli -- scan /path/to/your-repo/src

# with the diff-scoped gate
cd /path/to/your-repo
BASE=$(git merge-base origin/main HEAD)
dotnet run --project /path/to/letsgolegacy.portcullis/src/Portcullis.Cli -- \
  scan ./src --provenance-range "$BASE..HEAD" --provenance-repo .
```

You can also build the analyzer package locally and consume it from a private feed:

```sh
dotnet pack src/Portcullis.Rules/Portcullis.Rules.csproj -c Release -o ./artifacts
# then in your repo's nuget.config, add ./artifacts as a package source
```

---

## 8. What this cannot do yet

Stated here rather than discovered later:

- **Rule *shape* is fixed even though the conventions are configurable.** You can tell a
  rule where your adapters live (see [`CONFIGURATION.md`](CONFIGURATION.md)); you cannot
  redefine what counts as leaking a vendor type.
- **Six of fifteen principles.** Nine are unimplemented — five need cross-file or
  temporal judgment a single-snapshot analyzer does not have, four are not C# at all.
  [`SPEC.md` §5](SPEC.md) lists which and why.
- **The container image and release workflow are unexecuted.** Both need a real tag push;
  [`DISTRIBUTION.md`](DISTRIBUTION.md) says so explicitly rather than claiming they work.
- **A git failure degrades the gate rather than passing it.** If the provenance commands
  fail, the scan falls back to whole-tree gating (`gate.scope` `"all"`) and reports
  `gate.degradedReason`, instead of reporting an empty diff and passing.

If something here does not work as written, that is a documentation bug and worth an
issue — every command in this guide except the container examples was run before it was
written down.
