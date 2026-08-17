# Third-party notices

## Rust and Rust+ brand asset

- Owner: Facepunch Studios Ltd
- Official Rust page and press kit: https://facepunch.com/games/rust
- Official Rust+ listing: https://apps.apple.com/app/rust/id1487691681
- Fan-content guidelines: https://facepunch.com/legal/ugc

The Rust+ application emblem is used in the desktop window and application interface solely to
identify compatibility with Rust and Rust+. Copies are stored in the desktop project's `Assets`
and `wwwroot/assets` directories for the native WPF and embedded web interfaces. RustPlusHelper is
an unofficial community project and is not affiliated with or endorsed by Facepunch Studios. This
brand asset is not covered by the repository's MIT license.

## Material Design Icons

- Project: https://github.com/google/material-design-icons
- License: Apache-2.0

The Material Icons font and its upstream license are vendored under:

```text
src/RustPlusHelper.Desktop/wwwroot/vendor/material-icons/
```

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
