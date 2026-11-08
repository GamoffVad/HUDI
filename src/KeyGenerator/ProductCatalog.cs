using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hudi.Licensing;

namespace Hudi.Publisher;
internal sealed record ProductProfile(string Name,string Product,string PublicKey,string KeyFile,string Directory)
{public override string ToString()=>Name;}
internal sealed class ProductCatalog
{
    private readonly string root;
    internal ProductCatalog(string? testRoot=null)=>root=testRoot??Path.Combine(PublisherVault.Root,"products");
    internal ProductProfile[] Load()
    {
        var profiles=new List<ProductProfile>();Directory.CreateDirectory(root);
        foreach(string dir in Directory.EnumerateDirectories(root))
        {
            try
            {
                using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"product.json")));var x=json.RootElement;
                profiles.Add(new(x.GetProperty("name").GetString()!,x.GetProperty("productId").GetString()!,x.GetProperty("publicKey").GetString()!,Path.Combine(dir,"signing-key.dpapi"),dir));
            }
            catch(Exception ex) when(ex is IOException or JsonException or KeyNotFoundException) { }
        }
        return profiles.OrderBy(p=>p.Name).ToArray();
    }
    internal void EnsureHudi()
    {
        if(Load().Any(p=>p.Product==LicenseCodec.Product))return;
        string dir=Path.Combine(root,"hudi");Directory.CreateDirectory(dir);
        if(File.Exists(PublisherVault.KeyPath)&&!File.Exists(Path.Combine(dir,"signing-key.dpapi")))File.Copy(PublisherVault.KeyPath,Path.Combine(dir,"signing-key.dpapi"),false);
        Write(new("HUDI",LicenseCodec.Product,LicenseCodec.PublicKey,Path.Combine(dir,"signing-key.dpapi"),dir));
    }
    private static void Write(ProductProfile profile)=>File.WriteAllText(Path.Combine(profile.Directory,"product.json"),JsonSerializer.Serialize(new{name=profile.Name,productId=profile.Product,publicKey=profile.PublicKey},new JsonSerializerOptions{WriteIndented=true}));
    internal ProductProfile Create(string name,string product)
    {
        name=name.Trim();product=product.Trim();
        if(name.Length is <1 or >80||!Regex.IsMatch(product,@"^[a-zA-Z][a-zA-Z0-9_.-]{2,79}$"))throw new InvalidOperationException("Введите название и уникальный ID: 3–80 латинских букв, цифр, точек или дефисов.");
        if(Load().Any(p=>p.Product.Equals(product,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Этот ID уже используется другим питомцем.");
        string dir=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        using var signer=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var result=new ProductProfile(name,product,Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo()),Path.Combine(dir,"signing-key.dpapi"),dir);PublisherVault.Store(signer,result.KeyFile);Write(result);return result;
    }
    internal ProductProfile Import(string name,string product,ECDsa signer)
    {
        string pub=Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo());
        var existing=Load().FirstOrDefault(p=>p.Product.Equals(product,StringComparison.OrdinalIgnoreCase));
        if(existing is not null)
        {
            if(existing.PublicKey!=pub)throw new InvalidOperationException("Для этого питомца уже зарегистрирован другой ключ подписи.");
            PublisherVault.Store(signer,existing.KeyFile);return existing;
        }
        if(string.IsNullOrWhiteSpace(name)||string.IsNullOrWhiteSpace(product)||product.Length>80)throw new InvalidOperationException("Некорректные данные питомца.");
        string dir=Path.Combine(root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var result=new ProductProfile(name,product,pub,Path.Combine(dir,"signing-key.dpapi"),dir);PublisherVault.Store(signer,result.KeyFile);Write(result);return result;
    }
}
