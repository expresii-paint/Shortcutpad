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

The live config is **per user, not next to the exe**:

| OS | Path |
|---|---|
| Windows | `%APPDATA%\ShortcutPad\shortcut_pad.json` |
| Linux | `$XDG_CONFIG_HOME/ShortcutPad/shortcut_pad.json` (~/.config if unset) |
| macOS | `~/Library/Application Support/ShortcutPad/shortcut_pad.json` |

It is created on first run and every change is written back, so settings
survive a rebuild or a reinstall. Earlier versions kept it in the build output
folder, which `dotnet publish` wipes — that lost every setting on each rebuild.
An existing `shortcut_pad.json` next to the executable is migrated to the
per-user location on first run. Edit the file and the pad rebuilds itself
within a second; the Options window shows the path it is using.

## Features

- **Always on top**, tray icon (click to hide/show), right-click menu.
- **Touch-friendly options**: a **☰ Menu** button sits at the pad's bottom-right
  and opens an **Options window** with large buttons (auto flow, resizable,
  show/hide, reload, edit config, edit buttons, opacity, docking, exit). This
  exists because right-click is awkward on a touch screen, and because Windows
  11 keeps new tray icons in the hidden-icons overflow.
  A popup menu was tried first and does not work: opening the pad's
  `ContextMenu` sets `IsOpen = true` but nothing appears on screen (checked
  with `alwaysOnTop` both on and off), whereas the editor's ComboBox dropdown
  renders normally. So the options are a real window rather than a popup, and
  right-click on the pad currently shows nothing — use the ☰ Menu button.
- **Sends keystrokes to the app you were in** — it tracks the foreground window
  and hands focus back before sending, so your workflow is not interrupted.
- **Touch-sized buttons**: the target is 100 x 62 DIPs per button (well above
  the 44/48 pt minimums), with grid rows set to `Star`. The target is yours to
  set: **☰ Menu → Button size** offers Compact / Default / Large / XL plus width
  and height boxes (32–400 DIPs) that apply as you type. Auto flow divides the
  window by that target, so bigger buttons mean fewer, larger targets.
