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

The Phase 2 desktop application uses `ISecretStore` with Windows DPAPI current-user encryption.
SQLite holds ciphertext and non-secret metadata only. Additional entropy binds each protected value
to the application version, server ID, and secret purpose. Cross-machine export will require explicit
password-based re-encryption; copying DPAPI ciphertext is not a portable backup.

Automatic pairing uses a separate `IApplicationSecretStore` because FCM/Expo registration belongs
to this Windows user rather than one server. The registration flow starts only after an explicit UI
action, and the serialized credential bundle is DPAPI-protected immediately. Cleartext byte buffers
are zeroed after registration and after each listener operation. The temporary Steam authentication
result is neither returned to application/UI code nor stored. Pairing notifications are mapped to an
application-owned record inside the infrastructure boundary and are never logged as raw objects.

The Servers UI stores one Steam64 identity for the Windows user and accepts a separate signed player
token through a masked field for each manual server pairing. A token is never treated as an
account-global credential. The application validates it without logging it, converts the canonical
integer directly into a
short-lived byte buffer, passes that buffer to `ISecretStore`, and zeroes the buffer immediately.
The immutable UI string is dereferenced after submission and is never persisted. Editing a server
leaves the field blank and preserves the existing token unless the user enters a replacement.
Callers retrieving cleartext own the returned buffer and must zero it after use.

The explicit desktop connection test retrieves the token only after the user clicks **Test
connection**, parses it directly from the caller-owned UTF-8 buffer, and zeroes that buffer in a
`finally` block. It reports allowlisted status text rather than raw transport exceptions and validates
authentication only with the read-only server-information request. Expected adapter failures use a
credential-redacted exception type, allowing useful local diagnostics without exposing the token;
all other unexpected exceptions receive generic UI text. The short-lived socket is closed after the
test.

## Transport

The official companion server exposes direct `ws://`. Authenticated requests carry player identity
and token fields inside protobuf messages, so direct Internet transport must be treated as plaintext.

The application therefore:

- defaults to the Facepunch secure proxy supported by the selected libraries;
- requires `--allow-insecure-direct` when Phase 0 configuration disables the proxy;
- requires an explicit persisted desktop transport choice and displays a plaintext-credential
  warning before the desktop can use direct `ws://`;
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
