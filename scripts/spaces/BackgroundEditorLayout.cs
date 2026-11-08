using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;

public sealed class BackgroundEditorLayout
{
    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

    public Dictionary<string, BackgroundTransform> Transforms { get; set; } = new();
    public List<string> HiddenNodes { get; set; } = [];
    public List<BackgroundEditorObject> Objects { get; set; } = [];

    public static BackgroundEditorLayout Load(string backgroundId)
    {
        string path = GetPath(backgroundId);
        if (!File.Exists(path))
        {
            return new BackgroundEditorLayout();
        }

        try
        {
            return JsonSerializer.Deserialize<BackgroundEditorLayout>(File.ReadAllText(path)) ?? new BackgroundEditorLayout();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Logger.Error($"Unable to load background editor layout '{backgroundId}': {exception.Message}");
            return new BackgroundEditorLayout();
        }
    }

    public static bool Save(string backgroundId, BackgroundEditorLayout layout)
    {
        string path = GetPath(backgroundId);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(layout, jsonOptions));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Logger.Error($"Unable to save background editor layout '{backgroundId}': {exception.Message}");
            return false;
        }
    }

    public void Apply(BaseSpace space)
    {
        foreach (Node3D node in FindEditableNodes(space))
        {
            string nodePath = space.GetPathTo(node).ToString();
            if (Transforms.TryGetValue(nodePath, out BackgroundTransform transform))
            {
                transform.Apply(node);
            }

            if (HiddenNodes.Contains(nodePath))
            {
                node.Visible = false;
            }
        }

        foreach (BackgroundEditorObject editorObject in Objects)
        {
            MeshInstance3D node = new()
            {
                Name = editorObject.Name,
                Mesh = CreateMesh(editorObject.Type),
                Position = editorObject.Position.ToVector3(),
                RotationDegrees = editorObject.Rotation.ToVector3(),
                Scale = editorObject.Scale.ToVector3(),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = Color.FromHtml(editorObject.Color) },
            };
            node.SetMeta("background_editor_object", true);
            space.AddChild(node);
        }
    }

    public static List<Node3D> FindEditableNodes(Node root)
    {
        List<Node3D> nodes = [];
        collectEditableNodes(root, nodes);
        return nodes;
    }

    public static Mesh CreateMesh(string type)
    {
        return type switch
        {
            "Sphere" => new SphereMesh(),
            "Cylinder" => new CylinderMesh(),
            "Quad" => new QuadMesh(),
            _ => new BoxMesh(),
        };
    }

    private static void collectEditableNodes(Node root, List<Node3D> nodes)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is not Node3D node || node is WorldEnvironment)
            {
                continue;
            }

            nodes.Add(node);
            collectEditableNodes(node, nodes);
        }
    }

    public static string GetStoragePath(string backgroundId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(backgroundId));
        string fileName = Convert.ToHexString(hash).ToLowerInvariant() + ".json";
        return Path.Combine(Constants.USER_FOLDER, "background-editor", fileName);
    }

    private static string GetPath(string backgroundId) => GetStoragePath(backgroundId);
}

public sealed class BackgroundTransform
{
    public float[] Position { get; set; } = [0, 0, 0];
    public float[] Rotation { get; set; } = [0, 0, 0];
    public float[] Scale { get; set; } = [1, 1, 1];

    public static BackgroundTransform From(Node3D node)
    {
        return new BackgroundTransform
        {
            Position = [node.Position.X, node.Position.Y, node.Position.Z],
            Rotation = [node.RotationDegrees.X, node.RotationDegrees.Y, node.RotationDegrees.Z],
            Scale = [node.Scale.X, node.Scale.Y, node.Scale.Z],
        };
    }

    public void Apply(Node3D node)
    {
        node.Position = Position.ToVector3();
        node.RotationDegrees = Rotation.ToVector3();
        node.Scale = Scale.ToVector3();
    }
}

public sealed class BackgroundEditorObject
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Box";
    public string Color { get; set; } = "#ffffff";
    public float[] Position { get; set; } = [0, 0, 0];
    public float[] Rotation { get; set; } = [0, 0, 0];
    public float[] Scale { get; set; } = [1, 1, 1];
}

internal static class BackgroundEditorVectorExtensions
{
    public static Vector3 ToVector3(this float[] values)
    {
        return values.Length >= 3 ? new Vector3(values[0], values[1], values[2]) : Vector3.Zero;
    }
}
