#!/usr/bin/env bash
# Installs the pre-commit secret scan into this clone.
#
#   ./scripts/install-hooks.sh
#
# A hook that is not installed scans nothing; the CI job (secret-scan.yml) scans every
# push regardless, so skipping this costs a red build instead of a local refusal.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

if ! command -v gitleaks >/dev/null 2>&1; then
  echo "gitleaks is not installed: https://github.com/gitleaks/gitleaks/releases" >&2
  # Non-zero on purpose: an installer that reports success when nothing will scan is worse than none.
  exit 127
fi

cp scripts/hooks/pre-commit .git/hooks/pre-commit
chmod +x .git/hooks/pre-commit
echo "Installed .git/hooks/pre-commit (gitleaks $(gitleaks version))."
