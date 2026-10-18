using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Extensions.Common;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;

namespace GameManagement;

public class StorageInfo
{
    private readonly IPlayniteAPI _playniteAPI;

    public List<GameStorage> Games { get; set; } = new();

    public string TotalSize => "Total Size: " + Games.Sum(x => x.SizeOnDisk).ToFileSizeString();

    public StorageInfo(IPlayniteAPI playniteAPI)
    {
        _playniteAPI = playniteAPI;
    }

    public void LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var data = Serialization.FromJsonFile<List<GameStorage>>(path);
            if (data is null)
            {
                return;
            }

            Games = data;
            foreach (var game in Games)
            {
                game.PlayniteAPI = _playniteAPI;
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException)
        {
            Games = new List<GameStorage>();
        }
    }

    public void SaveToFile(string path)
    {
        var json = Serialization.ToJson(Games);
        if (json is null)
        {
            return;
        }

        File.WriteAllText(path, json, Encoding.UTF8);
    }

    private static bool TryGetDirectorySize(string path, out long size)
    {
        size = 0;
        try
        {
            size = Directory
                .EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool GameHasInstallDirectory(Game game)
    {
        return !string.IsNullOrWhiteSpace(game.InstallDirectory) && Directory.Exists(game.InstallDirectory);
    }

    public void UpdateStorageInfoForAllNewGames()
    {
        var knownGameIds = new HashSet<Guid>(Games.Select(x => x.GameId));
        var games = _playniteAPI.Database.Games
            .Where(game => !knownGameIds.Contains(game.Id) && GameHasInstallDirectory(game))
            .ToArray();
        var newStorage = new ConcurrentBag<GameStorage>();

        Parallel.ForEach(games, game =>
        {
            if (TryGetDirectorySize(game.InstallDirectory, out var size))
            {
                newStorage.Add(new GameStorage(_playniteAPI)
                {
                    GameId = game.Id,
                    SizeOnDisk = size
                });
            }
        });

        Games.AddRange(newStorage);
    }

    public void AddStorageInfo(Game game)
    {
        if (!GameHasInstallDirectory(game) || !TryGetDirectorySize(game.InstallDirectory, out var size))
        {
            return;
        }

        Games.RemoveAll(savedGame => savedGame.GameId.Equals(game.Id));
        Games.Add(new GameStorage(_playniteAPI)
        {
            GameId = game.Id,
            SizeOnDisk = size
        });
    }

    public void RemoveStorageInfo(Game game)
    {
        Games.RemoveAll(savedGame => savedGame.GameId.Equals(game.Id));
    }
}

public class GameStorage
{
    [DontSerialize]
    public IPlayniteAPI? PlayniteAPI { get; set; }

    public GameStorage()
    {
    }

    public GameStorage(IPlayniteAPI playniteAPI)
    {
        PlayniteAPI = playniteAPI;
    }

    public Guid GameId { get; set; }

    public long SizeOnDisk { get; set; }

    [DontSerialize]
    public string FileSizeString => SizeOnDisk.ToFileSizeString();

    [DontSerialize]
    public string GameName => PlayniteAPI?.Database.Games.FirstOrDefault(x => x.Id.Equals(GameId))?.Name ?? string.Empty;
}
