# Phase 0 live verification

The live command is read-only. It connects and requests server information, map, team, recent team
chat, and map markers. It does not send chat, control a device, pair an entity, or change server state.

## Option A: .NET user-secrets

User-secrets live outside the repository but are not encrypted. They are suitable only for this
development spike.

From PowerShell in the repository root:

```powershell
$project = '.\src\RustPlusHelper.Verification\RustPlusHelper.Verification.csproj'

dotnet user-secrets set 'RustPlus:Server' 'SERVER-IP-OR-HOST' --project $project
dotnet user-secrets set 'RustPlus:Port' '28082' --project $project
dotnet user-secrets set 'RustPlus:PlayerId' 'STEAM64-ID' --project $project
dotnet user-secrets set 'RustPlus:UseFacepunchProxy' 'true' --project $project

$playerToken = Read-Host 'Rust+ player token'
dotnet user-secrets set 'RustPlus:PlayerToken' $playerToken --project $project
Remove-Variable playerToken
```

The PowerShell history contains the variable name, not the typed token. The child `dotnet` process
still receives the value, so do this only on a trusted machine.

Run:

```powershell
dotnet run --project $project -- --live --timeout-seconds 90
```

To also test a camera code (raw error code/message printed to the console, no game client
required), add `--camera <code>`:

```powershell
dotnet run --project $project -- --live --timeout-seconds 90 --camera DOME1
```

## Option B: process environment

```powershell
$env:RUSTPLUS_SERVER = 'SERVER-IP-OR-HOST'
$env:RUSTPLUS_PORT = '28082'
$env:RUSTPLUS_PLAYER_ID = 'STEAM64-ID'
$env:RUSTPLUS_PLAYER_TOKEN = Read-Host 'Rust+ player token'
$env:RUSTPLUS_USE_FACEPUNCH_PROXY = 'true'

dotnet run --project .\src\RustPlusHelper.Verification -- --live --timeout-seconds 90

Remove-Item Env:RUSTPLUS_SERVER
Remove-Item Env:RUSTPLUS_PORT
Remove-Item Env:RUSTPLUS_PLAYER_ID
Remove-Item Env:RUSTPLUS_PLAYER_TOKEN
Remove-Item Env:RUSTPLUS_USE_FACEPUNCH_PROXY
```

## Direct transport

Do not disable the proxy merely to make a failed test pass. If direct `ws://` is intentionally being
tested, set `UseFacepunchProxy` to `false` and also pass:

```powershell
--allow-insecure-direct
```

There is no automatic fallback from proxy to direct transport.

## Output review

The command writes:

- `summary.json`: aggregate counts/status only;
- `map.jpg`: server-provided map image when available.

Both remain under ignored `artifacts/`. Before sharing either file:

1. Confirm the summary has no endpoint, IDs, names, messages, or token.
2. Decide whether the server map itself is acceptable to disclose.
3. Never commit output merely because it is redacted automatically.

## Clearing user-secrets

```powershell
dotnet user-secrets clear --project .\src\RustPlusHelper.Verification\RustPlusHelper.Verification.csproj
```
