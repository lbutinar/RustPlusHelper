# Security policy

Rust+ player tokens and push-registration credentials grant access to companion functionality and
must be treated as secrets.

If a secret is accidentally committed:

1. Stop using it and re-pair or rotate it where possible.
2. Remove it from the working tree and Git history.
3. Inspect logs, build artifacts, CI output, and shared copies.
4. Add a regression test if the leak came from application behavior.

Do not open a public issue containing a real token, pairing notification, authenticated protocol
message, server address tied to a player identity, or unredacted diagnostic archive.

The detailed threat model and handling rules are in [`docs/security-model.md`](docs/security-model.md).
