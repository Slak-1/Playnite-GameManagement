# Playnite GameManagement

A standalone source copy of the GameManagement extension from [erri120/Playnite.Extensions](https://github.com/erri120/Playnite.Extensions/tree/master/src/GameManagement), separated for easier browsing and development.

The extension adds game menu actions to uninstall a game by deleting its configured Installation Directory, and to uninstall and remove it from the Playnite library. Deletion is permanent and bypasses the Recycle Bin. Verify the Installation Directory before using either action.

The source depends on the included `Extensions.Common` project and shared `AssemblyLoader.cs`. It targets .NET Framework 4.6.2 and uses Playnite SDK 6.2.2, matching the original project. This is an archived upstream version, not a newer maintained release.

Licensed under GPL-3.0; see [LICENSE](LICENSE).