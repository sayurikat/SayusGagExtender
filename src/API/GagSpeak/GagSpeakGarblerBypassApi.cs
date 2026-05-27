using System;
using System.Linq;
using System.Reflection;
using Dalamud.Plugin.Services;

namespace SayusGagExtender.API.GagSpeak
{
    public sealed class GagSpeakGarblerBypassApi : IDisposable
    {
        private readonly GagSpeakReflectionContext context;
        private bool restorePending;
        private bool originalChatGarblerActive;
        private DateTime pauseUntilUtc = DateTime.MinValue;

        private static readonly TimeSpan PauseDuration = TimeSpan.FromSeconds(3);

        public GagSpeakGarblerBypassApi(Plugin plugin, GagSpeakReflectionContext context)
        {
            this.context = context;
        }

        public void Dispose()
        {
            Plugin.Framework.Update -= OnFrameworkUpdate;
            RestoreNow();
        }

        public void ExecuteWithoutGarbler(Action action)
        {
            if (action == null)
                return;

            PauseGarbler();
            action();
        }

        private void PauseGarbler()
        {
            try
            {
                var globals = GetClientGlobals();
                if (globals == null)
                    return;

                var current = GetBoolMember(globals, "ChatGarblerActive");
                if (current != true)
                    return;

                if (!restorePending)
                {
                    originalChatGarblerActive = true;
                    restorePending = true;
                    Plugin.Framework.Update += OnFrameworkUpdate;
                }

                pauseUntilUtc = DateTime.UtcNow + PauseDuration;
                SetBoolMember(globals, "ChatGarblerActive", false);
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug(ex, "Failed to pause GagSpeak chat garbler.");
            }
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            if (!restorePending || DateTime.UtcNow < pauseUntilUtc)
                return;

            RestoreNow();
        }

        private void RestoreNow()
        {
            if (!restorePending)
                return;

            try
            {
                var globals = GetClientGlobals();
                if (globals != null)
                    SetBoolMember(globals, "ChatGarblerActive", originalChatGarblerActive);
            }
            catch (Exception ex)
            {
                Plugin.Log.Debug(ex, "Failed to restore GagSpeak chat garbler.");
            }
            finally
            {
                restorePending = false;
                originalChatGarblerActive = false;
                pauseUntilUtc = DateTime.MinValue;
                Plugin.Framework.Update -= OnFrameworkUpdate;
            }
        }

        private object? GetClientGlobals()
        {
            if (context.EnsureReady())
            {
                var fromKnownAssembly = context.GagSpeakType.Assembly.GetTypes().FirstOrDefault(t => string.Equals(t.FullName, "GagSpeak.PlayerClient.ClientData", StringComparison.Ordinal));
                var globals = GetGlobalsFromClientDataType(fromKnownAssembly);
                if (globals != null)
                    return globals;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = assembly.GetName().Name ?? string.Empty;
                if (!name.Contains("GagSpeak", StringComparison.OrdinalIgnoreCase) && !name.Contains("GagspeakAPI", StringComparison.OrdinalIgnoreCase) && !name.Contains("ProjectGagSpeak", StringComparison.OrdinalIgnoreCase))
                    continue;

                var clientDataType = SafeGetTypes(assembly).FirstOrDefault(t => string.Equals(t.FullName, "GagSpeak.PlayerClient.ClientData", StringComparison.Ordinal) || string.Equals(t.Name, "ClientData", StringComparison.Ordinal));
                var globals = GetGlobalsFromClientDataType(clientDataType);
                if (globals != null)
                    return globals;
            }

            return null;
        }

        private static object? GetGlobalsFromClientDataType(Type? clientDataType)
        {
            if (clientDataType == null)
                return null;

            var globalsProperty = clientDataType.GetProperty("Globals", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (globalsProperty != null)
                return globalsProperty.GetValue(null);

            var globalsField = clientDataType.GetField("_clientGlobals", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) ?? clientDataType.GetField("Globals", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return globalsField?.GetValue(null);
        }

        private static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }
            catch
            {
                return [];
            }
        }

        private static bool? GetBoolMember(object obj, string name)
        {
            var type = obj.GetType();
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property?.GetValue(obj) is bool propertyValue)
                return propertyValue;

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field?.GetValue(obj) is bool fieldValue)
                return fieldValue;

            var backingField = type.GetField($"<{name}>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (backingField?.GetValue(obj) is bool backingFieldValue)
                return backingFieldValue;

            return null;
        }

        private static bool SetBoolMember(object obj, string name, bool value)
        {
            var type = obj.GetType();
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property?.CanWrite == true)
            {
                property.SetValue(obj, value);
                return true;
            }

            var backingField = type.GetField($"<{name}>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (backingField != null)
            {
                backingField.SetValue(obj, value);
                return true;
            }

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(obj, value);
                return true;
            }

            return false;
        }
    }
}
