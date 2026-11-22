using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

public partial class AchievementSystem : Node
{
    public sealed class AchievementDefinition
    {
        public string Id { get; init; }
        public string Name { get; init; }
        public string Description { get; init; }
        public string Category { get; init; }
        public bool Secret { get; init; }
    }

    private sealed class AchievementState
    {
        public Dictionary<string, double> UnlockedAt { get; set; } = [];
        public Dictionary<string, ulong> MapRestartCounts { get; set; } = [];
        public Dictionary<string, ulong> MapFailCounts { get; set; } = [];
        public HashSet<string> FailedMaps { get; set; } = [];
        public HashSet<string> PlayedMapsets { get; set; } = [];
        public ulong CreditsSpent { get; set; }
        public ulong GameplayInputs { get; set; }
    }

    private static readonly AchievementDefinition[] Definitions =
    [
        new() { Id = "first_steps", Name = "First Steps", Description = "Welcome to Rhythia!", Category = "Beginner" },
        new() { Id = "getting_started", Name = "Getting Started", Description = "It's not that hard!", Category = "Beginner" },
        new() { Id = "regular_player", Name = "Regular Player", Description = "You're getting the hang of this.", Category = "Beginner" },
        new() { Id = "dedicated", Name = "Dedicated", Description = "You've been here a while.", Category = "Beginner" },
        new() { Id = "rhythm_rookie", Name = "Rhythm Rookie", Description = "Finding your rhythm.", Category = "Beginner" },
        new() { Id = "almost_perfect", Name = "Almost Perfect", Description = "So close!", Category = "Beginner" },
        new() { Id = "perfection", Name = "Perfection", Description = "Not a single mistake.", Category = "Beginner" },
        new() { Id = "combo_starter", Name = "Combo Starter", Description = "Keep it going!", Category = "Score" },
        new() { Id = "combo_master", Name = "Combo Master", Description = "That's a lot of notes.", Category = "Score" },
        new() { Id = "combo_legend", Name = "Combo Legend", Description = "Absolutely insane.", Category = "Score" },
        new() { Id = "no_mistakes", Name = "No Mistakes", Description = "Clean run.", Category = "Score" },
        new() { Id = "untouchable", Name = "Untouchable", Description = "Flawless execution.", Category = "Score" },
        new() { Id = "score_chaser", Name = "Score Chaser", Description = "Beat your own record.", Category = "Score" },
        new() { Id = "overachiever", Name = "Overachiever", Description = "Beyond expectations.", Category = "Score" },
        new() { Id = "speed_demon", Name = "Speed Demon", Description = "Fast hands!", Category = "Gameplay" },
        new() { Id = "hardheaded", Name = "Hardheaded", Description = "No easy mode here.", Category = "Gameplay" },
        new() { Id = "double_trouble", Name = "Double Trouble", Description = "Two modifiers, one challenge.", Category = "Gameplay" },
        new() { Id = "marathon", Name = "Marathon", Description = "Still going?", Category = "Gameplay" },
        new() { Id = "variety_player", Name = "Variety Player", Description = "Try something new.", Category = "Gameplay" },
        new() { Id = "explorer", Name = "Explorer", Description = "What's this map?", Category = "Gameplay" },
        new() { Id = "comeback", Name = "Comeback", Description = "Never give up.", Category = "Gameplay" },
        new() { Id = "insert_coin", Name = "Insert Coin", Description = "Let's play!", Category = "Arcade" },
        new() { Id = "arcade_regular", Name = "Arcade Regular", Description = "A familiar face.", Category = "Arcade" },
        new() { Id = "big_spender", Name = "Big Spender", Description = "That's a lot of coins.", Category = "Arcade" },
        new() { Id = "free_spirit", Name = "Free Spirit", Description = "No coins required.", Category = "Arcade" },
        new() { Id = "high_roller", Name = "High Roller", Description = "Top of the machine.", Category = "Arcade" },
        new() { Id = "arcade_veteran", Name = "Arcade Veteran", Description = "You've been here for hours.", Category = "Arcade" },
        new() { Id = "just_one_more", Name = "Just One More", Description = "One more round...", Category = "Arcade" },
        new() { Id = "button_masher", Name = "The Button Masher", Description = "What are you doing?", Category = "Secret", Secret = true },
        new() { Id = "oops", Name = "Oops", Description = "That wasn't supposed to happen.", Category = "Secret", Secret = true },
        new() { Id = "night_owl", Name = "Night Owl", Description = "Still playing?", Category = "Secret", Secret = true },
        new() { Id = "wrong_way", Name = "Wrong Way", Description = "Interesting strategy.", Category = "Secret", Secret = true },
        new() { Id = "easter_egg", Name = "Easter Egg", Description = "You found it!", Category = "Secret", Secret = true },
        new() { Id = "commitment_issues", Name = "Commitment Issues", Description = "Changed your mind?", Category = "Secret", Secret = true },
        new() { Id = "afk_champion", Name = "AFK Champion", Description = "Truly impressive.", Category = "Secret", Secret = true },
        new() { Id = "touch_grass", Name = "Touch Grass", Description = "Maybe take a break.", Category = "Silly" },
        new() { Id = "certified_gamer", Name = "Certified Gamer", Description = "It's official.", Category = "Silly" },
        new() { Id = "why_though", Name = "Why Though?", Description = "You really did that.", Category = "Silly" },
        new() { Id = "the_collector", Name = "The Collector", Description = "Gotta catch them all.", Category = "Silly" },
        new() { Id = "masochist", Name = "Masochist", Description = "Why make it harder?", Category = "Silly" },
        new() { Id = "one_more_try", Name = "One More Try", Description = "Surely this time.", Category = "Silly" },
        new() { Id = "professional_button_presser", Name = "Professional Button Presser", Description = "Impressive finger movement.", Category = "Silly" },
    ];

