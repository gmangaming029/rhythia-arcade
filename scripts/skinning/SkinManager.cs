using System.IO;
using Godot;
using Tomlyn;

[GlobalClass]
public partial class SkinManager : Node
{
    public static SkinManager Instance { get; private set; }

    [Signal]
    public delegate void SavedEventHandler();

    [Signal]
    public delegate void LoadedEventHandler(SkinProfile skin);

    public SkinProfile Skin { get; set; } = new SkinProfile();

    public override void _Ready()
    {
        Instance = this;
    }

    public static void Save()
    {
        var settings = SettingsManager.Instance.Settings;
        var skin = Instance.Skin;

        if (Toml.TryFromModel(skin.Config, out string toml, out _))
        {
            File.WriteAllText($"{Constants.USER_FOLDER}/skins/{settings.Skin.Value}/config.toml", toml);
        }

        Logger.Log($"Saved skin {settings.Skin.Value}");

        Instance.EmitSignal(SignalName.Saved);
    }

    public static void Load()
    {
        var settings = SettingsManager.Instance.Settings;
        var skin = Instance.Skin;

        // Config

        skin.Config = new();

        string configFile = $"{Constants.USER_FOLDER}/skins/{settings.Skin.Value}/config.toml";

        if (File.Exists(configFile) && Toml.TryToModel(File.ReadAllText(configFile), out SkinConfig config, out _))
        {
            skin.Config = config;
        }
        else if (Toml.TryFromModel(skin.Config, out string toml, out _))
        {
            File.WriteAllText($"{Constants.USER_FOLDER}/skins/{settings.Skin.Value}/config.toml", toml);
        }

        // Textures

        skin.CursorImage = loadTexture("game/cursor.png");
        skin.GridImage = loadTexture("game/grid.png");
        skin.GridGuidesImage = loadTexture("game/grid_guides.png");
        skin.VisibilityAssistImage = loadTexture("game/visibility_assist.png");
        skin.PanelLeftBackgroundImage = loadTexture("game/panel_left_background.png");
        skin.PanelRightBackgroundImage = loadTexture("game/panel_right_background.png");
        skin.HealthImage = loadTexture("game/health.png");
        skin.HealthBackgroundImage = loadTexture("game/health_background.png");
        skin.ProgressImage = loadTexture("game/progress.png");
        skin.ProgressBackgroundImage = loadTexture("game/progress_background.png");
        skin.HitsImage = loadTexture("game/hits.png");
        skin.MissesImage = loadTexture("game/misses.png");
        skin.MissFeedbackImage = loadTexture("game/miss_feedback.png");

        skin.SettingsButtonImage = loadTexture("ui/buttons/settings.png");
        skin.OpenFolderButtonImage = loadTexture("ui/buttons/open_folder.png");
        skin.ImportButtonImage = loadTexture("ui/buttons/import.png");
        skin.RandomButtonImage = loadTexture("ui/buttons/random.png");
        skin.FilterButtonImage = loadTexture("ui/buttons/filter.png");
        skin.SortButtonImage = loadTexture("ui/buttons/sort.png");
        skin.SortAscendButtonImage = loadTexture("ui/buttons/sort_ascend.png");
        skin.AuthorButtonImage = loadTexture("ui/buttons/author.png");
        skin.SearchButtonImage = loadTexture("ui/buttons/search.png");
        skin.LayoutListButtonImage = loadTexture("ui/buttons/layout_list.png");
        skin.LayoutGridButtonImage = loadTexture("ui/buttons/layout_grid.png");

        skin.SpeedPresetMinusMinusButtonImage = loadTexture("ui/buttons/speed_minus_minus.png");
        skin.SpeedPresetMinusButtonImage = loadTexture("ui/buttons/speed_minus.png");
        skin.SpeedPresetMiddleButtonImage = loadTexture("ui/buttons/speed_middle.png");
        skin.SpeedPresetPlusButtonImage = loadTexture("ui/buttons/speed_plus.png");
        skin.SpeedPresetPlusPlusButtonImage = loadTexture("ui/buttons/speed_plus_plus.png");

        skin.PlayButtonImage = loadTexture("ui/buttons/play.png");
        skin.FavoriteButtonImage = loadTexture("ui/buttons/favorite.png");
        skin.UnfavoriteButtonImage = loadTexture("ui/buttons/unfavorite.png");
        skin.CopyButtonImage = loadTexture("ui/buttons/copy.png");
        skin.DeleteButtonImage = loadTexture("ui/buttons/delete.png");
        skin.AddVideoButtonImage = loadTexture("ui/buttons/add_video.png");
        skin.RemoveVideoButtonImage = loadTexture("ui/buttons/remove_video.png");

        skin.GrabberNormalImage = loadTexture("ui/buttons/grabber_normal.png");
        skin.GrabberPressedImage = loadTexture("ui/buttons/grabber_pressed.png");
        skin.GrabberTickImage = loadTexture("ui/buttons/grabber_tick.png");

        skin.JukeboxPlayImage = loadTexture("ui/jukebox_play.png");
        skin.JukeboxPauseImage = loadTexture("ui/jukebox_pause.png");
        skin.JukeboxSkipImage = loadTexture("ui/jukebox_skip.png");
        skin.BackgroundTileImage = loadTexture("ui/background_tile.png");

        skin.FavoriteImage = loadTexture("ui/play/favorite.png");
        skin.MapListMaskImage = loadTexture("ui/play/maplist_mask.png");
        skin.MapListSelectionCursorImage = loadTexture("ui/play/maplist_selection_cursor.png");
        skin.MapListScrollBarTopImage = loadTexture("ui/play/scrollbar_top.png");
        skin.MapListScrollBarMiddleImage = loadTexture("ui/play/scrollbar_middle.png");
        skin.MapListScrollBarBottomImage = loadTexture("ui/play/scrollbar_bottom.png");
        skin.MapListScrollBarBackgroundTopImage = loadTexture("ui/play/scrollbar_background_top.png");
        skin.MapListScrollBarBackgroundMiddleImage = loadTexture("ui/play/scrollbar_background_middle.png");
        skin.MapListScrollBarBackgroundBottomImage = loadTexture("ui/play/scrollbar_background_bottom.png");
        skin.MapListGridCoverBackgroundImage = loadTexture("ui/play/grid_cover_background.png");

        skin.MapInfoCoverBackgroundImage = loadTexture("ui/play/mapinfo_cover_background.png");

        skin.ModNoFailImage = loadTexture("modifiers/nofail.png");
        skin.ModAutoplayImage = loadTexture("modifiers/autoplay.png");
        skin.ModGhostImage = loadTexture("modifiers/ghost.png");
        skin.ModStrobeImage = loadTexture("modifiers/strobe.png");
        skin.ModChaosImage = loadTexture("modifiers/chaos.png");
        skin.ModVortexImage = loadTexture("modifiers/vortex.png");
        skin.ModEarthquakeImage = loadTexture("modifiers/earthquake.png");
        skin.ModHFlipImage = loadTexture("modifiers/hflip.png");
        skin.ModVFlipImage = loadTexture("modifiers/vflip.png");

        // Shaders

        skin.BackgroundTileShader = loadShader("ui/background_tile.gdshader");
        skin.MapButtonCoverShader = loadShader("ui/play/map_button_cover.gdshader");

        // Sounds

        skin.HitSoundBuffer = loadSound("hit.mp3");
        skin.MissSoundBuffer = loadSound("miss.mp3");
        skin.FailSoundBuffer = loadSound("fail.mp3");
        skin.MenuMusicBuffer = loadMenuMusic();

        // Meshes

        if (settings.NoteMesh == "skin")
        {
            skin.NoteMesh = loadMesh($"{Constants.USER_FOLDER}/meshes/{skin.Config.NoteMesh}.obj");
            skin.NoteTexture = loadNoteTexture(skin.Config.NoteTexture);
        }
        else if (settings.NoteMesh.Value.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
        {
            skin.NoteMesh = loadMesh($"{Constants.USER_FOLDER}/meshes/{skin.Config.NoteMesh}.obj");
            skin.NoteTexture = loadNoteTexture(settings.NoteMesh.Value);
        }
        else
        {
            skin.NoteMesh = loadMesh($"{Constants.USER_FOLDER}/meshes/{settings.NoteMesh.Value}.obj");
            skin.NoteTexture = null;
        }

        // Colors

        string colorsetPath =
            $"{Constants.USER_FOLDER}/colorsets/{(settings.NoteColors == "skin" ? skin.Config.NoteColors : settings.NoteColors)}.txt";

        if (File.Exists(colorsetPath))
        {
            string[] split = File.ReadAllText(colorsetPath).StripEdges().ReplaceLineEndings(",").Split(",");
            Color[] colors = new Color[split.Length];

            for (int i = 0; i < split.Length; i++)
            {
                colors[i] = Util.Misc.ParseColor(split[i], Colors.White);
            }

            skin.NoteColors = colors;
        }

        // Spaces

        skin.GameSpace = loadSpace(settings.GameSpace == "skin" ? skin.Config.GameSpace : settings.GameSpace.Value);
        skin.MenuSpace = loadSpace(settings.MenuSpace == "skin" ? skin.Config.MenuSpace : settings.MenuSpace.Value);

        /////

        if (!SettingsManager.HideNotifications)
        {
            ToastNotification.Notify($"Loaded skin [{settings.Skin.Value}]");
        }

        Logger.Log($"Loaded skin {settings.Skin.Value}");

        Instance.EmitSignal(SignalName.Loaded, skin);
    }

    public static void Reload()
    {
        Save();
        Load();
    }

    public static void UpdateVideoBackgroundDistance(double distance)
    {
        if (Instance == null)
        {
            return;
        }

        if (Instance.Skin.MenuSpace is VideoSpace menuVideoSpace)
        {
            menuVideoSpace.SetBackgroundDistance(distance);
        }

        if (Instance.Skin.GameSpace is VideoSpace gameVideoSpace)
        {
            gameVideoSpace.SetBackgroundDistance(distance);
        }
    }

    public static BaseSpace CreateSpaceForEditor(bool gameSpace)
    {
        string spaceName = gameSpace
            ? SettingsManager.Instance.Settings.GameSpace == "skin"
                ? Instance.Skin.Config.GameSpace
                : SettingsManager.Instance.Settings.GameSpace.Value
            : SettingsManager.Instance.Settings.MenuSpace == "skin"
                ? Instance.Skin.Config.MenuSpace
                : SettingsManager.Instance.Settings.MenuSpace.Value;

        return loadSpace(spaceName);
    }

    private static ImageTexture loadTexture(string skinPath)
    {
        var settings = SettingsManager.Instance.Settings;
        string fullPath = $"{Constants.USER_FOLDER}/skins/{settings.Skin.Value}/{skinPath}";

        if (!File.Exists(fullPath))
        {
            string fallbackPath = $"{Constants.USER_FOLDER}/skins/default/{skinPath}";
            if (!File.Exists(fallbackPath))
            {
                return null;
            }

            fullPath = fallbackPath;
        }

        var image = Image.LoadFromFile(fullPath);
        if (image == null)
            return null;

        image.Convert(Image.Format.Rgba8);
        image.GenerateMipmaps();

        return ImageTexture.CreateFromImage(image);
    }

    private static byte[] loadSound(string skinPath)
    {
        var settings = SettingsManager.Instance.Settings;
        string path = $"{Constants.USER_FOLDER}/skins/{settings.Skin.Value}/{skinPath}";
        byte[] buffer = [];

        if (File.Exists(path))
        {
            Godot.FileAccess file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            buffer = file.GetBuffer((long)file.GetLength());
            file.Close();
        }

        return buffer;
    }

    private static byte[] loadBundledMenuMusic()
    {
        const string path = "res://Rhythia_-_Menu_Theme_Redshift_KLICKAUD.mp3";
        Godot.FileAccess file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file == null)
        {
            Logger.Log($"Could not open bundled menu theme: {Godot.FileAccess.GetOpenError()}", true);
            return loadSound("menu.mp3");
        }

        byte[] buffer = file.GetBuffer((long)file.GetLength());
        file.Close();

        if (buffer.Length < 4)
        {
            Logger.Log($"Bundled menu theme is empty or invalid: {path}", true);
            return loadSound("menu.mp3");
        }

        return buffer;
    }

