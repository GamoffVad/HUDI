using Hudi.Licensing;
using System.Security.Cryptography;
using System.Text.Json;

if(args.Length<2)return 2;
if(args[0]=="diagnose-publisher")
{
    var checks=new Dictionary<string,bool>();string step="profile";
    try
    {
        string dir=Path.GetFullPath(args[1]);
        using var profile=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"product.json")));
        var data=profile.RootElement;
        step="load-signing-key";using var key=PublisherVault.Load(Path.Combine(dir,"signing-key.dpapi"));checks[step]=true;
        step="match-public-key";string pub=data.GetProperty("publicKey").GetString()!;checks[step]=Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())==pub;
        step="sign-and-verify";string product=data.GetProperty("productId").GetString()!;
        var claims=new LicenseClaims(1,product,Guid.NewGuid().ToString(),"Publisher diagnostic",DateTimeOffset.UtcNow.ToUnixTimeSeconds(),null);
        string signed=LicenseCodec.Sign(claims,key);checks[step]=LicenseCodec.Validate(signed,pub,out _,out _,expectedProduct:product);
        Console.WriteLine(JsonSerializer.Serialize(new{success=checks.Values.All(x=>x),checks}));return checks.Values.All(x=>x)?0:1;
    }
    catch(Exception ex)
    {Console.WriteLine(JsonSerializer.Serialize(new{success=false,step,errorType=ex.GetType().Name,hresult=ex.HResult,message=ex.Message,checks}));return 1;}
}
if(args[0]=="init") {PublisherVault.Initialize(args[1]);Console.WriteLine("Publisher signing key ready; public verification key exported.");return 0;}
if(args[0]=="owner")
{
    using var key=PublisherVault.Load();var claims=new LicenseClaims(1,LicenseCodec.Product,Guid.NewGuid().ToString(),"Владелец HUDI",DateTimeOffset.UtcNow.ToUnixTimeSeconds(),null);
    File.WriteAllText(args[1],LicenseCodec.Sign(claims,key));Console.WriteLine("Owner license saved.");return 0;
}
if(args[0]=="verify")
{
    using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);string pub=Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    var claims=new LicenseClaims(1,LicenseCodec.Product,Guid.NewGuid().ToString(),"Test",DateTimeOffset.UtcNow.ToUnixTimeSeconds(),null);
    string code=LicenseCodec.Sign(claims,key);var checks=new Dictionary<string,bool>();
    checks["valid"]=LicenseCodec.Validate(code,pub,out _,out _);
    checks["whitespace"]=LicenseCodec.Validate("  "+code+"\r\n",pub,out _,out _);
    checks["tampered"]=!LicenseCodec.Validate(code[..20]+(code[20]=='A'?'B':'A')+code[21..],pub,out _,out _);
    checks["wrong_product"]=!LicenseCodec.Validate(LicenseCodec.Sign(claims with{Product="other"},key),pub,out _,out _);
    checks["expired"]=!LicenseCodec.Validate(LicenseCodec.Sign(claims with{Issued=1,Expires=2},key),pub,out _,out _);
    checks["malformed"]=new[]{"","a","HUDI1.a.b","HUDI1...",new string('x',9000)}.All(c=>!LicenseCodec.Validate(c,pub,out _,out _));
    using var other=ECDsa.Create(ECCurve.NamedCurves.nistP256);checks["wrong_signer"]=!LicenseCodec.Validate(LicenseCodec.Sign(claims,other),pub,out _,out _);
    File.WriteAllText(args[1],JsonSerializer.Serialize(new{success=checks.Values.All(v=>v),checks},new JsonSerializerOptions{WriteIndented=true}));return checks.Values.All(v=>v)?0:1;
}
return 2;
