using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Launchy.Services;

public static class DataFiles
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launchy");

    public static string PathFor(string fileName) => Path.Combine(DataDirectory, fileName);

    public static T? Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(DataDirectory);

        // Write to a temp file first so a crash never leaves a half-written file.
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(tempPath, path, overwrite: true);
    }
}
