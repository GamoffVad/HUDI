using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hudi.Licensing;

internal sealed record LicenseClaims(int Version,string Product,string Id,string Customer,long Issued,long? Expires);
internal static class LicenseCodec
{
    // Temporary distribution policy: retain signing tools and existing codes,
    // but do not require activation for installation or normal runtime.
    internal static readonly bool ActivationRequired = false;
    internal const string Product="HUDI.DesktopPet";
    internal static string DirectoryPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HUDI");
    internal static string LicensePath=>Path.Combine(DirectoryPath,"license.key");
    internal static string PublicKey
    {
        get { using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("HUDI.license-public.key")??throw new InvalidOperationException("Нет ключа проверки лицензии.");using var reader=new StreamReader(stream);return reader.ReadToEnd().Trim(); }
    }
    private static string Encode(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static byte[] Decode(string text) { text=text.Replace('-','+').Replace('_','/');return Convert.FromBase64String(text.PadRight((text.Length+3)/4*4,'=')); }
    internal static string Normalize(string code)=>string.Concat(code.Where(c=>!char.IsWhiteSpace(c)));
    internal static string Sign(LicenseClaims claims,ECDsa signer)
    {
        string body="PET1."+Encode(JsonSerializer.SerializeToUtf8Bytes(claims));
        return body+"."+Encode(signer.SignData(Encoding.UTF8.GetBytes(body),HashAlgorithmName.SHA256));
    }
    internal static bool Validate(string code,string publicKey,out LicenseClaims? claims,out string error,DateTimeOffset? now=null,string expectedProduct=Product)
    {
        claims=null;error="Код активации не подходит. Проверьте, что он скопирован целиком.";
        if(string.IsNullOrWhiteSpace(code)||code.Length>8192)return false;
        try
        {
            code=Normalize(code);var parts=code.Split('.');if(parts.Length!=3||parts[0] is not ("PET1" or "HUDI1"))return false;
            using var verifier=ECDsa.Create();verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey),out _);
            if(!verifier.VerifyData(Encoding.UTF8.GetBytes(parts[0]+"."+parts[1]),Decode(parts[2]),HashAlgorithmName.SHA256))return false;
            var value=JsonSerializer.Deserialize<LicenseClaims>(Decode(parts[1]));
            if(value is null||value.Version!=1||value.Product!=expectedProduct||!Guid.TryParse(value.Id,out _)||string.IsNullOrWhiteSpace(value.Customer)||value.Customer.Length>160)return false;
            long time=(now??DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            if(value.Issued>time+86400) {error="Проверьте дату и время на компьютере.";return false;}
            if(value.Expires is {} expiry && expiry<=time) {error="Срок действия кода истёк. Нужен новый код активации.";return false;}
            if(value.Expires is {} end && end<=value.Issued)return false;
            claims=value;error="";return true;
        }
        catch(Exception ex) when(ex is FormatException or CryptographicException or JsonException or ArgumentException) {return false;}
    }
    internal static bool IsActivated(out LicenseClaims? claims)
    {
        claims=null;
        try {return File.Exists(LicensePath)&&Validate(File.ReadAllText(LicensePath),PublicKey,out claims,out _);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {return false;}
    }
    internal static void Save(string code)
    {
        if(!Validate(code,PublicKey,out _,out var error))throw new InvalidOperationException(error);
        Directory.CreateDirectory(DirectoryPath);string tmp=LicensePath+".tmp";
        File.WriteAllText(tmp,Normalize(code));File.Move(tmp,LicensePath,true);
    }
}
