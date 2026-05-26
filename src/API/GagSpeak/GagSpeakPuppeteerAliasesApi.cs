using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SayusGagExtender.API.GagSpeak
{
    public sealed class GagSpeakPuppeteerAliasesApi : IDisposable
    {
        private readonly Plugin plugin;
        private readonly GagSpeakReflectionContext context;

        public bool DebugLog = false;

        public GagSpeakPuppeteerAliasesApi(Plugin plugin, GagSpeakReflectionContext context)
        {
            this.plugin = plugin;
            this.context = context;
        }

        public void Dispose()
        {
        }

        public sealed class PuppeteerAliasInfo
        {
            public Guid Id { get; set; } = Guid.Empty;
            public string Name { get; set; } = string.Empty;
            public string TriggerCommand { get; set; } = string.Empty;
            public string FolderPath { get; set; } = string.Empty;
            public bool Enabled { get; set; } = false;
            public bool IgnoreCase { get; set; } = false;
            public bool IsValid { get; set; } = false;
            public List<string> WhitelistedUids { get; set; } = new();
            public List<string> WhitelistedNames { get; set; } = new();
            public List<string> WhitelistedPlayerNames { get; set; } = new();
        }

        public List<PuppeteerAliasInfo> GetAliases()
        {
            var result = new List<PuppeteerAliasInfo>();

            try
            {
                if (!context.EnsureReady())
                {
                    ChatPrintError("GagSpeak context is not ready.");
                    return result;
                }

                var manager = context.TryResolveServiceByTypeName("PuppeteerManager");
                if (manager == null)
                {
                    ChatPrintError("Could not resolve GagSpeak PuppeteerManager.");
                    return result;
                }

                var storage = GetPropertyValue(manager, "Storage");
                var items = GetPropertyValue(storage, "Items") as IEnumerable;
                if (items == null)
                {
                    ChatPrintError("GagSpeak PuppeteerManager.Storage.Items was null or not enumerable.");
                    return result;
                }

                foreach (var alias in items)
                {
                    if (alias == null)
                        continue;

                    var idObj = GetPropertyValue(alias, "Identifier");
                    var id = idObj is Guid guid ? guid : Guid.Empty;
                    var whitelist = GetStringSet(alias, "WhitelistedUIDs");

                    result.Add(new PuppeteerAliasInfo
                    {
                        Id = id,
                        Name = GetPropertyValue(alias, "Label") as string ?? string.Empty,
                        TriggerCommand = GetPropertyValue(alias, "InputCommand") as string ?? string.Empty,
                        FolderPath = GetAliasFolderPath(alias),
                        Enabled = GetPropertyValue(alias, "Enabled") is bool enabled && enabled,
                        IgnoreCase = GetPropertyValue(alias, "IgnoreCase") is bool ignoreCase && ignoreCase,
                        IsValid = InvokeBool(alias, "ValidAlias"),
                        WhitelistedUids = whitelist,
                        WhitelistedNames = whitelist.Select(ResolveKinksterNameOrUid).ToList(),
                        WhitelistedPlayerNames = whitelist.Select(ResolveKinksterPlayerName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    });
                }
            }
            catch (Exception ex)
            {
                ChatPrintError($"Failed to get GagSpeak puppeteer aliases: {ex}");
            }

            return result
                .OrderBy(a => a.FolderPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public void DumpAliases()
        {
            var aliases = GetAliases();
            if (aliases.Count == 0)
            {
                ChatPrint("GagSpeak puppeteer alias storage is empty.", force: true);
                return;
            }

            foreach (var alias in aliases)
            {
                var path = string.IsNullOrWhiteSpace(alias.FolderPath) ? "<root>" : alias.FolderPath;
                var whitelist = alias.WhitelistedNames.Count == 0 ? "Everyone" : string.Join(", ", alias.WhitelistedNames);
                ChatPrint($"GagSpeak alias: [{path}] {alias.Name} -> {alias.TriggerCommand} | Allowed: {whitelist}", force: true);
            }
        }

        private string GetAliasFolderPath(object alias)
        {
            try
            {
                var fileSystem = context.TryResolveServiceByTypeName("AliasesFileSystem");
                if (fileSystem == null)
                    return string.Empty;

                var findLeaf = fileSystem.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "FindLeaf" && m.GetParameters().Length == 2);
                if (findLeaf == null)
                    return string.Empty;

                var args = new object?[] { alias, null };
                var found = findLeaf.Invoke(fileSystem, args) is bool b && b;
                if (!found || args[1] == null)
                    return string.Empty;

                return GetPathFromLeaf(args[1]!);
            }
            catch (Exception ex)
            {
                ChatPrint($"Could not resolve GagSpeak alias folder path: {ex.Message}");
                return string.Empty;
            }
        }

        private static string GetPathFromLeaf(object leaf)
        {
            var directPath = GetStringMember(leaf, "FullPath") ?? GetStringMember(leaf, "FullName") ?? GetStringMember(leaf, "Path");
            if (!string.IsNullOrWhiteSpace(directPath))
                return TrimLeafNameFromPath(directPath, GetStringMember(leaf, "Name") ?? string.Empty);

            var folderNames = new List<string>();
            var current = GetObjectMember(leaf, "Parent") ?? GetObjectMember(leaf, "ParentFolder");
            var guard = 0;

            while (current != null && guard++ < 64)
            {
                var name = GetStringMember(current, "Name") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name) && !name.Equals("Root", StringComparison.OrdinalIgnoreCase))
                    folderNames.Add(name);

                current = GetObjectMember(current, "Parent") ?? GetObjectMember(current, "ParentFolder");
            }

            folderNames.Reverse();
            return string.Join("/", folderNames);
        }

        private static string TrimLeafNameFromPath(string path, string leafName)
        {
            path = path.Replace('\\', '/').Trim('/');
            leafName = leafName.Trim('/');

            if (!string.IsNullOrWhiteSpace(leafName) && path.EndsWith("/" + leafName, StringComparison.OrdinalIgnoreCase))
                return path[..^(leafName.Length + 1)];

            if (!string.IsNullOrWhiteSpace(leafName) && path.Equals(leafName, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return path;
        }

        private string ResolveKinksterNameOrUid(string uid)
        {
            try
            {
                var kinksters = context.TryResolveServiceByTypeName("KinksterManager");
                if (kinksters == null)
                    return uid;

                var method = kinksters.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "TryGetNickAliasOrUid" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(string));
                if (method == null)
                    return uid;

                var args = new object?[] { uid, null };
                var ok = method.Invoke(kinksters, args) is bool b && b;
                var name = args[1] as string;
                return ok && !string.IsNullOrWhiteSpace(name) ? name : uid;
            }
            catch
            {
                return uid;
            }
        }

        private string ResolveKinksterPlayerName(string uid)
        {
            try
            {
                var kinksters = context.TryResolveServiceByTypeName("KinksterManager");
                var allKinksters = GetObjectMember(kinksters!, "_allKinksters") as IEnumerable;
                if (allKinksters == null) return string.Empty;

                foreach (var entry in allKinksters)
                {
                    var key = GetObjectMember(entry, "Key");
                    var value = GetObjectMember(entry, "Value");
                    var keyUid = GetStringMember(key!, "UID") ?? string.Empty;
                    if (!string.Equals(keyUid, uid, StringComparison.OrdinalIgnoreCase)) continue;
                    return GetStringMember(value!, "PlayerName") ?? string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private static List<string> GetStringSet(object source, string propertyName)
        {
            var value = GetPropertyValue(source, propertyName) as IEnumerable;
            if (value == null)
                return new List<string>();

            return value.Cast<object?>()
                .Select(x => x?.ToString() ?? string.Empty)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool InvokeBool(object source, string methodName)
        {
            try
            {
                var method = source.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == 0);
                return method?.Invoke(source, Array.Empty<object>()) is bool value && value;
            }
            catch
            {
                return false;
            }
        }

        private static object? GetPropertyValue(object? source, string propertyName)
        {
            if (source == null)
                return null;

            return source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(source);
        }

        private static string? GetStringMember(object source, string memberName)
        {
            return GetObjectMember(source, memberName) as string;
        }

        private static object? GetObjectMember(object source, string memberName)
        {
            var type = source.GetType();
            var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null)
                return property.GetValue(source);

            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(source);
        }

        private void ChatPrint(string message, bool force = false)
        {
            if (DebugLog || force)
                Plugin.ChatGui.Print(message);
        }

        private void ChatPrintError(string message, bool force = false)
        {
            if (DebugLog || force)
                Plugin.ChatGui.PrintError(message);
        }
    }
}
