using System;
using System.ComponentModel;
using System.IO;
using Godot;
using Updatum;

public partial class Loading : BaseScene
{
    private Color opaque = new(1, 1, 1, 1);
    private Color transparent = new(1, 1, 1, 0);

    [Export]
    private ColorRect background;

    [Export]
    private TextureRect splash;

    [Export]
    private Label progressLabel;

    [Export]
    private Panel progressBar;

    [Export]
    private Panel progressBarFill;

    private ShaderMaterial splashMaterial;
    private VideoStreamPlayer introVideo;
    private Vector2 lastIntroViewportSize;
    private Vector2 lastIntroTextureSize;
    private bool warningVideoPlaying;
    private bool introFinished;
    private bool exitAfterIntro;

    public override async void _Ready()
    {
        base._Ready();

        SoundManager.RefreshMenuMusicPlayback();

        setupSkinIntro();

        splashMaterial = splash.Material as ShaderMaterial;
        splashMaterial.SetShaderParameter("shift", 0);

        progressLabel.Modulate = transparent;
        progressBar.Modulate = transparent;

        bool updateFound = false;
        try
        {
            updateFound = await Releases.CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            Logger.Log($"Could not get latest release: {ex.Message}");
        }

        if (updateFound)
        {
            var popup = new OptionPopup("Update Found", "Would you like to download the new version?");

            popup.AddOption("Update", Callable.From(updateStep));
            popup.AddOption("Cancel", Callable.From(mapInitializeStep));

            popup.Canceled += mapInitializeStep;

            popup.Show();
        }
        else
        {
            mapInitializeStep();
        }
    }

    public void UpdateProgressBar(float progress)
    {
        progressBarFill.AnchorRight = progress;
    }

    public void UpdateProgressLabel(string label)
    {
        progressLabel.Text = label;
    }

    public override void _Process(double delta)
    {
        fitIntroVideo();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (introVideo == null || !introVideo.IsPlaying() || !@event.IsPressed())
        {
            return;
        }

        if (@event is InputEventKey or InputEventMouseButton or InputEventJoypadButton)
        {
            if (warningVideoPlaying)
            {
                finishWarningVideo();
            }
            else
            {
                finishIntro();
            }
        }
    }

    private void setupSkinIntro()
    {
        if (SkinManager.Instance == null)
        {
            return;
        }

        string skinDirectory = Path.Combine(Constants.USER_FOLDER, "skins", SettingsManager.Instance.Settings.Skin.Value);
        string warningPath = Path.Combine(skinDirectory, "warning.ogv");
        if (File.Exists(warningPath) && startSkinVideo(warningPath, "SkinWarning", -80))
        {
            warningVideoPlaying = true;
            SoundManager.SetSkinWarningAudioMuted(true);
            introVideo.Finished += finishWarningVideo;
            return;
        }

        startIntroVideo(skinDirectory);
    }

    private void startIntroVideo(string skinDirectory)
    {
        string introPath = Path.Combine(skinDirectory, "intro.ogv");
        if (startSkinVideo(introPath, "SkinIntro", 0))
        {
            introVideo.Finished += finishIntro;
            return;
        }

        introFinished = true;
        if (exitAfterIntro)
        {
            exitAfterIntro = false;
            exit();
        }
    }

