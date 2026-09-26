# Configuration

Every rule portcullis ships is convention-based: it recognises a shared kernel, a domain
layer, an adapter boundary or a service entry point by **folder and file naming**, because
nothing in ordinary C# declares those roles explicitly.

Until this was configurable, that meant portcullis enforced *one specific* architecture. A
team whose shared kernel is called `Common/`, or whose adapters live in `External/`, got a
clean green scan that had checked nothing — and the only tuning knob, `.editorconfig`
severity, can turn a rule off but cannot tell it where to look.

This page is how you tell it where to look.

---

## The two channels read the same configuration

portcullis ships as a Roslyn analyzer package *and* as a CLI/Action/container. The two are
fed differently, because an analyzer is loaded by the compiler and cannot open files —
it may run out-of-process in an IDE, and `EnforceExtendedAnalyzerRules` makes
`System.IO` use a build error (RS1035).

So there is one convention model (`PortcullisConventions`) and one set of key names
(`PortcullisConventionKeys`), reached two ways:

| Channel | You write | It reaches the rules via |
|---|---|---|
| `Portcullis.Analyzers` (NuGet, in your build) | `.globalconfig` (or an `is_global = true` `.editorconfig`) | Roslyn's `AnalyzerConfigOptions.GlobalOptions` |
| `portcullis` CLI, the Action, the container | `portcullis.json` at the scan root | `PortcullisConfigFile`, flattened onto the same keys |

Neither host reimplements what a convention *is*. The JSON reader's only job is to produce
the same key/value pairs Roslyn would have supplied.

> Note: before this existed, the CLI path passed `options: null` to
> `CompilationWithAnalyzers`, which discarded analyzer configuration entirely. So the
> analyzer package could be configured and the CLI could not — same rules, same code, two
> behaviours. That is fixed; both channels now read the same thing.

---

## `portcullis.json`

Place it in the directory you scan (the same path you pass to `portcullis scan`). It is not
searched for up the directory tree — an implicit parent config is exactly the kind of
action-at-a-distance that makes "why is this rule not firing?" hard to answer.

```json
{
  "kernelFolders": ["Common", "Shared"],
  "entityFolders": ["Model"],
  "adapterFolders": ["External", "Gateways"],
  "vendorNamespaces": ["Stripe", "Google.Cloud", "Acme.Payments"],
  "entryPointFileNames": ["Program.cs", "Startup.cs"],
  "kernelLineCeiling": 500
}
```

Every property is optional. Omit one and its built-in default applies — fallback is
**per key**, so naming one convention never blanks the others.

## `.globalconfig`

The same values, as the flat keys the analyzer package reads. These are **global** options,
so they must live in a `.globalconfig` file, or in an `.editorconfig` carrying
`is_global = true` — a value inside an ordinary `[*.cs]` section is a per-file option and
will not be seen:

```ini
is_global = true

portcullis_kernel_folders = Common, Shared
portcullis_entity_folders = Model
portcullis_adapter_folders = External, Gateways
portcullis_vendor_namespaces = Stripe, Google.Cloud, Acme.Payments
portcullis_entry_point_file_names = Program.cs, Startup.cs
portcullis_kernel_line_ceiling = 500
```

Lists are comma-separated; surrounding whitespace is trimmed.

---

## What each key does

| Key / JSON property | Default | Used by |
|---|---|---|
| `portcullis_kernel_folders` / `kernelFolders` | `ServiceDefaults, Kernel, SharedKernel` | `PORTCULLIS_P2_KERNEL_LOC_CEILING`, `PORTCULLIS_P2_KERNEL_ENTITY_REFERENCE` |
| `portcullis_entity_folders` / `entityFolders` | `Domain, Entities` | `PORTCULLIS_P2_KERNEL_ENTITY_REFERENCE` |
| `portcullis_adapter_folders` / `adapterFolders` | `Infrastructure, Adapters, Integrations` | `PORTCULLIS_P11_VENDOR_SDK_OUTSIDE_ADAPTER` (the exemption) |
| `portcullis_vendor_namespaces` / `vendorNamespaces` | `Anthropic, OpenAI, Stripe, Twilio, SendGrid, PayPal, Amazon, Azure, Google.Cloud, Firebase, MailKit` | `PORTCULLIS_P11_VENDOR_SDK_OUTSIDE_ADAPTER` |
| `portcullis_entry_point_file_names` / `entryPointFileNames` | `Program.cs` | `PORTCULLIS_P15_MISSING_SERVICE_DEFAULTS`, and P11's composition-root exemption |
| `portcullis_kernel_line_ceiling` / `kernelLineCeiling` | `800` | `PORTCULLIS_P2_KERNEL_LOC_CEILING` |

