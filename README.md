# Shortcut Pad

An always-on-top on-screen button pad that sends real keystrokes (or types text,
or runs a program) to whatever app you were using. One C# codebase for
**Windows and macOS**, built on Avalonia.

```
src/ShortcutPad.App/     the app (Avalonia, net9.0 — no platform TFM)
probe/                   verification harness (clicks real buttons, captures a
                         low-level keyboard hook, reports what actually arrived)
tools/                   fetch_nuget.py (offline feed), build-macos.sh
local-feed/              Avalonia packages for offline restore on this PC
legacy-winforms-src/     the previous WinForms build (172 KB exe, Windows only)
dist-avalonia/           current Windows build
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
- **Semi-transparent**: `"opacity": 0.1 … 1.0` in the config, or the menu
  (100 / 85 / 70 / 50 %).
- **Docks to a screen edge**: `"dock": "left" | "right" | "none"`. While
  dragging, releasing within 24 px of the left/right edge snaps it there and
  pins it; the pad then spans the full work-area height.
- Buttons are hot-reloadable: edit the JSON and the pad rebuilds live.

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

## How it works

| Area | Windows | macOS |
|------|---------|-------|
| UI | Avalonia (one codebase, `net9.0`) | same |
| Keystrokes | `user32!SendInput` (`KeystrokeSender.Windows.cs`) | `CoreGraphics` `CGEvent` + flags (`KeystrokeSender.macOS.cs`) |
| Text | Unicode `SendInput` | `CGEventKeyboardSetUnicodeString` |
| "Restore previous app" | `GetForegroundWindow` / `SetForegroundWindow` | `NSWorkspace.activeApplication` / `NSRunningApplication.activateWithOptions:` |

The pad-neutral parts (config, key-spec parsing, grid, tray, docking,
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

Verified on this machine: `Hello` types into the target, `Copy` produces exactly
`DOWN ctrl, DOWN c, UP c, UP ctrl`, `Calc` launches the calculator, editing the
JSON renames a button live, `opacity` makes the pad see-through, and `dock`
snaps it flush to the screen edge at full work-area height.
