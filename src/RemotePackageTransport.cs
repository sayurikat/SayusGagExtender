using System;
using System.Collections.Generic;
using System.Linq;

namespace SayusGagExtender;

public sealed class RemotePackageTransport
{
    public const string CommandPrefix = "pkg";
    private const int MaxTellLength = 500;
    private const int SafetyMargin = 40;
    public const int DefaultMaxLineLength = MaxTellLength;

    private readonly Dictionary<string, PendingPackage> pendingPackages = [];

    public enum ReceiveResult
    {
        NotPackage,
        Pending,
        Complete,
        Invalid,
    }

    public readonly record struct ReceiveProgress(string Id, int Part, int Total, int Received, bool IsChunked);

    public IReadOnlyList<string> BuildTellLines(RemotePackage package, string prefix, int maxLineLength = DefaultMaxLineLength)
    {
        maxLineLength = Math.Clamp(maxLineLength, 100, MaxTellLength);
        var encoded = package.ToBase64Url();
        var singlePrefix = $"{prefix}::{CommandPrefix}:";
        var singleLine = $"{singlePrefix}{encoded}";

        if (singleLine.Length <= maxLineLength - SafetyMargin)
        {
            return [singleLine];
        }

        var id = Random.Shared.Next(0x100000, 0xFFFFFF).ToString("X6");
        var headerReserve = $"{prefix}::{CommandPrefix}:{id}:999/999:".Length;
        var chunkSize = Math.Max(50, maxLineLength - SafetyMargin - headerReserve);
        var total = (int)Math.Ceiling(encoded.Length / (double)chunkSize);
        var lines = new List<string>(total);

        for (var i = 0; i < total; i++)
        {
            var part = i + 1;
            var start = i * chunkSize;
            var length = Math.Min(chunkSize, encoded.Length - start);
            var chunk = encoded.Substring(start, length);
            lines.Add($"{prefix}::{CommandPrefix}:{id}:{part}/{total}:{chunk}");
        }

        return lines;
    }

    public bool TryReceive(string args, out RemotePackage package)
    {
        var result = TryReceive(args, out package, out _);
        return result == ReceiveResult.Complete;
    }

    public ReceiveResult TryReceive(string args, out RemotePackage package, out ReceiveProgress progress)
    {
        package = new RemotePackage(0);
        progress = default;

        if (!args.StartsWith($"{CommandPrefix}:", StringComparison.OrdinalIgnoreCase))
        {
            return ReceiveResult.NotPackage;
        }

        var rest = args[CommandPrefix.Length..].TrimStart(':');

        if (!rest.Contains(':'))
        {
            var complete = RemotePackage.TryFromBase64Url(rest, out package);
            progress = new ReceiveProgress(string.Empty, 1, 1, complete ? 1 : 0, false);
            return complete ? ReceiveResult.Complete : ReceiveResult.Invalid;
        }

        var firstColon = rest.IndexOf(':');
        var secondColon = rest.IndexOf(':', firstColon + 1);

        if (firstColon <= 0 || secondColon <= firstColon) return ReceiveResult.Invalid;

        var id = rest[..firstColon];
        var numbering = rest[(firstColon + 1)..secondColon];
        var chunk = rest[(secondColon + 1)..];

        var slash = numbering.IndexOf('/');
        if (slash <= 0) return ReceiveResult.Invalid;
        if (!int.TryParse(numbering[..slash], out var part)) return ReceiveResult.Invalid;
        if (!int.TryParse(numbering[(slash + 1)..], out var total)) return ReceiveResult.Invalid;
        if (part < 1 || total < 1 || part > total) return ReceiveResult.Invalid;

        if (!pendingPackages.TryGetValue(id, out var pending))
        {
            pending = new PendingPackage(total);
            pendingPackages[id] = pending;
        }

        if (pending.Total != total)
        {
            pendingPackages.Remove(id);
            progress = new ReceiveProgress(id, part, total, 0, true);
            return ReceiveResult.Invalid;
        }

        pending.Chunks[part - 1] = chunk;
        pending.LastUpdatedUtc = DateTime.UtcNow;
        var received = pending.Chunks.Count(x => !string.IsNullOrEmpty(x));
        progress = new ReceiveProgress(id, part, total, received, true);

        if (pending.Chunks.Any(string.IsNullOrEmpty))
        {
            return ReceiveResult.Pending;
        }

        pendingPackages.Remove(id);
        return RemotePackage.TryFromBase64Url(string.Concat(pending.Chunks), out package) ? ReceiveResult.Complete : ReceiveResult.Invalid;
    }

    public int ClearOldPendingPackages()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(2);
        var expired = pendingPackages.Where(x => x.Value.LastUpdatedUtc < cutoff).Select(x => x.Key).ToList();
        foreach (var entry in expired)
        {
            pendingPackages.Remove(entry);
        }
        return expired.Count;
    }

    private sealed class PendingPackage
    {
        public int Total { get; }
        public string[] Chunks { get; }
        public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

        public PendingPackage(int total)
        {
            Total = total;
            Chunks = new string[total];
        }
    }
}
