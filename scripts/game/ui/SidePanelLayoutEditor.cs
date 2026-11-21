using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class SidePanelLayoutEditor : CanvasLayer
{
    private sealed class PanelPreview
    {
        public string Name;
        public SubViewport Viewport;
        public Control Frame;
        public Dictionary<string, Control> Handles = new();
    }

    private readonly Dictionary<string, Control> widgets = new();
    private readonly Dictionary<string, string> widgetPanels = new();
    private readonly Dictionary<string, PanelPreview> previews = new();
    private OptionButton selector;
    private Control root;
    private Label status;
    private SpinBox xInput;
    private SpinBox yInput;
    private SpinBox widthInput;
    private SpinBox heightInput;
    private CheckButton visibilityInput;
    private bool updating;
    private bool dragging;
    private bool normalHudVisible;
    private bool altComboEnabled;
    private string selectedKey;
    private SidePanelLayout layout;

    public event Action Closed;

    public void OpenEditor(HudManager hudManager)
    {
        layout = SidePanelLayout.Load();
        widgets.Clear();
        widgetPanels.Clear();
        foreach (PanelPreview preview in previews.Values)
            preview.Frame.QueueFree();
        previews.Clear();
        selector.Clear();

        PanelLeft left = hudManager.GetNode<PanelLeft>("PanelLeft");
        PanelRight right = hudManager.GetNode<PanelRight>("PanelRight");
        normalHudVisible =
            !hudManager.Runner.Attempt.Settings.SimpleHUD.Value
            && !hudManager.Runner.Attempt.Settings.SuperSimpleHUD.Value;
        altComboEnabled = hudManager.Runner.Attempt.Settings.AltComboCounter.Value;
        addPanel("Left", left.GetNode<SubViewport>("PanelLeftViewport"));
        addPanel("Right", right.GetNode<SubViewport>("PanelRightViewport"));

        Input.MouseMode = Input.MouseModeEnum.Visible;
        root.Visible = true;
        selectWidget(widgets.Keys.FirstOrDefault());
        updateHandles();
    }

    public override void _Ready()
    {
        Layer = 120;
        root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Stop };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        ColorRect dimmer = new()
        {
            Color = new Color("#080b12c8"),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        dimmer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(dimmer);

        PanelContainer panel = new()
        {
            Name = "EditorPanel",
            Position = new Vector2(18, 18),
            Size = new Vector2(360, 690),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        panel.AddThemeStyleboxOverride(
            "panel",
            new StyleBoxFlat
            {
                BgColor = new Color("#111722f2"),
                ContentMarginLeft = 18,
                ContentMarginRight = 18,
                ContentMarginTop = 16,
                ContentMarginBottom = 16,
            }
        );
        root.AddChild(panel);

        VBoxContainer content = new() { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        panel.AddChild(content);
        HBoxContainer titleRow = new();
        content.AddChild(titleRow);
        Label title = new()
        {
            Text = "Side Panel Layout",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        titleRow.AddChild(title);
        Button close = new() { Text = "Close  [Esc]", FocusMode = Control.FocusModeEnum.None };
        close.Pressed += closeEditor;
        titleRow.AddChild(close);

        content.AddChild(new Label { Text = "Drag a widget in the preview, or adjust its position and size below." });

        selector = new() { Name = "WidgetSelector" };
        content.AddChild(selector);
        selector.ItemSelected += index =>
        {
            if (index >= 0 && index < widgets.Count)
                selectWidget(selector.GetItemMetadata((int)index).AsString());
        };

        visibilityInput = new CheckButton { Text = "Show selected widget" };
        visibilityInput.Toggled += visible =>
        {
            if (!updating && selectedKey != null)
            {
                layout.Widgets[selectedKey].Visible = visible;
                widgets[selectedKey].Visible = visible;
                updateHandles();
            }
        };
        content.AddChild(visibilityInput);

        xInput = addNumberField(content, "Horizontal position (%)", 0, 100, 0.5);
        yInput = addNumberField(content, "Vertical position (%)", 0, 100, 0.5);
        widthInput = addNumberField(content, "Width (%)", 1, 200, 0.5);
        heightInput = addNumberField(content, "Height (%)", 1, 200, 0.5);
        xInput.ValueChanged += value => updateSelected("X", (float)value / 100);
        yInput.ValueChanged += value => updateSelected("Y", (float)value / 100);
        widthInput.ValueChanged += value => updateSelected("Width", (float)value / 100);
        heightInput.ValueChanged += value => updateSelected("Height", (float)value / 100);

        HBoxContainer actions = new();
        content.AddChild(actions);
        Button reset = new()
        {
            Text = "Reset layout",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
        };
        reset.Pressed += resetLayout;
        actions.AddChild(reset);
        Button save = new()
        {
            Text = "Save",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
        };
        save.Pressed += saveLayout;
        actions.AddChild(save);

        status = new Label
        {
            Text = "Layout applies during gameplay.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        content.AddChild(status);

        root.Visible = false;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (root.Visible && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            closeEditor();
            GetViewport().SetInputAsHandled();
        }
    }

    private void addPanel(string name, SubViewport viewport)
    {
        Control frame = new() { Name = $"{name}Preview", Size = new Vector2(200, 500), MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.Position = name == "Left" ? new Vector2(410, 112) : new Vector2(635, 112);
        root.AddChild(frame);

        TextureRect image = new()
        {
            Texture = viewport.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        frame.AddChild(image);

        PanelPreview preview = new() { Name = name, Viewport = viewport, Frame = frame };
        previews.Add(name, preview);

        foreach (Node child in viewport.GetChildren())
        {
            if (child is not Control control || control.Name == "Background")
                continue;

            string key = $"{name}/{control.Name}";
            widgets.Add(key, control);
            widgetPanels.Add(key, name);
            if (!layout.Widgets.ContainsKey(key))
            {
                SidePanelLayout defaults = SidePanelLayout.CreateDefault();
                layout.Widgets[key] = defaults.Widgets.TryGetValue(key, out SidePanelWidgetLayout defaultWidget)
                    ? defaultWidget.Copy()
                    : SidePanelWidgetLayout.From(control, viewport);
            }
            selector.AddItem(key.Replace('/', ' '));
            selector.SetItemMetadata(selector.ItemCount - 1, key);

            Panel handle = new()
            {
                Name = $"{control.Name}Handle",
                MouseFilter = Control.MouseFilterEnum.Stop,
                TooltipText = control.Name,
                FocusMode = Control.FocusModeEnum.None,
            };
            handle.AddThemeStyleboxOverride(
                "panel",
                new StyleBoxFlat
                {
                    BgColor = new Color(0.1f, 0.45f, 0.9f, 0.18f),
                    BorderColor = Colors.Cyan,
                    BorderWidthLeft = 2,
                    BorderWidthTop = 2,
                    BorderWidthRight = 2,
                    BorderWidthBottom = 2,
                }
            );
            handle.GuiInput += input => handleInput(key, input);
            frame.AddChild(handle);
            preview.Handles.Add(key, handle);
        }
    }

    private SpinBox addNumberField(VBoxContainer content, string label, double min, double max, double step)
    {
        content.AddChild(new Label { Text = label });
        SpinBox input = new()
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            AllowGreater = false,
            AllowLesser = false,
        };
        content.AddChild(input);
        return input;
    }

    private void selectWidget(string key)
    {
        if (key == null || !widgets.ContainsKey(key))
            return;
        selectedKey = key;
        updating = true;
        selector.Select(widgets.Keys.ToList().IndexOf(key));
        SidePanelWidgetLayout widget = layout.Widgets[key];
        xInput.Value = widget.X * 100;
        yInput.Value = widget.Y * 100;
        widthInput.Value = widget.Width * 100;
        heightInput.Value = widget.Height * 100;
        visibilityInput.ButtonPressed = widget.Visible;
        updating = false;
        status.Text = $"Selected: {key.Replace('/', ' ')}";
        updateHandles();
    }

    private void updateSelected(string property, float value)
    {
        if (updating || selectedKey == null)
            return;
        SidePanelWidgetLayout widget = layout.Widgets[selectedKey];
        switch (property)
        {
            case "X": widget.X = value; break;
            case "Y": widget.Y = value; break;
            case "Width": widget.Width = value; break;
            case "Height": widget.Height = value; break;
        }
        applyWidget(selectedKey);
        updateHandles();
    }

    private void applyWidget(string key)
    {
        SidePanelWidgetLayout widget = layout.Widgets[key];
        Control control = widgets[key];
        SubViewport viewport = previews[widgetPanels[key]].Viewport;
        control.AnchorLeft = 0;
        control.AnchorTop = 0;
        control.AnchorRight = 0;
        control.AnchorBottom = 0;
        control.OffsetLeft = widget.X * viewport.Size.X;
        control.OffsetTop = widget.Y * viewport.Size.Y;
        control.OffsetRight = control.OffsetLeft + widget.Width * viewport.Size.X;
        control.OffsetBottom = control.OffsetTop + widget.Height * viewport.Size.Y;
    }

    private void updateHandles()
    {
        foreach ((string key, Control handle) in previews.Values.SelectMany(preview => preview.Handles))
        {
            SidePanelWidgetLayout widget = layout.Widgets[key];
            Control frame = previews[widgetPanels[key]].Frame;
            handle.Visible = widget.Visible;
            handle.Position = new Vector2(widget.X * frame.Size.X, widget.Y * frame.Size.Y);
            handle.Size = new Vector2(widget.Width * frame.Size.X, widget.Height * frame.Size.Y);
            handle.Modulate = key == selectedKey ? Colors.White : new Color(0.65f, 0.8f, 1, 0.65f);
        }
    }

    private void handleInput(string key, InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            selectWidget(key);
            dragging = true;
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
        {
            dragging = false;
            GetViewport().SetInputAsHandled();
        }
        else if (dragging && @event is InputEventMouseMotion motion && selectedKey == key)
        {
            Control frame = previews[widgetPanels[key]].Frame;
            SidePanelWidgetLayout widget = layout.Widgets[key];
            widget.X = Mathf.Clamp(widget.X + motion.Relative.X / frame.Size.X, 0, 1);
            widget.Y = Mathf.Clamp(widget.Y + motion.Relative.Y / frame.Size.Y, 0, 1);
            updating = true;
            xInput.Value = widget.X * 100;
            yInput.Value = widget.Y * 100;
            updating = false;
            applyWidget(key);
            updateHandles();
            GetViewport().SetInputAsHandled();
        }
    }

    private void resetLayout()
    {
        layout = new SidePanelLayout();
        SidePanelLayout defaults = SidePanelLayout.CreateDefault();
        foreach ((string key, Control control) in widgets)
        {
            if (defaults.Widgets.TryGetValue(key, out SidePanelWidgetLayout defaultWidget))
                layout.Widgets[key] = defaultWidget.Copy();
            else
                layout.Widgets[key] = SidePanelWidgetLayout.From(control, previews[widgetPanels[key]].Viewport);
        }
        foreach (string key in widgets.Keys)
        {
            SidePanelWidgetLayout widget = layout.Widgets[key];
            Control control = widgets[key];
            SubViewport viewport = previews[widgetPanels[key]].Viewport;
            control.AnchorLeft = 0;
            control.AnchorTop = 0;
            control.AnchorRight = 0;
            control.AnchorBottom = 0;
            control.OffsetLeft = widget.X * viewport.Size.X;
            control.OffsetTop = widget.Y * viewport.Size.Y;
            control.OffsetRight = control.OffsetLeft + widget.Width * viewport.Size.X;
            control.OffsetBottom = control.OffsetTop + widget.Height * viewport.Size.Y;
            control.Visible = widget.Visible && isWidgetAllowed(key);
        }
        selectWidget(widgets.Keys.FirstOrDefault());
        status.Text = "Layout reset. Save to keep the default layout.";
    }

    private bool isWidgetAllowed(string key)
    {
        if (key == "Right/SimpleMisses")
            return !normalHudVisible;
        if (key == "Left/ComboCount")
            return normalHudVisible && altComboEnabled;
        return normalHudVisible;
    }

    private void saveLayout()
    {
        status.Text = layout.Save() ? $"Saved to {Constants.USER_FOLDER}/side-panel-layout.json" : "Could not save layout. See the log for details.";
    }

    private void closeEditor()
    {
        dragging = false;
        root.Visible = false;
        Closed?.Invoke();
    }
}
