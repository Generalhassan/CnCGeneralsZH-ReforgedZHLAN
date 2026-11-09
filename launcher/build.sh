#!/bin/sh
# Builds ZHLauncher.exe with Mono's compiler against .NET Framework 4.8 (built into Windows 10/11).
# System.IO.Compression is referenced from Mono's 4.5 folder: its 4.8-api copy says 4.2.0.0,
# a version Windows' .NET Framework does not have (it ships 4.0.0.0).
set -e
cd "$(dirname "$0")"
mcs -sdk:4.8 -target:winexe -platform:anycpu -optimize+ -langversion:6 -win32icon:zh.ico -out:ZHLauncher.exe \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Runtime.Serialization.dll \
  -r:/usr/lib/mono/4.5/System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll -r:System.Core.dll \
  src/*.cs
