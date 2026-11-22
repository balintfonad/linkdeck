using System.Runtime.InteropServices;
using LinkDeck.Core.Abstractions;

namespace LinkDeck.Platform.Windows;

/// <summary>
/// DPAPI (CryptProtectData) for the current user: only this Windows account on this computer can unprotect the data,
/// and any change to the protected bytes makes unprotecting fail.
/// </summary>
public sealed unsafe partial class DpapiProtector : IDataProtector
{
    private const uint CryptProtectUiForbidden = 0x1;

    private static readonly byte[] Entropy = "LinkDeck.Trust.v1"u8.ToArray();

    public byte[] Protect(byte[] data)
    {
        var result = Transform(data, protect: true);
        return result ?? throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
    }

    public byte[]? Unprotect(byte[] data) => Transform(data, protect: false);

    private static byte[]? Transform(byte[] data, bool protect)
    {
        fixed (byte* input = data)
        fixed (byte* entropy = Entropy)
        {
            var inBlob = new DataBlob { Size = data.Length, Data = (nint)input };
            var entropyBlob = new DataBlob { Size = Entropy.Length, Data = (nint)entropy };
            var ok = protect
                ? CryptProtectData(in inBlob, null, in entropyBlob, 0, 0, CryptProtectUiForbidden, out var outBlob)
                : CryptUnprotectData(in inBlob, 0, in entropyBlob, 0, 0, CryptProtectUiForbidden, out outBlob);
            if (!ok)
            {
                return null;
            }

            try
            {
                return new ReadOnlySpan<byte>((void*)outBlob.Data, outBlob.Size).ToArray();
            }
            finally
            {
                LocalFree(outBlob.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public nint Data;
    }

    [LibraryImport("crypt32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(in DataBlob dataIn, string? description, in DataBlob entropy, nint reserved, nint prompt, uint flags, out DataBlob dataOut);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(in DataBlob dataIn, nint description, in DataBlob entropy, nint reserved, nint prompt, uint flags, out DataBlob dataOut);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
