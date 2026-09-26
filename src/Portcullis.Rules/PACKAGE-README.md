# Portcullis.Analyzers

Roslyn analyzers that fail the build when C# code violates your team's declared
architecture — not generic code quality, and not security: specifically the layering and
boundary decisions your own codebase already committed to.

```sh
dotnet add package Portcullis.Analyzers
```

That is the whole installation. Violations become ordinary compiler warnings and errors
on every `dotnet build`, in every IDE and every CI system — no runner setup, no container,
no separate tool to install.

## The rules

| Rule id | Default | What it catches |
|---|---|---|
| `PORTCULLIS_P2_KERNEL_LOC_CEILING` | error | A shared-kernel folder grown past an ~800-line ceiling |
| `PORTCULLIS_P2_KERNEL_ENTITY_REFERENCE` | error | The shared kernel referencing a business entity type |
| `PORTCULLIS_P4_ENSURE_CREATED_OUTSIDE_TEST` | error | `Database.EnsureCreated()` with no in-memory guard (freezes the schema at first-boot state) |
| `PORTCULLIS_P4_SEED_DATA_IN_MODEL` | warning | `HasData(...)` seeding inside `OnModelCreating` |
| `PORTCULLIS_P9_CONTROLLER_NO_DBCONTEXT` | error | A controller referencing a `DbContext` directly instead of delegating |
| `PORTCULLIS_P9_ORPHAN_ENTITY` | warning | An EF Core entity with a `DbSet<T>` that nothing outside its own file ever references |
| `PORTCULLIS_P10_CUSTOM_BASE_CLASS` | warning | Extension via a hand-rolled base class instead of an interface plus DI registration |
| `PORTCULLIS_P11_VENDOR_SDK_OUTSIDE_ADAPTER` | warning | A vendor SDK's types imported outside a designated adapter boundary |
| `PORTCULLIS_P15_MISSING_SERVICE_DEFAULTS` | warning | A service entry point that builds a host but never wires shared observability |
| `PORTCULLIS_CONVENTION_UNMATCHED` | warning | A convention you configured matches no path — likely a typo or a renamed folder |
| `PORTCULLIS_NO_CONVENTION_MATCHED` | warning | Nothing was configured and no convention matched, so the convention-driven rules checked nothing |

The rules implement principles P2, P4, P9, P10, P11 and P15 of
[`architecture-standards`](https://github.com/konradcinkusz/architecture-standards) — a
written architecture constitution for .NET services. You do not need to adopt that
standard to use these analyzers, but the principles are where each rule's reasoning is
written down.

## Telling the rules where to look

Every rule recognises a shared kernel, a domain layer, an adapter boundary or a service
entry point by **folder and file naming**, because nothing in ordinary C# declares those
roles. The names are configurable, so this enforces *your* layout rather than one specific
one. Put them in a `.globalconfig` (or an `.editorconfig` carrying `is_global = true` — these
are global options, so a value inside an ordinary `[*.cs]` section is not seen):

```ini
is_global = true

portcullis_kernel_folders = Common, Shared
portcullis_entity_folders = Model
portcullis_adapter_folders = External, Gateways
portcullis_vendor_namespaces = Stripe, Google.Cloud, Acme.Payments
portcullis_entry_point_file_names = Program.cs, Startup.cs
portcullis_kernel_line_ceiling = 500
```

Each key is optional and falls back to its default independently. Setting one **replaces**
that list rather than adding to it, and an explicitly empty value (`portcullis_kernel_folders =`)
declares "this project has none" rather than falling back.

If a convention you set matches nothing, `PORTCULLIS_CONVENTION_UNMATCHED` says so — a gate
that checked nothing should not look like a gate that found nothing. Full reference:
[`docs/CONFIGURATION.md`](https://github.com/konradcinkusz/letsgolegacy.portcullis/blob/main/docs/CONFIGURATION.md).

## Tuning severity

Every rule is an ordinary Roslyn diagnostic, so it is configured the ordinary way — in
`.editorconfig`, per rule and per path:

```ini
[*.cs]
# promote a warning to a build failure
dotnet_diagnostic.PORTCULLIS_P9_ORPHAN_ENTITY.severity = error

# or silence one rule while you pay down existing debt
dotnet_diagnostic.PORTCULLIS_P10_CUSTOM_BASE_CLASS.severity = none
```

Adopting on an existing codebase with pre-existing violations: start with everything at
`suggestion`, fix a class at a time, and raise each rule to `warning`/`error` as it goes
green. Nothing has to be clean on day one.

## What this package is not

It is not the whole product. The analyzers catch violations at build time, anywhere. The
[`portcullis` CLI and GitHub Action](https://github.com/konradcinkusz/letsgolegacy.portcullis) add the
things a compiler diagnostic cannot do: a machine-readable JSON scan result, a sticky
pull-request comment, an AI-provenance signal that applies a stricter threshold to
AI-authored lines, and a merge gate scoped to the pull request's own diff so pre-existing
debt elsewhere in the tree does not block unrelated work.

MIT licensed. Source, rule reasoning, and the honest record of what each rule does and
does not catch: <https://github.com/konradcinkusz/letsgolegacy.portcullis>
