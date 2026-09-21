using System.Text.Json;
using Godot;

namespace BantersTweaks.Integration;

internal static class ExternalIntegrationLoader
{
    private const string DirectoryPath = "user://banters_tweaks/integrations";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    internal static string GetAbsoluteDirectory()
        => ProjectSettings.GlobalizePath(DirectoryPath);

    internal static void LoadAll()
    {
        try
        {
            string directory = GetAbsoluteDirectory();
            Directory.CreateDirectory(directory);

            int loaded = 0;
            foreach (string path in Directory.EnumerateFiles(
                         directory,
                         "*.integration.json",
                         SearchOption.TopDirectoryOnly))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var manifest = JsonSerializer.Deserialize<IntegrationManifest>(json, JsonOptions);

                    if (manifest is null || !BantersIntegration.RegisterManifest(manifest))
                    {
                        SpireGPS.MainFile.Logger.Warn(
                            $"Integration manifest ignored: {Path.GetFileName(path)}");
                        continue;
                    }

                    loaded++;
                    SpireGPS.MainFile.Logger.Info(
                        $"Loaded integration manifest '{manifest.ModId}' from {Path.GetFileName(path)}.");
                }
                catch (Exception ex)
                {
                    SpireGPS.MainFile.Logger.Warn(
                        $"Integration manifest '{Path.GetFileName(path)}' failed: {ex.Message}");
                }
            }

            SpireGPS.MainFile.Logger.Info(
                $"Integration registry ready. External manifests loaded: {loaded}.");
        }
        catch (Exception ex)
        {
            SpireGPS.MainFile.Logger.Warn(
                $"External integration directory could not be initialized: {ex.Message}");
        }
    }
}
