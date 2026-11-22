using System.Security.Cryptography;
using System.Text;
using LinkDeck.Core.Abstractions;

namespace LinkDeck.Core.Tests;

/// <summary>Stands in for DPAPI: data protected by another "user" (or edited) does not unprotect.</summary>
public sealed class FakeProtector : IDataProtector
{
    private readonly byte[] _key;

    public FakeProtector(string user = "me") => _key = Encoding.UTF8.GetBytes(user.PadRight(8, '#'));

    public byte[] Protect(byte[] data) => [.. HMACSHA256.HashData(_key, data), .. data];

    public byte[]? Unprotect(byte[] data)
    {
        if (data.Length < 32)
        {
            return null;
        }

        var payload = data[32..];
        return HMACSHA256.HashData(_key, payload).AsSpan().SequenceEqual(data.AsSpan(0, 32)) ? payload : null;
    }
}
