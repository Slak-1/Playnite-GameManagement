# Playnite GameManagement

A standalone source copy of the GameManagement extension from [erri120/Playnite.Extensions](https://github.com/erri120/Playnite.Extensions/tree/master/src/GameManagement), separated for easier browsing and development.

This fork updates the project to Playnite SDK 6.18.0, the latest stable SDK at the time of this update, while retaining the .NET Framework 4.6.2 target required by Playnite 10. It hardens folder removal, persists installation state through the Playnite database, and handles per-game deletion failures without aborting the entire batch.

The extension adds one “Uninstall and Hide” game menu action for installed games with an existing local installation folder, including games managed by library plugins. It permanently deletes the configured Installation Directory, bypasses the Recycle Bin, marks the game uninstalled, and hides it in the library. Afterward, it removes empty parent folders up to the first folder that contains files, folders, or another game's configured installation path. Drive roots are never removed. Playnite's built-in uninstall item may also appear for library-managed games. Verify the Installation Directory before using the action.

Playnite 11 is a separate major version with a different SDK and is not covered by this update.

Licensed under GPL-3.0; see [LICENSE](LICENSE).