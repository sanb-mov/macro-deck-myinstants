# Macro Deck — legacy client (Android 4.2.2 / API 17)

A native Java client for Macro Deck, written for devices the main app cannot
reach. It talks the same protocol 2 as the Ionic client and installs on
Android 4.2.2 and anything newer.

## Why this exists instead of a build flag

The Ionic/Angular/Capacitor app in the repository root cannot be made to run on
Android 4.2.2. Three separate blockers, each fatal on its own:

| Blocker | Detail |
| --- | --- |
| Capacitor | `@capacitor/android` 7.2.0 declares `minSdk 23`. No Capacitor release ever went below API 21. |
| AndroidX | `appcompat` 1.6.1, `core` 1.10 and `core-splashscreen` all require API 19+/21+. |
| WebView | Android 4.2's WebView is the pre-Chromium WebKit engine (Chromium arrived in 4.4). No ES6, no `Promise`, no CSS custom properties, no Custom Elements / Shadow DOM — and **no WebSocket**, which is the client's only transport. |

Lowering `minSdkVersion` produces an APK that installs and then immediately
fails. So this module drops the web layer entirely and speaks the protocol from
Java.

## What it does

- Saved connections (host, port, SSL, token, name), persisted in `SharedPreferences`
- One connection can be marked auto-connect; it fires once per app launch
- Deck grid rendered natively, matching the web client's geometry
- Press, release, long press and long-press release, forwarded as
  `BUTTON_PRESS` / `BUTTON_RELEASE` / `BUTTON_LONG_PRESS` / `BUTTON_LONG_PRESS_RELEASE`
- Live updates via `GET_CONFIG`, `GET_BUTTONS`, `UPDATE_BUTTON`, `UPDATE_LABEL`
- Automatic reconnection every 3 s after an unclean drop
- Settings: long-press delay, skip SSL validation, keep screen on

## What it deliberately does not do

Out of scope for this build (present in the Ionic client): QR quick-setup
scanning, USB/ADB connections, forced screen orientation, appearance themes,
the button border style option, and the diagnostics/ping screens.

## Dependencies

None. No AndroidX, no support library, no WebSocket library.

The WebSocket client in `Ws.java` is a hand-written RFC 6455 implementation
(~450 lines: handshake, masking, fragmentation, ping/pong, close). Every
maintained Java WebSocket library now compiles against Java 8 APIs
(`java.util.function`, `java.time`) that Android 4.2 cannot load, so pulling one
in would have re-introduced the very problem this module exists to avoid.
Everything else — `org.json`, `android.util.Base64`, `javax.net.ssl` — ships
with Android 4.2 itself.

## Building

Requires a JDK 21 and an Android SDK with platform 35 and build-tools 35.
Create a `local.properties` next to `build.gradle` that points at the SDK
(`sdk.dir=/path/to/Android/Sdk`).

```sh
cd android-legacy
./gradlew assembleDebug
```

The APK lands in `app/build/outputs/apk/debug/app-debug.apk` (~70 KB).

On a fresh machine, install the SDK first:

```sh
sdkmanager --licenses
sdkmanager "platform-tools" "platforms;android-35" "build-tools;35.0.0"
echo "sdk.dir=C:/Users/you/Android/Sdk" > android-legacy/local.properties
```

Use forward slashes in `local.properties`: it is a Java properties file, so a
Windows path with single backslashes fails with a confusing
"the filename or directory name syntax is incorrect" error.

### Installing on the device

```sh
adb install -r app/build/outputs/apk/debug/app-debug.apk
```

Or copy the APK to the device and open it with "Unknown sources" enabled in
Settings -> Security.

### Signing

Android 4.2 only understands the v1 (jar) signature scheme. `app/build.gradle`
pins `enableV1Signing true` for exactly that reason — a v2-only APK will refuse
to install. For a release build, add a `signingConfigs.release` block with your
keystore and the same `enableV1Signing true`.

### About `targetSdk 17`

Deliberate. It keeps the install-time permission model (Android 4.2 has no
runtime permission dialogs) and the legacy window behaviour. The APK is meant
for sideloading; Google Play would reject this target level.

## Notes and known limits

- **Animated icons render as a still frame.** `BitmapFactory` decodes only the
  first frame of a GIF. Supporting animation would mean a frame decoder, which
  the old platform does not provide.
- **`ButtonBackground: false` hides the background fill entirely**, empty cells
  included. The Ionic client receives this flag but never applies it, so there
  was no existing behaviour to copy; this is the plain reading of the flag.
  If your host sets it and you want the web client's look instead, delete the
  `if (client.buttonBackground)` guard in `DeckView.drawButton`.
- **Single-touch only**, matching the web client's `pointerdown` handling. Two
  simultaneous button presses are not sent.
- **`wss://` is fragile on this platform.** API 17 supports TLS 1.1/1.2 but
  disables them by default, so `Ws.openSocket` enables every TLS protocol the
  device reports. The cipher suites a 2013 device offers are still old, and a
  modern host may refuse the handshake outright. Plain `ws://` on a trusted LAN
  is the reliable path. Self-signed host certificates need
  Settings → "Skip SSL certificate validation".
- **Heap pressure.** Frame payloads are capped at 16 MB. A deck of large icons
  on a 512 MB device can still hit the heap limit; a button whose image fails to
  decode renders without it rather than crashing the app.
- **Lint flags `TrulyRandom`** on the `SecureRandom` used for WebSocket frame
  masking. That is the 2013 Android PRNG seeding bug, real on 4.3 and older. The
  mask exists to stop proxy cache poisoning, which is not a threat model for a
  deck on a trusted LAN, so it is accepted rather than worked around.
- `Device-Type` is reported as `Web`, the same value the official client sends,
  because hosts key behaviour off that string.

## Layout of the code

| File | Role |
| --- | --- |
| `Ws.java` | RFC 6455 WebSocket client |
| `MdClient.java` | Protocol 2 state machine |
| `MdButton.java` | One button: colour, decoded icon/label bitmaps |
| `DeckView.java` | The grid, drawn and touch-handled on one canvas |
| `DeckActivity.java` | Deck screen, status overlay, reconnection |
| `MainActivity.java` | Connection list, add/edit/delete, settings |
| `Conn.java` | Saved connection model |
| `Store.java` | SharedPreferences persistence |
