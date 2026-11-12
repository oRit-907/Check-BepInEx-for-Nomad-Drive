# Third-party notices

## DotNetFix (src/Assets/DotNetFix.zip)

Nomad Drive ships a code-stripped `mscorlib.dll` that BepInEx's preloader can't start with. The app installs full
class libraries into the game's `DotNetFix` folder and points doorstop at them (`dll_search_path_override`).

- **Mono class libraries** (mscorlib, System, System.Core, System.Xml and the other `System.*` assemblies from the
  "Mono development team"). Copyright (c) Various Mono authors. Licensed under the MIT License:
  https://github.com/mono/mono/blob/main/LICENSE
- **netstandard.dll** (the .NET Standard 2.1 facade) and **Mono.Security.dll**, from the official Mono 6.12.0.206
  Windows build (https://download.mono-project.com). MIT License, as above. Nomad Drive ships no netstandard.dll, so
  mods built for .NET Standard 2.0/2.1 need it; Mono.Security matches the full System.dll above.
- **System.Collections.Immutable, System.Runtime.CompilerServices.Unsafe, System.Memory, System.Buffers**
  (where from Microsoft). Copyright (c) .NET Foundation and Contributors. Licensed under the MIT License:
  https://github.com/dotnet/runtime/blob/main/LICENSE.TXT

MIT License: Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction, including without
limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the
Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all copies or substantial portions of
the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED.

## BepInEx 5 (src/Assets/BepInEx_win_x64_5.4.23.6.zip, Nexus edition only)

The Nexus edition of Check BepInEx has the official BepInEx 5.4.23.6 release zip built in (unchanged, from
https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6), so it installs and repairs BepInEx without downloading.
BepInEx is licensed under the GNU Lesser General Public License v2.1 (https://github.com/BepInEx/BepInEx/blob/master/LICENSE);
it includes HarmonyX, MonoMod and Mono.Cecil (MIT). Its source code is at the link above.

## Unity Compatibility (mod/UnityCompat)

The patcher is this project's own code (it uses Mono.Cecil, MIT, which ships with BepInEx). The app doesn't ship
Unity's libraries: when you set up Unity Compatibility it downloads the ones for your game's Unity version from
BepInEx's server (https://unity.bepinex.dev/libraries/), the same files BepInEx itself uses, into BepInEx\unity-libs.
Unity is a trademark of Unity Technologies; its libraries remain under Unity's own terms.

## Nomad Mod Manager (mod/NomadModManager)

libwebp and libsharpyuv: Copyright (c) Google Inc. BSD 3-Clause License:
https://chromium.googlesource.com/webm/libwebp/+/refs/heads/main/COPYING

## The Mods menu's in-game browser (mod/NomadBrowser)

NomadBrowser is this project's own code. Its release zip (GitHub release browser-vX.Y.Z, downloaded by the Mods menu
the first time it's needed) also contains:

- **CefSharp** (CefSharp.OffScreen, CefSharp.Core, CefSharp.BrowserSubprocess). Copyright (c) The CefSharp Authors.
  BSD 3-Clause License: https://github.com/cefsharp/CefSharp/blob/master/LICENSE
- **Chromium Embedded Framework (CEF)** and **Chromium** (libcef.dll, its .pak, .dat and .bin files, chrome_elf,
  SwiftShader, ANGLE). Copyright (c) Marshall A. Greenblatt and The Chromium Authors. BSD 3-Clause License, with the
  licenses of Chromium's own third-party parts: https://bitbucket.org/chromiumembedded/cef/src/master/LICENSE.txt
  and https://chromium.googlesource.com/chromium/src/+/main/LICENSE
- **SharpCompress** (unpacks .7z, .rar and other archives). Copyright (c) Adam Hathcock. MIT License (above):
  https://github.com/adamhathcock/sharpcompress/blob/master/LICENSE.txt
- **System.Buffers, System.Memory, System.Numerics.Vectors, System.Runtime.CompilerServices.Unsafe,
  System.Text.Encoding.CodePages, System.Threading.Tasks.Extensions, Microsoft.Bcl.AsyncInterfaces**. Copyright (c)
  .NET Foundation and Contributors. MIT License (above).
- **Microsoft Visual C++ runtime** (msvcp140.dll, vcruntime140.dll, vcruntime140_1.dll), which CEF needs. Copyright (c)
  Microsoft Corporation. Redistributed app-locally as Microsoft's Visual C++ redistribution terms allow.
- **DirectX shader compilers** (d3dcompiler_47.dll, dxcompiler.dll, dxil.dll), which ship with CEF. Copyright (c)
  Microsoft Corporation, redistributable with applications.
