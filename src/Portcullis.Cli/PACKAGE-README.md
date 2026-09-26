# Portcullis CLI

Scans a C# codebase against your team's declared architecture rules, prints a
machine-readable JSON result, and exits non-zero when the merge gate blocks.

```sh
dotnet tool install -g Portcullis.Cli
portcullis scan ./src
```

## What it adds over the analyzer package

[`Portcullis.Analyzers`](https://www.nuget.org/packages/Portcullis.Analyzers) surfaces the
same rules as ordinary build warnings and errors, which is the right default for most
teams. This CLI exists for the things a compiler diagnostic cannot do:

- **A machine-readable result.** One JSON document per scan (schema in
  [`docs/SPEC.md`](https://github.com/konradcinkusz/letsgolegacy.portcullis/blob/main/docs/SPEC.md))
  that a pull-request comment, a dashboard, or your own tooling can consume.
- **A diff-scoped merge gate.** Given a commit range, only violations inside that
  range's changed lines can fail the build — so pre-existing debt elsewhere in the tree
  does not block work that never touched it.
- **An AI-provenance signal.** Violations on lines attributable to an AI coding agent
  (read from real git history — commit trailers and authorship, never code style) get a
  stricter severity threshold than the same violation written by a human.

## Usage

```sh
# scan a path; exits 1 if any error-severity violation exists anywhere
portcullis scan ./src

# scope the gate to one pull request's own diff, and escalate AI-authored violations
portcullis scan ./src \
  --provenance-range "$BASE_SHA..$HEAD_SHA" \
  --provenance-repo .
```

Exit codes: `0` not blocked, `1` blocked, `2` bad usage.

In GitHub Actions, prefer the action — it wires the range, the sticky PR comment and the
gate for you:

```yaml
- uses: konradcinkusz/letsgolegacy.portcullis@v0
  with:
    path: src
```

MIT licensed. Source and full documentation:
<https://github.com/konradcinkusz/letsgolegacy.portcullis>
