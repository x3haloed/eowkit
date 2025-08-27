param([string]$Cmd="run", [string]$Cfg="configs/eowkit.toml")
switch ($Cmd) {
  "build"   { dotnet build -c Release }
  "probe"   { dotnet run --project src/EowKit.Cli -- probe   $Cfg }
  "install" { dotnet run --project src/EowKit.Cli -- install $Cfg }
  "run"     { dotnet run --project src/EowKit.Cli -- run     $Cfg }
  default   { Write-Host "Usage: .\eowkit.ps1 [build|probe|install|run]"; exit 1 }
}