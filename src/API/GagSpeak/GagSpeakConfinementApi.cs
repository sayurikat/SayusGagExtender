using Dalamud.Game.ClientState.Conditions;
using System;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace SayusGagExtender.API.GagSpeak
{
    public sealed class GagSpeakConfinementApi : IDisposable
    {
        private static readonly TimeSpan InitialReleaseDuration = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PostMovementSettleDuration = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan MaxReleaseDuration = TimeSpan.FromMinutes(3);
        private const float MovementDistanceSq = 0.01f;

        private bool wasConfined;
        private DateTime releaseUntilUtc = DateTime.MinValue;
        private DateTime maxReleaseUntilUtc = DateTime.MinValue;
        private Vector3 lastPosition;
        private bool hasLastPosition;

        public GagSpeakConfinementApi(Plugin plugin)
        {
        }

        public void Dispose()
        {
            ResetReleaseWindow();
        }

        public bool IsConfined()
        {
            return TryGetConfinementState(out var state) && state.IsConfined;
        }

        public bool IsConfined(out GagSpeakConfinementState state)
        {
            return TryGetConfinementState(out state) && state.IsConfined;
        }

        public bool ShouldTemporarilyReleaseMovementLocks()
        {
            var now = DateTime.UtcNow;

            if (!TryGetConfinementState(out var state) || !state.IsConfined)
            {
                wasConfined = false;
                ResetReleaseWindow();
                return false;
            }

            if (!wasConfined)
            {
                wasConfined = true;
                BeginReleaseWindow(now, InitialReleaseDuration);
            }

            if (IsConfinementTravelConditionActive())
                ExtendReleaseWindow(now, PostMovementSettleDuration);

            var player = Plugin.ObjectTable.LocalPlayer;
            if (player == null)
            {
                ExtendReleaseWindow(now, PostMovementSettleDuration);
            }
            else if (!hasLastPosition)
            {
                lastPosition = player.Position;
                hasLastPosition = true;
            }
            else
            {
                var dx = player.Position.X - lastPosition.X;
                var dz = player.Position.Z - lastPosition.Z;
                var movedSq = (dx * dx) + (dz * dz);

                lastPosition = player.Position;

                if (movedSq > MovementDistanceSq)
                    ExtendReleaseWindow(now, PostMovementSettleDuration);
            }

            if (maxReleaseUntilUtc > DateTime.MinValue && now > maxReleaseUntilUtc)
                return false;

            return now < releaseUntilUtc;
        }

        private bool TryGetConfinementState(out GagSpeakConfinementState state)
        {
            state = default;

            try
            {
                var clientDataType = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(IsGagSpeakAssembly)
                    .Select(a => a.GetType("GagSpeak.PlayerClient.ClientData", throwOnError: false))
                    .FirstOrDefault(t => t != null);

                if (clientDataType == null)
                    return false;

                var hardcoreProperty = clientDataType.GetProperty("Hardcore", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                var hardcore = hardcoreProperty?.GetValue(null);
                if (hardcore == null)
                    return false;

                var confinement = ReadStringProperty(hardcore, "IndoorConfinement");
                if (string.IsNullOrWhiteSpace(confinement))
                    return true;

                state = new GagSpeakConfinementState(confinement, ReadDateTimeOffsetProperty(hardcore, "ConfinementTimer"), ReadUInt16Property(hardcore, "ConfinedWorld"), ReadInt32Property(hardcore, "ConfinedCity"), ReadInt32Property(hardcore, "ConfinedWard"), ReadInt32Property(hardcore, "ConfinedPlaceId"), ReadBoolProperty(hardcore, "ConfinedInApartment"), ReadBoolProperty(hardcore, "ConfinedInSubdivision"));

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "Failed to read GagSpeak confinement state.");
                return false;
            }
        }

        private void BeginReleaseWindow(DateTime now, TimeSpan duration)
        {
            releaseUntilUtc = now + duration;
            maxReleaseUntilUtc = now + MaxReleaseDuration;
            hasLastPosition = false;
        }

        private void ExtendReleaseWindow(DateTime now, TimeSpan duration)
        {
            if (maxReleaseUntilUtc == DateTime.MinValue)
                maxReleaseUntilUtc = now + MaxReleaseDuration;

            var wantedUntil = now + duration;
            if (wantedUntil > releaseUntilUtc)
                releaseUntilUtc = wantedUntil;
        }

        private void ResetReleaseWindow()
        {
            releaseUntilUtc = DateTime.MinValue;
            maxReleaseUntilUtc = DateTime.MinValue;
            hasLastPosition = false;
        }

        private static bool IsConfinementTravelConditionActive()
        {
            return Plugin.Condition.Any(ConditionFlag.BetweenAreas, ConditionFlag.BetweenAreas51, ConditionFlag.Occupied, ConditionFlag.Occupied30, ConditionFlag.OccupiedInEvent, ConditionFlag.OccupiedInQuestEvent, ConditionFlag.Occupied33, ConditionFlag.OccupiedInCutSceneEvent, ConditionFlag.Casting, ConditionFlag.Mounting, ConditionFlag.Mounting71, ConditionFlag.Jumping, ConditionFlag.Jumping61);
        }

        private static bool IsGagSpeakAssembly(Assembly assembly)
        {
            var name = assembly.GetName().Name ?? string.Empty;
            return name.Contains("GagSpeak", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("ProjectGagSpeak", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("GagspeakAPI", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadStringProperty(object obj, string name)
            => obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(obj) as string ?? string.Empty;

        private static bool ReadBoolProperty(object obj, string name)
            => obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(obj) is bool value && value;

        private static int ReadInt32Property(object obj, string name)
        {
            var value = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(obj);
            return value == null ? 0 : Convert.ToInt32(value);
        }

        private static ushort ReadUInt16Property(object obj, string name)
        {
            var value = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(obj);
            return value == null ? (ushort)0 : Convert.ToUInt16(value);
        }

        private static DateTimeOffset ReadDateTimeOffsetProperty(object obj, string name)
        {
            var value = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(obj);
            return value is DateTimeOffset timer ? timer : DateTimeOffset.MinValue;
        }
    }

    public readonly record struct GagSpeakConfinementState(string IndoorConfinement, DateTimeOffset ConfinementTimer, ushort ConfinedWorld, int ConfinedCity, int ConfinedWard, int ConfinedPlaceId, bool ConfinedInApartment, bool ConfinedInSubdivision)
    {
        public bool IsConfined => !string.IsNullOrWhiteSpace(IndoorConfinement);
    }
}
