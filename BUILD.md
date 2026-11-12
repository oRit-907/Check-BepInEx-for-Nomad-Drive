# Building Check BepInEx

The full source is in [`src/`](src). It's a .NET 8 WinForms app (C#). The website edition is published as one self-contained Windows exe; the Nexus edition as a plain .NET app (see the end).

## What you need

- Windows 10 or 11, x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

The NuGet packages restore by themselves on the first build: SharpCompress and System.Security.Cryptography.ProtectedData.

## Build

From the `src` folder:

```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publish
```

The app is written to `src/publish/NomadModDownloader.exe` (published as `CheckBepInEx-X.Y.Z.exe`). It's deliberately not compressed (about 160 MB): compressed single-file exes look "packed" to antivirus scanners and get flagged. The version number comes from `<Version>` in `src/NomadModDownloader.csproj`.

## What the app does on your PC

- It reads and writes only the Nomad Drive game folder you choose, and its own folder `%AppData%\NomadModDownloader`: settings, the list of installed files, backups and downloads.
- It doesn't install mods (the in-game Mods menu does). BepInEx is downloaded (website edition) from nomaddrivemods.online's "BepInEx for Nomad Drive" listing, or BepInEx's official GitHub releases, and only after you say yes.
- App updates (website edition) come from nomaddrivemods.online (3.2.0 on; before that, GitHub releases). Each download is checked against the site's SHA-256. An installed copy runs the new installer quietly; a copy run on its own swaps in the new exe with a small script, `apply-update.cmd`, after the app closes.

## Third-party files

- Noto Sans font: SIL Open Font License, see `src/Art/OFL.txt`.
- The logo and icon (a highway sign) were made for Nomad Drive Mods. No art from the game is used.

## Microsoft Store

The app isn't on the Microsoft Store any more: it's only released here, as the EXE. Copies installed from the Store
before (`StoreApp.IsPackaged`) don't update; the app and the in-game Mods menu point their players to the EXE.

## BepInEx for Nomad Drive packages

The "BepInEx 5 for Nomad Drive" package (nomaddrivemods.online and Nexus Mods) is built in the [NomadModDownloader](https://github.com/oRit-907/NomadModDownloader) repo (`bepinex-package/`), which has the source of every part and the build steps.

## Installer

From the `installer` folder, after the build above (needs [NSIS](https://nsis.sourceforge.io), on Windows or Linux):

```
makensis -DVERSION=3.2.0 -DAPP=../src/publish/NomadModDownloader.exe -DOUT=CheckBepInEx-Setup-3.2.0.exe CheckBepInEx.nsi
```

It installs for the current Windows user (no admin) into `%LOCALAPPDATA%\Programs\Check BepInEx`, with Start menu and desktop shortcuts and an uninstaller in Windows Settings › Apps. New versions are published on nomaddrivemods.online, not as GitHub releases.

## Nexus edition

The copy posted on Nexus Mods is built with:

```
dotnet publish src -c Release -r win-x64 -p:NexusEdition=true -p:DebugType=none -o publish
```

It's a plain .NET 8 app (CheckBepInEx.exe and CheckBepInEx.dll, needs the .NET 8 Desktop Runtime) instead of one
self-contained file. It never updates itself, never contacts nomaddrivemods.online and never touches link handlers in
the registry. BepInEx 5.4.23.6 (official, unchanged) is built in, so installing and repairing BepInEx downloads nothing.
The only download is Unity Compatibility's Unity libraries, from unity.bepinex.dev, after the player clicks Yes
(see `Edition.Nexus` in src/Core/Edition.cs and the places that check it).

