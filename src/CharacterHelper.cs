using Dalamud.Plugin.Services;
using System;
using System.Xml.Linq;

namespace SayusGagExtender
{
    public sealed class CharacterHelper : IDisposable
    {
        private readonly Plugin plugin;

        private DateTime nextCheckUtc = DateTime.MinValue;
        private readonly TimeSpan checkCooldown = TimeSpan.FromSeconds(1);

        private bool wasLoggedIn;
        private CharacterIdentity? currentCharacter;
        private CharacterIdentity? lastCharacter;

        public bool IsLoggedIn => Plugin.ClientState.IsLoggedIn;
        public CharacterIdentity? CurrentCharacter => currentCharacter;

        // General login: fires as soon as Dalamud says a character is logged in.
        // LocalPlayer may still be null here during loading/zoning.
        public event Action? OnLogin;

        // First time the actual character identity is available.
        public event Action<CharacterIdentity>? OnCharacterReady;
        public event Action<CharacterIdentity, CharacterIdentity>? OnCharacterChanged;
        public event Action<CharacterIdentity?>? OnLogout;

        public readonly record struct CharacterIdentity(
            string Name,
            uint HomeWorldId
        );

        public CharacterHelper(Plugin plugin)
        {
            this.plugin = plugin;
            Plugin.Framework.Update += OnFrameworkUpdate;
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            var now = DateTime.UtcNow;
            if (nextCheckUtc > now)
                return;

            nextCheckUtc = now + checkCooldown;
            
            var isLoggedIn = Plugin.ClientState.IsLoggedIn;

            if (!isLoggedIn)
            {
                if (wasLoggedIn)
                    OnLogout?.Invoke(currentCharacter);

                wasLoggedIn = false;
                currentCharacter = null;
                return;
            }

            if (!wasLoggedIn)
            {
                wasLoggedIn = true;
                OnLogin?.Invoke();
            }

            var character = TryGetCurrentCharacter();

            // Logged in, but LocalPlayer is temporarily unavailable.
            // This can happen while zoning, so do nothing.
            if (character is null)
                return;
            
            if (currentCharacter is null)
            {
                currentCharacter = character.Value;
                OnCharacterReady?.Invoke(currentCharacter.Value);
                return;
            }

            if (lastCharacter == null || !lastCharacter.Value.Equals(currentCharacter.Value))
            {
                OnCharacterChanged?.Invoke(lastCharacter.Value, currentCharacter.Value);
                lastCharacter = currentCharacter;

                
            }
        }

        public void Dispose()
        {
            Plugin.Framework.Update -= OnFrameworkUpdate;
        }

        private CharacterIdentity? TryGetCurrentCharacter()
        {
            var player = Plugin.ObjectTable.LocalPlayer;

            if (player is null)
                return null;

            var name = player.Name.ToString();
            var homeWorldId = player.HomeWorld.RowId;

            if (string.IsNullOrWhiteSpace(name) || homeWorldId == 0u)
                return null;
            
            return new CharacterIdentity(name, homeWorldId);
        }
    }
}
