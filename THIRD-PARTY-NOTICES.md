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

- Versions: core 2.0.0-beta.7; FCM and FCM Registration 2.0.0-beta.6
- Project: https://github.com/HandyS11/RustPlusApi
- License: MIT

RustPlusApi and its FCM registration packages are consumed as pinned NuGet dependencies and are not
vendored into this repository.

## Microsoft.Data.Sqlite

- Version: 10.0.10
- Project: https://learn.microsoft.com/dotnet/standard/data/sqlite/
- License: MIT

## K4os.Compression.LZ4.Legacy

- Version: 1.3.8
- Project: https://github.com/MiloszKrajewski/K4os.Compression.LZ4
- License: MIT

The package decodes the documented legacy LZ4 stream in imported Rust `.map` files.

## protobuf-net

- Version: 3.2.56
- Project: https://github.com/protobuf-net/protobuf-net
- License: Apache-2.0

## Rust Map Parser research reference

- Project: https://github.com/Cooperkit/Rustmap-Parser
- Reviewed version: PyPI 0.4.0 / repository state on 2026-08-17
- License: MIT

Rust Map Parser is not bundled or executed by RustPlusHelper. Its current parser and orientation
behavior were used as an independent implementation reference alongside Facepunch's map-data
documentation.

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
