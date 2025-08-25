# Vini Upgrade Button Mod

This repository contains the source code for the **Vini-Upgrade** mod for *7 Days to Die*.

## Building

1. Install the [.NET SDK](https://dotnet.microsoft.com/) (4.7.2 compatible).
2. From the repository root run:
   ```bash
   dotnet build Source/Vini.Upgrade.csproj -c Release
   ```
3. Copy the generated `Vini.Upgrade.dll` from `Source/bin/Release/net472/` into the mod root.

The mod only works when a compiled `Vini.Upgrade.dll` is present. The previous repository
version accidentally included a text file instead of the compiled DLL, which caused the
"Invalid Image" load error.