    private static byte[] loadMenuMusic()
    {
        if (SettingsManager.Instance.Settings.Skin.Value != "default")
        {
            byte[] skinMusic = loadSound("menu.mp3");
            if (skinMusic.Length > 0)
            {
                return skinMusic;
            }
        }

        return loadBundledMenuMusic();
    }

    private static Shader loadShader(string skinPath)
    {
        var settings = SettingsManager.Instance.Settings;
        string shader = File.ReadAllText($"{Constants.USER_FOLDER}/skins/{settings.Skin.Value}/{skinPath}");

        return new() { Code = shader };
    }

    private static ArrayMesh loadMesh(string path)
    {
        string mtlPath = path.TrimSuffix(".obj") + ".mtl";

        return resourceExists(path)
            ? Util.Misc.OBJParser.Call("load_obj", path, resourceExists(mtlPath) ? mtlPath : null).As<ArrayMesh>()
            : GD.Load<ArrayMesh>("res://user/meshes/squircle.obj");
    }

    private static ImageTexture loadNoteTexture(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (
            fileName.Contains('/') || fileName.Contains('\\')
            || fileName != Path.GetFileName(fileName)
            || !string.Equals(Path.GetExtension(fileName), ".png", System.StringComparison.OrdinalIgnoreCase)
        )
        {
            Logger.Log($"Note texture must be a PNG file in the user meshes folder: {fileName}", true);
            return null;
        }

        string path = Path.Combine(Constants.USER_FOLDER, "meshes", fileName);
        if (!File.Exists(path))
        {
            Logger.Log($"Could not find note texture: {path}", true);
            return null;
        }

        var image = Image.LoadFromFile(path);
        if (image == null || image.IsEmpty())
        {
            Logger.Log($"Could not load note texture: {path}", true);
            return null;
        }

        image.Convert(Image.Format.Rgba8);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static BaseSpace loadSpace(string spaceName)
    {
        if (spaceName.StartsWith("video:", System.StringComparison.Ordinal))
        {
            string fileName = spaceName["video:".Length..];
            if (
                fileName != Path.GetFileName(fileName)
                || !string.Equals(Path.GetExtension(fileName), ".ogv", System.StringComparison.OrdinalIgnoreCase)
            )
            {
                Logger.Log($"Invalid video background selection: {spaceName}", true);
                return GD.Load<PackedScene>("res://prefabs/spaces/void.tscn").Instantiate<BaseSpace>();
            }

            string videoPath = Path.Combine(Constants.USER_FOLDER, "spaces", fileName);
            if (!File.Exists(videoPath))
            {
                Logger.Log($"Could not find background video: {videoPath}", true);
                return GD.Load<PackedScene>("res://prefabs/spaces/void.tscn").Instantiate<BaseSpace>();
            }

            return new VideoSpace { VideoPath = videoPath, BackgroundId = spaceName };
        }

        string path = $"res://prefabs/spaces/{spaceName}.tscn";
        BaseSpace space = GD.Load<PackedScene>(resourceExists(path) ? path : "res://prefabs/spaces/void.tscn")
            .Instantiate<Node3D>() as BaseSpace;
        space.BackgroundId = spaceName;
        return space;
    }

    private static bool resourceExists(string path)
    {
        return ResourceLoader.Exists(path) || Godot.FileAccess.FileExists(path);
    }
}
