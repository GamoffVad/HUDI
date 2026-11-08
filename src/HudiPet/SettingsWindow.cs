using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace HudiPet;

internal sealed partial class SettingsWindow : GlassWindow
{
    private readonly ComboBox provider, protocol, tokenParameter;
    private readonly TextBox endpoint, model, systemPrompt, temperature, limit, timeout, visibleKey;
    private readonly PasswordBox hiddenKey;
    private readonly CheckBox useKey, streaming, history, sendTemperature;
    private readonly TextBlock status;
    private readonly IModelClient client;
    private readonly Action<ModelSettings,string> save;
    private string credentialOrigin;
    private readonly CancellationTokenSource lifetime = new();
    private bool syncingKey;
    private int connectionRevision;
    private readonly Button testButton, modelsButton, saveButton;
    private ModelSettings Read()
    {
        if(!int.TryParse(limit.Text,out int max) || !int.TryParse(timeout.Text,out int seconds) || !double.TryParse(temperature.Text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out double heat))
            throw new ModelException("Проверьте числовые параметры: лимит, тайм-аут и температуру.");
        var result=new ModelSettings { Provider=ProviderPresets.All[Math.Max(0,provider.SelectedIndex)].Id, BaseUrl=endpoint.Text.Trim(),
            Protocol=protocol.SelectedIndex==1?"chat":"responses", Model=model.Text.Trim(), SystemPrompt=systemPrompt.Text,
            MaxOutputTokens=max,TimeoutSeconds=seconds,Temperature=heat,SendTemperature=sendTemperature.IsChecked==true,Stream=streaming.IsChecked==true,
            KeepHistory=history.IsChecked==true,UseApiKey=useKey.IsChecked==true,ChatTokenParameter=tokenParameter.SelectedIndex==1?"max_completion_tokens":"max_tokens" };
        if(result.UseApiKey && hiddenKey.Password.Length>0 && result.Origin!=credentialOrigin)
            throw new ModelException("Адрес сервера изменился. Введите ключ для нового сервера.");
        return result;
    }
    private void BindEditedKey()
    {
        try{credentialOrigin=new ModelSettings{BaseUrl=endpoint.Text}.Origin;}catch(ModelException){credentialOrigin="";}
    }
    private void ApplyProvider()
    {
        connectionRevision++;
        var preset=ProviderPresets.All[Math.Max(0,provider.SelectedIndex)];var value=preset.Defaults();
        hiddenKey.Password="";visibleKey.Text="";credentialOrigin="";
        endpoint.Text=value.BaseUrl;model.Text=value.Model;protocol.SelectedIndex=value.Protocol=="chat"?1:0;
        useKey.IsChecked=value.UseApiKey;sendTemperature.IsChecked=value.SendTemperature;
        temperature.Text=value.Temperature.ToString("0.##",CultureInfo.CurrentCulture);
        limit.Text=value.MaxOutputTokens.ToString(CultureInfo.InvariantCulture);timeout.Text=value.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        tokenParameter.SelectedIndex=value.ChatTokenParameter=="max_completion_tokens"?1:0;
        streaming.IsChecked=value.Stream;history.IsChecked=value.KeepHistory;
        // The pet's instructions belong to the user, not to the provider preset.
        Status(preset.Hint,Ui.Muted);
    }
    private string Key(ModelSettings value)=>value.UseApiKey?hiddenKey.Password.Trim():"";
    private void Save()
    {
        try { var value=Read(); value.Validate(Key(value)); save(value,Key(value)); Close(); }
        catch(Exception error) when(error is ModelException or System.IO.IOException or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException)
        { Status(error is ModelException?error.Message:"Не удалось сохранить настройки. Попробуйте ещё раз.",Ui.Error); }
    }
    private async Task TestConnection(bool choose)
    {
        int revision=connectionRevision;
        testButton.IsEnabled=modelsButton.IsEnabled=saveButton.IsEnabled=false; Status("Проверяю подключение…",Ui.Muted);
        try
        {
            var settings=Read(); var models=await client.ListModelsAsync(settings,Key(settings),lifetime.Token);
            if(IsClosing||revision!=connectionRevision)return;
            Status($"Связь установлена · моделей: {models.Length}",Ui.Accent);
            if(choose && models.Length>0)
            {
                var picker=new GlassWindow(Owner as PetWindow,"Выбрать модель",400);SettingsAppearance.Apply(picker);
                var filter=Ui.Input("","Поиск модели"); filter.ToolTip="Поиск по названию"; picker.Body.Children.Add(filter);
                var rows=new StackPanel { Margin=new Thickness(0,10,0,0) }; picker.Body.Children.Add(new ScrollViewer { Content=rows,MaxHeight=300,VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
                void Refresh() { rows.Children.Clear(); foreach(string id in models.Where(id=>id.Contains(filter.Text,StringComparison.OrdinalIgnoreCase)).Take(100)) rows.Children.Add(Ui.MenuRow("\uE8A5",id,()=> { model.Text=id; picker.Close(); })); }
                filter.TextChanged+=(_,_)=>Refresh(); Refresh(); picker.Show();
                Closed+=(_,_)=> { if(!picker.IsClosing)picker.Close(); };
            }
        }
        catch(OperationCanceledException) { }
        catch(ModelException error) { if(revision==connectionRevision)Status(error.Message,Ui.Error); }
        finally { if(!IsClosing)testButton.IsEnabled=modelsButton.IsEnabled=saveButton.IsEnabled=true; }
    }
}
