# Shortcut Pad

An always-on-top on-screen button pad that sends real keystrokes (or types text,
or runs a program) to whatever app you were using. One C# codebase for
**Windows, macOS and Linux (X11)**, built on Avalonia.

```
src/ShortcutPad.App/     the app (Avalonia, net9.0 — no platform TFM)
probe/                   verification harness (clicks real buttons, captures a
                         low-level keyboard hook, reports what actually arrived)
tools/                   fetch_nuget.py (offline feed), build-macos.sh
local-feed/              Avalonia packages for offline restore on this PC
legacy-winforms-src/     the previous WinForms build (172 KB exe, Windows only)
dist-avalonia/           Windows build (framework-dependent)
dist-linux/              Linux build (self-contained, X11)
```

## Run (Windows)

```
dist-avalonia\ShortcutPad.exe
```

Needs the .NET 9 Desktop Runtime (already installed on this machine).
`shortcut_pad.json` sits next to the exe — edit it and the pad rebuilds itself
within a second.

## Features

- **Always on top**, tray icon (double-click to hide/show), right-click menu.
- **Sends keystrokes to the app you were in** — it tracks the foreground window
  and hands focus back before sending, so your workflow is not interrupted.
- **Touch-sized buttons**: each is about 100 x 62 DIPs (well above the 44/48 pt
  minimums), with the grid rows set to `Star` so a docked pad stretches its
  buttons over the whole work-area height.
- **Icons for common actions** (Material Design Icons, Apache-2.0, embedded as
  vector paths — nothing binary ships): copy, paste, cut, undo, redo, save,
  select-all, find, enter, esc, tab, backspace, home, delete, task manager,
  volume +/-/mute, play-pause, next/prev, desktop, calculator. The icon is
  inferred from the action; a button with no known action falls back to a large
  (20 pt semibold) glanceable label.
- **Semi-transparent**: `"opacity": 0.1 … 1.0` in the config, or the menu
  (100 / 85 / 70 / 50 %).
- **Docks to a screen edge**: `"dock": "left" | "right" | "none"`. While
  dragging, releasing within 24 px of the left/right edge snaps it there and
  pins it; the pad then spans the full work-area height.
- Buttons are hot-reloadable: edit the JSON and the pad rebuilds live.
- **Scriptable**: `ShortcutPad --send "ctrl+c"` / `ShortcutPad --type "text"`
  run headless (no window) — handy in scripts and for testing a platform's
  input path.

## Config

```json
{
  "columns": 5,
  "alwaysOnTop": true,
  "opacity": 0.85,
  "dock": "left",
  "x": 60, "y": 60,
  "buttons": [
    { "label": "Copy",     "keys": "ctrl+c",              "color": "#2d6cdf" },
    { "label": "Hello",    "text": "hello from pad",      "color": "#2e9e5b" },
    { "label": "Calc",     "run": "calc.exe" },
    { "label": "Vol +",    "keys": "volume_up" }
  ]
}
```

`color` is optional (white text is picked automatically for dark colors).
Note: a button with no `color` gets a light-grey brush — an explicit background
is required, otherwise Avalonia does not hit-test the button.

### Icons

`"icon"` is optional and overrides the automatic choice. Names are listed in
`src/ShortcutPad.App/Icons.cs` (`PadIcons.Names`); unknown names are ignored and
the action-based fallback applies:

| Action | Icon | Action | Icon |
|---|---|---|---|
| `ctrl+c` | content-copy | `ctrl+shift+esc` | view-dashboard |
| `ctrl+v` | content-paste | `volume_down` | volume-minus |
| `ctrl+x` | content-cut | `volume_mute` | volume-mute |
| `ctrl+z` | undo | `volume_up` | volume-plus |
| `ctrl+y`, `ctrl+shift+z` | redo | `media_play_pause` | play-pause |
| `ctrl+s` | content-save | `media_next` | skip-next |
| `ctrl+a` | select-all | `media_prev` | skip-previous |
| `ctrl+f` / `ctrl+h` | magnify | `win+d` | monitor |
| `enter` | keyboard-return | `run` containing `calc` | calculator |
| `esc` | keyboard-esc | `tab` | keyboard-tab |
| `backspace` | backspace | `delete` | backspace-outline |
| `home` | home | | |

### Key syntax

`+`-separated tokens. Modifiers: `ctrl`, `alt`, `shift`, `win` (`cmd` is an
alias). Named keys: `enter` `esc` `tab` `space` `backspace` `delete` `insert`
`home` `end` `pageup` `pagedown` `left` `up` `right` `down` `printscreen`
`f1`…`f12`, plus media keys `volume_up` `volume_down` `volume_mute`
`media_play_pause` `media_next` `media_prev` `media_stop`. Anything else must
be a single character.

Examples: `ctrl+s`, `ctrl+alt+delete`, `shift+tab`, `win+left`, `ctrl+shift+t`.

## Build (Windows, this PC)

`dotnet.exe` here is denied outbound TCP, so restore uses a local folder feed
that `tools/fetch_nuget.py` populates (curl/python do have network access):

