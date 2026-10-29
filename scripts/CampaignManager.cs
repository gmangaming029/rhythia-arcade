using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

public static class CampaignManager
{
    private const int CampaignStageCount = 12;
    private static readonly string SavePath = Path.Combine(Constants.USER_FOLDER, "campaign-progress.json");
    private static CampaignSave progress;
    private static string activeMapName;

    public sealed class Stage
    {
        public int Number { get; init; }
        public Map Map { get; init; }
        public bool IsComplete { get; init; }
        public bool IsUnlocked { get; init; }
    }

    public sealed class CampaignSave
    {
        public List<string> MapNames { get; set; } = [];
        public List<string> CompletedMapNames { get; set; } = [];
    }

    public static IReadOnlyList<Stage> GetStages()
    {
        if (!MapManager.Initialized)
            return [];

        loadProgress();
        updateStageList();

        Dictionary<string, Map> installedMaps = MapManager.Maps.ToDictionary(map => map.Name, StringComparer.Ordinal);
        int unlockedCount = 0;
        while (
            unlockedCount < progress.MapNames.Count
            && progress.CompletedMapNames.Contains(progress.MapNames[unlockedCount], StringComparer.Ordinal)
        )
        {
            unlockedCount++;
        }

        List<Stage> stages = [];
        for (int index = 0; index < progress.MapNames.Count; index++)
        {
            string mapName = progress.MapNames[index];
            installedMaps.TryGetValue(mapName, out Map map);
            stages.Add(
                new Stage
                {
                    Number = index + 1,
                    Map = map,
                    IsComplete = progress.CompletedMapNames.Contains(mapName, StringComparer.Ordinal),
                    IsUnlocked = index <= unlockedCount,
                }
            );
        }

        return stages;
    }

    public static bool BeginStage(Map map)
    {
        if (map == null || !GetStages().Any(stage => stage.Map?.Name == map.Name && stage.IsUnlocked))
            return false;

        activeMapName = map.Name;
        return true;
    }

    public static bool CancelActiveStage()
    {
        bool wasActive = activeMapName != null;
        activeMapName = null;
        return wasActive;
    }

    public static bool FinishStage(Attempt attempt, bool passed)
    {
        if (activeMapName == null)
            return false;

        string completedMapName = activeMapName;
        activeMapName = null;
        if (attempt.Map.Name != completedMapName || !passed)
            return false;

        loadProgress();
        if (progress.CompletedMapNames.Contains(completedMapName, StringComparer.Ordinal))
            return false;

        progress.CompletedMapNames.Add(completedMapName);
        saveProgress();

        int completedCount = GetStages().Count(stage => stage.IsComplete);
        _ = ToastNotification.Notify(
            completedCount == progress.MapNames.Count
                ? "Campaign complete! All stages cleared."
                : $"Stage cleared! Stage {completedCount + 1} is now unlocked."
        );
        return true;
    }

    private static void loadProgress()
    {
        if (progress != null)
            return;

        if (!File.Exists(SavePath))
        {
            progress = new CampaignSave();
            return;
        }

        try
        {
            progress = JsonSerializer.Deserialize<CampaignSave>(File.ReadAllText(SavePath)) ?? new CampaignSave();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.Error($"Unable to load campaign progress: {exception.Message}");
            progress = new CampaignSave();
        }
    }

    private static void updateStageList()
    {
        List<Map> installedMaps = MapManager
            .Maps.Where(map => !map.Ephemeral && map.Notes.Length > 0)
            .OrderBy(map => map.Difficulty)
            .ThenBy(map => map.Rating)
            .ThenBy(map => map.PrettyTitle, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (progress.MapNames.Count == 0)
        {
            int stageCount = Math.Min(CampaignStageCount, installedMaps.Count);
            for (int stage = 0; stage < stageCount; stage++)
            {
                int mapIndex = stageCount == 1
                    ? 0
                    : (int)Math.Round((double)stage * (installedMaps.Count - 1) / (stageCount - 1));
                string mapName = installedMaps[mapIndex].Name;
                if (!progress.MapNames.Contains(mapName, StringComparer.Ordinal))
                    progress.MapNames.Add(mapName);
            }

            saveProgress();
            return;
        }

        int previousStageCount = progress.MapNames.Count;
        HashSet<string> existingMaps = progress.MapNames.ToHashSet(StringComparer.Ordinal);
        foreach (Map map in installedMaps.Where(map => !existingMaps.Contains(map.Name)).Take(CampaignStageCount - progress.MapNames.Count))
        {
            progress.MapNames.Add(map.Name);
            existingMaps.Add(map.Name);
        }

        if (progress.MapNames.Count != previousStageCount)
            saveProgress();
    }

    private static void saveProgress()
    {
        try
        {
            Directory.CreateDirectory(Constants.USER_FOLDER);
            File.WriteAllText(SavePath, JsonSerializer.Serialize(progress, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.Error($"Unable to save campaign progress: {exception.Message}");
        }
    }
}
