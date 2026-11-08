using System.IO;
using System.Linq;
using System;
using System.Collections.Generic;
using Godot;

public partial class FileInitializer : Node
{
    private Node objExporter;

    public override void _Ready()
    {
        var script = GD.Load<Script>("res://scripts/OBJExporter.gd");

        objExporter = new Node();
        AddChild(objExporter);
        objExporter.SetScript(script);

        importBundledUserFolder();

        // Temporary for 0.1.2 as the mesh's faces were fixed
        if (File.Exists($"{Constants.USER_FOLDER}/meshes/squircle.obj"))
        {
            File.Delete($"{Constants.USER_FOLDER}/meshes/squircle.obj");
        }

        deepCopy();
    }

    private void importBundledUserFolder()
    {
        string executableDirectory = Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
        string sourceDirectory = Path.Combine(executableDirectory, "Rhythia");
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        string destinationDirectory = Path.GetFullPath(Constants.USER_FOLDER);
        sourceDirectory = Path.GetFullPath(sourceDirectory);
        StringComparison pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string sourcePrefix = sourceDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string destinationPrefix = destinationDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (
            string.Equals(sourceDirectory, destinationDirectory, pathComparison)
            || sourceDirectory.StartsWith(destinationPrefix, pathComparison)
            || destinationDirectory.StartsWith(sourcePrefix, pathComparison)
        )
        {
            Logger.Error("The bundled Rhythia folder overlaps the configured user folder; skipping its import.");
            return;
        }

        int copiedFiles = 0;
        Stack<string> directories = new();
        directories.Push(sourceDirectory);
        try
        {
            Directory.CreateDirectory(destinationDirectory);
            while (directories.Count > 0)
            {
                string currentSource = directories.Pop();
                string relativeDirectory = Path.GetRelativePath(sourceDirectory, currentSource);
                string currentDestination = relativeDirectory == "."
                    ? destinationDirectory
                    : Path.Combine(destinationDirectory, relativeDirectory);
                Directory.CreateDirectory(currentDestination);

                foreach (string sourceFile in Directory.EnumerateFiles(currentSource))
                {
                    string relativeFile = Path.GetRelativePath(sourceDirectory, sourceFile);
                    if (
                        string.Equals(relativeFile, "data.db", pathComparison)
                        || string.Equals(relativeFile, "arcade-api.token", pathComparison)
                    )
                    {
                        continue;
                    }

                    string destinationFile = Path.Combine(destinationDirectory, relativeFile);
                    if (File.Exists(destinationFile))
                    {
                        continue;
                    }

                    try
                    {
                        File.Copy(sourceFile, destinationFile, overwrite: false);
                        copiedFiles++;
                    }
                    catch (IOException) when (File.Exists(destinationFile))
                    {
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        Logger.Error($"Unable to import bundled user file '{relativeFile}': {exception.Message}");
                    }
                }

                foreach (string sourceSubdirectory in Directory.EnumerateDirectories(currentSource))
                {
                    if ((File.GetAttributes(sourceSubdirectory) & FileAttributes.ReparsePoint) == 0)
                    {
                        directories.Push(sourceSubdirectory);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Logger.Error($"Unable to import the bundled Rhythia folder: {exception.Message}");
        }

        if (copiedFiles > 0)
        {
            Logger.Log($"Imported {copiedFiles} missing file(s) from the bundled Rhythia folder.");
        }
    }

    private void deepCopy(string resDir = "")
    {
        string userDir = $"{Constants.USER_FOLDER}{resDir}";

        if (!Directory.Exists(userDir))
        {
            Directory.CreateDirectory(userDir);
        }

        foreach (string resFile in DirAccess.GetFilesAt($"res://user{resDir}"))
        {
            string userFile = $"{userDir}/{resFile}";
            string ext = resFile.GetExtension();

            if (File.Exists(userFile) || ext == "import" || ext == "uid" || ext == "gitkeep")
            {
                continue;
            }

            var source = Godot.FileAccess.Open($"res://user{resDir}/{resFile}", Godot.FileAccess.ModeFlags.Read);
            byte[] buffer = source.GetBuffer((long)source.GetLength());
            source.Close();

            Godot.FileAccess copy = Godot.FileAccess.Open(userFile, Godot.FileAccess.ModeFlags.Write);
            copy.StoreBuffer(buffer);
            copy.Close();
        }

        // Attempts to load files that have not been added as a resource.

        foreach (string resFile in ResourceLoader.ListDirectory($"res://user{resDir}").Where(x => x.Last() != '/'))
        {
            string userFile = $"{userDir}/{resFile}";
            string ext = resFile.GetExtension();

            if (File.Exists(userFile) || ext == "import" || ext == "uid" || ext == "gitkeep")
            {
                continue;
            }

            var resource = GD.Load($"res://user{resDir}/{resFile}");
            byte[] buffer = [];

            switch (resource.GetType().Name)
            {
                case "CompressedTexture2D":
                    buffer = (resource as CompressedTexture2D).GetImage().SavePngToBuffer();
                    break;
                case "AudioStreamMP3":
                    buffer = (resource as AudioStreamMP3).Data;
                    break;
                case "ArrayMesh":
                    objExporter.Call("save_mesh_to_files", resource, userDir, resFile.Replace(".obj", ""));
                    continue;
                default:
                    Logger.Error($"[{resFile}] {resource.GetType().Name} is not supported for the user folder.");
                    continue;
            }

            File.WriteAllBytes(userFile, buffer);
        }

        foreach (string dir in DirAccess.GetDirectoriesAt($"res://user{resDir}"))
        {
            deepCopy($"{resDir}/{dir}");
        }
    }
}
