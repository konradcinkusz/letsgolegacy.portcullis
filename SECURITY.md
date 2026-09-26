# Security policy

## Supported versions

None yet, and that is not a formality: no `v*` tag has been pushed, nothing is on
nuget.org, and no container has been published. There is no released version to backport
a fix to. Until the first release, the supported version is `main`.

| Version | Supported |
|---|---|
| `main` | Yes |
| Any published release | Does not exist yet |

## Reporting a vulnerability

Report privately, not in a public issue:

1. **Preferred** — GitHub's private vulnerability reporting on this repository:
   *Security → Advisories → Report a vulnerability*. It creates a private thread and a
   draft advisory in one step.
2. If that is unavailable to you, open a public issue saying only that you have a
   security report and asking for a private channel. Do not include details.

What helps most, in order: the input that triggers it (a C# snippet, a repository layout,
a workflow file), what an attacker gains, and the version or commit you saw it on. A
proof of concept is welcome but not required to file.

Expect an acknowledgement within a week. If a fix is warranted it lands on `main` first;
once releases exist, a patched release follows and the advisory is published with credit
unless you ask otherwise.

## What is in scope

This project runs inside other people's builds and CI, so the interesting surface is
narrow but real:

- **The analyzer package** — code loaded by the compiler in every consuming build and IDE.
- **The CLI and the container** — run against a checkout, and they shell out to `git` to
  compute provenance.
- **The GitHub Action and `Portcullis.CiComment`** — run with a token that has
  `pull-requests: write`, and post a comment built from scan output.
- **The release pipeline** — what it builds, signs, and pushes under this project's name.

Things worth reporting: a crafted repository, file path, branch name or commit message
that causes command injection, path traversal, token disclosure in logs or in a PR
comment, or arbitrary code execution in a consumer's build.

## What is not a vulnerability

- **A rule that misses a violation.** That is a bug, and a valuable one — file it
  publicly. This is a conformance gate, not a security scanner; it has never claimed to
  detect malicious code.
- **A false positive.** Also an ordinary bug, and treated as high priority per
  [`CONTRIBUTING.md`](CONTRIBUTING.md).
- **The gate being bypassable by someone who can edit the workflow.** Anyone who can
  change CI configuration can remove the gate; no gate defends against that.
