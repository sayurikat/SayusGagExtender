using System;
using System.Collections.Generic;
using System.Linq;

namespace SayusGagExtender;

public sealed class RemotePackageTransport
{
    public const string CommandPrefix = "pkg";
    private const int MaxTellLength = 500;
    private const int SafetyMargin = 20;

    private readonly Dictionary<string, PendingPackage> pendingPackages = [];

    public IReadOnlyList<string> BuildTellLines(RemotePackage package, string prefix)
    {
  
        var encoded = package.ToBase64Url();
        var singlePrefix = $"{prefix}::{CommandPrefix}:";
        var singleLine = $"{singlePrefix}{encoded}";

        if (singleLine.Length <= MaxTellLength - SafetyMargin)
        {
            return [singleLine];
        }

        var id = Random.Shared.Next(0x100000, 0xFFFFFF).ToString("X6");
        var headerReserve = $"{prefix}::{CommandPrefix}:{id}:999/999:".Length;
        var chunkSize = Math.Max(50, MaxTellLength - SafetyMargin - headerReserve);
        var total = (int)Math.Ceiling(encoded.Length / (double)chunkSize);
        var lines = new List<string>(total);

        for (var i = 0; i < total; i++)
        {
            var part = i + 1;
            var start = i * chunkSize;
            var length = Math.Min(chunkSize, encoded.Length - start);
            var chunk = encoded.Substring(start, length);
            lines.Add($"{CommandPrefix}:{id}:{part}/{total}:{chunk}");
        }

        return lines;
    }

    public bool TryReceive(string args, out RemotePackage package)
    {
        package = new RemotePackage(0);

        if (!args.StartsWith($"{CommandPrefix}:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = args[CommandPrefix.Length..].TrimStart(':');

        if (!rest.Contains(':'))
        {
            return RemotePackage.TryFromBase64Url(rest, out package);
        }

        var firstColon = rest.IndexOf(':');
        var secondColon = rest.IndexOf(':', firstColon + 1);

        if (firstColon <= 0 || secondColon <= firstColon) return false;

        var id = rest[..firstColon];
        var numbering = rest[(firstColon + 1)..secondColon];
        var chunk = rest[(secondColon + 1)..];

        var slash = numbering.IndexOf('/');
        if (slash <= 0) return false;
        if (!int.TryParse(numbering[..slash], out var part)) return false;
        if (!int.TryParse(numbering[(slash + 1)..], out var total)) return false;
        if (part < 1 || total < 1 || part > total) return false;

        if (!pendingPackages.TryGetValue(id, out var pending))
        {
            pending = new PendingPackage(total);
            pendingPackages[id] = pending;
        }

        if (pending.Total != total)
        {
            pendingPackages.Remove(id);
            return false;
        }

        pending.Chunks[part - 1] = chunk;

        if (pending.Chunks.Any(string.IsNullOrEmpty))
        {
            return false;
        }

        pendingPackages.Remove(id);
        return RemotePackage.TryFromBase64Url(string.Concat(pending.Chunks), out package);
    }

    public void ClearOldPendingPackages()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(2);
        foreach (var entry in pendingPackages.Where(x => x.Value.CreatedUtc < cutoff).Select(x => x.Key).ToList())
        {
            pendingPackages.Remove(entry);
        }
    }

    private sealed class PendingPackage
    {
        public int Total { get; }
        public string[] Chunks { get; }
        public DateTime CreatedUtc { get; } = DateTime.UtcNow;

        public PendingPackage(int total)
        {
            Total = total;
            Chunks = new string[total];
        }
    }
}
