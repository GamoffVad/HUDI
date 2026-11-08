using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hudi.Distribution;
using Hudi.Licensing;
using Microsoft.Win32;

namespace Hudi.Publisher;
internal static class Program
{
    [STAThread]private static int Main(string[] args)
    {
        if(args.Length==2&&args[0]=="--verify-catalog")return CatalogVerification.Run(args[1]);
        var app=new Application();BrandWindow.Initialize();
        if(args.Length==2&&args[0]=="--verify-generation")return PublisherWindow.VerifyGeneration(args[1]);
        if(args.Length==2&&args[0]=="--render-ui"){VisualPreview.Save(new PublisherWindow(),args[1]);return 0;}
        return app.Run(new PublisherWindow());
    }
}
internal sealed partial class PublisherWindow:BrandWindow
{
    private readonly ProductCatalog catalog;
    private readonly bool isVerification;
    private readonly TextBox customer,days,code;
    private readonly TextBlock status;
    private readonly ComboBox duration,products;
    private readonly Button copyCode,saveCode;
    private string generationStep="";
    private ProductProfile Profile=>(ProductProfile)products.SelectedItem;
    internal PublisherWindow(ProductCatalog? testCatalog=null):base("Ключи для ваших питомцев","Выберите персонажа, укажите покупателя и выпустите персональный код активации.")
    {
        catalog=testCatalog??new ProductCatalog();isVerification=testCatalog is not null;
        Width=Math.Min(1020,SystemParameters.WorkArea.Width-30);Title="Pet Keys — генератор активаций";ContextLabel.Text="PET KEYS  /  ПАНЕЛЬ ИЗДАТЕЛЯ";
        if(testCatalog is null)catalog.EnsureHudi();products=new ComboBox{MinWidth=190,ItemsSource=catalog.Load(),SelectedIndex=0};
        var productRow=new Grid();productRow.ColumnDefinitions.Add(new());productRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});productRow.Children.Add(products);
        var actions=Row(Button("+ Питомец",AddProduct),Button("Для сборки…",ExportIntegration));actions.Margin=new Thickness(10,0,0,0);Grid.SetColumn(actions,1);productRow.Children.Add(actions);Field(Body,"Персонаж",productRow);
        customer=Input();Field(Body,"Покупатель или номер заказа",customer);
        duration=new ComboBox{ItemsSource=new[]{"Бессрочная лицензия","На указанное число дней"},SelectedIndex=0};Field(Body,"Срок действия",duration);
        days=Input("30");days.Margin=new Thickness(0,12,0,0);days.Visibility=Visibility.Collapsed;Body.Children.Add(days);duration.SelectionChanged+=(_,_)=>days.Visibility=duration.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
        var generate=Button("Выпустить код  ↗",Generate,true);generate.Margin=new Thickness(0,20,0,0);Body.Children.Add(generate);
        status=Text("",12,Muted);status.Margin=new Thickness(0,10,0,0);Body.Children.Add(status);
        code=Input();code.IsReadOnly=true;code.TextWrapping=TextWrapping.Wrap;code.Height=94;code.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;code.FontSize=11;Field(Body,"Код активации",code);
        copyCode=Button("Копировать",()=>Safe(()=>{if(code.Text.Length>0)Clipboard.SetText(code.Text);}));saveCode=Button("Сохранить…",SaveCode);
        copyCode.IsEnabled=saveCode.IsEnabled=false;
        code.TextChanged+=(_,_)=>copyCode.IsEnabled=saveCode.IsEnabled=!string.IsNullOrWhiteSpace(code.Text);
        var codeActions=Row(copyCode,saveCode,Button("Резервная копия",()=>Backup(false)),Button("Восстановить",()=>Backup(true)));codeActions.Margin=new Thickness(0,16,0,0);Body.Children.Add(codeActions);
        products.SelectionChanged+=(_,_)=>Selected();Selected();
    }
    private void Selected()
    {
        if(products.SelectedItem is not ProductProfile profile)return;
        code.Text="";BrandName.Text="PET KEYS";BrandCaption.Text=profile.Name+"\n"+profile.Product;
        if(Hero is not null)Hero.Visibility=profile.Product==LicenseCodec.Product?Visibility.Visible:Visibility.Collapsed;
        status.Foreground=Muted;
        try
        {
            using var signer=PublisherVault.Load(profile.KeyFile);
            status.Text=Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo())==profile.PublicKey?"Готов к выпуску · "+profile.Name:"Ключ подписи не подходит этому питомцу. Восстановите резервную копию.";
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            LogFailure("check-signing-key",ex);
            status.Foreground=new SolidColorBrush(Color.FromRgb(255,181,174));
            status.Text="Ключ подписи недоступен. Восстановите секрет этого питомца из резервной копии.";
        }
    }
    private void Select(ProductProfile profile) {products.ItemsSource=catalog.Load();products.SelectedItem=((ProductProfile[])products.ItemsSource).First(p=>p.Product==profile.Product);}
    private void Safe(Action action)
    {try{action();}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException or JsonException or System.Runtime.InteropServices.COMException)
    {
        LogFailure("publisher-action",ex);
        status.Foreground=new SolidColorBrush(Color.FromRgb(255,181,174));status.Text=ex is InvalidOperationException?ex.Message:"Не удалось выполнить операцию. Проверьте доступ к файлу или резервную копию.";
    }}
    private void LogFailure(string step,Exception ex)
    {
        if(isVerification)return;
        // Never log customer names, activation codes, key bytes or passwords.
        string entry=JsonSerializer.Serialize(new{time=DateTimeOffset.Now,step,type=ex.GetType().Name,hresult=ex.HResult,file=(ex as FileNotFoundException)?.FileName,stack=ex.StackTrace})+Environment.NewLine;
        foreach(string path in new[]{Path.Combine(PublisherVault.Root,"publisher-errors.jsonl"),Path.Combine(Path.GetTempPath(),"PetKeys-errors.jsonl")})
        {try{File.AppendAllText(path,entry);return;}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    private void Generate()
    {
        code.Clear();status.Foreground=Muted;
        try
        {
            generationStep="validate-input";
            if(products.SelectedItem is not ProductProfile profile)throw new InvalidOperationException("Выберите питомца для выпуска кода.");
            string name=customer.Text.Trim();
            if(name.Length==0||name.Length>160){customer.Focus();throw new InvalidOperationException("Укажите покупателя или номер заказа: от 1 до 160 символов.");}
            int count=0;
            if(duration.SelectedIndex==1&&(!int.TryParse(days.Text,out count)||count<1||count>36500))
            {days.Focus();throw new InvalidOperationException("Укажите срок: от 1 до 36 500 дней.");}
            generationStep="load-signing-key";
            using var signer=PublisherVault.Load(profile.KeyFile);
            if(Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo())!=profile.PublicKey)throw new InvalidOperationException("Ключ подписи не подходит этому питомцу. Восстановите его резервную копию.");
            var now=DateTimeOffset.UtcNow;
            var claims=new LicenseClaims(1,profile.Product,Guid.NewGuid().ToString(),name,now.ToUnixTimeSeconds(),count>0?now.AddDays(count).ToUnixTimeSeconds():null);
            generationStep="sign";
            string result=LicenseCodec.Sign(claims,signer);
            if(!LicenseCodec.Validate(result,profile.PublicKey,out _,out _,expectedProduct:profile.Product))throw new InvalidOperationException("Не удалось проверить созданный код. Проверьте дату и время Windows.");

            // A journal is an aid to the publisher, not a prerequisite for using
            // a successfully signed and verified code. Show it before file I/O.
            code.Text=result;code.SelectAll();status.Foreground=Accent;
            status.Text="Код готов · "+profile.Name+(count>0?$" · до {now.AddDays(count).ToLocalTime():dd.MM.yyyy}":" · бессрочный");
            generationStep="write-journal";
            try{File.AppendAllText(Path.Combine(profile.Directory,"issued-licenses.jsonl"),JsonSerializer.Serialize(new{claims,code=result})+Environment.NewLine);}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
            {
                LogFailure(generationStep,ex);status.Foreground=new SolidColorBrush(Color.FromRgb(255,214,145));
                status.Text="Код готов. Не удалось сохранить его в журнал — скопируйте код или нажмите «Сохранить…».";
            }
            if(IsLoaded)code.BringIntoView();
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or CryptographicException or InvalidOperationException or JsonException or FormatException)
        {
            LogFailure(generationStep,ex);status.Foreground=new SolidColorBrush(Color.FromRgb(255,181,174));
            status.Text=ex switch
            {
                InvalidOperationException=>ex.Message,
                FileNotFoundException or DirectoryNotFoundException=>"Не найден ключ подписи питомца. Восстановите его через «Восстановить».",
                UnauthorizedAccessException=>"Нет доступа к ключу подписи питомца. Проверьте права доступа к папке издателя.",
                CryptographicException when generationStep=="load-signing-key"=>"Windows не смогла открыть ключ подписи. Используйте учётную запись, в которой создан ключ, или восстановите его резервную копию.",
                CryptographicException=>"Не удалось подписать код. Восстановите ключ подписи питомца из резервной копии.",
                _=>"Не удалось прочитать данные питомца. Проверьте его профиль и резервную копию."
            };
            if(IsLoaded)status.BringIntoView();
        }
        finally{generationStep="";}
    }
    private void SaveCode()=>Safe(()=>
    {if(code.Text.Length==0)return;var dialog=new SaveFileDialog{Filter="Код питомца (*.pet-key)|*.pet-key",FileName=Profile.Product+"-activation.pet-key"};if(dialog.ShowDialog(this)==true)File.WriteAllText(dialog.FileName,code.Text);});
    private void AddProduct()
    {
        var dialog=new BrandWindow("Новый персонаж","Каждому питомцу — свой ID и собственная подпись. Коды разных персонажей не взаимозаменяемы."){Owner=this};
        dialog.BrandName.Text="PET KEYS";if(dialog.Hero is not null)dialog.Hero.Visibility=Visibility.Collapsed;
        var name=Input();var id=Input();Field(dialog.Body,"Имя персонажа",name);Field(dialog.Body,"ID продукта, например Luna.DesktopPet",id);var note=Text("",12,Muted);note.Margin=new Thickness(0,12,0,12);dialog.Body.Children.Add(note);
        dialog.Body.Children.Add(Button("Создать питомца",()=>
        {
            try{Select(catalog.Create(name.Text,id.Text));dialog.Close();}
            catch(Exception ex) when(ex is InvalidOperationException or IOException or CryptographicException or UnauthorizedAccessException){note.Text=ex is InvalidOperationException?ex.Message:"Не удалось сохранить персонажа.";}
        },true));dialog.ShowDialog();
    }
    private void ExportIntegration()=>Safe(()=>
    {
        var file=new SaveFileDialog{Filter="Комплект для сборки (*.zip)|*.zip",FileName=Profile.Product+"-licensing.zip"};if(file.ShowDialog(this)!=true)return;
        using var output=File.Create(file.FileName);using var zip=new ZipArchive(output,ZipArchiveMode.Create);
        void Add(string name,string text){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(text);}
        Add("license-profile.json",JsonSerializer.Serialize(new{format="pet-license-profile-v1",name=Profile.Name,productId=Profile.Product,publicKey=Profile.PublicKey,algorithm="ECDSA-P256-SHA256",codePrefix="PET1"},new JsonSerializerOptions{WriteIndented=true}));
        Add("license-public.key",Profile.PublicKey);
        using var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("PetKeys.Verifier.cs")!;using var reader=new StreamReader(resource);Add("PetLicenseVerifier.cs",reader.ReadToEnd());
        Add("README.txt","Подключение лицензии питомца\n\nДобавьте PetLicenseVerifier.cs в приложение и установщик. Встройте productId и publicKey из license-profile.json. Вызывайте PetLicenseVerifier.TryVerify(code, productId, publicKey, out claims) перед установкой, при запуске и периодически во время работы. Храните лицензию отдельно от API-ключа модели.\n\nКомплект содержит только публичные данные. Секрет подписания остаётся в Pet Keys. Для переноса издателя используйте зашифрованную резервную копию из генератора.\nОфлайн-лицензия не ограничивает число устройств и не поддерживает удалённый отзыв.\n");
        status.Text="Комплект для «"+Profile.Name+"» сохранён. Секрет подписи в него не включён.";
    });
    private void Backup(bool restore)
    {
        var profile=Profile;
        var dialog=new BrandWindow(restore?"Восстановить питомца":"Сохранить секрет",restore?"Профиль и ключ подписи восстановятся из зашифрованной резервной копии.":"Резервная копия: "+profile.Name+". Храните её отдельно от покупательского установщика."){Owner=this};
        dialog.BrandName.Text="PET KEYS";var password=new PasswordBox();Field(dialog.Body,"Пароль резервной копии",password);var note=Text("Минимум 12 символов",12,Muted);note.Margin=new Thickness(0,10,0,14);dialog.Body.Children.Add(note);
        dialog.Body.Children.Add(Button(restore?"Выбрать файл и восстановить":"Сохранить зашифрованный файл",()=>
        {
            try
            {
                if(password.Password.Length<12){note.Text="Используйте пароль не короче 12 символов.";return;}
                if(restore)
                {
                    var file=new OpenFileDialog{Filter="Секрет издателя (*.pet-secret;*.hudi-secret)|*.pet-secret;*.hudi-secret"};if(file.ShowDialog(dialog)!=true)return;
                    if(new FileInfo(file.FileName).Length>1000000)throw new InvalidOperationException("Файл слишком большой.");
                    byte[] bytes=File.ReadAllBytes(file.FileName);string name=profile.Name,product=profile.Product,publicKey=profile.PublicKey;
                    if(bytes.Length>0&&bytes[0]=='{')
                    {
                        using var json=JsonDocument.Parse(bytes);var data=json.RootElement;
                        if(data.GetProperty("format").GetString()!="pet-publisher-backup-v1")throw new CryptographicException();
                        name=data.GetProperty("name").GetString()!;product=data.GetProperty("productId").GetString()!;publicKey=data.GetProperty("publicKey").GetString()!;bytes=Convert.FromBase64String(data.GetProperty("encryptedPrivateKey").GetString()!);
                    }
                    using var signer=ECDsa.Create();signer.ImportEncryptedPkcs8PrivateKey(password.Password,bytes,out _);
                    if(Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo())!=publicKey)throw new CryptographicException();
                    Select(catalog.Import(name,product,signer));status.Text="Профиль и секрет восстановлены.";
                }
                else
                {
                    using var signer=PublisherVault.Load(profile.KeyFile);var file=new SaveFileDialog{Filter="Секрет издателя (*.pet-secret)|*.pet-secret",FileName=profile.Product+"-backup.pet-secret"};if(file.ShowDialog(dialog)!=true)return;
                    var bytes=signer.ExportEncryptedPkcs8PrivateKey(password.Password,new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc,HashAlgorithmName.SHA256,600000));
                    File.WriteAllText(file.FileName,JsonSerializer.Serialize(new{format="pet-publisher-backup-v1",name=profile.Name,productId=profile.Product,publicKey=profile.PublicKey,encryptedPrivateKey=Convert.ToBase64String(bytes)}));status.Text="Зашифрованная копия сохранена.";
                }
                password.Password="";dialog.Close();
            }
            catch(Exception ex) when(ex is CryptographicException or IOException or UnauthorizedAccessException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException){note.Text=ex is InvalidOperationException?ex.Message:"Проверьте пароль и файл резервной копии.";}
        },true));dialog.ShowDialog();
    }
}
