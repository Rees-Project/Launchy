using System.IO;
using Launchy.Models;

namespace Launchy.Services;

public sealed class SettingsStore
{
    public string SettingsPath { get; } = DataFiles.PathFor("settings.json");

    public AppSettings Load() =>
        File.Exists(SettingsPath) ? DataFiles.Read<AppSettings>(SettingsPath) ?? new AppSettings() : new AppSettings();

    public void Save(AppSettings settings) => DataFiles.Write(SettingsPath, settings);
}
