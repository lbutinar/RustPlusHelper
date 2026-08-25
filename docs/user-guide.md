# RustPlusHelper user guide

This guide is for people using RustPlusHelper, not for developers. It assumes the app is already
running on your screen.

RustPlusHelper is an unofficial community project. It is not affiliated with or endorsed by Facepunch
Studios. Rust and Rust+ names and brand assets belong to Facepunch Studios.

> A Windows installer (`.msi`) can be built from source today (see
> [README.md](../README.md#building-the-windows-installer)), but it is not yet signed or published
> anywhere you can just download it — see [development-plan.md](development-plan.md) Phase 11.
> Everything below describes how the app behaves once it is running, whichever way you got it there.

## What you need

- Windows 10 or 11.
- The Microsoft Edge WebView2 Runtime, which almost every current Windows 10/11 PC already has
  (it ships with Windows Update and modern Edge). The **Diagnostics** page (see below) tells you
  immediately if it is missing.
- Your Rust+ pairing details for a real server. Without a server, RustPlusHelper still opens with
  deterministic fake data so you can look around safely.

## Everything stays on your PC

RustPlusHelper keeps all of its data — saved servers, your Steam64 ID, Rust+ tokens, cached maps, and
history — in a local SQLite database under your Windows user profile. Tokens are encrypted with
Windows DPAPI so that only your Windows account on this PC can read them; nothing is uploaded
anywhere by the app itself. See [local-storage.md](local-storage.md) for the technical detail.

## Adding your first server

Open the **Servers** page from the left sidebar.

### Option A — automatic pairing (recommended)

1. Click **Set up automatic pairing** once. This opens Chrome or Edge for you to sign in with Steam;
   RustPlusHelper never sees your Steam password.
2. Click **Listen for server pairing**, then in Rust, open the server's Rust+ companion pairing screen
   and pair with your phone/app as usual — RustPlusHelper receives the same pairing notification and
   fills in the server address, port, your Steam64 ID, and the per-server player token automatically.
3. The new server appears in the saved list, already marked **PAIRING SAVED**.

You can click **Reset registration** at any time to sign out and register a different Steam account,
or **Cancel** while a step is in progress.

### Option B — manual entry

If you already know a server's Rust+ companion host, port, and player token, fill in **Display name**,
**Companion host or IP**, **Companion port**, and **Rust+ player token** yourself, then
**Save server and pairing**. You still need to save your **Steam64 ID** once under **Player identity**
— it's shared across every server you add.

### Secure proxy vs. direct connection

**Use Facepunch secure proxy** is checked by default and is the encrypted, recommended path.
Unchecking it switches to a direct, unencrypted `ws://` connection to the server — RustPlusHelper
warns you when you do this and never falls back to it automatically. Only turn it off if you know the
server requires it.

### Testing and opening a server

- **Test connection** performs a one-off, read-only check and reports exactly what happened
  (success, wrong pairing, rejected authentication, or a transport failure) without leaving anything
  connected afterward.
- **Open map** switches the app to that server's live map. It only becomes available once the server
  shows **PAIRING SAVED**.
- **Select** makes a server the default without opening its map. **Edit** lets you change its saved
  details (leave the player-token field blank to keep the existing one). **Remove** asks for
  confirmation before deleting a server and its saved pairing.
- Each saved server shows a quick status summary (player count, time since the last wipe, and its
  most recent event) from whatever was last cached, without opening a live connection. Rust+ only
  reports the *last* wipe, never a schedule, so if you set a **Wipe cycle estimate** (Weekly/Biweekly/
  Monthly), the "next wipe" shown is always your own guess — labelled "(your estimate)" — not
  something Rust+ told the app.

## The map

The **Map** page is the main view once a server is open:

- **Refresh team + markers** re-reads team positions, chat, and world markers without re-downloading
  the map image itself.
- **Refresh everything** also re-downloads the current map image and server info.
- **Choose .map manually** lets you point RustPlusHelper at a Rust `.map` file yourself. Normally it
  finds this automatically from your Steam library or Rust's own connection log, and it never guesses
  when more than one same-size file could match — it asks you instead.
- The toolbar over the map lets you fit the map to the window, toggle the reference grid, and jump
  straight to a grid square by typing its label (e.g. `H14`) into the search box next to it.
- The layer panel lets you turn map layers on and off independently — team, vending, world-event
  markers, monuments, and (once a matching `.map` file is imported) biome, elevation, roads/rails/
  rivers, build-planning, and similar terrain layers. Toggling a layer is instant; it never
  re-downloads or re-renders the base map.
- **Movement trails** draws each teammate's path on the map, saved across restarts so it carries over
  a multi-day play session — not just the current app session. It's downsampled to a point roughly
  every 1-2 minutes (not every exact step) so a multi-day trail stays readable instead of turning into
  a tangle. It resets automatically at the next server wipe, and a teammate's existing path stays
  visible while they're offline — only new points stop being added until they're back online.
- **Personal pins** let you mark your own spots on the map — a loot stash, an ambush point, anywhere
  worth remembering. Type a grid reference (e.g. `H14`) and a short note in the layer panel, then
  click **Add pin**. Pins are entirely local to you and this server, never sent to the game or your
  team, and survive restarting the app; remove one with its **Remove** button.

Grid squares and monument names match the labels used in the official Rust+ app.

## Team and events

- **Team** shows your team roster with live online/alive state and positions, refreshed automatically
  while a server is open. The chat panel shows recent team chat and lets you send a message from the
  box at the bottom — type and press Enter or click **Send**. A sent message appears immediately; if
  it fails to send, an error appears below the box instead.
- **Clan chat**, below team chat, works the same way but for your Rust clan (a separate, optional
  concept from your team) — click **Load clan chat** to check it (most players are not in a clan, so
  this is never checked automatically), then **Refresh** to check again or send a message.
- **Events** is a running history of what RustPlusHelper has actually observed for this server:
  connection lost/restored, teammates coming online, dying, or crossing into a new grid square, world
  markers appearing or disappearing, a crate spawning near a known oil rig (a best-effort guess at
  activation — Rust+ does not report this directly), and (see below) vending and Smart Alarm changes.
  This history is kept locally per server and survives restarting the app. Click **Export as CSV**
  to save it to a file for your own records — nothing is sent anywhere.

## Vending

The **Vending** page searches every vending machine's sell orders it can see, by machine name, item
name, or numeric item/currency ID. Each result shows its price, remaining stock, derived grid
reference, and distance from your nearest online teammate. Clicking a result jumps to it on the map.
Price, stock, and new/removed offers are also recorded to the Events history as they change.

Item names come from a bundled catalogue that can occasionally lag a very recent Rust update — if a
name is ever unrecognized, RustPlusHelper shows the raw numeric ID rather than guessing.

## Devices and cameras

Rust+ has no way to list your devices or cameras automatically — you always pair or add them
yourself, once per server.

**Smart devices:** click **Listen for device pairing**, then pair a Smart Switch, Smart Alarm, or
Storage Monitor from Rust the same way you would with the official app. Switches support **Toggle**
and a 3-second **Strobe**; Storage Monitors show live capacity and contents; Alarms are read-only —
RustPlusHelper shows a desktop notification when one triggers (see Notifications below), but Rust+
does not expose a way to arm/disarm one remotely. Once you've paired at least one alarm, a **Recent
alarm activity** list appears below your devices showing every trigger — this covers all of your
paired alarms together, since Rust+ doesn't tell the app which specific alarm triggered beyond its
in-game name.

**Cameras:** enter a camera's in-game code (shown on its computer station) and a nickname, then
**Add camera**. Click **View** to watch its live feed. Only the controls that camera actually supports
appear — Zoom for PTZ cameras, Shoot/Reload for auto-turrets, directional look nudges for cameras that
can pan, and forward/back/strafe/ascend/descend for drones. For cameras that can pan, you can also
click and drag the video itself to look around continuously instead of clicking the nudge buttons
repeatedly. For drones, holding **W/A/S/D** flies forward/left/back/right and **E**/**Q** ascends/
descends for as long as the key is held (one direction at a time — it does not combine into diagonal
movement). **Stop viewing** releases the camera so someone else (or you, on another camera) can use
it — Rust+ only allows one active subscription per connection.

## Settings

- **Start with Windows** launches RustPlusHelper minimized to the tray when you sign in.
- Each notification category (connection, team, markers, vending, Smart Alarms) has its own toggle,
  so you can, for example, keep alarm notifications on while muting vending price chatter.
- **Play a sound** plays the Windows system asterisk sound alongside every notification from an
  enabled category above; turn it off to keep notifications silent.
- **Quiet hours** suppresses the toast/sound during a time window you set (e.g. overnight) — the
  underlying event is still recorded to the Events history either way, only the desktop notification
  is held back.

## Staying minimized

Closing the main window does not exit RustPlusHelper — it minimizes to a tray icon next to your clock,
and background monitoring (team, markers, alarms) keeps running. Double-click the tray icon, or
right-click it and choose **Open RustPlusHelper**, to bring the window back. Choose **Exit** from that
same menu to actually close the app.

## Diagnostics

The **Diagnostics** page runs a few local health checks — whether the WebView2 runtime is present,
whether the local database is intact and up to date, and whether Windows DPAPI can protect secrets for
your account — and shows OK/ISSUE for each.

**Export diagnostics** saves a `.zip` you can attach when asking for help. It intentionally leaves out
anything sensitive: no server addresses, no player IDs, no tokens, and never the database file itself
— just the health-check results, app/OS version, a list of your saved servers by name and connection
type only, and the app's own local log files (which are scrubbed of tokens before they're even written
to disk). See [local-storage.md](local-storage.md) for exactly what is and isn't included.

## Troubleshooting

- **A server won't leave "NOT PAIRED":** automatic pairing wasn't completed for that server, or the
  saved token is missing. Try manual entry, or repeat pairing from the Servers page.
- **Test connection fails:** the result message tells you which stage failed — connecting, pairing,
  or authentication. A rejected authentication usually means the saved player token no longer matches
  what the server expects; re-pair to refresh it.
- **The Diagnostics page shows an ISSUE:** the detail text explains what's wrong (for example, a
  missing WebView2 runtime, or a database integrity problem). Export diagnostics and include the zip
  when asking for help.
- **Removing the app:** if you installed the `.msi`, uninstall it the normal Windows way (Settings →
  Apps, or Control Panel → Programs and Features). That removes the installed program files but
  deliberately leaves your saved data behind. If you also want to remove your saved servers and
  history, separately delete the `RustPlusHelper` folder under your Windows user's local application
  data — back it up first if you might want it back later. If you're running a development build
  instead, "uninstalling" just means deleting the build output (and, again, that same data folder if
  you want a clean slate).
