using Dalamud.Bindings.ImGui;

namespace SayusGagExtender.Windows;

public partial class ConfigWindow
{
    private void DrawXIVMessengerTab()
    {
        var enabled = configuration.XivMessengerManagerEnabled;

        if (ImGui.Checkbox("Enable XIV Messenger bridge", ref enabled))
        {
            configuration.XivMessengerManagerEnabled = enabled;
            configuration.Save();

            plugin.XIVMessengerManager.Enforce();
        }

        ImGui.TextWrapped(
            "Bridges GagSpeak chat restrictions to XIV Messenger.");

        ImGui.TextWrapped(
            "When GagSpeak hides chat or a blindfold is active, XIV Messenger will be kept closed.");

        ImGui.TextWrapped(
            "When GagSpeak hides or disables chat input, XIV Messenger text input will also be disabled until both restrictions are removed.");
    }
}