    private static readonly string StatePath = Path.Combine(Constants.USER_FOLDER, "achievements.json");
    private AchievementState state = new();
    private ulong sessionMenuButtons;
    private ulong sessionGames;
    private ulong sessionHitInputs;
    private double menuIdleSeconds;

    [Signal]
    public delegate void AchievementUnlockedEventHandler(string id);

    public static AchievementSystem Instance { get; private set; }
    public static IReadOnlyList<AchievementDefinition> AllDefinitions => Definitions;

    public override void _Ready()
    {
        Instance = this;
        Load();
        sessionMenuButtons = 0;
        sessionGames = 0;
        sessionHitInputs = 0;
        CheckExistingStats();
    }

    public override void _Process(double delta)
    {
        if (SceneManager.Scene is MainMenu)
        {
            menuIdleSeconds += delta;
            if (menuIdleSeconds >= 1800)
                Unlock("afk_champion");
        }

        if (Stats.Instance != null)
        {
            if (Stats.Instance.TotalPlaytime >= 10 * 3600)
                Unlock("arcade_veteran");
            if (Stats.Instance.TotalPlaytime >= 24 * 3600)
                Unlock("touch_grass");
        }
    }

    public void ResetMenuIdleTimer() => menuIdleSeconds = 0;

    public bool IsUnlocked(string id) => state.UnlockedAt.ContainsKey(id);
    public AchievementDefinition GetDefinition(string id) => Definitions.FirstOrDefault(definition => definition.Id == id);
    public IReadOnlyDictionary<string, double> UnlockedAchievements => state.UnlockedAt;

    public void RecordMenuButtonPress()
    {
        sessionMenuButtons++;
        if (sessionMenuButtons >= 100)
            Unlock("button_masher");
    }

    public void RecordGameplayInput()
    {
        sessionHitInputs++;
        state.GameplayInputs++;
        if (sessionHitInputs >= 100000 || state.GameplayInputs >= 100000)
            Unlock("professional_button_presser");
        Save();
    }

    public void RecordGameStarted(bool creditBased)
    {
        sessionGames++;
        if (sessionGames >= 10)
            Unlock("just_one_more");
        if (creditBased)
            Unlock("insert_coin");
    }

    public void RecordCreditsSpent(int amount)
    {
        state.CreditsSpent += (ulong)Math.Max(0, amount);
        if (state.CreditsSpent >= 100)
            Unlock("arcade_regular");
        if (state.CreditsSpent >= 1000)
            Unlock("big_spender");
        Save();
    }

    public void RecordRestart(string mapName)
    {
        state.MapRestartCounts.TryGetValue(mapName, out ulong count);
        state.MapRestartCounts[mapName] = count + 1;
        if (count + 1 >= 10)
            Unlock("commitment_issues");
        Save();
    }

    public void RecordRecoverableError() => Unlock("oops");

    public void RecordEasterEgg() => Unlock("easter_egg");

    public void RecordWrongWayModifier() => Unlock("wrong_way");

