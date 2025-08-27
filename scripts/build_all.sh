#!/bin/zsh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/src/EowKit.Cli/EowKit.Cli.csproj"
DIST="$ROOT/dist"
PUBLISH="$ROOT/publish"

RIDS=(
  linux-x64
  linux-arm64
  osx-x64
  osx-arm64
  win-x64
)

command -v dotnet >/dev/null 2>&1 || { echo "dotnet SDK not found" >&2; exit 1; }

rm -rf "$DIST" "$PUBLISH"
mkdir -p "$DIST" "$PUBLISH"

publish_one() {
  local rid="$1"
  local out="$PUBLISH/$rid"

  echo "==> Publishing $rid"
  rm -rf "$out"
  dotnet publish "$PROJ" -c Release -r "$rid" --self-contained true -o "$out" | cat

  local pkgdir="$DIST/eowkit-$rid"
  mkdir -p "$pkgdir"

  local bin dest
  if [[ "$rid" == win-* ]]; then
    bin="$out/EowKit.Cli.exe"
    dest="$pkgdir/eowkit.exe"
  else
    bin="$out/EowKit.Cli"
    dest="$pkgdir/eowkit"
  fi

  cp "$bin" "$dest"
  chmod +x "$dest" 2>/dev/null || true

  cp -R "$ROOT/configs" "$pkgdir/"
  cp "$ROOT/README.md" "$pkgdir/"

  if [[ "$rid" == win-* ]]; then
    (cd "$DIST" && zip -qr "eowkit-$rid.zip" "eowkit-$rid")
  else
    (cd "$DIST" && tar -czf "eowkit-$rid.tar.gz" "eowkit-$rid")
  fi
}

for rid in "${RIDS[@]}"; do
  publish_one "$rid"
done

echo "\nArtifacts:"
ls -1 "$DIST"


