## What this changes

<!-- One or two sentences. What is different after this merges. -->

## What was actually run

<!-- The house rule: claims are things that were run. Paste the commands and their real
     output, not a summary. If you could not run something, say so — that is a normal and
     welcome outcome here, and better than implying you did. -->

```
dotnet build portcullis.sln
dotnet test portcullis.sln
```

## Evidence it does what it says

<!-- For a rule change: the firing case, the does-not-fire case, and the mutant.
     For a gate or CI change: the verdict before and after, on a real diff.
     For docs: what you checked the text against. -->

## Limitations you already know about

<!-- Anything this does not cover, degrades on, or leaves open. Several documents in this
     repository exist purely to record a gap that was found and deliberately not fixed. -->

## Checklist

- [ ] `dotnet build portcullis.sln` clean — zero warnings, not "few"
- [ ] `dotnet test portcullis.sln` green
- [ ] A new rule has a unit test in both directions **and** a mutant (`CONTRIBUTING.md` → Adding a rule)
- [ ] Docs updated where this changes documented behaviour
- [ ] No claim in this PR or in the docs it touches is stated more strongly than what was verified