    public void RecordAttempt(Attempt attempt, bool isPersonalBest, bool isLocalLeaderboardFirst)
    {
        if (attempt.IsReplay)
            return;

        bool passed = attempt.Alive && attempt.Qualifies;
        string mapName = attempt.Map.Name;
        bool wasExplorer = attempt.Map.PlayCount <= 1;

        if (wasExplorer)
            Unlock("explorer");

        if (passed)
        {
            if (state.FailedMaps.Contains(mapName))
                Unlock("comeback");
            state.FailedMaps.Remove(mapName);

            Unlock("first_steps");
            if (Stats.Instance.Passes >= 5) Unlock("getting_started");
            if (Stats.Instance.Passes >= 25) Unlock("regular_player");
            if (Stats.Instance.Passes >= 50) Unlock("certified_gamer");
            if (Stats.Instance.Passes >= 100) Unlock("dedicated");

            if (attempt.Accuracy >= 90) Unlock("rhythm_rookie");
            if (attempt.Accuracy >= 99) Unlock("almost_perfect");
            if (attempt.Accuracy >= 100) Unlock("perfection");
            if (attempt.Misses == 0) Unlock("no_mistakes");

            if (attempt.Accuracy < 30)
                Unlock("why_though");
            if (attempt.Accuracy >= 95)
                Unlock("overachiever");

            bool noNonPerfectHits = attempt.HitsInfo.Take((int)attempt.Sum).All(value => value == 0 || Math.Abs(value) <= 25);
            if (noNonPerfectHits && attempt.Hits > 0)
                Unlock("untouchable");

            if (attempt.Speed >= 1.5)
                Unlock("speed_demon");
            if (attempt.Map.Length >= 600000)
                Unlock("marathon");
            if (attempt.Modifiers.Count >= 2)
                Unlock("double_trouble");

            bool hardRock = attempt.Modifiers.Any(mod => mod.Name.Contains("hard", StringComparison.OrdinalIgnoreCase)
                || mod.Name.Contains("rock", StringComparison.OrdinalIgnoreCase));
            if (hardRock)
            {
                Unlock("hardheaded");
                if (attempt.Speed > 1 && attempt.Map.Difficulty >= 7)
                    Unlock("masochist");
            }

            string mapset = !string.IsNullOrWhiteSpace(attempt.Map.Collection)
                ? attempt.Map.Collection
                : Path.GetDirectoryName(attempt.Map.FolderPath) ?? attempt.Map.Name;
            state.PlayedMapsets.Add(mapset);
            if (state.PlayedMapsets.Count >= 10)
                Unlock("variety_player");

            if (isPersonalBest)
                Unlock("score_chaser");
            if (isLocalLeaderboardFirst)
                Unlock("high_roller");
            if (ArcadeService.Instance?.FreePlay == true)
                Unlock("free_spirit");

            int hour = DateTime.Now.Hour;
            if (hour >= 0 && hour < 5)
                Unlock("night_owl");

            if (attempt.Modifiers.Count >= 2)
                RecordWrongWayModifier();
        }
        else
        {
            state.FailedMaps.Add(mapName);
            state.MapFailCounts.TryGetValue(mapName, out ulong failures);
            state.MapFailCounts[mapName] = failures + 1;
            if (failures + 1 >= 20)
                Unlock("one_more_try");
        }

        if (attempt.Combo >= 100) Unlock("combo_starter");
        if (attempt.Combo >= 500) Unlock("combo_master");
        if (attempt.Combo >= 1000) Unlock("combo_legend");

        CheckExistingStats();
        Save();
    }

    private void CheckExistingStats()
    {
        if (Stats.Instance == null)
            return;

        if (Stats.Instance.Passes >= 1) Unlock("first_steps");
        if (Stats.Instance.Passes >= 5) Unlock("getting_started");
        if (Stats.Instance.Passes >= 25) Unlock("regular_player");
        if (Stats.Instance.Passes >= 50) Unlock("certified_gamer");
        if (Stats.Instance.Passes >= 100) Unlock("dedicated");
        if (Stats.Instance.HighestCombo >= 100) Unlock("combo_starter");
        if (Stats.Instance.HighestCombo >= 500) Unlock("combo_master");
        if (Stats.Instance.HighestCombo >= 1000) Unlock("combo_legend");
        if (Stats.Instance.AverageAccuracy >= 90) Unlock("rhythm_rookie");
        if (Stats.Instance.AverageAccuracy >= 99) Unlock("almost_perfect");
        if (Stats.Instance.AverageAccuracy >= 100) Unlock("perfection");
        if (Stats.Instance.TotalPlaytime >= 10 * 3600) Unlock("arcade_veteran");
        if (Stats.Instance.TotalPlaytime >= 24 * 3600) Unlock("touch_grass");
    }

    public void Unlock(string id)
    {
        if (state.UnlockedAt.ContainsKey(id))
            return;

        AchievementDefinition definition = GetDefinition(id);
        if (definition == null)
            return;

        state.UnlockedAt[id] = Time.GetUnixTimeFromSystem();
        Save();
        EmitSignal(SignalName.AchievementUnlocked, id);
        _ = ToastNotification.Notify($"Achievement unlocked: {definition.Name}");

        if (state.UnlockedAt.Count >= 25)
            Unlock("the_collector");
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                state = new();
                return;
            }
            state = JsonSerializer.Deserialize<AchievementState>(File.ReadAllText(StatePath)) ?? new();
            state.UnlockedAt ??= [];
            state.MapRestartCounts ??= [];
            state.MapFailCounts ??= [];
            state.FailedMaps ??= [];
            state.PlayedMapsets ??= [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.Error($"Unable to load achievements: {exception.Message}");
            state = new();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Constants.USER_FOLDER);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(state));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.Error($"Unable to save achievements: {exception.Message}");
        }
    }
}
