namespace Orbit.WindowsAgent;

public static class LocalConfigurationFile
{
    public static string? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public static void Update(string path, Func<string?, string> update)
    {
        using var writeLock = new FileStream(path + ".lock", FileMode.OpenOrCreate,
            FileAccess.Write, FileShare.None, 4096, FileOptions.DeleteOnClose);
        var original = Read(path);
        var updated = update(original);
        if (string.Equals(original, updated, StringComparison.Ordinal))
            return;

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, updated);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
