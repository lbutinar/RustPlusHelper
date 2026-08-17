# Third-party notices

## Leaflet

- Version: 1.9.4
- Project: https://leafletjs.com/
- Source package: `leaflet@1.9.4` from npm
- License: BSD-2-Clause

The unmodified runtime distribution files and upstream license are vendored under:

```text
src/RustPlusHelper.Desktop/wwwroot/vendor/leaflet/
```

## RustPlusApi

- Version: 2.0.0-beta.7
- Project: https://github.com/HandyS11/RustPlusApi
- License: MIT

RustPlusApi is consumed as a pinned NuGet dependency and is not vendored into this repository.

## Microsoft.Data.Sqlite

- Version: 10.0.10
- Project: https://learn.microsoft.com/dotnet/standard/data/sqlite/
- License: MIT

## SQLitePCLRaw and bundled SQLite

- SQLite bundle: `SQLitePCLRaw.bundle_e_sqlite3` 2.1.12
- Project: https://github.com/ericsink/SQLitePCL.raw
- SQLitePCLRaw license: Apache-2.0
- SQLite license: Public domain

The native package is directly pinned to 2.1.12 to avoid the vulnerable 2.1.11 asset otherwise
selected transitively by Microsoft.Data.Sqlite 10.0.10.

## System.Security.Cryptography.ProtectedData

- Version: 10.0.10
- Project: https://github.com/dotnet/dotnet
- License: MIT
