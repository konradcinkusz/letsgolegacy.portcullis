# Migration rules

Four Roslyn analyzers for code that has been moved from the .NET Framework to modern .NET
(.NET 10). Each looks for an idiom that a mechanical migration — a person in a hurry, or an
AI agent — tends to carry across: code that still compiles, through a compatibility shim or
a NuGet package, and still passes its tests, but keeps an assumption the old runtime made
and the new one does not.

| Rule id | Default | Catches |
|---|---|---|
| [`PORTCULLIS_MIG_SYSTEM_WEB`](#portcullis_mig_system_web) | warning | Any use of a System.Web namespace or type |
| [`PORTCULLIS_MIG_HTTPCONTEXT_CURRENT`](#portcullis_mig_httpcontext_current) | error | Reading or writing the static `System.Web.HttpContext.Current` |
| [`PORTCULLIS_MIG_SYNC_OVER_ASYNC`](#portcullis_mig_sync_over_async) | warning | Blocking on a task: `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` |
| [`PORTCULLIS_MIG_CONFIGURATION_MANAGER`](#portcullis_mig_configuration_manager) | error | Settings read through `ConfigurationManager` instead of `IOptions<T>`/`IConfiguration` |

The ids are a contract: the Second Key standards pack, built in another repository, cites
them verbatim, and `RuleRegistryTests` pins them. Every rule carries the diagnostic
category `Migration` — no single architecture-standards principle names these idioms.

Contents: [how the rules decide](#how-the-rules-decide) ·
[configuration](#configuration) · [the four rules](#portcullis_mig_system_web) ·
[severity and the gate](#severity-and-the-gate) · [evidence](#evidence)

---

## How the rules decide

**On symbols, never on text.** Every decision is made on the symbol a name binds to:
a type counts as System.Web because the namespace it is declared in *is* System.Web; a
`.Result` counts because the property is declared by `Task<TResult>`; a configuration read
counts because the member is declared by `System.Configuration.ConfigurationManager`. That
is what lets the rules

- catch the idiom however it is written — through an alias
  (`using Ctx = System.Web.HttpContext;`), a `using static` import, a fully-qualified name
  or a method group — and
- leave the look-alikes alone: ASP.NET Core's `Microsoft.AspNetCore.Http.HttpContext`, a
  team's own `HttpContext` or `ConfigurationManager` class, and the modern
  `Microsoft.Extensions.Configuration.ConfigurationManager` that
  `WebApplicationBuilder.Configuration` returns — which a text search for
  "ConfigurationManager" would flag in every ASP.NET Core `Program.cs`.

Types are identified by their fully-qualified metadata name
(`src/Portcullis.Rules/MigrationSymbols.cs`), not by the assembly that declares them, so
the real `System.Web.dll`, the System.Web adapters package and a hand-written shim are all
recognised the same way.

**Both channels see the same symbols.** In the analyzer package the compiler binds names
against the project's real references. The `portcullis` CLI, the Action and the container
compile the source themselves, without a project build, so the scan references two things
instead (`src/Portcullis.Engine/Semantics/`):

- the framework assemblies of the .NET runtime the scan runs on — so
  `client.GetStringAsync(url).Result` knows it is looking at a `Task<string>`;
- declarations of the .NET Framework APIs these rules look for — System.Web's core
  request, response, session and cache types and `HttpContext.Current`,
  `System.Web.Configuration.WebConfigurationManager` and the `System.Configuration`
  configuration entry points — which no modern .NET runtime ships.

NuGet packages and other projects are still not referenced in a scan; the gaps that
causes are listed per rule below. Nor does a scan see the global usings the SDK generates
at build time for `<ImplicitUsings>enable</ImplicitUsings>`: they are written to `obj/`,
which a scan skips. In a file that relies on them, a type name written without its
`using` — `Task`, `HttpClient` — does not bind in a scan, while a member of an expression
whose type comes from a call still does (`client.GetStringAsync(url).Result` is seen; a
parameter declared `Task<int>` in such a file is not).

## Configuration

| `portcullis.json` | `.globalconfig` key | Default | Effect |
|---|---|---|---|
| `migrationExemptFolders` | `portcullis_migration_exempt_folders` | *(none)* | Folders in which none of the four rules reports — a compatibility layer that still talks to System.Web on purpose during an incremental migration, or a project not migrated yet |
| `systemWebAllowedTypes` | `portcullis_system_web_allowed_types` | `System.Web.HttpUtility`, `System.Web.IHtmlString` | Fully-qualified System.Web types `PORTCULLIS_MIG_SYSTEM_WEB` does not report |

Folders match the way every other folder convention does
([`CONFIGURATION.md`](../CONFIGURATION.md)): a path segment equal to the name or ending in
`.Name`, case-insensitively, relative to the scan root. As with the other conventions,
setting a list replaces the default, and an explicitly empty list is honoured.

Severity is tuned the ordinary way in the analyzer package:

```ini
[*.cs]
dotnet_diagnostic.PORTCULLIS_MIG_SYNC_OVER_ASYNC.severity = error
```

---

## PORTCULLIS_MIG_SYSTEM_WEB

**Default: warning.** Any use of a namespace or type in `System.Web` or a namespace
nested in it: a `using` directive (`using`, `using static`, `using X = …`), a qualified
name, a type reference — parameter and field types, `new`, `typeof`, `catch`, generic
arguments, attributes.

**Why it matters in a migration.** System.Web is the ASP.NET of the .NET Framework and does
not exist on modern .NET. Code that still names it builds only through the System.Web
adapters or a reference left over from the old project, and either way the request
pipeline, modules, handlers, session and caching it assumes are emulated rather than
native. Each hit is a piece of the migration that was carried across rather than done.

```csharp
// Reported: 'System.Web.HttpRequest' is System.Web …
using System.Web;

public class AuditService
{
    public void Record(HttpRequest request) => Write(request.UserHostAddress);
}
```

```csharp
// Not reported: the ASP.NET Core type, from Microsoft.AspNetCore.Http.
using Microsoft.AspNetCore.Http;

public class AuditService
{
    public void Record(HttpRequest request) => Write(request.HttpContext.Connection.RemoteIpAddress?.ToString());
}
```

One reference is one report: `System.Web.HttpContext.Current` is reported once, at
`System.Web.HttpContext`. A name whose System.Web namespace does not resolve in the scan
(`System.Web.Mvc.Controller`) is reported at its `System.Web` prefix rather than lost.

**Deliberately not reported.**

- `using System.Web;` on its own: `System.Web.HttpUtility` and `System.Web.IHtmlString` live
  there and ship in modern .NET (`System.Web.HttpUtility.dll`). The legacy types used
  through the directive are reported where they are used. Remove both from
  `systemWebAllowedTypes` and the directive itself is reported too.
- Names in a `namespace` declaration — declaring types in System.Web is implementing it, as
  a shim does, not using it — and names in documentation comments.
- In a CLI scan only: an *unqualified* name in expression position inside a class whose base
  class the scan cannot resolve. A migrated ASP.NET Core controller is the case this exists
  for: `HttpContext.Request` there is `ControllerBase.HttpContext`, a property the scan
  cannot see because ASP.NET Core is not referenced, and reporting it as System.Web would be
  a false positive on exactly the code a migration produces. Qualified names and names in a
  type position (a field's type, say) are still reported in such a class.

**Known gaps.**

- In a CLI scan, a type from `System.Web.Mvc`, `System.Web.UI`, `System.Web.Http` and other
  namespaces the scan does not declare is caught at its `using` directive and at a
  qualified `System.Web` prefix, but not where it is used by its short name.
- A use that never writes a System.Web name — `var context = GetContext();
  context.Request…` — is reported only where `GetContext` names the type.
- Anything that is not C#: `.cshtml`, `.aspx`, `.ascx`, `Global.asax`, `web.config`; and
  names in strings (`Type.GetType("System.Web.HttpContext, System.Web")`).

## PORTCULLIS_MIG_HTTPCONTEXT_CURRENT

**Default: error.** A read or a write of the static `System.Web.HttpContext.Current`,
however it is named: through `using System.Web;`, fully qualified, through an alias or
`using static`.

**Why it matters in a migration.** `HttpContext.Current` is how .NET Framework code reaches
the current request from anywhere — a repository, a helper, a logger. It works only on a
request's own flow, it hides a dependency on the web layer inside code that looks
framework-free, and on ASP.NET Core it exists only through the System.Web adapters. It is
also the idiom that outlives the rest of System.Web: a team can move every type to its
ASP.NET Core equivalent and still be reading request state through a static, which is why
it is a rule of its own and why a line using it gets both this diagnostic and
`PORTCULLIS_MIG_SYSTEM_WEB`.

```csharp
// Reported: 'HttpContext.Current' reaches the current request through the static …
public class OrderService
{
    public void Place(Order order) => order.PlacedBy = HttpContext.Current.User.Identity.Name;
}
```

```csharp
// Compliant: the dependency is explicit, and the service is testable without a request.
public class OrderService(IHttpContextAccessor accessor)
{
    public void Place(Order order) => order.PlacedBy = accessor.HttpContext?.User.Identity?.Name;
}
// Better still: pass the user in from the endpoint, and the service needs no web type at all.
```

**Deliberately not reported.** A team's own static `Current` on its own `HttpContext`;
instance members of `HttpContext`, `HttpContextBase` and `HttpContextWrapper` (those are
`PORTCULLIS_MIG_SYSTEM_WEB`'s); `nameof(HttpContext.Current)`, which reads nothing.

**Known gaps.** `IHttpContextAccessor` used as a service locator deep inside a domain layer
has the same shape of problem but is the modern API, and is not reported. A team's own
static wrapper around `HttpContext.Current` is reported once, inside the wrapper; its callers
are not. Reflection is not seen.

## PORTCULLIS_MIG_SYNC_OVER_ASYNC

**Default: warning.** Blocking a thread on a task:

- `.Result` on `Task<T>` and `ValueTask<T>`;
- `.Wait()` in every overload on `Task`/`Task<T>`, and the static `Task.WaitAll`/`Task.WaitAny`;
- `.GetAwaiter().GetResult()` on `Task`, `Task<T>`, `ValueTask` and `ValueTask<T>`, with or
  without `ConfigureAwait(…)`, and `GetResult()` on any of those eight awaiter types held
  in a variable.

**Why it matters in a migration.** It is the idiom a mechanical migration produces most: a
synchronous call site meets an API that is async-only on modern .NET (`HttpClient`,
`Stream`, EF Core) and gets `.Result` glued on. Under classic ASP.NET's synchronization
context that deadlocks; on ASP.NET Core it does not deadlock, but it holds a thread-pool
thread for the whole wait — one per concurrent request — which is how a service that
passed every test falls over at its first real load.

```csharp
// Reported: '_client.GetStringAsync(url).Result' blocks the calling thread …
public string Rates(string url) => _client.GetStringAsync(url).Result;
```

```csharp
// Compliant.
public async Task<string> RatesAsync(string url) => await _client.GetStringAsync(url);
```

**Deliberately not reported** — the two shapes in which the rule can prove from the code in
front of it that the task has already finished:

- the antecedent inside a continuation: `task.ContinueWith(previous => previous.Result)`;
- the synchronous fast path — the same local, parameter, field or property on the true
  branch of a condition that requires `IsCompleted` or `IsCompletedSuccessfully` as a
  conjunction: `if (task.IsCompletedSuccessfully) return task.Result;`. A negated check
  (`!task.IsCompleted`), an or-ed one, the `else` branch, or a check on a different task
  proves nothing and is reported.

Also not reported: anything that only looks like a task — `SemaphoreSlim.Wait()`,
`ManualResetEventSlim.Wait()`, `Monitor.Wait`, `Task.Yield().GetAwaiter().GetResult()`, a
team's own `Result` property — and `nameof(Task<int>.Result)`.

**Known false positives**, reported although the task has completed, because proving it
needs control flow the rule does not follow: `.Result` after `await Task.WhenAll(…)` (await
the finished task instead — it costs nothing), after an early `return` on `!IsCompleted`,
after a `Status == TaskStatus.RanToCompletion` check, and inside a
`Task.Factory.ContinueWhenAll`/`ContinueWhenAny` continuation (the continuation exclusion
covers `ContinueWith` only). A blocking wait in a place that cannot be async — a
constructor, `Dispose`, a property getter — is still a blocking wait and is reported.

**Known gaps.** In a CLI scan, a task returned by a NuGet package's API
(`query.ToListAsync().Result` with EF Core) is not seen: the package is not referenced, so
the call's type does not resolve. Framework APIs and the codebase's own async methods are
seen; the analyzer package sees everything. Also in a CLI scan only, a file that relies on
implicit global usings loses the shapes that need the name `Task` itself: `Task.WaitAll(…)`,
and `.Result` on a parameter or field declared as `Task<T>` (see
[how the rules decide](#how-the-rules-decide)). Portcullis's own scan shows this: in
`GitCommandRunner.cs`, the two `.Result` reads on tasks returned by
`StreamReader.ReadToEndAsync()` are reported, the `Task.WaitAll` before them is not. `async void` methods and lambdas are a
different hazard and are not this rule's.

## PORTCULLIS_MIG_CONFIGURATION_MANAGER

**Default: error.** Settings read through the .NET Framework configuration API: every
static member of `System.Configuration.ConfigurationManager` (`AppSettings`,
`ConnectionStrings`, `GetSection`, `OpenExeConfiguration`, …), of its ASP.NET twin
`System.Web.Configuration.WebConfigurationManager`, and of the long-obsolete
`System.Configuration.ConfigurationSettings` — as property reads, calls, method groups or
through `using static`.

**Why it matters in a migration.** This is the defect that survives every build. The
`System.Configuration.ConfigurationManager` package makes the code compile on modern .NET,
but there it reads `<app>.dll.config` — not `web.config`, and never `appsettings.json` or
environment variables — so a setting that used to arrive comes back `null` at run time,
with no build error and no failing unit test.

```csharp
// Reported: 'ConfigurationManager.AppSettings' reads settings through System.Configuration.ConfigurationManager …
public static string Currency() => ConfigurationManager.AppSettings["Currency"];
```

```csharp
// Compliant: bound once, validated at start-up, injected where needed.
public sealed class ShopOptions { public required string Currency { get; init; } }

builder.Services.AddOptions<ShopOptions>().Bind(builder.Configuration.GetSection("Shop")).ValidateOnStart();

public class PriceFormatter(IOptions<ShopOptions> options) { /* options.Value.Currency */ }
```

**Deliberately not reported.** `Microsoft.Extensions.Configuration.ConfigurationManager`
(the modern class — a different type with the same short name), `IConfiguration`, a team's
own `ConfigurationManager`, and `typeof(ConfigurationManager)` / `nameof(…)`, neither of which
reads a setting.

**Known gaps.** Members of the `Configuration` object that `OpenExeConfiguration` returns
are not reported separately (the `OpenExeConfiguration` call itself is). Settings designer
classes deriving from `ApplicationSettingsBase` (`Properties.Settings.Default.X`) read
`app.config` too and are not reported. Declaring a custom `ConfigurationSection` is not a
read and is not reported.

---

## Severity and the gate

The two rules whose hit is a runtime defect on modern .NET — ambient request state and
null settings — default to **error**; the two whose hit is debt that can still run —
System.Web through a shim, and a blocking wait — default to **warning**. On the pull-request
gate, provenance changes that for AI-written code: a warning on a line attributed to an AI
coding agent is escalated to an error ([`SPEC.md` §4](../SPEC.md)), so on a migration pull
request written by an agent all four rules block — and only on the lines that pull request
changed ([`DIFF-GATE.md`](../DIFF-GATE.md)).

## Evidence

Each rule has tests in both directions under `tests/Portcullis.Engine.Tests/Rules/`, an
end-to-end scan through the CLI's own path (`ScannerMigrationTests`), hand-written mutants,
and a Stryker.NET run over all four rules' source; the numbers are in
[`MUTATIONS.md` §8](../MUTATIONS.md#8-migration-rules-hand-written-mutants-and-a-strykernet-score).

Both channels were also run for real on 2026-09-26. The analyzer package, packed and
consumed from a throwaway `net10.0` web project that references the real compatibility
packages (`Microsoft.AspNetCore.SystemWebAdapters` 2.3.0, `System.Configuration
.ConfigurationManager` 10.0.12):

```
LegacyOrders.cs(9,34):  error   PORTCULLIS_MIG_CONFIGURATION_MANAGER: 'LegacyConfig.AppSettings' reads settings through System.Configuration.ConfigurationManager. …
LegacyOrders.cs(11,30): error   PORTCULLIS_MIG_HTTPCONTEXT_CURRENT: 'System.Web.HttpContext.Current' reaches the current request through the static …
LegacyOrders.cs(11,30): warning PORTCULLIS_MIG_SYSTEM_WEB: 'System.Web.HttpContext' is System.Web, which modern .NET does not have: …
LegacyOrders.cs(13,30): warning PORTCULLIS_MIG_SYNC_OVER_ASYNC: '_client.GetStringAsync("https://example.test/rates").Result' blocks the calling thread …
```

The same project's `System.Web.HttpUtility.UrlEncode(…)` and `builder.Configuration["…"]`
(the modern `ConfigurationManager`) were not reported. It also showed why the rules are
semantic: with the Web SDK's implicit usings, a bare `ConfigurationManager` in that project
does not even compile (CS0104, ambiguous between the two), and the legacy read had to be
written through an alias — which the rule still caught. And `portcullis scan` over a
directory of legacy code of the same kind, with no project build at all, reported all four
rules (`ScannerMigrationTests` holds that scan as a test).
None of this is yet evidence against a real migrated codebase: that is the nopCommerce
bench's ticket (P8 in [`WORKPLAN.md`](../WORKPLAN.md)), not this one.