    private bool startSkinVideo(string videoPath, string playerName, float volumeDb)
    {
        if (!File.Exists(videoPath))
        {
            return false;
        }

        string playbackDirectory = Path.Combine(OS.GetUserDataDir(), "skin-intro");
        string fileName = Path.GetFileName(videoPath);
        string playbackPath = Path.Combine(playbackDirectory, fileName);
        try
        {
            Directory.CreateDirectory(playbackDirectory);
            if (!string.Equals(Path.GetFullPath(videoPath), Path.GetFullPath(playbackPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(videoPath, playbackPath, true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Logger.Error($"Unable to prepare skin video '{fileName}': {exception.Message}");
            return false;
        }

        VideoStream stream = ResourceLoader.Load<VideoStream>($"user://skin-intro/{fileName}");
        if (stream == null)
        {
            Logger.Log($"Could not load skin video: {videoPath}", true);
            return false;
        }

        introVideo = new VideoStreamPlayer
        {
            Name = playerName,
            Stream = stream,
            Autoplay = false,
            Loop = false,
            Expand = true,
            VolumeDb = volumeDb,
            ZIndex = 100,
        };
        introVideo.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
        introVideo.Size = GetViewport().GetVisibleRect().Size;
        lastIntroTextureSize = Vector2.Zero;
        lastIntroViewportSize = Vector2.Zero;
        AddChild(introVideo);
        introVideo.Play();
        return true;
    }

    private void fitIntroVideo()
    {
        if (introVideo == null || !introVideo.IsPlaying())
        {
            return;
        }

        Vector2 textureSize = introVideo.GetVideoTexture()?.GetSize() ?? Vector2.Zero;
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        if (textureSize.X <= 0 || textureSize.Y <= 0 || viewportSize.X <= 0 || viewportSize.Y <= 0)
        {
            return;
        }

        if (textureSize == lastIntroTextureSize && viewportSize == lastIntroViewportSize)
        {
            return;
        }

        lastIntroTextureSize = textureSize;
        lastIntroViewportSize = viewportSize;

        float scale = Mathf.Min(viewportSize.X / textureSize.X, viewportSize.Y / textureSize.Y);
        Vector2 fittedSize = textureSize * scale;
        introVideo.Size = fittedSize;
        introVideo.Position = (viewportSize - fittedSize) / 2;
    }

    private void finishWarningVideo()
    {
        if (!warningVideoPlaying || introVideo == null)
        {
            return;
        }

        warningVideoPlaying = false;
        introVideo.Stop();
        introVideo.QueueFree();
        introVideo = null;
        SoundManager.SetSkinWarningAudioMuted(false);
        startIntroVideo(Path.Combine(Constants.USER_FOLDER, "skins", SettingsManager.Instance.Settings.Skin.Value));
    }

    private void finishIntro()
    {
        if (introFinished || introVideo == null)
        {
            return;
        }

        introFinished = true;
        introVideo.Stop();
        introVideo.QueueFree();
        introVideo = null;

        if (exitAfterIntro)
        {
            exitAfterIntro = false;
            exit();
        }
    }

    private Tween enter()
    {
        var inTween = CreateTween().SetTrans(Tween.TransitionType.Quad).SetParallel();
        inTween.TweenProperty(background, "color", Color.FromHtml("#060509"), 1);
        inTween.TweenProperty(progressLabel, "modulate", opaque, 0.5);
        inTween.TweenProperty(progressBar, "modulate", opaque, 0.5);
        inTween
            .SetTrans(Tween.TransitionType.Quint)
            .SetEase(Tween.EaseType.Out)
            .TweenMethod(
                Callable.From(
                    (float shift) =>
                    {
                        splashMaterial.SetShaderParameter("shift", shift);
                    }
                ),
                0.2,
                1.0,
                1.5
            );

        return inTween;
    }

    private void exit()
    {
        if (introVideo?.IsPlaying() == true || warningVideoPlaying)
        {
            exitAfterIntro = true;
            return;
        }

        var outTween = CreateTween().SetTrans(Tween.TransitionType.Quad).SetParallel();
        outTween.TweenProperty(background, "color", Color.Color8(0, 0, 0), 0.25);
        outTween
            .Chain()
            .TweenCallback(
                Callable.From(() =>
                {
                    SceneManager.Load("res://scenes/main_menu.tscn");
                })
            );
    }

    private void updateStep()
    {
        enter();

        progressLabel.Text = $"Downloading {Releases.MANAGER.DownloadedPercentage} %";

        Releases.MANAGER.PropertyChanged += updateDownloadBar;
        Releases.MANAGER.DownloadCompleted += (_, _) =>
        {
            Releases.MANAGER.PropertyChanged -= updateDownloadBar;
            progressLabel.Text = "Installing";
        };
        Releases.UpdateToLatest();
    }

    private void mapInitializeStep()
    {
        int toSync = MapCache.FilesToSync.Value;
        bool allSynced = MapCache.FilesSynced.Value == toSync;

        if (allSynced)
        {
            progressLabel.Text = "Done!";
            progressBarFill.AnchorRight = 1;
        }
        else
        {
            MapCache.FilesSynced.ValueChanged += (_, _) =>
            {
                if (allSynced)
                {
                    return;
                }

                int synced = MapCache.FilesSynced.Value;
                float progress = synced / (float)toSync;

                progressLabel.Text = $"Initializing maps ({synced}/{toSync})";
                progressBarFill.AnchorRight = progress;

                if (progress >= 1)
                {
                    allSynced = true;
                    progressLabel.Text = "Done!";
                }
            };
        }

        var inTween = enter();

        inTween.Chain();

        inTween.TweenCallback(
            Callable.From(() =>
            {
                if (MapManager.Initialized)
                {
                    exit();
                }
                else
                {
                    MapManager.MapsInitialized += _ => Callable.From(exit).CallDeferred();
                }
            })
        );
    }

    private void updateDownloadBar(object _, PropertyChangedEventArgs @event)
    {
        if (@event.PropertyName == nameof(UpdatumManager.DownloadedPercentage))
        {
            CallDeferred("UpdateProgressLabel", $"Downloading {Releases.MANAGER.DownloadedPercentage} %");
            float progress = (float)Releases.MANAGER.DownloadedPercentage / 100;
            CallDeferred("UpdateProgressBar", progress);
        }
    }
}
