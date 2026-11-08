using System.IO;
using System.Text.Json;
using Hudi.Licensing;

namespace Hudi.Publisher;

internal sealed partial class PublisherWindow
{
    internal static int VerifyGeneration(string directory)
    {
        Directory.CreateDirectory(directory);
        var catalog=new ProductCatalog(Path.Combine(directory,Guid.NewGuid().ToString("N")));
        var profile=catalog.Create("Проверка выпуска","Test.Generation");
        var window=new PublisherWindow(catalog);
        var checks=new Dictionary<string,bool>();
        window.customer.Text="555";
        window.Generate();
        string first=window.code.Text;
        checks["code_displayed_and_verified"]=LicenseCodec.Validate(first,profile.PublicKey,out _,out _,expectedProduct:profile.Product);
        checks["copy_and_save_enabled"]=window.copyCode.IsEnabled&&window.saveCode.IsEnabled;
        string journal=Path.Combine(profile.Directory,"issued-licenses.jsonl");
        checks["journal_records_displayed_code"]=File.ReadAllText(journal).Contains(first);

        using(var locked=new FileStream(journal,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            window.Generate();
            checks["locked_journal_does_not_hide_code"]=window.code.Text!=first&&LicenseCodec.Validate(window.code.Text,profile.PublicKey,out _,out _,expectedProduct:profile.Product);
            checks["locked_journal_warns_and_allows_save"]=window.status.Text.StartsWith("Код готов. Не удалось сохранить")&&window.copyCode.IsEnabled&&window.saveCode.IsEnabled;
        }
        window.customer.Text="";window.Generate();
        checks["empty_customer_explained"]=window.code.Text.Length==0&&window.status.Text.StartsWith("Укажите покупателя")&&!window.copyCode.IsEnabled&&!window.saveCode.IsEnabled;
        window.customer.Text="Проверка";window.duration.SelectedIndex=1;window.days.Text="0";window.Generate();
        checks["invalid_duration_explained"]=window.code.Text.Length==0&&window.status.Text.StartsWith("Укажите срок");
        window.days.Text="7";window.Generate();
        checks["dated_code_valid"]=LicenseCodec.Validate(window.code.Text,profile.PublicKey,out var dated,out _,expectedProduct:profile.Product)&&dated?.Expires-dated?.Issued==7*86400;

        string savedKey=profile.KeyFile+".test-backup";File.Move(profile.KeyFile,savedKey);
        try
        {
            window.Generate();checks["missing_signing_key_explained"]=window.code.Text.Length==0&&window.status.Text.StartsWith("Не найден ключ подписи")&&!window.saveCode.IsEnabled;
        }
        finally{File.Move(savedKey,profile.KeyFile);}
        window.Generate();checks["retry_after_key_restored"]=LicenseCodec.Validate(window.code.Text,profile.PublicKey,out _,out _,expectedProduct:profile.Product);
        bool success=checks.Values.All(x=>x);
        File.WriteAllText(Path.Combine(directory,"generation-tests.json"),JsonSerializer.Serialize(new{success,checks},new JsonSerializerOptions{WriteIndented=true}));
        window.Close();
        return success?0:1;
    }
}
