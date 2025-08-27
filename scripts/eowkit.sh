#!/usr/bin/env zsh
set -euo pipefail

cmd="${1:-run}"
cfg="configs/eowkit.toml"

case "$cmd" in
  build)
    dotnet build -c Release
    ;;
  probe|install|run)
    dotnet run --project src/EowKit.Cli -- "$cmd" "$cfg"
    ;;
  *)
    echo "Usage: $0 [build|probe|install|run]"
    exit 1
    ;;
esac