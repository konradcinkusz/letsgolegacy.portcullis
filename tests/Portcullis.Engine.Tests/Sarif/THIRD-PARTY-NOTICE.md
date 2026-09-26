# `sarif-schema-2.1.0.json`

The JSON schema for SARIF 2.1.0, copied unchanged from the OASIS Static Analysis Results
Interchange Format (SARIF) Technical Committee's repository, so the tests can validate
Portcullis's SARIF output against the official schema without a network connection.

| | |
|---|---|
| Source | <https://github.com/oasis-tcs/sarif-spec>, file `sarif-2.1/schema/sarif-schema-2.1.0.json` |
| Commit | `adbb670c018335b0f384e6dd8819f4ea055d7ee1` |
| Schema `id` | `https://docs.oasis-open.org/sarif/sarif/v2.1.0/errata01/os/schemas/sarif-schema-2.1.0.json` |
| SHA-256 (LF line endings) | `c3b4bb2d6093897483348925aaa73af03b3e3f4bd4ca38cef26dcb4212a2682e` |

`SarifSchemaTests` checks the hash, so an edit to the file fails the build rather than
quietly changing what "valid SARIF" means here.

Copyright © OASIS Open. The file is part of the SARIF TC's work, governed by the OASIS
policies — the IPR Policy and the TC Process — as set out in that repository's
[`LICENSE.md`](https://github.com/oasis-tcs/sarif-spec/blob/main/LICENSE.md). It is not
covered by this repository's MIT licence.
