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
- **Touch-sized buttons**: the target is 100 x 62 DIPs per button (well above
  the 44/48 pt minimums), with grid rows set to `Star`.
- **Resizable**: drag any edge or corner. The window remembers its size.
- **Auto flow** (`"autoFlow": true`, also a menu/tray toggle): the column and
  row counts follow the window, so the buttons keep the target size instead of
  being squeezed. Measured on this machine: 900 px wide → 8 x 4, 420 px →
  3 x 9, 1600 px → 15 x 2, with button widths always at or above the target.
  With auto flow off, `"columns"` is fixed and resizing just stretches the
  buttons.
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
- **GUI editor**: right-click the pad (or the tray icon) → **Edit buttons…**, or
  run `ShortcutPad --edit-buttons`. Add, delete, reorder and edit every field —
  label, action, colours, icon, sizes, corner radius and spans — then Save. The
  editor works on a copy, so nothing changes until you press Save; Revert
  re-reads the file. Editing the JSON by hand still works.
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
  "resizable": true,
  "autoFlow": true,
  "buttonWidth": 100,
  "buttonHeight": 62,
  "width": 526,
  "height": 381,
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

### Customizing a button

Every button accepts these optional fields:
| Field | Meaning | Default |
|---|---|---|
| `label` | Text shown under the icon | — |
| `keys` / `text` / `run` | What the button does | — |
| `color` | Background colour | light grey |
| `foreground` | Text and icon colour (auto: white on dark, black on light) | auto |
| `icon` | Icon name from `Icons.cs` | inferred from the action |
| `iconSize` | Icon size in DIPs | 30 |
| `fontSize` | Caption size | 13 with an icon, 20 without |
| `cornerRadius` | Corner radius in DIPs | theme (4) |
| `spanX` / `spanY` | How many grid cells the button occupies | 1 / 1 |

```json
{ "label": "Select all", "keys": "ctrl+a", "spanX": 2 },
{ "label": "Big save",   "keys": "ctrl+s", "color": "#1f6f4a",
  "foreground": "#ffffff", "iconSize": 48, "fontSize": 18, "cornerRadius": 16 }
```

Spans are packed left-to-right, top-to-bottom, so a wide button pushes the
buttons after it to the next free space; the row count grows to fit. A span is
clamped to the grid, and `spanX`/`spanY` below 1 are treated as 1.

To edit them without touching JSON: right-click the pad → **Edit buttons…**
(also in the tray menu, or `ShortcutPad --edit-buttons`). The editor lists the
buttons on the left and edits the selected one on the right — label, which of
keys/text/run it performs, background and foreground colour (with live
swatches), icon, icon size, font size, corner radius and spans — with Move
up/down, Add and Delete. It edits a copy of the config: **Save** writes
`shortcut_pad.json` and the pad hot-reloads it, **Revert** discards the changes
and re-reads the file.

### Layout

| Field | Meaning |
|---|---|
| `resizable` | Window can be resized (forced off while docked). Default `true`. |
| `autoFlow` | Column/row counts follow the window size. Default `true`. |
| `buttonWidth` / `buttonHeight` | Target button size in DIPs used by auto flow. |
| `width` / `height` | Remembered window size in DIPs (auto flow). |
| `columns` | Fixed column count, used when `autoFlow` is `false`. |

Auto flow derives the columns from the width (`floor(width / buttonWidth)`)
and the rows from the button count, so a button is never narrower than the
target. The window's minimum height then grows with the row count — narrowing
the pad makes it taller rather than squeezing the buttons. One column is always
allowed as the minimum width.

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
./bin/Release/net9.0-windows/probe.exe --layout      # grid + every button rect
./bin/Release/net9.0-windows/probe.exe --shot pad.png
```

Verified on this machine (Windows): `Hello` types into the target, `Copy`
produces exactly `DOWN ctrl, DOWN c, UP c, UP ctrl`, `Calc` launches the
calculator, editing the JSON renames a button live, `opacity` makes the pad
see-through, and `dock` snaps it flush to the screen edge at full work-area
height. The button grid is 5 x 5 buttons of ~92 x 54 DIPs each; 24 of the 25
default buttons draw their icon (checked visually and by sampling the rendered
pixels), and the one action without an icon (`Hello`) renders at 20 pt.

Resizing and auto flow were verified by driving the real window with
`SetWindowPos` and reading the resulting grid back through UI Automation
(`probe.exe --layout` dumps every button's on-screen rectangle):

| Window (DIP) | Grid | Button width |
|---|---|---|
| 526 x 381 | 5 x 5 | 92 |
| 900 x 620 | 8 x 4 | 101 |
| 420 x 900 | 3 x 9 | 123 |
| 1600 x 500 | 15 x 2 | 97 |

Button width never drops below the 100 DIP target (the small deficit at 526 is
the 4 DIP button margin); the height stretches with the window because the
rows are `Star`. Narrowing the pad makes it taller (the row count grows)
instead of squeezing the buttons. With `"autoFlow": false` the grid stays
5 x 5 at 1200 x 700 and at 480 x 500, and the buttons stretch.

Per-button customization was verified the same way, plus pixel sampling of a
DPI-aware capture: `spanX: 2` rendered a 346 px button (2 columns) and `spanY: 2`
a 218 px button (2 rows) in a 5-column grid with the remaining buttons packed
around them; `cornerRadius: 28` rounded the corner while `cornerRadius: 0` left
it square; `iconSize: 72` drew a 126 px glyph against 52 px for the default 30;
`fontSize: 34` vs `fontSize: 9` produced caption bands of 53 px vs 32 px; and
`foreground` overrode the automatic choice (amber glyphs on a grey button).

The GUI editor was driven through UI Automation, including the full field-edit
round trip: it opens with the first button loaded (label `Copy`, keys `ctrl+c`,
background `#2d6cdf`, icon `(auto)`), lists all 25 buttons with the selected one
highlighted, and shows the toolbar (Move up/down, Add, Delete) and Revert/Save.
The label field was rewritten to `CopyTest` via `ValuePattern.SetValue`, Save was
invoked, `shortcut_pad.json` was rewritten with `label: CopyTest`, the editor
closed, and the pad hot-reloaded the new label — all without desktop clicks.

The first editor build crashed after ~31 minutes: an unhandled
`NullReferenceException` in the ListBox item template. The virtualizing panel
builds a container for a stale index while the list is refreshed, so the item
handed to the template can be null; `Describe` is now null-safe.

The editor also exposed a latent bug: the app had **no theme at all** (no
`App.axaml`), so every control ran on the unstyled default template. The pad
looked fine because its buttons set their own backgrounds, but the editor's
`ListBox` had no template and rendered nothing — `GetVisualDescendants()` on it
returned 0. Adding `App.axaml` with `<FluentTheme />` fixed the list and gives
the whole app the standard Fluent look; the pad's grid, icons and colours were
re-verified afterwards (5 x 5, icons crisp, white glyphs on the dark buttons).

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
