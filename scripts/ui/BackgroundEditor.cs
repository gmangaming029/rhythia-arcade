using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

public partial class BackgroundEditor : CanvasLayer
{
    private readonly Dictionary<TreeItem, Node3D> treeNodes = [];
    private readonly SpinBox[,] transformInputs = new SpinBox[3, 3];
    private Control root;
    private Tree objectTree;
    private SubViewport previewViewport;
    private BaseSpace previewSpace;
    private Label backgroundLabel;
    private Label statusLabel;
    private ColorPickerButton objectColor;
    private bool updatingControls;
    private bool gameSpace;
    private Input.MouseModeEnum previousMouseMode;
    private Node3D selectedNode;
    private BackgroundEditorLayout layout;

    public event Action Closed;

    public bool IsOpen => root?.Visible ?? false;

    public override void _Ready()
    {
        root = new Control
        {
            Name = "BackgroundEditor",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        SubViewportContainer previewContainer = new()
        {
            Name = "PreviewContainer",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Stretch = true,
        };
        previewContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(previewContainer);

        previewViewport = new SubViewport
        {
            Name = "PreviewViewport",
            Size = new Vector2I(1280, 720),
            World3D = new World3D(),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false,
        };
        previewContainer.AddChild(previewViewport);

        PanelContainer panel = new()
        {
            Name = "EditorPanel",
            CustomMinimumSize = new Vector2(390, 0),
        };
        panel.AnchorLeft = 0;
        panel.AnchorRight = 0;
        panel.AnchorTop = 0;
        panel.AnchorBottom = 1;
        panel.OffsetLeft = 0;
        panel.OffsetRight = 430;
        panel.OffsetTop = 0;
        panel.OffsetBottom = 0;
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
            Text = "Background Editor",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        titleRow.AddChild(title);
        Button closeButton = new() { Text = "Close  [Esc]", FocusMode = Control.FocusModeEnum.None };
        closeButton.Pressed += CloseEditor;
        titleRow.AddChild(closeButton);

        backgroundLabel = new Label { Text = "Previewing menu background" };
        content.AddChild(backgroundLabel);

        HBoxContainer targetRow = new();
        content.AddChild(targetRow);
        Button menuTarget = new() { Text = "Menu", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        menuTarget.Pressed += () => loadPreview(false);
        targetRow.AddChild(menuTarget);
        Button gameTarget = new() { Text = "Game", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        gameTarget.Pressed += () => loadPreview(true);
        targetRow.AddChild(gameTarget);

        objectTree = new Tree
        {
            Name = "Objects",
            HideRoot = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 150),
        };
        objectTree.ItemSelected += onTreeItemSelected;
        content.AddChild(objectTree);

        HBoxContainer addRow = new();
        content.AddChild(addRow);
        foreach (string type in new[] { "Box", "Sphere", "Cylinder", "Quad" })
        {
            Button addButton = new()
            {
                Text = $"+ {type}",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                FocusMode = Control.FocusModeEnum.None,
            };
            addButton.Pressed += () => addObject(type);
            addRow.AddChild(addButton);
        }

        HBoxContainer colorRow = new();
        content.AddChild(colorRow);
        colorRow.AddChild(new Label { Text = "New object color", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        objectColor = new ColorPickerButton
        {
            Color = Colors.White,
            CustomMinimumSize = new Vector2(72, 30),
            EditAlpha = false,
        };
        colorRow.AddChild(objectColor);

        content.AddChild(new HSeparator());
        content.AddChild(new Label { Text = "Selected object transform" });
        addTransformControls(content);

        HBoxContainer actions = new();
        content.AddChild(actions);
        Button removeButton = new()
        {
            Text = "Remove / Hide",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
        };
        removeButton.Pressed += removeSelectedObject;
        actions.AddChild(removeButton);

        Button resetButton = new()
        {
            Text = "Reset background",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
        };
        resetButton.Pressed += resetBackground;
        actions.AddChild(resetButton);

        HBoxContainer footer = new();
        content.AddChild(footer);
        statusLabel = new Label
        {
            Text = "Changes are previewed live.",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        footer.AddChild(statusLabel);
        Button saveButton = new() { Text = "Save", FocusMode = Control.FocusModeEnum.None };
        saveButton.Pressed += saveLayout;
        footer.AddChild(saveButton);

        root.Visible = false;
    }

    public void OpenEditor()
    {
        previousMouseMode = Input.MouseMode;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        root.Visible = true;
        loadPreview(false);
    }

    public void CloseEditor()
    {
        if (!IsOpen)
        {
            return;
        }

        root.Visible = false;
        previewSpace?.QueueFree();
        previewSpace = null;
        Input.MouseMode = previousMouseMode;
        Closed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (IsOpen && @event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            CloseEditor();
            GetViewport().SetInputAsHandled();
        }
    }

    private void addTransformControls(VBoxContainer content)
    {
        GridContainer grid = new() { Columns = 4 };
        content.AddChild(grid);
        grid.AddChild(new Label { Text = "" });
        grid.AddChild(new Label { Text = "X", HorizontalAlignment = HorizontalAlignment.Center });
        grid.AddChild(new Label { Text = "Y", HorizontalAlignment = HorizontalAlignment.Center });
        grid.AddChild(new Label { Text = "Z", HorizontalAlignment = HorizontalAlignment.Center });

        string[] labels = ["Position", "Rotation", "Scale"];
        for (int row = 0; row < labels.Length; row++)
        {
            grid.AddChild(new Label { Text = labels[row], VerticalAlignment = VerticalAlignment.Center });
            for (int column = 0; column < 3; column++)
            {
                SpinBox input = new()
                {
                    Step = row == 2 ? 0.05 : 1,
                    MinValue = row == 2 ? 0.01 : -1000,
                    MaxValue = row == 2 ? 100 : 1000,
                    CustomMinimumSize = new Vector2(74, 0),
                    AllowLesser = row != 2,
                    AllowGreater = true,
                };
                int capturedRow = row;
                int capturedColumn = column;
                input.ValueChanged += _ => updateSelectedTransform(capturedRow, capturedColumn);
                transformInputs[row, column] = input;
                grid.AddChild(input);
            }
        }
    }

    private void loadPreview(bool useGameSpace)
    {
        gameSpace = useGameSpace;
        previewSpace?.QueueFree();
        previewSpace = SkinManager.CreateSpaceForEditor(gameSpace);
        previewViewport.AddChild(previewSpace);
        previewSpace.Load();
        layout = BackgroundEditorLayout.Load(previewSpace.BackgroundId);
        backgroundLabel.Text = $"Previewing {(gameSpace ? "game" : "menu")} background: {previewSpace.BackgroundId}";
        selectedNode = null;
        populateObjectTree();
        setTransformInputsEnabled(false);
        statusLabel.Text = "Changes are previewed live.";
    }

    private void populateObjectTree(string selectName = null)
    {
        objectTree.Clear();
        treeNodes.Clear();
        TreeItem rootItem = objectTree.CreateItem();
        foreach (Node3D node in BackgroundEditorLayout.FindEditableNodes(previewSpace))
        {
            addTreeNode(rootItem, node, selectName);
        }
    }

    private void addTreeNode(TreeItem parentItem, Node3D node, string selectName)
    {
        TreeItem item = objectTree.CreateItem(parentItem);
        item.SetText(0, $"{node.GetType().Name}  {node.Name}");
        item.SetMetadata(0, previewSpace.GetPathTo(node).ToString());
        treeNodes[item] = node;
        if (selectName == node.Name)
        {
            item.Select(0);
        }

        foreach (Node child in node.GetChildren())
        {
            if (child is Node3D child3D)
            {
                addTreeNode(item, child3D, selectName);
            }
        }
    }

    private void onTreeItemSelected()
    {
        TreeItem item = objectTree.GetSelected();
        selectedNode = item != null && treeNodes.TryGetValue(item, out Node3D node) ? node : null;
        setTransformInputsEnabled(selectedNode != null);
        if (selectedNode == null)
        {
            return;
        }

        updatingControls = true;
        Vector3[] values = [selectedNode.Position, selectedNode.RotationDegrees, selectedNode.Scale];
        for (int row = 0; row < values.Length; row++)
        {
            transformInputs[row, 0].Value = values[row].X;
            transformInputs[row, 1].Value = values[row].Y;
            transformInputs[row, 2].Value = values[row].Z;
        }

        updatingControls = false;
    }

    private void updateSelectedTransform(int row, int column)
    {
        if (updatingControls || selectedNode == null)
        {
            return;
        }

        Vector3 value = row switch
        {
            0 => selectedNode.Position,
            1 => selectedNode.RotationDegrees,
            _ => selectedNode.Scale,
        };
        float component = (float)transformInputs[row, column].Value;
        value = column switch
        {
            0 => new Vector3(component, value.Y, value.Z),
            1 => new Vector3(value.X, component, value.Z),
            _ => new Vector3(value.X, value.Y, component),
        };

        switch (row)
        {
            case 0:
                selectedNode.Position = value;
                break;
            case 1:
                selectedNode.RotationDegrees = value;
                break;
            default:
                selectedNode.Scale = value;
                break;
        }

        captureSelectedTransform();
    }

    private void captureSelectedTransform()
    {
        if (selectedNode == null)
        {
            return;
        }

        BackgroundTransform transform = BackgroundTransform.From(selectedNode);

        if (selectedNode.HasMeta("background_editor_object"))
        {
            BackgroundEditorObject editorObject = layout.Objects.FirstOrDefault(item => item.Name == selectedNode.Name);
            if (editorObject != null)
            {
                editorObject.Position = transform.Position;
                editorObject.Rotation = transform.Rotation;
                editorObject.Scale = transform.Scale;
            }
        }
        else
        {
            string nodePath = previewSpace.GetPathTo(selectedNode).ToString();
            layout.Transforms[nodePath] = transform;
        }
    }

    private void addObject(string type)
    {
        Camera3D camera = previewSpace.Camera;
        Vector3 worldPosition = camera.GlobalPosition + camera.GlobalBasis * new Vector3(0, 0, -12);
        string objectName = $"Editor{type}{Guid.NewGuid():N}";
        BackgroundEditorObject editorObject = new()
        {
            Name = objectName,
            Type = type,
            Color = objectColor.Color.ToHtml(false),
            Position = previewSpace.ToLocal(worldPosition).ToArray(),
        };
        layout.Objects.Add(editorObject);

        MeshInstance3D node = new()
        {
            Name = objectName,
            Mesh = BackgroundEditorLayout.CreateMesh(type),
            Position = editorObject.Position.ToVector3(),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = objectColor.Color },
        };
        node.SetMeta("background_editor_object", true);
        previewSpace.AddChild(node);
        populateObjectTree(objectName);
        statusLabel.Text = $"Added {type}. Save to keep the change.";
    }

    private void removeSelectedObject()
    {
        if (selectedNode == null || selectedNode == previewSpace || selectedNode is Camera3D)
        {
            statusLabel.Text = "Select a background object first.";
            return;
        }

        if (selectedNode.HasMeta("background_editor_object"))
        {
            layout.Objects.RemoveAll(item => item.Name == selectedNode.Name);
            selectedNode.QueueFree();
        }
        else
        {
            string nodePath = previewSpace.GetPathTo(selectedNode).ToString();
            if (selectedNode.Visible)
            {
                if (!layout.HiddenNodes.Contains(nodePath))
                {
                    layout.HiddenNodes.Add(nodePath);
                }

                selectedNode.Visible = false;
                statusLabel.Text = "Object hidden. Select it and use Remove / Hide to restore it.";
            }
            else
            {
                layout.HiddenNodes.Remove(nodePath);
                selectedNode.Visible = true;
                statusLabel.Text = "Object restored. Save to keep the change.";
            }
        }

        if (selectedNode.HasMeta("background_editor_object"))
        {
            statusLabel.Text = "Object removed from preview. Save to keep the change.";
        }

        selectedNode = null;
        populateObjectTree();
        setTransformInputsEnabled(false);
    }

    private void resetBackground()
    {
        if (string.IsNullOrEmpty(previewSpace.BackgroundId))
        {
            statusLabel.Text = "This background cannot be reset.";
            return;
        }

        string savedId = previewSpace.BackgroundId;
        string path = BackgroundEditorLayout.GetStoragePath(savedId);
        try
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            Logger.Error($"Unable to reset background editor layout: {exception.Message}");
            statusLabel.Text = "Could not reset the saved background.";
            return;
        }

        loadPreview(gameSpace);
        statusLabel.Text = "Background reset.";
    }

    private void saveLayout()
    {
        if (previewSpace == null || string.IsNullOrWhiteSpace(previewSpace.BackgroundId))
        {
            statusLabel.Text = "Cannot save this background.";
            return;
        }

        foreach (Node3D node in BackgroundEditorLayout.FindEditableNodes(previewSpace))
        {
            if (!node.HasMeta("background_editor_object"))
            {
                string path = previewSpace.GetPathTo(node).ToString();
                if (!layout.HiddenNodes.Contains(path))
                {
                    layout.Transforms[path] = BackgroundTransform.From(node);
                }
            }
        }

        if (BackgroundEditorLayout.Save(previewSpace.BackgroundId, layout))
        {
            statusLabel.Text = "Background saved.";
        }
        else
        {
            statusLabel.Text = "Save failed; check the game log.";
        }
    }

    private void setTransformInputsEnabled(bool enabled)
    {
        foreach (SpinBox input in transformInputs)
        {
            input.Editable = enabled;
        }
    }
}

internal static class BackgroundEditorArrayExtensions
{
    public static float[] ToArray(this Vector3 value)
    {
        return [value.X, value.Y, value.Z];
    }
}