### Folder matching

A path segment matches a configured name either exactly (`Common`) or as a project-name
suffix (`Acme.Common`) — the shape real solutions actually use. Matching is
case-insensitive.

### Vendor namespace matching

An import matches on a **dotted-segment boundary**: `Azure` matches `using Azure;` and
`using Azure.Storage.Blobs;`, but not `using AzureFoo;`. Multi-segment entries like
`Google.Cloud` work as you would expect.

### Paths are matched relative to the scan root

`portcullis scan` matches conventions against each file's path **relative to the directory
you scanned**, so a checkout that happens to live under `~/Domain/` does not make every
file in the repository an "entity" file, and a CI workspace path containing
`Integrations` does not exempt the whole tree from the vendor rule.

The analyzer package has no equivalent notion of a scan root — the compiler hands it
absolute paths — so there it matches the full path unless you say otherwise:

```ini
portcullis_path_root = /home/runner/work/my-repo/my-repo
```

Rarely needed, since project directories seldom collide with these names, but it is the
escape hatch if one does.

### Configuration replaces, it does not extend

Setting `kernelFolders` to `["Common"]` means `Common` — not `Common` plus the three
defaults. An additive model would make it impossible to *narrow* a convention.

An **explicitly empty** list is honoured as a deliberate declaration:

```json
{ "kernelFolders": [] }
```

means "this project has no shared kernel", and switches those rules off without
suppressing their diagnostics. That is different from omitting the key, which falls back
to the defaults.

---

## When a convention matches nothing

This is the part that matters more than the file format: a misconfigured gate should be
loud, not vacuously green.

**`PORTCULLIS_CONVENTION_UNMATCHED`** (warning) — fires per convention that *you configured*
and which matched no path in the scan. Almost always a typo, or a folder renamed after the
config was written.

**`PORTCULLIS_NO_CONVENTION_MATCHED`** (warning) — fires at most once, and only when a scan
configured nothing at all *and* matched none of the conventions. That is the vacuous case:
the convention-driven rules evaluated nothing and the scan can pass without having checked
them.

The second one is deliberately all-or-nothing rather than per-convention. Plenty of
healthy services legitimately have no shared kernel, so reporting each unmatched default
separately produced four warnings on ordinary code — and a rule that fires on healthy code
gets switched off, after which it reports nothing when it matters either.

Both default to **warning**, so neither blocks a build on its own.

On the **analyzer package** channel you can raise either to an error the ordinary Roslyn
way, and the compiler enforces it:

```ini
dotnet_diagnostic.PORTCULLIS_NO_CONVENTION_MATCHED.severity = error
```

> **This does not work on the CLI/Action/container channel yet.** `portcullis scan` builds
> its own compilation and reports each diagnostic at its `defaultSeverity`; it does not
> apply `dotnet_diagnostic.*.severity` overrides. So severity tuning is analyzer-package
> only today, in both directions — you cannot raise a rule to blocking, and you cannot
> silence one, from the CLI. Said here rather than left for someone to discover when their
> `.editorconfig` has no effect on a gate.

---

## Checking what was actually used

`portcullis scan` reports the config it read, so "the file was not where you thought it was"
is visible rather than silent:

```jsonc
"configuration": {
  "path": "/repo/portcullis.json",  // null when no config file was found
  "error": null                   // set when a config file exists but could not be parsed
}
```

A malformed `portcullis.json` does **not** take the gate down — the scan continues on
defaults — but `error` is populated and the CLI prints it, because a team running
indefinitely on defaults while believing their config is in force is the same
vacuously-green failure in a different disguise.

---

## What is not configurable

- **The shape of a rule.** You can say where your adapters are; you cannot redefine what
  counts as leaking a vendor type.
- **Which rules exist.** `RuleRegistry.All` is fixed. Turn individual rules off with
  `.editorconfig` severity.
- **Severity, here.** That is Roslyn's own
  `dotnet_diagnostic.<ID>.severity` mechanism and is documented in the package README.
