# Check BepInEx for Nomad Drive

A free Windows app for Nomad Drive that sets up the mod loader, BepInEx 5 or Ignite Loader, and keeps it working, by AkoRit. (Until 3.0 it was
**Nomad Mod Downloader**.) Mods are installed from the **Mods menu inside the game** (Nomad Mod Manager).

## What it does

- **Pick the mod loader** (3.4.0): at the top of the Check page, **BepInEx 5** or **Ignite Loader**
  ([nomaddrivemods.online/mod/20](https://nomaddrivemods.online/mod/20)), Nomad Drive's own loader that runs BepInEx 5
  and 6 mods and MelonLoader mods. Picking one sets it up (shown first, confirmed), and the checks, logs, settings and
  mod pages follow it. Only one loader starts: switching swaps winhttp.dll, doorstop_config.ini and .doorstop_version,
  and keeps the other loader's copies, so switching back needs no download and nothing is deleted.

- **Check BepInEx**: installs, repairs and updates BepInEx 5 set up for Nomad Drive, and finds and fixes everything that
  stops mods loading: the .NET fix, Unity Compatibility, BepInEx 6 support, a switched-off doorstop, blocked files,
  other mod loaders. **Fix all** shows exactly what will change first, and backs up anything it replaces.
- **Mods not loading**: every mod in `BepInEx\plugins`, whether it started at the last game start and, if not, why,
  with a one-click fix. Plus the errors and crashes from the game's logs, a live log, duplicates and clashes, and
  **Find the bad mod**.
- **Safety net**: backups and uninstall, game update watch, verify game files (Steam), **Export logs for mod devs**
  (one zip, Windows user name hidden).
- **Play** button, 12 languages.

## Two editions

| | Website edition | Nexus edition |
|---|---|---|
| Where | [nomaddrivemods.online](https://nomaddrivemods.online/index.php?p=bepinex) | Nexus Mods |
| Build | `dotnet publish src ...` (see [BUILD.md](BUILD.md)) | `dotnet publish src ... -p:NexusEdition=true` |
| Form | one self-contained exe (and an installer) | CheckBepInEx.exe + CheckBepInEx.dll, needs the .NET 8 Desktop Runtime |
| BepInEx | downloaded when installing (from the site, or GitHub) | BepInEx 5.4.23.6 built in, no download |
| Ignite Loader | its site edition, downloaded from the site (SHA-256 checked) | its Nexus edition (3.3.0) built in, no download |
| Updates | updates itself from nomaddrivemods.online | never updates itself |
| Internet | the site and GitHub | only Unity Compatibility's Unity libraries (unity.bepinex.dev), after asking |

Everything that differs is behind `Edition.Nexus` in [`src/Core/Edition.cs`](src/Core/Edition.cs).

## Source

- [`src/`](src): the app (.NET 8 WinForms, C#). [BUILD.md](BUILD.md) has the build steps and what the app does on your PC.
- [`installer/`](installer): the website edition's NSIS installer.
- The parts the app installs into the game are built in the
  [NomadModDownloader](https://github.com/oRit-907/NomadModDownloader) repo and shipped here in `src/Assets`:
  Unity Compatibility (`mod/UnityCompat5`), BepInEx 6 Compatibility (`mod/BepInEx6Compat`) and the Mod Files patcher
  (`mod/NomadModManager/Patcher`). `src/Core/Shared` holds copies of the Mods menu's `Categories.cs` and `ModVersion.cs`.

Third-party files and their licences: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

> Don't add Nomad Drive's art (logo, menu backgrounds) to the app or this repo. It belongs to the game's developers;
> the app reads it from each player's own install.

A fan-made tool, not made by or affiliated with Indie Devil (the makers of Nomad Drive), BepInEx or Nexus Mods.
