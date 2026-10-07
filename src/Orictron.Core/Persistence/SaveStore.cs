using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Orictron.Persistence;

/// <summary>
/// Reads and writes <see cref="SaveData"/> as JSON in the platform's per-app data folder.
/// Writes go to a temp file first and the previous file is kept as a backup, so a crash or
/// the OS killing the app mid-write can never lose the high scores.
/// </summary>
public sealed class SaveStore
{
    public const string FileName = "orictron-save.json";

    public SaveStore(string directory)
    {
        Directory = directory;
        FilePath = Path.Combine(directory, FileName);
        BackupPath = FilePath + ".bak";
    }

    public string Directory { get; }
    public string FilePath { get; }
    public string BackupPath { get; }

    /// <summary>
    /// The per-app writable folder on each platform:
    /// Windows %LOCALAPPDATA% (redirected into the MSIX package's private store when packaged),
    /// macOS ~/Library/Application Support (inside the sandbox container),
    /// iOS the app's Library folder, Android the app's internal files folder.
    /// </summary>
    public static string DefaultDirectory()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(root)) root = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        if (string.IsNullOrEmpty(root)) root = AppContext.BaseDirectory;
        return Path.Combine(root, "Orictron");
    }

    public SaveData Load()
    {
        return TryRead(FilePath) ?? TryRead(BackupPath) ?? new SaveData();
    }

    public bool Save(SaveData data)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            string tmp = FilePath + ".tmp";
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, data, SaveJsonContext.Default.SaveData);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(FilePath)) File.Copy(FilePath, BackupPath, overwrite: true);
            File.Move(tmp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Orictron: failed to save: {ex.Message}");
            return false;
        }
    }

    private static SaveData? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            var data = JsonSerializer.Deserialize(stream, SaveJsonContext.Default.SaveData);
            data?.Normalize();
            return data;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine($"Orictron: ignoring unreadable save {path}: {ex.Message}");
            return null;
        }
    }
}
