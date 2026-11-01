# Mario Kart World Save File Editor (Experimental)

An **experimental Mario Kart World (MKW) save file editor** for Windows, built with C#, .NET 8, and WinUI 3.

## Current features

- **Character unlocking:** view and edit character/outfit unlocks in supported save files.
- Search and bulk selection controls.
- Open a save file manually, or click **Find Eden save** to search common Eden emulator save locations.
- Confirmation before writing changes and a `.bak` backup when saving.

## Planned features

Support for **karts and other unlockables** will hopefully be added in future updates. These features are **not implemented yet**.

## Supported saves and limitations

- **Eden emulator `account.sav`** (850,928 bytes): character unlock editing using identified offsets.
- **Nintendo Switch `account.sav`** (864,156 bytes): experimental support only. Driver IDs may be appended, but ownership metadata is not fully understood, so changes may not unlock anything in-game. Removal is disabled.
- Grand Prix trophies, karts, and other unlockables are not currently supported.

**Warning:** This is experimental software. Back up your save before editing.
I WILL NOT BE HELD RESPONSIBLE IF YOU BREAK YOUR OWN SAVES!

## Build on Windows

Requires **.NET 8 SDK** and **Visual Studio 2022** with Windows App SDK/packaging build tools. From Visual Studio Developer PowerShell, run:

```powershell
msbuild MKW.WinUI.csproj /restore /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:WindowsAppSDKSelfContained=true
```

You can also try `build_windows.bat`. Keep the resulting application files together.

## How to use

1. Launch the editor. It starts without loading a save.
2. Click **Find Eden save** to locate an Eden save, or **Open save…** to browse manually.
3. Select the characters/outfits you want to unlock.
4. Click **Save** and confirm to write changes.