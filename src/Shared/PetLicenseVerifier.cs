// Reusable verifier for any character. No publisher secret is required.
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DesktopPets.Licensing;
public sealed record PetLicenseClaims(int Version,string Product,string Id,string Customer,long Issued,long? Expires);
public static class PetLicenseVerifier
{
    public static bool TryVerify(string code,string productId,string publicKey,out PetLicenseClaims? claims)
    {
        claims=null;if(string.IsNullOrWhiteSpace(code)||code.Length>8192)return false;
        try
        {
            var parts=string.Concat(code.Where(c=>!char.IsWhiteSpace(c))).Split('.');
            if(parts.Length!=3||parts[0] is not ("PET1" or "HUDI1"))return false;
            byte[] Decode(string x){x=x.Replace('-','+').Replace('_','/');return Convert.FromBase64String(x.PadRight((x.Length+3)/4*4,'='));}
            using var key=ECDsa.Create();key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey),out _);
            if(!key.VerifyData(Encoding.UTF8.GetBytes(parts[0]+"."+parts[1]),Decode(parts[2]),HashAlgorithmName.SHA256))return false;
            var value=JsonSerializer.Deserialize<PetLicenseClaims>(Decode(parts[1]));long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if(value is null||value.Version!=1||value.Product!=productId||!Guid.TryParse(value.Id,out _)||string.IsNullOrWhiteSpace(value.Customer)||value.Customer.Length>160||value.Issued>now+86400)return false;
            if(value.Expires is {} end&&(end<=now||end<=value.Issued))return false;
            claims=value;return true;
        }
        catch(Exception ex) when(ex is FormatException or JsonException or CryptographicException or ArgumentException){return false;}
    }
}
