using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LinkDeck.Core.Abstractions;
using LinkDeck.Core.Models;

namespace LinkDeck.Core.Config;

/// <summary>
/// Remembers which items the user added or confirmed through LinkDeck itself, as fingerprints of what each item runs
/// (type, target, arguments, working directory, RDP host/port/user; not its name, icon or colour).
/// An item added or changed by editing <c>config.json</c> from outside has no matching fingerprint, so it must be
/// confirmed before it opens. The file is protected with <see cref="IDataProtector"/> (DPAPI, current user):
/// an edited or copied file trusts nothing. Thread-safe.
/// </summary>
/// <remarks>
/// This is not a hard boundary against malware running as the same user (it could call DPAPI too), but it stops
/// other users, synced or shared config files and untargeted tampering from silently changing what a tile opens.
/// </remarks>
public sealed class TrustStore
{
    private const string Header = "linkdeck-trust-v1";

    private readonly object _gate = new();
    private readonly IDataProtector _protector;
    private HashSet<string> _trusted = new(StringComparer.Ordinal);

    public TrustStore(string trustPath, IDataProtector protector)
    {
        TrustPath = Path.GetFullPath(trustPath);
        _protector = protector;
    }

    public string TrustPath { get; }

    /// <summary>A trust file existed when <see cref="Load"/> ran (readable or not).</summary>
    public bool Existed { get; private set; }

    /// <summary>The trust file existed but was edited, damaged or belongs to another user or computer.</summary>
    public bool WasUnreadable { get; private set; }

    public void Load()
    {
        lock (_gate)
        {
            _trusted = new HashSet<string>(StringComparer.Ordinal);
            Existed = File.Exists(TrustPath);
            WasUnreadable = false;
            if (!Existed)
            {
                return;
            }

            byte[]? plain;
            try
            {
                plain = _protector.Unprotect(File.ReadAllBytes(TrustPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                plain = null;
            }

            var lines = plain is null ? null : Encoding.UTF8.GetString(plain).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines is null || lines.Length == 0 || lines[0] != Header)
            {
                WasUnreadable = true;
                return;
            }

            _trusted = new HashSet<string>(lines.Skip(1), StringComparer.Ordinal);
        }
    }

    public bool IsTrusted(Item item)
    {
        var fingerprint = Fingerprint(item);
        lock (_gate)
        {
            return _trusted.Contains(fingerprint);
        }
    }

    /// <summary>Adds <paramref name="trust"/>, forgets every fingerprint not in <paramref name="trust"/> or <paramref name="keep"/>, and saves.</summary>
    public void Update(IEnumerable<Item> trust, IEnumerable<Item> keep)
    {
        var add = trust.Select(Fingerprint).ToList();
        var retain = keep.Select(Fingerprint).ToHashSet(StringComparer.Ordinal);
        lock (_gate)
        {
            var next = new HashSet<string>(_trusted.Where(retain.Contains), StringComparer.Ordinal);
            next.UnionWith(add);
            var text = Header + "\n" + string.Join("\n", next.Order(StringComparer.Ordinal)) + "\n";
            AtomicFile.WriteBytes(TrustPath, _protector.Protect(Encoding.UTF8.GetBytes(text)), backupPath: null);
            _trusted = next;
            Existed = true;
            WasUnreadable = false;
        }
    }

    /// <summary>SHA-256 over everything that decides what an item runs.</summary>
    public static string Fingerprint(Item item)
    {
        string[] fields = item switch
        {
            UrlItem u => ["url", u.Url],
            PathItem p => ["path", p.Path, p.Args ?? "", p.WorkingDir ?? ""],
            RdpItem r => ["rdp", r.Host, r.EffectivePort.ToString(CultureInfo.InvariantCulture), r.Username ?? ""],
            _ => ["unknown", item.Target],
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\0', fields))));
    }
}
