using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Hudi.Licensing;

internal static class PublisherVault
{
    internal static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HUDI Publisher");
    internal static string KeyPath=>Path.Combine(Root,"signing-key.dpapi");
    [StructLayout(LayoutKind.Sequential)]private struct Blob {internal int Length;internal IntPtr Data;}
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)]private static extern bool CryptProtectData(ref Blob input,string? description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)]private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32.dll")]private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Protect(byte[] bytes,bool encrypt)
    {
        var input=new Blob {Length=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length)};Blob output=default;Marshal.Copy(bytes,0,input.Data,bytes.Length);
        try
        {
            bool ok=encrypt?CryptProtectData(ref input,"HUDI publisher",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if(!ok)throw new CryptographicException(Marshal.GetLastWin32Error());
            var result=new byte[output.Length];Marshal.Copy(output.Data,result,0,result.Length);return result;
        }
        finally {Marshal.Copy(new byte[input.Length],0,input.Data,input.Length);Marshal.FreeHGlobal(input.Data);if(output.Data!=IntPtr.Zero){Marshal.Copy(new byte[output.Length],0,output.Data,output.Length);LocalFree(output.Data);}CryptographicOperations.ZeroMemory(bytes);}
    }
    internal static ECDsa Load(string? keyPath=null)
    {
        var bytes=Protect(File.ReadAllBytes(keyPath??KeyPath),false);
        try {var signer=ECDsa.Create();signer.ImportPkcs8PrivateKey(bytes,out _);return signer;}
        finally {CryptographicOperations.ZeroMemory(bytes);}
    }
    internal static void Store(ECDsa signer,string? keyPath=null)
    {
        string path=keyPath??KeyPath;Directory.CreateDirectory(Path.GetDirectoryName(path)!);var bytes=Protect(signer.ExportPkcs8PrivateKey(),true);
        string temp=path+".tmp";File.WriteAllBytes(temp,bytes);File.Move(temp,path,true);
    }
    internal static void Initialize(string publicFile)
    {
        if(!File.Exists(KeyPath)) {using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);Store(key);}
        using var signer=Load();string publicKey=Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo());
        if(File.Exists(publicFile)&&File.ReadAllText(publicFile).Trim()!=publicKey)throw new InvalidOperationException("Ключ издателя не соответствует существующей сборке. Восстановите исходный секрет.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(publicFile))!);File.WriteAllText(publicFile,publicKey);
    }
}
