# Security model

## Protected data

The following are credentials and must never be logged or committed:

- per-server Rust+ player token;
- FCM Android ID and security token;
- FCM registration token;
- Expo token;
- Facepunch/Rust+ authentication token;
- temporary Steam/browser login material;
- future Discord or Telegram credentials.

Steam IDs, companion server IDs, server addresses, player names, chat, and positions are not
authentication secrets, but they are personal/server-derived data and should be minimized in logs
and fixtures.

## Phase 0 credential flow

The verification command accepts credentials only through:

- .NET user-secrets; or
- process environment variables.

It does not accept credential command-line arguments. .NET user-secrets are a development convenience,
not encrypted production storage.

The production desktop application will use `ISecretStore` with Windows DPAPI current-user encryption.
SQLite will hold ciphertext and non-secret metadata only. Cross-machine export will require explicit
password-based re-encryption; copying DPAPI ciphertext is not a portable backup.

## Transport

The official companion server exposes direct `ws://`. Authenticated requests carry player identity
and token fields inside protobuf messages, so direct Internet transport must be treated as plaintext.

The application therefore:

- defaults to the Facepunch secure proxy supported by the selected libraries;
- requires `--allow-insecure-direct` when Phase 0 configuration disables the proxy;
- will never silently downgrade to direct transport;
- still requires live confirmation that proxy behavior works for the selected server.

## Output policy

The Phase 0 report deliberately excludes:

- endpoint and server branding;
- player token and player ID;
- team member IDs and names;
- chat names and bodies;
- marker coordinates and vending names.

It retains only counts, timestamps, dimensions, marker-kind counts, unknown numeric marker types, and
the map-image hash. Reports and map JPEGs are written to ignored `artifacts/`.

## Logging policy

Future structured logging uses allowlisted properties. Never log entire connection records, requests,
responses, pairing notifications, configuration providers, or database rows containing ciphertext.

Required security tests include connection-string formatting, known-secret redaction, key-based
redaction, aggregate-report privacy, and staged-repository secret scans.
