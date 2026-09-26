# Portcullis CI comment publisher

Turns an [Portcullis](https://github.com/konradcinkusz/letsgolegacy.portcullis) scan result into a
Markdown pull-request comment, updates **one** sticky comment in place rather than
posting a new one on every push, and exits non-zero when the merge gate blocks.

```sh
dotnet tool install -g Portcullis.CiComment

portcullis scan ./src > current.json
portcullis-ci-comment --current current.json --previous last.json --pr 42
```

Publishing needs `GITHUB_TOKEN` and `GITHUB_REPOSITORY` in the environment; without them
it renders to stdout and skips publishing, which is what makes a local dry run painless.

Most people should not call this directly — the
[`konradcinkusz/letsgolegacy.portcullis` action](https://github.com/konradcinkusz/letsgolegacy.portcullis) wires the
scan, the diff range, the previous-scan cache and this publisher together in one step.

MIT licensed. Source: <https://github.com/konradcinkusz/letsgolegacy.portcullis>
