using System;
using Godot;

public partial class DisplayTestOverlay : CanvasLayer
{
    private static readonly (string Title, string Description)[] patterns =
    [
        ("Color Bars", "Check that colors look distinct and that no band blends into its neighbors."),
        ("Grayscale", "Check the gray ramp and distinguishable near-black and near-white patches."),
        ("Geometry and Safe Area", "Check that the circle looks round and all four borders remain visible."),
        ("CRT Checks", "Check the overscan frames, fine stripes, and aligned red, green, and blue marks."),
        ("Gameplay Readability", "Check that sample notes, score, combo, accuracy, and health remain legible."),
    ];

    private Control root;
    private DisplayTestPattern patternView;
    private Label titleLabel;
    private Label descriptionLabel;
    private Label pageLabel;
    private int patternIndex;
    private Input.MouseModeEnum previousMouseMode;

    public event Action Closed;

    public bool IsOpen => root?.Visible ?? false;

    public override void _Ready()
    {
        root = new Control
        {
            Name = "DisplayTest",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        ColorRect backdrop = new()
        {
            Color = new Color("#090b11"),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(backdrop);

        MarginContainer margin = new();
        margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 32);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_right", 32);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        root.AddChild(margin);

        VBoxContainer layout = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        margin.AddChild(layout);

        HBoxContainer header = new();
        layout.AddChild(header);

        titleLabel = new Label
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 28);
        header.AddChild(titleLabel);

        Button closeButton = new() { Text = "Close  [Esc]", FocusMode = Control.FocusModeEnum.None };
        closeButton.Pressed += CloseTest;
        header.AddChild(closeButton);

        descriptionLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 32),
        };
        descriptionLabel.AddThemeColorOverride("font_color", new Color("#c4cbd7"));
        layout.AddChild(descriptionLabel);

        patternView = new DisplayTestPattern
        {
            CustomMinimumSize = new Vector2(480, 320),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            ClipContents = true,
        };
        layout.AddChild(patternView);

        HBoxContainer footer = new();
        layout.AddChild(footer);

        Button previousButton = new() { Text = "Previous", FocusMode = Control.FocusModeEnum.None };
        previousButton.Pressed += () => selectPattern(patternIndex - 1);
        footer.AddChild(previousButton);

        pageLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        footer.AddChild(pageLabel);

        Button nextButton = new() { Text = "Next", FocusMode = Control.FocusModeEnum.None };
        nextButton.Pressed += () => selectPattern(patternIndex + 1);
        footer.AddChild(nextButton);

        Label help = new()
        {
            Text = "Use Left/Right or 1-5 to change tests. Compare this screen on each display.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        help.AddThemeColorOverride("font_color", new Color("#929baa"));
        layout.AddChild(help);

        root.Visible = false;
        selectPattern(0);
    }

    public void ShowTest()
    {
        previousMouseMode = Input.MouseMode;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        root.Visible = true;
    }

    public void CloseTest()
    {
        if (!IsOpen)
        {
            return;
        }

        root.Visible = false;
        Input.MouseMode = previousMouseMode;
        Closed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen)
        {
            return;
        }

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            switch (key.Keycode)
            {
                case Key.Escape:
                    CloseTest();
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.Left:
                case Key.Pageup:
                    selectPattern(patternIndex - 1);
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.Right:
                case Key.Pagedown:
                    selectPattern(patternIndex + 1);
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.Key1:
                case Key.Key2:
                case Key.Key3:
                case Key.Key4:
                case Key.Key5:
                    selectPattern((int)key.Keycode - (int)Key.Key1);
                    GetViewport().SetInputAsHandled();
                    return;
            }
        }
    }

    private void selectPattern(int index)
    {
        patternIndex = (index % patterns.Length + patterns.Length) % patterns.Length;
        if (titleLabel != null)
            titleLabel.Text = patterns[patternIndex].Title;
        if (descriptionLabel != null)
            descriptionLabel.Text = patterns[patternIndex].Description;
        if (pageLabel != null)
            pageLabel.Text = $"{patternIndex + 1} / {patterns.Length}";

        if (patternView != null)
        {
            patternView.Pattern = (DisplayTestPattern.PatternType)patternIndex;
            patternView.QueueRedraw();
        }
    }
}
