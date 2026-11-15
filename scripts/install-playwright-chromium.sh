#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PLAYWRIGHT_BROWSERS_PATH="${PLAYWRIGHT_BROWSERS_PATH:-"$root/.playwright"}"
mkdir -p "$PLAYWRIGHT_BROWSERS_PATH"

mapfile -t scripts < <(find "$root/tests/MicroSharp.Browser.Tests/bin" -path '*/Microsoft.Playwright.TestAdapter' -prune -o -type f -name 'playwright.ps1' -print | sort)
if [[ ${#scripts[@]} -eq 0 ]]; then
  dotnet build "$root/tests/MicroSharp.Browser.Tests/MicroSharp.Browser.Tests.csproj" --configuration Release --nologo
  mapfile -t scripts < <(find "$root/tests/MicroSharp.Browser.Tests/bin" -type f -name 'playwright.ps1' -print | sort)
fi

if [[ ${#scripts[@]} -eq 0 ]]; then
  echo "Playwright script was not produced by the browser test build." >&2
  exit 1
fi

pwsh "${scripts[0]}" install chromium
echo "Chromium installed under ${PLAYWRIGHT_BROWSERS_PATH}"