- **Resizable**: drag any edge or corner. The window remembers its size. A
  utility pad has no useful maximized state, so `IPadPlatform.SetMaximizeButton`
  asks the platform to hide the maximize control (Avalonia has no cross-platform
  `CanMaximize`): it drops `WS_MAXIMIZEBOX` on Windows (leaving `WS_THICKFRAME`),
  hides the zoom button on macOS and removes the two maximize actions from
  `_NET_WM_ALLOWED_ACTIONS` on X11. **Caveat, Windows 11**: the caption buttons
  are drawn by the DWM there and ignore `WS_MAXIMIZEBOX`, so the style bit is
  cleared but the glyph is still drawn and still works. Removing it properly
  needs either a custom title bar or opting out of DWM non-client rendering
  (which changes the caption's look).
- **Auto flow** (`"autoFlow": true`, also a menu/tray toggle): the column and
  row counts are derived from the window's actual size, so the window stays
  freely resizable and the buttons keep the target size — whatever does not fit
  is left out rather than squeezed, and the status line reports
  `cols x rows (shown of total)`. With auto flow off, `"columns"` is fixed and
  every button stays visible.
- **Icons for common actions** (Material Design Icons, Apache-2.0, embedded as
  vector paths — nothing binary ships): copy, paste, cut, undo, redo, save,
  select-all, find, enter, esc, tab, backspace, home, delete, task manager,
  volume +/-/mute, play-pause, next/prev, desktop, calculator. The icon is
  inferred from the action; a button with no known action falls back to a large
  (20 pt semibold) glanceable label.
- **Semi-transparent**: `"opacity": 0.1 … 1.0` in the config, or the menu
  (100 / 85 / 70 / 50 %).
- **Docks to a screen edge**: `"dock": "left" | "right" | "none"`. While you are
  dragging the pad, releasing within 24 px of the left/right edge snaps it there
  and pins it; the pad then spans the full work-area height. The snap only
  applies to a window being dragged — restoring a saved position on startup
  never docks, so a window parked near an edge keeps its size and place.
- Buttons are hot-reloadable: edit the JSON and the pad rebuilds live.
- **GUI editor**: right-click the pad (or the tray icon) → **Edit buttons…**, or
  run `ShortcutPad --edit-buttons`. Add, delete, reorder and edit every field —
  label, action, colours, icon, sizes, corner radius and spans — then Save. Rows
  can be reordered by **dragging them** in the list (a drop target is
  highlighted) as well as with the Move up / Move down buttons. The editor works
  on a copy, so nothing changes until you press Save; Revert re-reads the file.
  Editing the JSON by hand still works.
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
  "height": 400,
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

To edit them without touching JSON: tap **☰ Menu** on the pad → **Edit buttons…**
(or right-click the pad, use the tray menu, or run
`ShortcutPad --edit-buttons`). The editor lists the
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

Auto flow derives the columns from the available width
(`floor((width - margin) / buttonWidth)`) and the rows from the available
height, so the window stays freely resizable and a button is never narrower or
shorter than the target. **Buttons that do not fit are left out** rather than
squeezed, and the status line reports `cols x rows (shown of total)` when that
happens. Auto flow sets only a small minimum size, so the pad can be resized
down to a single button. With `"autoFlow": false` the grid keeps the configured
column count, every button stays visible, and the window's minimum size grows
with the row count so the buttons keep their target size.

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

**No console window:** the app is a GUI (`WinExe`) build. A published exe is
always subsystem `windows`, so double-clicking it opens no console. A plain
`dotnet build` on Windows is given a *console-subsystem* apphost by the .NET
SDK, which would pop a cmd window; the project therefore pins
`RuntimeIdentifier=win-x64` for RID-less builds so `dotnet build`,
`dotnet run` and an IDE start button all produce a GUI exe too. (macOS and
Linux have no PE subsystem, so they are left alone.)

**Icon:** `src/ShortcutPad.App/app.ico` is the single source of truth.
`ApplicationIcon` in the project file embeds it in the exe (Explorer, taskbar,
alt-tab), and the same file is an `EmbeddedResource` that `AppIcon` loads for
the window title bars and the tray icon — so all three show the same art.
`icon.png` is the 256px master. To change the mark, edit
`tools/gen_icon.py` (it draws the tile, the 2x2 key grid and the return arrow)
and run `python tools/gen_icon.py` from that directory, which rewrites both
files.

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

| Window (DIP) | Grid | Buttons shown |
|---|---|---|
| 526 x 400 (default) | 5 x 5 | 25 of 25 |
| 526 x 381 | 5 x 4 | 20 of 25 |
| 400 x 300 | 3 x 3 | 9 of 25 |
| 300 x 250 | 2 x 2 | 4 of 25 |
| 200 x 200 | 1 x 1 | 1 of 25 |
| 900 x 620 | 8 x 8 | 25 of 25 |

With auto flow on, the window is freely resizable: the grid follows the space
it is given and the buttons that no longer fit are left out (the status line
shows `cols x rows (shown of total)`), so a button is never squeezed below the
target size. Verified that a hidden button is genuinely absent from the
accessibility tree (`BUTTON 'Hello' NOT FOUND` at 300 x 250) while a visible
one still works (`CLICK OK`). The rows are `Star`, so the buttons fill the
height they are given. With `"autoFlow": false` the grid stays 5 x 5 at every
window size tested (1200 x 700 down to 200 x 200) with all 25 buttons present,
and the window's minimum size keeps them at the target size.

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

The list now also keeps its items in an `ObservableCollection` that is assigned
to `ItemsSource` once and mutated in place. Reassigning `ItemsSource` — even to
a fresh list — makes the item containers briefly hold a null item, which is what
actually triggered the crash when Add/Delete/Move rebuilt the list.
`PadButton` implements `INotifyPropertyChanged` and exposes a `Description`
property, so the list text follows edits live. Move up/down is done with
Remove + Insert rather than `ObservableCollection.Move`, because the
virtualizing panel ignores Move notifications and the list looked unchanged.

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
