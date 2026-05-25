using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SayusGagExtender.API;

public sealed class CammyApi : IDisposable
{
    private readonly Plugin plugin;

    private Type? cammyType;
    private Type? presetManagerType;

    private PropertyInfo? configProperty;
    private FieldInfo? configField;

    private FieldInfo? presetsField;
    private PropertyInfo? currentPresetProperty;
    private PropertyInfo? activePresetProperty;
    private PropertyInfo? presetOverrideProperty;
    private PropertyInfo? defaultPresetProperty;

    private FieldInfo? presetNameField;
    private PropertyInfo? presetNameProperty;

    private DateTime nextErrorPrintUtc = DateTime.MinValue;

    public CammyApi(Plugin plugin)
    {
        this.plugin = plugin;
        RefreshCache();
    }

    public void Dispose()
    {
        ClearCache();
    }

    public bool IsAvailable()
    {
        RefreshCache();
        return cammyType != null
               && presetManagerType != null
               && GetConfigObject() != null
               && presetsField != null
               && currentPresetProperty != null;
    }

    public IReadOnlyList<CammyPresetInfo> GetAllPresets()
    {
        try
        {
            RefreshCache();

            var presets = GetPresetList();
            if (presets == null)
                return Array.Empty<CammyPresetInfo>();

            var result = new List<CammyPresetInfo>();

            var index = 0;
            foreach (var preset in presets)
            {
                if (preset == null)
                {
                    index++;
                    continue;
                }

                result.Add(new CammyPresetInfo(
                    Index: index,
                    Name: GetPresetName(preset),
                    IsCurrent: IsSamePreset(preset, GetCurrentPresetObject()),
                    IsActivePreset: IsSamePreset(preset, GetActivePresetObject()),
                    IsOverridePreset: IsSamePreset(preset, GetPresetOverrideObject())
                ));

                index++;
            }

            return result;
        }
        catch (Exception ex)
        {
            PrintErrorThrottled($"Failed to list Cammy presets: {ex.Message}");
            return Array.Empty<CammyPresetInfo>();
        }
    }

    public CammyPresetInfo? GetCurrentActivePreset()
    {
        try
        {
            RefreshCache();

            var current = GetCurrentPresetObject();
            if (current == null)
                return null;

            var presets = GetPresetList();
            if (presets == null)
                return null;

            var index = 0;
            foreach (var preset in presets)
            {
                if (preset == null)
                {
                    index++;
                    continue;
                }

                if (ReferenceEquals(preset, current))
                {
                    return new CammyPresetInfo(
                        Index: index,
                        Name: GetPresetName(preset),
                        IsCurrent: true,
                        IsActivePreset: IsSamePreset(preset, GetActivePresetObject()),
                        IsOverridePreset: IsSamePreset(preset, GetPresetOverrideObject())
                    );
                }

                index++;
            }

            // Cammy's CurrentPreset can also be the default preset, which is not in Config.Presets.
            if (IsSamePreset(current, GetDefaultPresetObject()))
            {
                return new CammyPresetInfo(
                    Index: -1,
                    Name: "Default",
                    IsCurrent: true,
                    IsActivePreset: false,
                    IsOverridePreset: false
                );
            }

            return new CammyPresetInfo(
                Index: -1,
                Name: GetPresetName(current),
                IsCurrent: true,
                IsActivePreset: IsSamePreset(current, GetActivePresetObject()),
                IsOverridePreset: IsSamePreset(current, GetPresetOverrideObject())
            );
        }
        catch (Exception ex)
        {
            PrintErrorThrottled($"Failed to get current Cammy preset: {ex.Message}");
            return null;
        }
    }

    public bool SetActivePreset(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
            return false;

        try
        {
            RefreshCache();

            var preset = FindPresetByName(presetName);
            if (preset == null)
                return false;

            return SetCurrentPresetObject(preset);
        }
        catch (Exception ex)
        {
            PrintErrorThrottled($"Failed to set Cammy preset '{presetName}': {ex.Message}");
            return false;
        }
    }

    public bool SetActivePreset(int presetIndex)
    {
        try
        {
            RefreshCache();

            var presets = GetPresetList();
            if (presets == null)
                return false;

            var preset = presets.Cast<object?>()
                .Where(x => x != null)
                .ElementAtOrDefault(presetIndex);

            if (preset == null)
                return false;

            return SetCurrentPresetObject(preset);
        }
        catch (Exception ex)
        {
            PrintErrorThrottled($"Failed to set Cammy preset index {presetIndex}: {ex.Message}");
            return false;
        }
    }

