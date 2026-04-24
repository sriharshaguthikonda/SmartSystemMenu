# SmartSystemMenu — Agent / Contributor Guide

## Active work

- **Open issues:** see [`TODO.md`](./TODO.md) for P0/P1-sorted index, or [GitHub Issues](https://github.com/sriharshaguthikonda/SmartSystemMenu/issues).
- **Working branch:** `codex/window-target-hide-picker` (non-default; default is `master`).

---


## Architecture

Two components ship together:

| Component | Language | Output |
|---|---|---|
| `SmartSystemMenu/` | C# (.NET 4.8, WinForms) | `SmartSystemMenu.exe` (x86) / `SmartSystemMenu64.exe` (x64) |
| `SmartSystemMenuHook/` | C++ (Win32 DLL) | `SmartSystemMenuHook.dll` (x86) / `SmartSystemMenuHook64.dll` (x64) |

The hook DLL is injected into target processes to add the custom system-menu items. The C# exe hosts the tray icon, settings UI, and hot-key handling.

Runtime data files (must live next to the exe):
- `Language.xml` — all UI strings, keyed by language code
- `SmartSystemMenu.xml` — user settings (written on save)
- `Window.xml` / `Window64.xml` — per-window state

## Prerequisites

- **Visual Studio 2022** with:
  - `.NET desktop development` workload (C# + WinForms)
  - `Desktop development with C++` workload (for the hook DLL)
- **MSBuild** (included with VS)

## Quick build — IDE

1. Open `SmartSystemMenu.sln` (or the `.code-workspace` in `Application/`)
2. Select configuration `Release | x86` or `Release | x64`
3. Build → output goes to `SmartSystemMenu/bin/x86|x64/Release/`

When running from the IDE the exe directory must contain `Language.xml`. The csproj has `CopyToOutputDirectory=PreserveNewest` for it, so MSBuild copies it automatically.

## Full distribution build

Produces a ready-to-run `Application/` folder (both architectures + both DLLs):

```bat
cd Build
Build.cmd
```

`Build.cmd` calls `VsDevCmd.bat` (hard-coded to VS 2022 Community) then runs `Build.xml` which:
1. Builds hook DLL x86 → copies to `Application/SmartSystemMenuHook.dll`
2. Builds hook DLL x64 → copies to `Application/SmartSystemMenuHook64.dll`
3. Builds C# exe x64 → copies to `Application/SmartSystemMenu64.exe` (embedded as resource placeholder first)
4. Builds C# exe x86 → copies to `Application/SmartSystemMenu.exe`
5. Copies `SmartSystemMenu.xml` and `Language.xml` into `Application/`

If VS is installed to a different path, edit line 3 of `Build/Build.cmd`.

## Adding new UI strings

All UI text comes from `Language.xml`. `GetValue(key)` returns `""` for missing keys (blank labels). Steps:

1. Add `<item name="your_key" value="English text" />` inside the `<en>` block
2. Optionally add translations to other language blocks
3. Reference via `settings.Language.GetValue("your_key")` in C#

New dynamically-created controls can use the `GetLanguageText("key", "fallback")` helper in `ApplicationSettingsForm.cs` instead of `GetValue` to avoid blank labels when a language is incomplete.

## Common issues

| Symptom | Cause | Fix |
|---|---|---|
| All settings dialog labels blank | `Language.xml` missing from exe directory | Rebuild (csproj copies it), or manually copy from `SmartSystemMenu/` |
| Hook not injecting | Wrong DLL bitness next to exe | Run x86 exe with `SmartSystemMenuHook.dll` (x86), or x64 exe with `SmartSystemMenuHook64.dll` |
| Dark theme text invisible | Theme applied before controls have text set | `ThemeUtils.ApplyTheme` must be called after `InitializeControls` — keep that order |
