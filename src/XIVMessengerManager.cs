using Dalamud.Plugin.Services;
using System;

namespace SayusGagExtender;

public sealed class XIVMessengerManager : IDisposable
{
    private readonly Plugin plugin;

    private DateTime nextRefreshUtc = DateTime.MinValue;
    private readonly TimeSpan RefreshCooldown = TimeSpan.FromMilliseconds(250);

    private bool lastWantedTextInputEnabled = true;

    public bool IsActive { get; private set; }

    public bool IsClosedByChatHidden { get; private set; }
    public bool IsClosedByBlindfold { get; private set; }
    public bool IsTextInputBlocked { get; private set; }

    public XIVMessengerManager(Plugin plugin)
    {
        this.plugin = plugin;

        Plugin.Framework.Update += OnFrameworkUpdate;

        plugin.GagSpeakRestrictionsApi.OnBlindfoldStateChanged += OnBlindfoldChanged;

        plugin.GagSpeakChatMonitorApi.OnChatboxHiddenChanged += OnChatboxHiddenChanged;
        plugin.GagSpeakChatMonitorApi.OnChatInputHiddenChanged += OnChatInputHiddenChanged;
        plugin.GagSpeakChatMonitorApi.OnChatInputDisabledChanged += OnChatInputDisabledChanged;

        Enforce();
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnFrameworkUpdate;

        plugin.GagSpeakRestrictionsApi.OnBlindfoldStateChanged -= OnBlindfoldChanged;

        plugin.GagSpeakChatMonitorApi.OnChatboxHiddenChanged -= OnChatboxHiddenChanged;
        plugin.GagSpeakChatMonitorApi.OnChatInputHiddenChanged -= OnChatInputHiddenChanged;
        plugin.GagSpeakChatMonitorApi.OnChatInputDisabledChanged -= OnChatInputDisabledChanged;

        ReleaseTextInputIfNeeded();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (DateTime.UtcNow < nextRefreshUtc)
            return;

        nextRefreshUtc = DateTime.UtcNow + RefreshCooldown;

        Enforce();
    }

    private void OnBlindfoldChanged(bool enabled)
    {
        Enforce();
    }

    private void OnChatboxHiddenChanged(bool enabled)
    {
        Enforce();
    }

    private void OnChatInputHiddenChanged(bool enabled)
    {
        Enforce();
    }

    private void OnChatInputDisabledChanged(bool enabled)
    {
        Enforce();
    }

    public void Enforce()
    {
        if (!plugin.Configuration.XivMessengerManagerEnabled)
        {
            IsActive = false;
            IsClosedByChatHidden = false;
            IsClosedByBlindfold = false;
            IsTextInputBlocked = false;

            ReleaseTextInputIfNeeded();
            return;
        }

        IsClosedByChatHidden = plugin.ChatMonitor.chatboxHidden;
        IsClosedByBlindfold = plugin.BlindfoldMonitor.blindfolded;

        IsTextInputBlocked =
            plugin.ChatMonitor.chatboxInputHidden ||
            plugin.ChatMonitor.chatboxInputDisabled;

        IsActive =
            IsClosedByChatHidden ||
            IsClosedByBlindfold ||
            IsTextInputBlocked;

        EnforceWindowClosed();
        EnforceTextInput();
    }

    private void EnforceWindowClosed()
    {
        if (!IsClosedByChatHidden && !IsClosedByBlindfold)
            return;

        if (!plugin.XivMessengerApi.IsWindowOpen())
            return;

        plugin.XivMessengerApi.CloseWindow();
    }

    private void EnforceTextInput()
    {
        var wantedEnabled = !IsTextInputBlocked;

        if (wantedEnabled == lastWantedTextInputEnabled)
            return;

        if (plugin.XivMessengerApi.ToggleTextInput(wantedEnabled))
            lastWantedTextInputEnabled = wantedEnabled;
    }

    private void ReleaseTextInputIfNeeded()
    {
        if (lastWantedTextInputEnabled)
            return;

        if (plugin.XivMessengerApi.ToggleTextInput(true))
            lastWantedTextInputEnabled = true;
    }
}
