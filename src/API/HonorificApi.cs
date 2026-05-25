using Dalamud.Plugin.Ipc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SayusGagExtender.API;

public sealed class HonorificApi : IDisposable
{
    private readonly Plugin plugin;

    private readonly ICallGateSubscriber<(uint Major, uint Minor)> apiVersion;
    private readonly ICallGateSubscriber<int, string, object> setCharacterTitle;
    private readonly ICallGateSubscriber<int, object> clearCharacterTitle;
    private readonly ICallGateSubscriber<int, string> getCharacterTitle;
    private readonly ICallGateSubscriber<string> getLocalCharacterTitle;

    // PatMeHonorific uses 32, and this matches Honorific's practical temporary title limit.
    public const int MaxTitleLength = 32;

    public HonorificApi(Plugin plugin)
    {
        this.plugin = plugin;

        apiVersion = Plugin.PluginInterface.GetIpcSubscriber<(uint Major, uint Minor)>(
            "Honorific.ApiVersion");

        setCharacterTitle = Plugin.PluginInterface.GetIpcSubscriber<int, string, object>(
            "Honorific.SetCharacterTitle");

        clearCharacterTitle = Plugin.PluginInterface.GetIpcSubscriber<int, object>(
            "Honorific.ClearCharacterTitle");

        getCharacterTitle = Plugin.PluginInterface.GetIpcSubscriber<int, string>(
            "Honorific.GetCharacterTitle");

        getLocalCharacterTitle = Plugin.PluginInterface.GetIpcSubscriber<string>(
            "Honorific.GetLocalCharacterTitle");
    }

    public bool IsAvailable()
    {
        try
        {
            var version = apiVersion.InvokeFunc();
            return version.Major >= 3;
        }
        catch
        {
            return false;
        }
    }

    public (uint Major, uint Minor)? GetVersion()
    {
        try
        {
            return apiVersion.InvokeFunc();
        }
        catch
        {
            return null;
        }
    }

    public bool SetLocalTitle(
        string title,
        bool isPrefix = false,
        Vector3? color = null,
        Vector3? glow = null)
    {
        return SetTitleForObjectIndex(
            0,
            title,
            isPrefix,
            color,
            glow);
    }

    public bool SetTitleForObjectIndex(
        int objectIndex,
        string title,
        bool isPrefix = false,
        Vector3? color = null,
        Vector3? glow = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            return ClearTitleForObjectIndex(objectIndex);

        title = title.Trim();

        if (title.Length > MaxTitleLength)
        {
            Plugin.ChatGui.PrintError(
                $"Honorific title too long: {title.Length}/{MaxTitleLength} characters.");

            return false;
        }

        try
        {
            var titleData = new HonorificTitleData
            {
                Title = title,
                IsPrefix = isPrefix,
                Color = color,
                Glow = glow,
            };

            var json = JsonConvert.SerializeObject(titleData);

            setCharacterTitle.InvokeAction(objectIndex, json);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to set Honorific title.");
            return false;
        }
    }

    public bool ClearLocalTitle()
    {
        return ClearTitleForObjectIndex(0);
    }

    public bool ClearTitleForObjectIndex(int objectIndex)
    {
        try
        {
            clearCharacterTitle.InvokeAction(objectIndex);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to clear Honorific title.");
            return false;
        }
    }

    public string GetLocalTitleJson()
    {
        try
        {
            return getLocalCharacterTitle.InvokeFunc();
        }
        catch
        {
            return string.Empty;
        }
    }
    public bool SetLocalTitleJson(string titleJson)
    {
        return SetTitleJsonForObjectIndex(0, titleJson);
    }

    public bool SetTitleJsonForObjectIndex(int objectIndex, string titleJson)
    {
        if (string.IsNullOrWhiteSpace(titleJson))
            return ClearTitleForObjectIndex(objectIndex);

        try
        {
            setCharacterTitle.InvokeAction(objectIndex, titleJson);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to set Honorific title JSON.");
            return false;
        }
    }

    public string GetTitleJsonForObjectIndex(int objectIndex)
    {
        try
        {
            return getCharacterTitle.InvokeFunc(objectIndex);
        }
        catch
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
        // IPC subscribers do not need explicit unregistering.
        // Keeping this class disposable matches the rest of the project API wrappers.
    }

    private sealed class HonorificTitleData
    {
        public string Title { get; set; } = string.Empty;
        public bool IsPrefix { get; set; }
        public Vector3? Color { get; set; }
        public Vector3? Glow { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken>? ExtraData { get; set; }
    }

    public bool SetLocalTitleFromEditedJson(string sourceJson, string newTitle)
    {
        return SetTitleFromEditedJsonForObjectIndex(0, sourceJson, newTitle);
    }

    public bool SetTitleFromEditedJsonForObjectIndex(
        int objectIndex,
        string sourceJson,
        string newTitle)
    {
        if (string.IsNullOrWhiteSpace(sourceJson))
            return SetTitleForObjectIndex(objectIndex, newTitle);

        if (string.IsNullOrWhiteSpace(newTitle))
            return ClearTitleForObjectIndex(objectIndex);

        newTitle = newTitle.Trim();

        if (newTitle.Length > MaxTitleLength)
        {
            Plugin.ChatGui.PrintError(
                $"Honorific title too long: {newTitle.Length}/{MaxTitleLength} characters.");

            return false;
        }

        try
        {
            var titleData = JsonConvert.DeserializeObject<HonorificTitleData>(sourceJson)
                            ?? new HonorificTitleData();

            titleData.Title = newTitle;

            var editedJson = JsonConvert.SerializeObject(titleData);

            setCharacterTitle.InvokeAction(objectIndex, editedJson);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to edit Honorific title JSON.");
            return false;
        }
    }
}