    public bool ClearActivePresetOverride()
    {
        try
        {
            RefreshCache();

            // Matches Cammy's own `/cammy preset` without a name:
            // PresetManager.CurrentPreset = null;
            return SetCurrentPresetObject(null);
        }
        catch (Exception ex)
        {
            PrintErrorThrottled($"Failed to clear Cammy preset override: {ex.Message}");
            return false;
        }
    }

    private object? FindPresetByName(string presetName)
    {
        var presets = GetPresetList();
        if (presets == null)
            return null;

        foreach (var preset in presets)
        {
            if (preset == null)
                continue;

            var name = GetPresetName(preset);

            if (string.Equals(name, presetName, StringComparison.OrdinalIgnoreCase))
                return preset;
        }

        return null;
    }

    private bool SetCurrentPresetObject(object? preset)
    {
        if (currentPresetProperty == null)
            return false;

        currentPresetProperty.SetValue(null, preset);
        return true;
    }

    private object? GetCurrentPresetObject()
    {
        return currentPresetProperty?.GetValue(null);
    }

    private object? GetActivePresetObject()
    {
        return activePresetProperty?.GetValue(null);
    }

    private object? GetPresetOverrideObject()
    {
        return presetOverrideProperty?.GetValue(null);
    }

    private object? GetDefaultPresetObject()
    {
        return defaultPresetProperty?.GetValue(null);
    }

    private IEnumerable? GetPresetList()
    {
        var config = GetConfigObject();
        if (config == null || presetsField == null)
            return null;

        return presetsField.GetValue(config) as IEnumerable;
    }

    private object? GetConfigObject()
    {
        if (cammyType == null)
            return null;

        return configProperty?.GetValue(null)
               ?? configField?.GetValue(null);
    }

    private string GetPresetName(object preset)
    {
        try
        {
            return presetNameField?.GetValue(preset)?.ToString()
                   ?? presetNameProperty?.GetValue(preset)?.ToString()
                   ?? "Unnamed Preset";
        }
        catch
        {
            return "Unnamed Preset";
        }
    }

    private static bool IsSamePreset(object? a, object? b)
    {
        return a != null && b != null && ReferenceEquals(a, b);
    }

    private void RefreshCache()
    {
        if (cammyType != null
            && presetManagerType != null
            && presetsField != null
            && currentPresetProperty != null)
        {
            return;
        }

        ClearCache();

        cammyType = FindType("Cammy.Cammy");
        presetManagerType = FindType("Cammy.PresetManager");

        if (cammyType == null || presetManagerType == null)
            return;

        const BindingFlags staticFlags =
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Static |
            BindingFlags.FlattenHierarchy;

        const BindingFlags instanceFlags =
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Instance;

        configProperty = cammyType.GetProperty("Config", staticFlags);
        configField = cammyType.GetField("Config", staticFlags);

        currentPresetProperty = presetManagerType.GetProperty("CurrentPreset", staticFlags);
        activePresetProperty = presetManagerType.GetProperty("ActivePreset", staticFlags);
        presetOverrideProperty = presetManagerType.GetProperty("PresetOverride", staticFlags);
        defaultPresetProperty = presetManagerType.GetProperty("DefaultPreset", staticFlags);

        var config = GetConfigObject();
        if (config == null)
            return;

        presetsField = config.GetType().GetField("Presets", instanceFlags);

        var presets = GetPresetList();
        var firstPreset = presets?.Cast<object?>().FirstOrDefault(x => x != null);

        if (firstPreset == null)
            return;

        var presetType = firstPreset.GetType();
        presetNameField = presetType.GetField("Name", instanceFlags);
        presetNameProperty = presetType.GetProperty("Name", instanceFlags);
    }

    private static Type? FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = null;

            try
            {
                type = assembly.GetType(fullName, throwOnError: false, ignoreCase: false);
            }
            catch
            {
                // Ignore broken/unloaded reflection candidates.
            }

            if (type != null)
                return type;
        }

        return null;
    }

    private void ClearCache()
    {
        cammyType = null;
        presetManagerType = null;

        configProperty = null;
        configField = null;

        presetsField = null;
        currentPresetProperty = null;
        activePresetProperty = null;
        presetOverrideProperty = null;
        defaultPresetProperty = null;

        presetNameField = null;
        presetNameProperty = null;
    }

    private void PrintErrorThrottled(string message)
    {
        var now = DateTime.UtcNow;

        if (now < nextErrorPrintUtc)
            return;

        nextErrorPrintUtc = now + TimeSpan.FromSeconds(5);
        Plugin.ChatGui.PrintError(message);
    }
}

public readonly record struct CammyPresetInfo(
    int Index,
    string Name,
    bool IsCurrent,
    bool IsActivePreset,
    bool IsOverridePreset);
