using System;
using System.IO;
using Godot;

public partial class VideoSpace : BaseSpace
{
    private const float DefaultBackgroundDistance = 100f;

    public string VideoPath { get; set; }

    private Camera3D videoCamera;
    private MeshInstance3D videoBackground;
    private float videoAspectRatio = 16f / 9f;
    private double backgroundDistance = DefaultBackgroundDistance;

    public override void _Ready()
    {
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Colors.Black,
        };
        AddChild(new WorldEnvironment { Name = "WorldEnvironment", Environment = environment });
        videoCamera = new Camera3D { Name = "Camera3D", Current = true, Fov = 70 };
        AddChild(videoCamera);
        SetBackgroundDistance(
            SettingsManager.Instance == null
                ? DefaultBackgroundDistance
                : SettingsManager.Instance.Settings.VideoBackgroundDistance
        );

        if (
            string.IsNullOrWhiteSpace(VideoPath)
            || !Path.IsPathRooted(VideoPath)
            || !string.Equals(Path.GetExtension(VideoPath), ".ogv", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(VideoPath)
        )
        {
            Logger.Log($"Could not load background video: {VideoPath}", true);
            base._Ready();
            return;
        }

        string fileName = Path.GetFileName(VideoPath);
        string playbackPath;
        if (
            string.Equals(
                Path.GetFullPath(Constants.USER_FOLDER),
                Path.GetFullPath(OS.GetUserDataDir()),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            playbackPath = $"user://spaces/{fileName}";
        }
        else
        {
            string playbackDirectory = Path.Combine(OS.GetUserDataDir(), "video-backgrounds");
            string playbackFile = Path.Combine(playbackDirectory, fileName);
            try
            {
                Directory.CreateDirectory(playbackDirectory);
                File.Copy(VideoPath, playbackFile, true);
                playbackPath = $"user://video-backgrounds/{fileName}";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Logger.Error($"Unable to prepare the background video: {exception.Message}");
                base._Ready();
                return;
            }
        }

        VideoStream stream = ResourceLoader.Load<VideoStream>(playbackPath);
        if (stream == null)
        {
            Logger.Log($"Could not load background video: {VideoPath}", true);
            base._Ready();
            return;
        }

        var viewport = new SubViewport
        {
            Name = "VideoViewport",
            Size = new Vector2I(1280, 720),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        videoAspectRatio = viewport.Size.X / (float)viewport.Size.Y;
        AddChild(viewport);

        var player = new VideoStreamPlayer
        {
            Name = "VideoPlayer",
            Stream = stream,
            Loop = true,
            Expand = true,
            VolumeDb = -80,
        };
        player.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        viewport.AddChild(player);

        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoTexture = viewport.GetTexture(),
        };
        videoBackground = new MeshInstance3D
        {
            Name = "VideoBackground",
            Mesh = new QuadMesh { Material = material },
        };
        AddChild(videoBackground);
        updateBackgroundTransform();

        base._Ready();
        player.Play();
    }

    public void SetBackgroundDistance(double distance)
    {
        backgroundDistance = Math.Clamp(distance, 20, 200);
        updateBackgroundTransform();
    }

    private void updateBackgroundTransform()
    {
        if (videoCamera == null || videoBackground?.Mesh is not QuadMesh quad)
        {
            return;
        }

        float distance = (float)backgroundDistance;
        float height = 2f * distance * Mathf.Tan(Mathf.DegToRad(videoCamera.Fov / 2f)) * 1.05f;
        float width = height * videoAspectRatio;
        quad.Size = new Vector2(width, height);
        videoBackground.Position = new Vector3(0, 0, -distance);
    }
}