```
python tools/fetch_nuget.py "Avalonia@11.2.3" "Avalonia.Desktop@11.2.3" \
  "Avalonia.Themes.Fluent@11.2.3" "Avalonia.Fonts.Inter@11.2.3"
dotnet publish src/ShortcutPad.App/ShortcutPad.App.csproj -c Release \
  -r win-x64 --self-contained false -p:PublishSingleFile=true \
  -p:NuGetAudit=false -o dist-avalonia
```

`NuGet.config` (repo root) points at that feed; `NuGet.config.macOS` is the
normal nuget.org config for machines with network access.

## Build (macOS)

On the Mac:

```
cp NuGet.config.macOS NuGet.config    # or delete NuGet.config
bash tools/build-macos.sh             # -> dist-mac/ShortcutPad.app
open dist-mac/ShortcutPad.app
```

The first run is blocked by macOS until you grant **Privacy & Security →
Accessibility** — the same prompt every hotkey utility gets. Without it
`CGEventPost` silently does nothing.

## Build (Linux, X11)

Self-contained, so the target only needs the X11 client libraries (libX11,
libXtst — `libxtst6` on Debian/Ubuntu):

```
dotnet publish src/ShortcutPad.App/ShortcutPad.App.csproj -c Release \
  -r linux-x64 --self-contained true -p:PublishSingleFile=true \
  -p:NuGetAudit=false -o dist-linux
./dist-linux/ShortcutPad
```

The offline feed also needs the runtime packs (`microsoft.netcore.app.runtime.linux-x64`
and `microsoft.netcore.app.host.linux-x64`, 9.0.1) plus
`microsoft.aspnetcore.app.runtime.linux-x64` — `tools/fetch_nuget.py` can pull
them, or copy them into `local-feed/` by hand.

**X11 only for injection.** `KeystrokeSender.Linux.cs` uses XTest, which talks to
the X server, so it works on X11 sessions and under XWayland — but not on a
native Wayland session, which has no generic injection API (that case needs a
virtual input device via `/dev/uinput`). Focus tracking uses EWMH
`_NET_ACTIVE_WINDOW` / `_NET_WM_PID`; a Wayland compositor exposes neither, so
the "hand focus back" half degrades there. Docking also relies on the client
being allowed to position itself, which Wayland forbids.

## How it works

| Area | Windows | macOS | Linux (X11) |
|------|---------|-------|-------------|
| UI | Avalonia (one codebase, `net9.0`) | same | same |
| Keystrokes | `user32!SendInput` (`KeystrokeSender.Windows.cs`) | `CoreGraphics` `CGEvent` + flags (`KeystrokeSender.macOS.cs`) | X11/XTest, keysym → keycode (`KeystrokeSender.Linux.cs`) |
| Text | Unicode `SendInput` | `CGEventKeyboardSetUnicodeString` | per-character keysym + Shift (Latin-1; other scripts need a layout that maps them) |
| "Restore previous app" | `GetForegroundWindow` / `SetForegroundWindow` | `NSWorkspace.activeApplication` / `NSRunningApplication.activateWithOptions:` | `_NET_ACTIVE_WINDOW` + `_NET_WM_PID`, EWMH client message |

The pad-neutral parts (config, key-spec parsing, grid, icons, tray, docking,
foreground tracking) are platform-independent; `IKeystrokeSender` and
`IPadPlatform` are the two seams.

## Verification

`probe/` proves the pad really delivers input: it opens a target text box,
installs a low-level keyboard hook, clicks a real pad button (located through
UI Automation), and reports the captured key events plus the text that landed.

```
cd probe && dotnet build -c Release
./bin/Release/net9.0-windows/probe.exe --pad "Shortcut Pad" --name Hello
./bin/Release/net9.0-windows/probe.exe --pad "Shortcut Pad" --name Copy
./bin/Release/net9.0-windows/probe.exe --pad "Shortcut Pad" --name Calc
```

Verified on this machine (Windows): `Hello` types into the target, `Copy`
produces exactly `DOWN ctrl, DOWN c, UP c, UP ctrl`, `Calc` launches the
calculator, editing the JSON renames a button live, `opacity` makes the pad
see-through, and `dock` snaps it flush to the screen edge at full work-area
height. The button grid is 5 x 5 buttons of ~92 x 54 DIPs each; 24 of the 25
default buttons draw their icon (checked visually and by sampling the rendered
pixels), and the one action without an icon (`Hello`) renders at 20 pt.

Verified on Linux (WSL Ubuntu 24.04 under WSLg's X11 display): the self-contained
build runs, the window appears, the config hot-reloads, `dock: right`/`left`
moves it flush to the edge at full height, and the icon set renders.

**Not verified, and why:** the actual keystroke *injection* on Linux. WSLg
advertises the XTEST extension and `XTestFakeKeyEvent` returns success, but the
events never reach any client (a control test using `XSendEvent` *did* arrive, so
the listener and the focus were fine). That is a WSLg limitation, not a pad bug
— XTest is the mechanism `xdotool` uses on a normal X11 desktop — but it means
the Linux send path needs a run on real X11 hardware before it can be called
proven. The keysym → keycode mapping it relies on was verified directly
(`c`→54, `Return`→36, `Escape`→9, `Control_L`→37, `Shift_L`→50 on the WSL
keyboard layout).
