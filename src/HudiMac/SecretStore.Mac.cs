using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace HudiPet;

internal static class SecretStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private static readonly byte[] Service = "com.hudi.desktop.api"u8.ToArray();

    internal static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var id = Guid.NewGuid().ToString("N");
        byte[] account = Encoding.ASCII.GetBytes(id), key = Encoding.UTF8.GetBytes(value);
        try
        {
            Check(SecKeychainAddGenericPassword(IntPtr.Zero, (uint)Service.Length, Service,
                (uint)account.Length, account, (uint)key.Length, key, IntPtr.Zero));
            return "keychain:" + id;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    internal static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        const string prefix = "keychain:";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(value[prefix.Length..], "N", out _))
            throw new CryptographicException("Введите ключ API на этом Mac.");
        byte[] account = Encoding.ASCII.GetBytes(value[prefix.Length..]);
        Check(SecKeychainFindGenericPassword(IntPtr.Zero, (uint)Service.Length, Service,
            (uint)account.Length, account, out uint length, out IntPtr data, IntPtr.Zero));
        try { return Marshal.PtrToStringUTF8(data, checked((int)length)); }
        finally { SecKeychainItemFreeContent(IntPtr.Zero, data); }
    }

    private static void Check(int status)
    {
        if (status != 0) throw new CryptographicException($"Ошибка Связки ключей macOS ({status}).");
    }

    [DllImport(Security)]
    private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLength,
        byte[] service, uint accountLength, byte[] account, uint length, byte[] data, IntPtr item);

    [DllImport(Security)]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength,
        byte[] service, uint accountLength, byte[] account, out uint length, out IntPtr data, IntPtr item);

    [DllImport(Security)]
    private static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);
}
