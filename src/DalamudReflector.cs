using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;

namespace SayusGagExtender
{
    //from LLib

    public sealed class DalamudReflector
    {
        public DalamudReflector(IDalamudPluginInterface pluginInterface, IFramework framework, IPluginLog pluginLog)
        {
            this._pluginInterface = pluginInterface;
            this._framework = framework;
            this._pluginLog = pluginLog;
            object pluginManager = this.GetPluginManager();
            pluginManager.GetType().GetEvent("OnInstalledPluginsChanged").AddEventHandler(pluginManager, new Action(this.OnInstalledPluginsChanged));
            this._framework.Update += new IFramework.OnUpdateDelegate(this.FrameworkUpdate);
        }

        public void Dispose()
        {
            this._framework.Update -= new IFramework.OnUpdateDelegate(this.FrameworkUpdate);
            object pluginManager = this.GetPluginManager();
            pluginManager.GetType().GetEvent("OnInstalledPluginsChanged").RemoveEventHandler(pluginManager, new Action(this.OnInstalledPluginsChanged));
        }

        private void FrameworkUpdate(IFramework framework)
        {
            if (this._pluginsChanged)
            {
                this._pluginsChanged = false;
                this._pluginCache.Clear();
            }
        }

        private object GetPluginManager()
        {
            return this._pluginInterface.GetType().Assembly.GetType("Dalamud.Service`1", true).MakeGenericType(new Type[]
            {
                this._pluginInterface.GetType().Assembly.GetType("Dalamud.Plugin.Internal.PluginManager", true)
            }).GetMethod("Get").Invoke(null, BindingFlags.Default, null, Array.Empty<object>(), null);
        }

        public bool TryGetDalamudPlugin(string internalName, [MaybeNullWhen(false)] out IDalamudPlugin instance, bool suppressErrors = false, bool ignoreCache = false)
        {
            if (!ignoreCache && this._pluginCache.TryGetValue(internalName, out instance))
            {
                return true;
            }
            bool result;
            try
            {
                object pluginManager = this.GetPluginManager();
                foreach (object obj in ((IList)pluginManager.GetType().GetProperty("InstalledPlugins").GetValue(pluginManager)))
                {
                    if ((string)obj.GetType().GetProperty("Name").GetValue(obj) == internalName)
                    {
                        IDalamudPlugin dalamudPlugin = (IDalamudPlugin)((obj.GetType().Name == "LocalDevPlugin") ? obj.GetType().BaseType : obj.GetType()).GetField("instance", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj);
                        if (dalamudPlugin != null)
                        {
                            instance = dalamudPlugin;
                            this._pluginCache[internalName] = dalamudPlugin;
                            return true;
                        }
                        if (!suppressErrors)
                        {
                            this._pluginLog.Warning("[DalamudReflector] Found requested plugin " + internalName + " but it was null", Array.Empty<object>());
                        }
                    }
                }
                instance = null;
                result = false;
            }
            catch (Exception ex)
            {
                if (!suppressErrors)
                {
                    this._pluginLog.Error(ex, "Can't find " + internalName + " plugin: " + ex.Message, Array.Empty<object>());
                }
                instance = null;
                result = false;
            }
            return result;
        }

        private void OnInstalledPluginsChanged()
        {
            this._pluginLog.Verbose("Installed plugins changed event fired", Array.Empty<object>());
            this._pluginsChanged = true;
        }

        private readonly IDalamudPluginInterface _pluginInterface;

        private readonly IFramework _framework;

        private readonly IPluginLog _pluginLog;

        private readonly Dictionary<string, IDalamudPlugin> _pluginCache = new Dictionary<string, IDalamudPlugin>();

        private bool _pluginsChanged;
    }
}

