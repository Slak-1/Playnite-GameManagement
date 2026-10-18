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
        _logger = playniteAPI.CreateLogger();
        _storageInfo = new StorageInfo(_playniteAPI);
        _storageInfo.LoadFromFile(StoragePath);
    }

    public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
    {
        if (args.IsGlobalSearchRequest)
        {
            yield break;
        }

        yield return new GameMenuItem
        {
            Action = UninstallGameMenuAction,
            Description = "Uninstall"
        };

        yield return new GameMenuItem
        {
            Action = UninstallAndRemoveGameMenuAction,
            Description = "Uninstall and Remove"
        };
    }

    private void UninstallGameMenuAction(GameMenuItemActionArgs args)
    {
        UninstallGames(args);
    }

    private void UninstallAndRemoveGameMenuAction(GameMenuItemActionArgs args)
    {
        var games = UninstallGames(args);
        foreach (var game in games)
        {
            _playniteAPI.Database.Games.Remove(game);
        }
    }

    private List<Game> UninstallGames(GameMenuItemActionArgs args)
    {
        var games = args.Games;
        if (games is null || games.Count == 0)
        {
            return new List<Game>();
        }

        var confirmation = _playniteAPI.Dialogs.ShowMessage(
            "Do you really want to delete the installation folder for " + games.Count + " game(s)?",
            "Confirmation",
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
            game.IsInstalled = false;
            _playniteAPI.Database.Games.Update(game);
            _storageInfo.RemoveStorageInfo(game);
        }

        try
        {
            _storageInfo.SaveToFile(StoragePath);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            _logger.Error(ex, "Could not save storage statistics.");
        }

        if (failures.Count > 0)
        {
            var details = string.Join(Environment.NewLine, failures.Take(5));
            if (failures.Count > 5)
            {
                details += Environment.NewLine + "And " + (failures.Count - 5) + " more.";
            }

            _playniteAPI.Dialogs.ShowErrorMessage(
                "Some game folders could not be deleted:" + Environment.NewLine + details,
                "Game Management");
        }

        return uninstalledGames;
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

    public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
    {
        Task.Run(() =>
        {
            try
            {
                _storageInfo.UpdateStorageInfoForAllNewGames();
                _storageInfo.SaveToFile(StoragePath);
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
        _storageInfo.SaveToFile(StoragePath);
    }

    public override void OnGameUninstalled(OnGameUninstalledEventArgs args)
    {
        _storageInfo.RemoveStorageInfo(args.Game);
        _storageInfo.SaveToFile(StoragePath);
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
