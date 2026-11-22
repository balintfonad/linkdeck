using System.Security.Cryptography;
using System.Text;

namespace LinkDeck.Core.Config;

internal static class AtomicFile
{
    /// <summary>Writes via <c>path.tmp</c> then <see cref="File.Replace(string, string, string?)"/>, keeping <c>path.bak</c>.</summary>
    public static void Write(string path, string content, string? backupPath) =>
        WriteBytes(path, Utf8NoBom.GetBytes(content), backupPath);

    public static void WriteBytes(string path, byte[] content, string? backupPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, content);
        if (File.Exists(path))
        {
            File.Replace(tmp, path, backupPath, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmp, path);
        }
    }

    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string Hash(string content) => Hash(Encoding.UTF8.GetBytes(content));

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
