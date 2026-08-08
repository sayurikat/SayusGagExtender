using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using static SayusGagExtender.CharacterHelper;

namespace SayusGagExtender;

public sealed class CharacterProfileManager : IDisposable
{
    private const int CharacterProfileConfigurationVersion = 1;

    private readonly Plugin plugin;
    private readonly Configuration rootConfiguration;
    private string activeProfileKey = string.Empty;

    public string ActiveProfileDisplayName { get; private set; } = "No character loaded";
    public event Action<CharacterIdentity, Configuration> ProfileChanged;

    public CharacterProfileManager(Plugin plugin, Configuration rootConfiguration)
    {
        this.plugin = plugin;
        this.rootConfiguration = rootConfiguration;

        NormalizeStoredProfiles();

        plugin.CharacterHelper.OnCharacterReady += ActivateProfile;
        plugin.CharacterHelper.OnCharacterChanged += OnCharacterChanged;
        plugin.CharacterHelper.OnLogout += OnLogout;

        if (plugin.CharacterHelper.CurrentCharacter is { } character)
            ActivateProfile(character);
    }

    public void Dispose()
    {
        plugin.CharacterHelper.OnCharacterReady -= ActivateProfile;
        plugin.CharacterHelper.OnCharacterChanged -= OnCharacterChanged;
        plugin.CharacterHelper.OnLogout -= OnLogout;
    }

    public void Save()
    {
        rootConfiguration.Version = Math.Max(rootConfiguration.Version, CharacterProfileConfigurationVersion);
        Plugin.PluginInterface.SavePluginConfig(rootConfiguration);
    }

    private void OnCharacterChanged(CharacterIdentity previousCharacter, CharacterIdentity currentCharacter)
    {
        ActivateProfile(currentCharacter);
    }

    private void OnLogout(CharacterIdentity? character)
    {
        Save();
    }

    private void ActivateProfile(CharacterIdentity character)
    {
        var profileKey = GetProfileKey(character);
        if (string.Equals(activeProfileKey, profileKey, StringComparison.OrdinalIgnoreCase))
            return;

        if (!rootConfiguration.CharacterProfiles.TryGetValue(profileKey, out var profile) || profile is null)
        {
            profile = ShouldMigrateLegacyConfiguration(character) ? CreateLegacyProfile(character) : CreateEmptyProfile();
            rootConfiguration.CharacterProfiles[profileKey] = profile;
        }

        MarkAsCharacterProfile(profile);

        activeProfileKey = profileKey;
        ActiveProfileDisplayName = GetProfileDisplayName(character);
        plugin.ActivateConfiguration(profile);
        Save();
        ProfileChanged?.Invoke(character, profile);
    }

    private void NormalizeStoredProfiles()
    {
        rootConfiguration.CharacterProfiles = rootConfiguration.CharacterProfiles is null
            ? new Dictionary<string, Configuration>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, Configuration>(rootConfiguration.CharacterProfiles, StringComparer.OrdinalIgnoreCase);

        foreach (var profile in rootConfiguration.CharacterProfiles.Values)
        {
            if (profile is not null)
                MarkAsCharacterProfile(profile);
        }
    }

    private bool ShouldMigrateLegacyConfiguration(CharacterIdentity character)
    {
        if (rootConfiguration.CharacterProfilesMigrated)
            return false;

        var configuredMainName = rootConfiguration.GagSpeakMasterName?.Trim();
        if (string.IsNullOrWhiteSpace(configuredMainName))
            return true;

        if (!string.Equals(configuredMainName, character.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var configuredMainWorld = rootConfiguration.GagSpeakMasterWorld?.Trim();
        if (string.IsNullOrWhiteSpace(configuredMainWorld))
            return true;

        var currentWorld = plugin.Utils.WorldRowIDToString(character.HomeWorldId);
        return string.Equals(configuredMainWorld, currentWorld, StringComparison.OrdinalIgnoreCase);
    }

    private Configuration CreateLegacyProfile(CharacterIdentity character)
    {
        var profileJson = JObject.FromObject(rootConfiguration);
        profileJson.Remove(nameof(Configuration.CharacterProfiles));
        profileJson.Remove(nameof(Configuration.CharacterProfilesMigrated));
        profileJson.Remove(nameof(Configuration.ActiveRestraintSet));
        profileJson.Remove(nameof(Configuration.ActiveRestrictions));
        profileJson.Remove(nameof(Configuration.ActiveGags));
        profileJson.Remove(nameof(Configuration.GagSpeakRestraintCloner));
        profileJson.Remove(nameof(Configuration.GagSpeakEnforcedRestraintCloner));
        profileJson.Remove(nameof(Configuration.GagSpeakMasterName));
        profileJson.Remove(nameof(Configuration.GagSpeakMasterWorld));

        var profile = profileJson.ToObject<Configuration>() ?? new Configuration();
        PruneLegacyCharacterCollections(profile, character);
        MarkAsCharacterProfile(profile);
        rootConfiguration.CharacterProfilesMigrated = true;
        return profile;
    }

    private static Configuration CreateEmptyProfile()
    {
        var profile = new Configuration();
        MarkAsCharacterProfile(profile);
        return profile;
    }

    private static void MarkAsCharacterProfile(Configuration profile)
    {
        profile.IsCharacterProfile = true;
        profile.CharacterProfiles = null;
    }

    private static void PruneLegacyCharacterCollections(Configuration profile, CharacterIdentity character)
    {
        if (profile.JobRouletteWhitelistedGearsetsByCharacter is null)
            return;

        var characterKey = GetProfileKey(character);
        if (profile.JobRouletteWhitelistedGearsetsByCharacter.TryGetValue(characterKey, out var whitelist) && whitelist is not null)
        {
            profile.JobRouletteWhitelistedGearsetsByCharacter = new Dictionary<string, List<Configuration.JobRouletteGearsetConfig>>(StringComparer.OrdinalIgnoreCase)
            {
                [characterKey] = whitelist,
            };
            return;
        }

        profile.JobRouletteWhitelistedGearsetsByCharacter = new Dictionary<string, List<Configuration.JobRouletteGearsetConfig>>(StringComparer.OrdinalIgnoreCase);
    }

    private string GetProfileDisplayName(CharacterIdentity character)
    {
        var world = plugin.Utils.WorldRowIDToString(character.HomeWorldId);
        return string.IsNullOrWhiteSpace(world) ? GetProfileKey(character) : $"{character.Name.Trim()}@{world}";
    }

    private static string GetProfileKey(CharacterIdentity character)
    {
        return $"{character.Name.Trim()}@{character.HomeWorldId}";
    }
}
