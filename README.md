# Playnite GameManagement

A standalone source copy of the GameManagement extension from [erri120/Playnite.Extensions](https://github.com/erri120/Playnite.Extensions/tree/master/src/GameManagement), separated for easier browsing and development.

This fork updates the project to Playnite SDK 6.18.0, the latest stable SDK at the time of this update, while retaining the .NET Framework 4.6.2 target required by Playnite 10. It hardens folder removal, persists installation state through the Playnite database, and handles per-game deletion failures without aborting the entire batch.

The extension adds game menu actions to delete a game's configured Installation Directory, with a confirmation prompt. Deletion is permanent and bypasses the Recycle Bin. Verify the Installation Directory before using either action.

Playnite 11 is a separate major version with a different SDK and is not covered by this update.

Licensed under GPL-3.0; see [LICENSE](LICENSE).