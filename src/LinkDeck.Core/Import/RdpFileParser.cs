using System.Globalization;
using LinkDeck.Core.Models;

namespace LinkDeck.Core.Import;

public sealed record RdpFileInfo(string Host, int? Port, string? Username);

/// <summary>Reads <c>full address</c>, <c>server port</c> and <c>username</c> from .rdp file text. Passwords are ignored.</summary>
public static class RdpFileParser
{
    public static RdpFileInfo? Parse(string content)
    {
        string? address = null;
        string? username = null;
        int? serverPort = null;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('﻿');
            var first = line.IndexOf(':', StringComparison.Ordinal);
            if (first <= 0)
            {
                continue;
            }

            var second = line.IndexOf(':', first + 1);
            if (second < 0)
            {
                continue;
            }

            var key = line[..first].Trim().ToLowerInvariant();
            var value = line[(second + 1)..].Trim();
            switch (key)
            {
                case "full address":
                    address = value;
                    break;
                case "username":
                    username = value.Length > 0 ? value : null;
                    break;
                case "server port":
                    if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var p) && RdpAddress.IsValidPort(p))
                    {
                        serverPort = p;
                    }

                    break;
            }
        }

        if (address is null || !RdpAddress.TryParse(address, out var host, out var port))
        {
            return null;
        }

        port ??= serverPort;
        if (port == RdpItem.DefaultPort)
        {
            port = null;
        }

        return new RdpFileInfo(host, port, username);
    }
}
