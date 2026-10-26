using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace happy_agent_coding.Services;

public static class LayoutSettingValues
{
    public const double DefaultMainContentWidth = 520;
    public const double MinMainContentWidth = 400;
    public const double MaxMainContentWidth = 900;
    public const double DefaultAuxiliaryWidth = 520;
    public const double MinAuxiliaryWidth = 410;
    public const double MaxAuxiliaryWidth = 900;
    public const double LegacyWorkspaceWidth = 920;
    public const double DefaultTopPanelRatio = 0.5;
    public const double MinTopPanelHeight = 340;
    public const double MinBottomPanelHeight = 180;
    public const double MinTopPanelRatio = 0.15;
    public const double MaxTopPanelRatio = 0.85;

    public static double ClampMainContentWidth(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(value, MinMainContentWidth, MaxMainContentWidth)
            : DefaultMainContentWidth;

    public static double ClampTopPanelRatio(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(value, MinTopPanelRatio, MaxTopPanelRatio)
            : DefaultTopPanelRatio;

    public static double ClampAuxiliaryWidth(double value) =>
        double.IsFinite(value)
            ? Math.Clamp(value, MinAuxiliaryWidth, MaxAuxiliaryWidth)
            : DefaultAuxiliaryWidth;
}

public sealed record AppSettings(
    int SidebarDisplayModeIndex = 0,
    int ThemeIndex = 0,
    double MainContentWidth = LayoutSettingValues.DefaultMainContentWidth,
    double TopPanelRatio = LayoutSettingValues.DefaultTopPanelRatio,
    double AuxiliaryWidth = LayoutSettingValues.DefaultAuxiliaryWidth);

public static class SettingsKeys
{
    public const string SidebarDisplayMode = "Appearance.SidebarDisplayMode";
    public const string Theme = "Appearance.Theme";
    public const string MainContentWidth = "Layout.MainContentWidth";
    public const string LegacyRightPanelRatio = "Layout.RightPanelRatio";
    public const string LegacyRightPanelWidth = "Layout.RightPanelWidth";
    public const string TopPanelRatio = "Layout.TopPanelRatio";
    public const string AuxiliaryWidth = "Layout.AuxiliaryWidth";
}

public static class SettingsValueNames
{
    public const string IconOnly = "IconOnly";
    public const string TextOnly = "TextOnly";
    public const string IconAndText = "IconAndText";
    public const string System = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";
}

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

public sealed class JsonSettingsStore : ISettingsStore
{
    private const string ApplicationFolderName = "VisualizedAgentCoding";
    private const string SettingsFileName = "settings.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public JsonSettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? GetDefaultFilePath();
    }

    public string FilePath { get; }

    public AppSettings Load()
    {
        if (!File.Exists(FilePath))
            return new AppSettings();

        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                File.ReadAllText(FilePath));

            if (values is null)
                return new AppSettings();

            var mainContentWidth = values.ContainsKey(SettingsKeys.MainContentWidth)
                ? ReadDouble(values, SettingsKeys.MainContentWidth, LayoutSettingValues.DefaultMainContentWidth)
                : values.ContainsKey(SettingsKeys.LegacyRightPanelRatio)
                    ? LayoutSettingValues.LegacyWorkspaceWidth *
                      (1 - ReadDouble(values, SettingsKeys.LegacyRightPanelRatio, 0.5))
                    : values.ContainsKey(SettingsKeys.LegacyRightPanelWidth)
                        ? LayoutSettingValues.LegacyWorkspaceWidth -
                          ReadDouble(values, SettingsKeys.LegacyRightPanelWidth, 420)
                        : LayoutSettingValues.DefaultMainContentWidth;

            return new AppSettings(
                ClampIndex(ReadIndex(values, SettingsKeys.SidebarDisplayMode, MapSidebarDisplayMode)),
                ClampIndex(ReadIndex(values, SettingsKeys.Theme, MapTheme)),
                LayoutSettingValues.ClampMainContentWidth(mainContentWidth),
                LayoutSettingValues.ClampTopPanelRatio(
                    ReadDouble(values, SettingsKeys.TopPanelRatio, LayoutSettingValues.DefaultTopPanelRatio)),
                LayoutSettingValues.ClampAuxiliaryWidth(
                    ReadDouble(values, SettingsKeys.AuxiliaryWidth, LayoutSettingValues.DefaultAuxiliaryWidth)));
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var values = new Dictionary<string, object>
        {
            [SettingsKeys.SidebarDisplayMode] = SidebarDisplayModeName(settings.SidebarDisplayModeIndex),
            [SettingsKeys.Theme] = ThemeName(settings.ThemeIndex),
            [SettingsKeys.MainContentWidth] = LayoutSettingValues.ClampMainContentWidth(settings.MainContentWidth),
            [SettingsKeys.TopPanelRatio] = LayoutSettingValues.ClampTopPanelRatio(settings.TopPanelRatio),
            [SettingsKeys.AuxiliaryWidth] = LayoutSettingValues.ClampAuxiliaryWidth(settings.AuxiliaryWidth),
        };

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = FilePath + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(values, SerializerOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static string GetDefaultFilePath()
    {
        var roamingFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(roamingFolder))
            roamingFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.Combine(roamingFolder, ApplicationFolderName, SettingsFileName);
    }

    private static int ReadIndex(
        IReadOnlyDictionary<string, JsonElement> values,
        string key,
        Func<string, int> mapName)
    {
        if (!values.TryGetValue(key, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var index))
            return index;

        return value.ValueKind == JsonValueKind.String && value.GetString() is { } name
            ? mapName(name)
            : 0;
    }

    private static double ReadDouble(
        IReadOnlyDictionary<string, JsonElement> values,
        string key,
        double fallback)
    {
        if (!values.TryGetValue(key, out var value))
            return fallback;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;

        return value.ValueKind == JsonValueKind.String &&
               double.TryParse(
                   value.GetString(),
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out var textNumber)
            ? textNumber
            : fallback;
    }

    private static int MapSidebarDisplayMode(string name) => name switch
    {
        SettingsValueNames.IconOnly => 0,
        SettingsValueNames.TextOnly => 1,
        SettingsValueNames.IconAndText => 2,
        _ => 0,
    };

    private static int MapTheme(string name) => name switch
    {
        SettingsValueNames.System => 0,
        SettingsValueNames.Light => 1,
        SettingsValueNames.Dark => 2,
        _ => 0,
    };

    private static string SidebarDisplayModeName(int value) => ClampIndex(value) switch
    {
        1 => SettingsValueNames.TextOnly,
        2 => SettingsValueNames.IconAndText,
        _ => SettingsValueNames.IconOnly,
    };

    private static string ThemeName(int value) => ClampIndex(value) switch
    {
        1 => SettingsValueNames.Light,
        2 => SettingsValueNames.Dark,
        _ => SettingsValueNames.System,
    };

    private static int ClampIndex(int value) => Math.Clamp(value, 0, 2);
}
