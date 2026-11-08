using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Hudi.Licensing;
using DesktopPets.Licensing;

namespace Hudi.Publisher;
internal static class CatalogVerification
{
    internal static int Run(string directory)
    {
        Directory.CreateDirectory(directory);var checks=new Dictionary<string,bool>();
        try
        {
            var catalog=new ProductCatalog(Path.Combine(directory,Guid.NewGuid().ToString("N")));
            var a=catalog.Create("Персонаж А","Example.A");var b=catalog.Create("Персонаж Б","Example.B");
            using var keyA=PublisherVault.Load(a.KeyFile);using var keyB=PublisherVault.Load(b.KeyFile);
            var claims=new LicenseClaims(1,a.Product,Guid.NewGuid().ToString(),"Тест",DateTimeOffset.UtcNow.ToUnixTimeSeconds(),null);string code=LicenseCodec.Sign(claims,keyA);
            checks["separate_keys"]=a.PublicKey!=b.PublicKey;
            checks["own_product_valid"]=LicenseCodec.Validate(code,a.PublicKey,out _,out _,expectedProduct:a.Product);
            checks["other_product_rejected"]=!LicenseCodec.Validate(code,a.PublicKey,out _,out _,expectedProduct:b.Product);
            checks["other_key_rejected"]=!LicenseCodec.Validate(code,b.PublicKey,out _,out _,expectedProduct:a.Product);
            checks["exported_verifier_valid"]=PetLicenseVerifier.TryVerify(code,a.Product,a.PublicKey,out _);
            checks["exported_verifier_rejects_other"]=!PetLicenseVerifier.TryVerify(code,b.Product,b.PublicKey,out _);
            checks["catalog_roundtrip"]=catalog.Load().Length==2;
            try{catalog.Create("Дубликат","example.a");checks["duplicate_rejected"]=false;}catch(InvalidOperationException){checks["duplicate_rejected"]=true;}
            const string testPassword="fixture-password-only-123";
            byte[] encrypted=keyA.ExportEncryptedPkcs8PrivateKey(testPassword,new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc,HashAlgorithmName.SHA256,600000));
            using var restored=ECDsa.Create();restored.ImportEncryptedPkcs8PrivateKey(testPassword,encrypted,out _);
            checks["encrypted_backup_roundtrip"]=Convert.ToBase64String(restored.ExportSubjectPublicKeyInfo())==a.PublicKey;
            using var bad=ECDsa.Create();try{bad.ImportEncryptedPkcs8PrivateKey("incorrect-password",encrypted,out _);checks["bad_password_rejected"]=false;}catch(CryptographicException){checks["bad_password_rejected"]=true;}
            try{catalog.Import(a.Name,a.Product,keyB);checks["wrong_restore_rejected"]=false;}catch(InvalidOperationException){checks["wrong_restore_rejected"]=true;}
            bool success=checks.Values.All(x=>x);File.WriteAllText(Path.Combine(directory,"catalog-tests.json"),JsonSerializer.Serialize(new{success,checks},new JsonSerializerOptions{WriteIndented=true}));return success?0:1;
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(directory,"catalog-tests.json"),JsonSerializer.Serialize(new{success=false,error=ex.ToString(),checks}));return 1;}
    }
}
