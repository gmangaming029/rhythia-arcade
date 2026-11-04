using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class MainMenu : BaseScene
{
    public Panel CurrentMenu;
    public Panel LastMenu;

    public Panel HomeMenu;
    public Panel PlayMenu;
    public Panel TopBar;
    public JukeboxPanel Jukebox;
    public MapList MapList;
    public MapInfo MapInfo;

    private Panel menuHolder;
    private Node topBarButtonsContainer;
    private Timer idleTimer;
    private Timer attractScoreTimer;
    private Control attractOverlay;
    private VideoStreamPlayer attractVideo;
    private Label attractMapLabel;
    private Label attractScoreLabel;
    private Label attractPlayerLabel;
    private Label attractAccuracyLabel;
    private string lastAttractScoreKey;
    private string selectedAttractReplayPath;
    private Map selectedAttractMap;
    private readonly HashSet<string> unavailableAttractReplays = [];
    private Label arcadeStatus;
    private Panel campaignMenu;
    private VBoxContainer campaignStages;
    private Label campaignProgress;
    private RepositoryBrowser repositoryBrowser;

    public override void _Ready()
    {
        base._Ready();

        menuHolder = GetNode<Panel>("Menus");

        HomeMenu = menuHolder.GetNode<Panel>("Home");
        PlayMenu = menuHolder.GetNode<Panel>("Play");
        LastMenu = HomeMenu;

        TopBar = GetNode<Panel>("TopBar");
        topBarButtonsContainer = TopBar.GetNode("MenuButtons");
        Jukebox = GetNode<JukeboxPanel>("JukeboxPanel");
        MapList = PlayMenu.GetNode<MapList>("MapList");
        MapInfo = PlayMenu.GetNode<MapInfo>("MapInfo");

        setupArcadeStatus();
        setupAttractMode();
        setupCampaignMenu();
        setupRepositoryBrowser();

        CurrentMenu = HomeMenu;
        idleTimer?.Start();

        Input.MouseMode = SettingsManager.Instance.Settings.UseCursorInMenus ? Input.MouseModeEnum.Hidden : Input.MouseModeEnum.Visible;

        List<Node> menuButtons = [];
        menuButtons.AddRange(topBarButtonsContainer.GetChildren());

        foreach (var container in HomeMenu.GetNode("Buttons").GetChildren())
        {
            menuButtons.Add(container.GetChild<Button>(0));
        }

        foreach (Button button in menuButtons.Cast<Button>())
        {
            var menu = (Panel)menuHolder.FindChild(button.Name, false);

            if (menu != null)
            {
                button.Pressed += () => Transition(menu);
            }
        }
    }

    private void setupRepositoryBrowser()
    {
        var toolbar = PlayMenu.GetNode<HBoxContainer>("SubTopBar/HBoxContainer");
        repositoryBrowser = new RepositoryBrowser { Name = "RepositoryBrowser" };
        AddChild(repositoryBrowser);

        Button repositoryButton = new()
        {
            Name = "Repository",
            Text = "Online Charts",
            CustomMinimumSize = new Vector2(0, 36),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            Theme = toolbar.Theme
        };
        repositoryButton.AddThemeColorOverride("font_color", Colors.White);
        repositoryButton.Pressed += repositoryBrowser.Open;
        toolbar.AddChild(repositoryButton);
    }

    public override void _Input(InputEvent @event)
    {
        resetIdleTimer();

        if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
        {
            switch (mouseButton.ButtonIndex)
            {
                case MouseButton.Xbutton1:
                    Transition(HomeMenu);
                    break;
                case MouseButton.Xbutton2:
                    Transition(LastMenu);
                    break;
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        resetIdleTimer();

        if (@event is InputEventKey key && key.Pressed)
        {
            switch (key.Keycode)
            {
                case Key.Escape:
                    Transition(HomeMenu);
                    break;
            }
        }
    }

    public override void Load()
    {
        base.Load();

        DisplayServer.WindowSetVsyncMode(
            SettingsManager.Instance.Settings.VSyncMenus ? DisplayServer.VSyncMode.Adaptive : DisplayServer.VSyncMode.Disabled
        );

        // Apply any map selection that was deferred while menu was off-tree (e.g. import from another scene)
        MapInfo.ApplyPendingSelection();

        MapInfo.InfoContainer?.Refresh();
        SceneManager.Space?.UpdateState(false);

        var map = MapManager.Selected.Value;

        if (map != null)
        {
            SceneManager.Space?.UpdateMap(map);
        }

        SoundManager.RefreshMenuMusicPlayback();
        if (Game.AttractReplay)
        {
            Game.AttractReplay = false;
            startAttractMode();
        }
        else
        {
            idleTimer?.Start();
        }
        refreshCampaignPanel();

        if (SettingsManager.Instance.Settings.AutoplayJukebox.Value && !SoundManager.Song.Playing && SoundManager.Map != null)
        {
            SoundManager.PlayJukebox(SoundManager.JukeboxIndex);
        }
    }

    public override void _Process(double delta)
    {
        if (Rhythia.Quitting && SoundManager.Song.VolumeDb > float.NegativeInfinity)
        {
            SoundManager.Song.VolumeDb = Mathf.Lerp(SoundManager.Song.VolumeDb, -80f, (float)delta * 2);
        }
    }

    public void Transition(Panel menu, bool instant = false)
    {
        if (CurrentMenu == menu)
        {
            return;
        }

        LastMenu = CurrentMenu;
        CurrentMenu = menu;

        topBarButtonsContainer.GetNode<Button>(new(LastMenu.Name)).Disabled = false;
        topBarButtonsContainer.GetNode<Button>(new(CurrentMenu.Name)).Disabled = true;

        double tweenTime = instant ? 0 : 0.15;

        Tween outTween = CreateTween().SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        outTween.TweenProperty(LastMenu, "modulate", Color.Color8(255, 255, 255, 0), tweenTime);
        outTween.TweenCallback(
            Callable.From(() =>
            {
                LastMenu.Visible = false;
            })
        );

        CurrentMenu.Visible = true;

        Tween inTween = CreateTween().SetTrans(Tween.TransitionType.Quad);
        inTween.TweenProperty(CurrentMenu, "modulate", Color.Color8(255, 255, 255), tweenTime);

        SoundManager.RefreshMenuMusicPlayback();
    }

    private void setupCampaignMenu()
    {
        campaignMenu = new Panel
        {
            Name = "Campaign",
            Visible = false,
            Modulate = new Color(1, 1, 1, 0),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        campaignMenu.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        campaignMenu.AddThemeStyleboxOverride(
            "panel",
            new StyleBoxFlat
            {
                BgColor = Color.FromHtml("#100e18"),
                ContentMarginLeft = 56,
                ContentMarginRight = 56,
                ContentMarginTop = 100,
                ContentMarginBottom = 36,
            }
        );
        menuHolder.AddChild(campaignMenu);

        VBoxContainer content = new() { Name = "Content" };
        content.AddThemeConstantOverride("separation", 12);
        campaignMenu.AddChild(content);

        Label title = new()
        {
            Text = "THE SIGNAL RUN",
            CustomMinimumSize = new Vector2(0, 48),
        };
        title.AddThemeFontSizeOverride("font_size", 34);
        title.AddThemeColorOverride("font_color", Color.FromHtml("#e9d9ff"));
        content.AddChild(title);

        Label introduction = new()
        {
            Text = "A 12-stage journey through your installed charts. Clear each stage to unlock the next; campaign progress is saved on this device.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 44),
        };
        introduction.AddThemeFontSizeOverride("font_size", 16);
        introduction.AddThemeColorOverride("font_color", Color.FromHtml("#b8adc8"));
        content.AddChild(introduction);

        campaignProgress = new Label { Text = "Loading campaign..." };
        campaignProgress.AddThemeFontSizeOverride("font_size", 18);
        campaignProgress.AddThemeColorOverride("font_color", Color.FromHtml("#80e0c0"));
        content.AddChild(campaignProgress);

        ScrollContainer scroll = new()
        {
            Name = "StageScroll",
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        content.AddChild(scroll);

        campaignStages = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        campaignStages.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(campaignStages);

        Button homeCampaign = HomeMenu.GetNode<Button>("Buttons/MarginContainer/Play").Duplicate() as Button;
        homeCampaign.Name = "Campaign";
        homeCampaign.Text = "CAMPAIGN";
        homeCampaign.Pressed += () => Transition(campaignMenu);
        MarginContainer homeCampaignMargin = new() { Name = "CampaignButton" };
        homeCampaignMargin.AddChild(homeCampaign);
        HomeMenu.GetNode<VBoxContainer>("Buttons").AddChild(homeCampaignMargin);
        HomeMenu.GetNode<VBoxContainer>("Buttons").MoveChild(homeCampaignMargin, 1);

        Button topBarCampaign = topBarButtonsContainer.GetNode<Button>("Play").Duplicate() as Button;
        topBarCampaign.Name = "Campaign";
        topBarCampaign.Text = "Campaign";
        topBarCampaign.Pressed += () => Transition(campaignMenu);
        topBarButtonsContainer.AddChild(topBarCampaign);

        MapManager.MapsInitialized += _ => Callable.From(refreshCampaignPanel).CallDeferred();
        MapManager.MapUpdated += _ => refreshCampaignPanel();
        MapManager.MapDeleted += _ => refreshCampaignPanel();
        refreshCampaignPanel();
    }

    private void refreshCampaignPanel()
    {
        if (!IsInstanceValid(campaignStages))
            return;

        foreach (Node child in campaignStages.GetChildren())
        {
            campaignStages.RemoveChild(child);
            child.QueueFree();
        }

        IReadOnlyList<CampaignManager.Stage> stages = CampaignManager.GetStages();
        int completed = stages.Count(stage => stage.IsComplete);
        campaignProgress.Text = stages.Count == 0
            ? MapManager.Initialized ? "No playable charts found. Import a chart to begin." : "Loading your installed charts..."
            : $"{completed} / {stages.Count} STAGES CLEARED";

        string[] chapters = ["FIRST CONTACT", "RISING SIGNAL", "OVERDRIVE"];
        foreach (CampaignManager.Stage stage in stages)
        {
            string chapter = chapters[Math.Min((stage.Number - 1) / 4, chapters.Length - 1)];
            string title = stage.Map?.PrettyTitle ?? "Chart unavailable";
            string state = stage.IsComplete ? "CLEARED" : stage.IsUnlocked ? "READY" : "LOCKED";
            Button stageButton = new()
            {
                Name = $"CampaignStage{stage.Number}",
                Text = $"{stage.Number:00}   {chapter}   |   {title}   |   {state}",
                CustomMinimumSize = new Vector2(0, 54),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Disabled = !stage.IsUnlocked || stage.Map == null,
                FocusMode = Control.FocusModeEnum.None,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            stageButton.AddThemeFontSizeOverride("font_size", 17);
            stageButton.AddThemeColorOverride(
                "font_color",
                stage.IsComplete ? Color.FromHtml("#80e0c0") : Color.FromHtml("#e9d9ff")
            );
            stageButton.Pressed += () =>
            {
                if (Lobby.Map == null || Lobby.Map.Name != stage.Map.Name)
                    Lobby.SetMap(stage.Map);

                Game.Play(stage.Map, 1, 0, new CameraLock(), [], campaignStage: true);
            };
            campaignStages.AddChild(stageButton);
        }
    }

    private void setupArcadeStatus()
    {
        arcadeStatus = new Label
        {
            Name = "ArcadeStatus",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Theme = TopBar.Theme,
            Text = "API ON  ·  CREDITS 0  ·  FREE PLAY OFF",
        };
        arcadeStatus.AddThemeColorOverride("font_color", Color.FromHtml("#d1c1e4"));
        arcadeStatus.AddThemeFontSizeOverride("font_size", 9);
        arcadeStatus.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        arcadeStatus.OffsetLeft = -360;
        arcadeStatus.OffsetRight = -14;
        arcadeStatus.OffsetTop = 0;
        arcadeStatus.OffsetBottom = 0;
        TopBar.AddChild(arcadeStatus);

        ArcadeService.Instance.StateChanged += updateArcadeStatus;
        updateArcadeStatus(ArcadeService.Instance.Credits, ArcadeService.Instance.FreePlay);
    }

    private void updateArcadeStatus(int credits, bool freePlay)
    {
        if (!IsInstanceValid(arcadeStatus))
            return;

        string api = ArcadeService.Instance.ApiAvailable ? "API ONLINE" : "API OFFLINE";
        arcadeStatus.Text = $"{api}  ·  CREDITS {credits}  ·  FREE PLAY {(freePlay ? "ON" : "OFF")}";
    }

    private void setupAttractMode()
    {
        string videoPath = OS.GetEnvironment("RHYTHIA_ATTRACT_VIDEO");
        if (string.IsNullOrWhiteSpace(videoPath))
            videoPath = Path.Combine(Constants.USER_FOLDER, "arcade", "attract.ogv");

        VideoStream videoStream = null;
        if (!Path.IsPathRooted(videoPath) || !Path.GetExtension(videoPath).Equals(".ogv", StringComparison.OrdinalIgnoreCase))
        {
            Logger.Log("The attract video must be a local .ogv file; continuing with the idle high-score display.", true);
        }
        else if (!File.Exists(videoPath))
        {
            Logger.Log($"No attract video found at {videoPath}; idle high scores will display without video.");
        }
        else
        {
            string playbackPath = Path.Combine(OS.GetUserDataDir(), "arcade", "attract.ogv");
            try
            {
                string videoDirectory = Path.GetDirectoryName(videoPath);
                if (!string.IsNullOrEmpty(videoDirectory))
                    Directory.CreateDirectory(videoDirectory);

                Directory.CreateDirectory(Path.GetDirectoryName(playbackPath));
                if (!string.Equals(Path.GetFullPath(videoPath), Path.GetFullPath(playbackPath), StringComparison.OrdinalIgnoreCase))
                    File.Copy(videoPath, playbackPath, true);

                videoStream = ResourceLoader.Load<VideoStream>("user://arcade/attract.ogv");
                if (videoStream == null)
                    Logger.Log($"Unable to load the attract video at {videoPath}; idle high scores will display without video.", true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Logger.Error($"Unable to prepare the attract video: {exception.Message}");
            }
        }

        if (videoStream != null)
        {
            attractVideo = new VideoStreamPlayer
            {
                Name = "AttractVideo",
                Stream = videoStream,
                Autoplay = false,
                Loop = true,
                Expand = true,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            attractVideo.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        }

        Label attractLabel = new()
        {
            Text = "MOVE TO START",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        attractLabel.AddThemeColorOverride("font_color", Colors.White);
        attractLabel.AddThemeFontSizeOverride("font_size", 24);
        attractLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomWide);
        attractLabel.OffsetTop = -72;
        attractLabel.OffsetBottom = -25;

        Control videoOverlay = new()
        {
            Name = "AttractOverlay",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        videoOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        ColorRect attractBackdrop = new()
        {
            Color = videoStream == null ? Color.FromHtml("#100e18f5") : Color.FromHtml("#00000066"),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        attractBackdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        videoOverlay.AddChild(attractBackdrop);
        if (attractVideo != null)
            videoOverlay.AddChild(attractVideo);
        videoOverlay.AddChild(attractLabel);
        videoOverlay.AddChild(createAttractScoreCard());

        CanvasLayer canvasLayer = new() { Layer = 100 };
        videoOverlay.Visible = false;
        canvasLayer.AddChild(videoOverlay);
        AddChild(canvasLayer);
        attractOverlay = videoOverlay;

        idleTimer = new Timer { Name = "ArcadeIdleTimer", WaitTime = 60, OneShot = true };
        idleTimer.Timeout += startAttractMode;
        AddChild(idleTimer);

        attractScoreTimer = new Timer { Name = "AttractScoreTimer", WaitTime = 8 };
        attractScoreTimer.Timeout += showRandomAttractScore;
        AddChild(attractScoreTimer);
    }

    private void resetIdleTimer()
    {
        if (attractOverlay == null)
        {
            idleTimer?.Start();
            return;
        }

        if (attractOverlay.Visible)
        {
            attractVideo?.Stop();
            attractOverlay.Visible = false;
            attractScoreTimer.Stop();
        }

        idleTimer.Start();
    }

    private void startAttractMode()
    {
        if (!IsInsideTree() || CurrentMenu == null)
            return;

        idleTimer.Stop();
        attractOverlay.Visible = true;
        attractVideo?.Play();
        showRandomAttractScore();
    }

    private Control createAttractScoreCard()
    {
        PanelContainer card = new()
        {
            Name = "TopScoreCard",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        card.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        card.OffsetLeft = -430;
        card.OffsetTop = -130;
        card.OffsetRight = 430;
        card.OffsetBottom = 130;
        card.AddThemeStyleboxOverride(
            "panel",
            new StyleBoxFlat
            {
                BgColor = Color.FromHtml("#100e18e8"),
                BorderColor = Color.FromHtml("#a36cff"),
                BorderWidthBottom = 1,
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                BorderWidthTop = 1,
                CornerRadiusBottomLeft = 12,
                CornerRadiusBottomRight = 12,
                CornerRadiusTopLeft = 12,
                CornerRadiusTopRight = 12,
                ContentMarginLeft = 32,
                ContentMarginRight = 32,
                ContentMarginTop = 24,
                ContentMarginBottom = 24,
            }
        );

        VBoxContainer content = new()
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        content.AddThemeConstantOverride("separation", 8);
        card.AddChild(content);

        Label heading = new()
        {
            Text = "ARCADE RECORDS",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        heading.AddThemeFontSizeOverride("font_size", 17);
        heading.AddThemeColorOverride("font_color", Color.FromHtml("#bca1e8"));
        content.AddChild(heading);

        attractMapLabel = new()
        {
            Text = "Waiting for charts...",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        attractMapLabel.AddThemeFontSizeOverride("font_size", 25);
        attractMapLabel.AddThemeColorOverride("font_color", Colors.White);
        content.AddChild(attractMapLabel);

        attractScoreLabel = new()
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        attractScoreLabel.AddThemeFontSizeOverride("font_size", 42);
        attractScoreLabel.AddThemeColorOverride("font_color", Color.FromHtml("#80e0c0"));
        content.AddChild(attractScoreLabel);

        attractPlayerLabel = new()
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        attractPlayerLabel.AddThemeFontSizeOverride("font_size", 20);
        attractPlayerLabel.AddThemeColorOverride("font_color", Color.FromHtml("#e9d9ff"));
        content.AddChild(attractPlayerLabel);

        attractAccuracyLabel = new()
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        attractAccuracyLabel.AddThemeFontSizeOverride("font_size", 16);
        attractAccuracyLabel.AddThemeColorOverride("font_color", Color.FromHtml("#b8adc8"));
        content.AddChild(attractAccuracyLabel);

        return card;
    }

    private void showRandomAttractScore()
    {
        if (!IsInstanceValid(attractMapLabel) || !MapManager.Initialized)
            return;

        List<(string Key, Map Map, Leaderboard.Score Score, string ReplayPath)> records = [];
        foreach (Map map in MapManager.Maps)
        {
            string path = Path.Combine(Constants.USER_FOLDER, "pbs", map.Name);
            if (!Leaderboard.TryRead(path, out List<Leaderboard.Score> scores))
                continue;

            records.AddRange(
                scores.Where(score => score.Qualifies)
                    .Select(score =>
                        (
                            $"{map.Name}:{score.AttemptID}",
                            map,
                            score,
                            Path.Combine(Constants.USER_FOLDER, "replays", $"{score.AttemptID}.phxr")
                        )
                    )
            );
        }

        if (records.Count == 0)
        {
            selectedAttractReplayPath = null;
            selectedAttractMap = null;
            attractMapLabel.Text = "No records yet";
            attractScoreLabel.Text = "PLAY TO SET A TOP SCORE";
            attractPlayerLabel.Text = "";
            attractAccuracyLabel.Text = "";
            attractScoreTimer.Stop();
            return;
        }

        List<(string Key, Map Map, Leaderboard.Score Score, string ReplayPath)> replayChoices = records
            .Where(record => File.Exists(record.ReplayPath) && !unavailableAttractReplays.Contains(record.ReplayPath))
            .ToList();
        List<(string Key, Map Map, Leaderboard.Score Score, string ReplayPath)> pool = replayChoices.Count > 0 ? replayChoices : records;
        List<(string Key, Map Map, Leaderboard.Score Score, string ReplayPath)> choices = pool
            .Where(record => record.Key != lastAttractScoreKey)
            .ToList();
        if (choices.Count == 0)
            choices = pool;

        var selected = choices[Random.Shared.Next(choices.Count)];
        lastAttractScoreKey = selected.Key;
        attractMapLabel.Text = selected.Map.PrettyTitle;
        attractScoreLabel.Text = Util.String.PadMagnitude(selected.Score.Value);
        attractPlayerLabel.Text = $"TOP PLAYER  ·  {selected.Score.Player}";
        selectedAttractReplayPath = File.Exists(selected.ReplayPath) && !unavailableAttractReplays.Contains(selected.ReplayPath)
            ? selected.ReplayPath
            : null;
        selectedAttractMap = selected.Map;

        if (selectedAttractReplayPath == null)
        {
            attractAccuracyLabel.Text = "REPLAY NOT FOUND";
            attractScoreTimer.Start();
            return;
        }

        attractAccuracyLabel.Text = $"{selected.Score.Accuracy:F2}% ACCURACY  ·  {selected.Score.Speed:F2}x";
        attractScoreTimer.Stop();
        startAttractReplay();
    }

    private void startAttractReplay()
    {
        if (!attractOverlay.Visible || selectedAttractMap == null || string.IsNullOrEmpty(selectedAttractReplayPath))
            return;

        string replayPath = selectedAttractReplayPath;
        try
        {
            Replay replay = new(replayPath);
            if (!replay.Valid)
            {
                unavailableAttractReplays.Add(replayPath);
                showRandomAttractScore();
                return;
            }

            attractVideo?.Stop();
            if (!Game.PlayAttractReplay(selectedAttractMap, replay))
            {
                Logger.Error($"Unable to start attract replay: {replayPath}");
                attractScoreTimer.Start();
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
                or OverflowException or IndexOutOfRangeException
        )
        {
            Logger.Error($"Unable to read attract replay {replayPath}: {exception.Message}");
            unavailableAttractReplays.Add(replayPath);
            showRandomAttractScore();
        }
    }
}
