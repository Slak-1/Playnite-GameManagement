using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Extensions.Common;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace GameManagement;

public class GameManagementPlugin : GenericPlugin
{
    private readonly IPlayniteAPI _playniteAPI;
    private readonly StorageInfo _storageInfo;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _source = new();

    private string StoragePath => Path.Combine(GetPluginUserDataPath(), "storage.json");

    public override Guid Id => Guid.Parse("a37e0963-91ac-4432-be2a-69e366c44726");

    public GameManagementPlugin(IPlayniteAPI playniteAPI) : base(playniteAPI)
    {
        _playniteAPI = playniteAPI;
        _logger = LogManager.GetLogger(nameof(GameManagementPlugin));
        _storageInfo = new StorageInfo(_playniteAPI);
        _storageInfo.LoadFromFile(StoragePath);
    }

    public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
    {
        if (args.IsGlobalSearchRequest)
        {
            yield break;
        }

        if (args.Games is null || args.Games.Count == 0 || args.Games.Any(game => !CanUninstallAndHide(game)))
        {
            yield break;
        }

        yield return new GameMenuItem
        {
            Action = UninstallAndHideGameMenuAction,
            Description = "Uninstall and Hide"
        };
    }

    private void UninstallAndHideGameMenuAction(GameMenuItemActionArgs args)
    {
        UninstallGames(args);
    }

    private static bool CanUninstallAndHide(Game game)
    {
        return game is not null &&
               game.IsInstalled &&
               !game.Hidden &&
               !game.IsRunning &&
               !game.IsInstalling &&
               !game.IsUninstalling &&
               !string.IsNullOrWhiteSpace(game.InstallDirectory) &&
               Directory.Exists(game.InstallDirectory);
    }

    private List<Game> UninstallGames(GameMenuItemActionArgs args)
    {
        var games = args.Games;
        if (games is null || games.Count == 0 || games.Any(game => !CanUninstallAndHide(game)))
        {
            return new List<Game>();
        }

        var confirmation = _playniteAPI.Dialogs.ShowMessage(
            BuildConfirmationMessage(games),
            "Confirm permanent deletion",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            return new List<Game>();
        }

        _logger.Info("Uninstalling " + games.Count + " game(s).");
        var uninstalledGames = new List<Game>(games.Count);
        var failures = new List<string>();

        _playniteAPI.Dialogs.ActivateGlobalProgress(progressArgs =>
        {
            progressArgs.ProgressMaxValue = games.Count;
            progressArgs.CurrentProgressValue = 0;

            foreach (var game in games)
            {
                if (progressArgs.CancelToken.IsCancellationRequested)
                {
                    _logger.Info("Uninstallation was canceled.");
                    break;
                }

                progressArgs.CurrentProgressValue += 1;
                progressArgs.Text = "Uninstalling " + game.Name;

                try
                {
                    if (game.InstallationStatus != InstallationStatus.Installed)
                    {
                        throw new InvalidOperationException("Playnite does not mark this game as installed.");
                    }

                    var installDirectory = GetSafeInstallDirectory(game);
                    EnsureNoOtherGameUsesDirectory(game, installDirectory);
                    Directory.Delete(installDirectory, true);
                    uninstalledGames.Add(game);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                                           ex is ArgumentException || ex is InvalidOperationException ||
                                           ex is NotSupportedException || ex is System.Security.SecurityException)
                {
                    failures.Add(game.Name + ": " + ex.Message);
                    _logger.Error(ex, "Could not uninstall " + game.Name + ".");
                }
            }
        }, new GlobalProgressOptions("Uninstalling " + games.Count + " game(s)", true));

        foreach (var game in uninstalledGames)
        {
            try
            {
                game.IsInstalled = false;
                game.Hidden = true;
                _playniteAPI.Database.Games.Update(game);
            }
            catch (Exception ex)
            {
                failures.Add(game.Name + ": folder was deleted, but Playnite could not update its installed status.");
                _logger.Error(ex, "Could not update Playnite's installed status for " + game.Name + ".");
            }

            _storageInfo.RemoveStorageInfo(game);
        }

        SaveStorageInfo();

        if (failures.Count > 0)
        {
            var details = string.Join(Environment.NewLine, failures.Take(5));
            if (failures.Count > 5)
            {
                details += Environment.NewLine + "And " + (failures.Count - 5) + " more.";
            }

            _playniteAPI.Dialogs.ShowErrorMessage(
                "Some folders could not be deleted or game states could not be updated:" + Environment.NewLine + details,
                "Game Management");
        }

        return uninstalledGames;
    }

    private string BuildConfirmationMessage(IList<Game> games)
    {
        const int maxEntries = 10;
        var entries = games.Take(maxEntries).Select(game =>
        {
            var path = string.IsNullOrWhiteSpace(game.InstallDirectory)
                ? "(no installation folder configured)"
                : game.InstallDirectory;
            return game.Name + Environment.NewLine + path;
        }).ToList();

        if (games.Count > maxEntries)
        {
            entries.Add("And " + (games.Count - maxEntries) + " more game(s).");
        }

        return "This permanently deletes the listed installation folder(s), bypassing the Recycle Bin:" +
               Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine + Environment.NewLine, entries);
    }

    private void EnsureNoOtherGameUsesDirectory(Game game, string installDirectory)
    {
        var conflict = _playniteAPI.Database.Games.FirstOrDefault(otherGame =>
            otherGame.Id != game.Id &&
            !string.IsNullOrWhiteSpace(otherGame.InstallDirectory) &&
            IsSameOrChildPath(installDirectory, otherGame.InstallDirectory));

        if (conflict is not null)
        {
            throw new InvalidOperationException(
                "The folder also contains the installation directory configured for \"" + conflict.Name + "\".");
        }
    }

    private static bool IsSameOrChildPath(string parentPath, string candidatePath)
    {
        try
        {
            var parent = Path.GetFullPath(parentPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(candidatePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase) ||
                   candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   candidate.StartsWith(parent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
        {
            return false;
        }
    }

    private static string GetSafeInstallDirectory(Game game)
    {
        if (string.IsNullOrWhiteSpace(game.InstallDirectory) || !Directory.Exists(game.InstallDirectory))
        {
            throw new DirectoryNotFoundException("The installation folder was not found.");
        }

        var fullPath = Path.GetFullPath(game.InstallDirectory);
        var rootPath = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(rootPath) ||
            string.Equals(
                fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The installation folder cannot be a drive root.");
        }

        return fullPath;
    }

    private void SaveStorageInfo()
    {
        try
        {
            _storageInfo.SaveToFile(StoragePath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not save storage statistics.");
        }
    }

    public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
    {
        Task.Run(() =>
        {
            try
            {
                _storageInfo.UpdateStorageInfoForAllNewGames();
                SaveStorageInfo();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not update storage statistics.");
            }
        }, _source.Token);
    }

    public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
    {
        if (!_source.IsCancellationRequested)
        {
            _source.Cancel();
        }

        _source.Dispose();
    }

    public override void OnGameInstalled(OnGameInstalledEventArgs args)
    {
        _storageInfo.AddStorageInfo(args.Game);
        SaveStorageInfo();
    }

    public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
    {
        _storageInfo.RemoveStorageInfo(args.Game);
        SaveStorageInfo();
    }

    public override IEnumerable<SidebarItem> GetSidebarItems()
    {
        yield return new SidebarItem
        {
            Title = "View Storage Statistics",
            Type = SiderbarItemType.View,
            Visible = true,
            Opened = () => new StorageStatisticsView
            {
                DataContext = _storageInfo
            }
        };
    }
}
