using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

public sealed class SidePanelLayout
{
    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
    private static readonly string filePath = Path.Combine(Constants.USER_FOLDER, "side-panel-layout.json");
    private static readonly Dictionary<string, SidePanelWidgetLayout> defaultWidgets = new();

    public Dictionary<string, SidePanelWidgetLayout> Widgets { get; set; } = new();

    public static SidePanelLayout Load()
    {
        if (!File.Exists(filePath))
            return new SidePanelLayout();

        try
        {
            return JsonSerializer.Deserialize<SidePanelLayout>(File.ReadAllText(filePath)) ?? new SidePanelLayout();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.Error($"Unable to load side panel layout: {exception.Message}");
            return new SidePanelLayout();
        }
    }

    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, JsonSerializer.Serialize(this, jsonOptions));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Logger.Error($"Unable to save side panel layout: {exception.Message}");
            return false;
        }
    }

    public static void CaptureDefault(string key, Control control, SubViewport viewport, bool visible = true)
    {
        if (!defaultWidgets.ContainsKey(key))
        {
            SidePanelWidgetLayout widget = SidePanelWidgetLayout.From(control, viewport);
            widget.Visible = visible;
            defaultWidgets.Add(key, widget);
        }
    }

    public static SidePanelLayout CreateDefault()
    {
        SidePanelLayout defaults = new();
        foreach ((string key, SidePanelWidgetLayout widget) in defaultWidgets)
            defaults.Widgets.Add(key, widget.Copy());
        return defaults;
    }

    public void Apply(string key, Control control, SubViewport viewport, bool allowVisible)
    {
        if (!Widgets.TryGetValue(key, out SidePanelWidgetLayout widget))
            return;

        control.AnchorLeft = 0;
        control.AnchorTop = 0;
        control.AnchorRight = 0;
        control.AnchorBottom = 0;
        control.OffsetLeft = widget.X * viewport.Size.X;
        control.OffsetTop = widget.Y * viewport.Size.Y;
        control.OffsetRight = control.OffsetLeft + widget.Width * viewport.Size.X;
        control.OffsetBottom = control.OffsetTop + widget.Height * viewport.Size.Y;
        control.Visible = widget.Visible && allowVisible;
    }
}

public sealed class SidePanelWidgetLayout
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 1;
    public float Height { get; set; } = 1;
    public bool Visible { get; set; } = true;

    public static SidePanelWidgetLayout From(Control control, SubViewport viewport)
    {
        Rect2 rect = control.GetRect();
        return new SidePanelWidgetLayout
        {
            X = rect.Position.X / viewport.Size.X,
            Y = rect.Position.Y / viewport.Size.Y,
            Width = rect.Size.X / viewport.Size.X,
            Height = rect.Size.Y / viewport.Size.Y,
            Visible = control.Visible,
        };
    }

    public SidePanelWidgetLayout Copy()
    {
        return new SidePanelWidgetLayout { X = X, Y = Y, Width = Width, Height = Height, Visible = Visible };
    }
}
