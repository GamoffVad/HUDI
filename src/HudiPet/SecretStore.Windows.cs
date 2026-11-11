using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace HudiPet;

internal static class SecretStore
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool ok = protect ? CryptProtectData(ref input, "HUDI API", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                              : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException("Windows не смогла защитить или прочитать ключ.");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length); Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) { Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length); LocalFree(output.Data); }
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
    internal static string Protect(string value) => string.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), true));
    internal static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var bytes = Transform(Convert.FromBase64String(value), false);
        try { return Encoding.UTF8.GetString(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
