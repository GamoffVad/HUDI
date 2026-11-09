using System.IO;
using System.Text.Json;

namespace HudiPet;

internal sealed partial class SettingsWindow
{
    internal static void VerifyControls(PetWindow owner,IModelClient client,string directory)
    {
        var checks=new Dictionary<string,bool>();
        var initial=new ModelSettings {Model="test-model",TimeoutSeconds=77,SendTemperature=true,Temperature=.4,MaxOutputTokens=4096,KeepHistory=false,Protocol="chat",ChatTokenParameter="max_completion_tokens"};
        var panel=new SettingsWindow(owner,initial,"fixture-only-key",client,(_,_)=>{});
        var read=panel.Read();checks["advanced_values_preserved"]=read.TimeoutSeconds==77&&read.SendTemperature&&read.Temperature==.4&&read.MaxOutputTokens==4096&&!read.KeepHistory&&read.Protocol=="chat"&&read.ChatTokenParameter=="max_completion_tokens";
        panel.endpoint.Text="https://other.example/v1";
        try{panel.Read();checks["origin_guard_preserved"]=false;}catch(ModelException){checks["origin_guard_preserved"]=true;}
        panel.visibleKey.Text="changed-fixture-key";checks["key_views_stay_in_sync"]=panel.hiddenKey.Password==panel.visibleKey.Text;
        panel.provider.SelectedIndex=ProviderPresets.Index("ollama");read=panel.Read();checks["local_provider_configured"]=read.Provider=="ollama"&&read.Protocol=="chat"&&!read.UseApiKey&&panel.hiddenKey.Password.Length==0&&read.BaseUrl=="http://localhost:11434/v1";
        panel.sendTemperature.IsChecked=false;checks["temperature_toggle_works"]=!panel.temperature.IsEnabled&&!panel.Read().SendTemperature;
        panel.systemPrompt.Text="Мои личные инструкции";
        foreach(var preset in ProviderPresets.All)
        {
            panel.hiddenKey.Password="fixture-only-key";panel.provider.SelectedIndex=ProviderPresets.Index(preset.Id);read=panel.Read();
            checks[preset.Id+"_autofill"]=read.Provider==preset.Id&&read.BaseUrl==preset.BaseUrl&&read.Model==preset.Model&&read.Protocol==preset.Protocol&&read.UseApiKey==preset.UseApiKey&&read.SendTemperature==preset.SendTemperature&&read.Temperature==preset.Temperature&&read.MaxOutputTokens==preset.Limit&&read.TimeoutSeconds==preset.Timeout&&read.ChatTokenParameter==preset.TokenParameter&&read.Stream&&read.KeepHistory&&panel.hiddenKey.Password.Length==0&&panel.visibleKey.Text.Length==0&&read.SystemPrompt=="Мои личные инструкции";
        }
        panel.provider.SelectedIndex=ProviderPresets.Index("deepseek");panel.hiddenKey.Password="fixture-new-secret";panel.endpoint.Text="https://different.example/v1";
        try{panel.Read();checks["edited_key_bound_to_server"]=false;}catch(ModelException){checks["edited_key_bound_to_server"]=true;}
        var restored=new SettingsWindow(owner,new(){Provider="deepseek",BaseUrl="https://api.deepseek.com",Model="my-custom-model",Temperature=.2,TimeoutSeconds=97},"fixture-only",client,(_,_)=>{});
        checks["opening_preserves_saved_custom_values"]=restored.Read().Provider=="deepseek"&&restored.Read().Model=="my-custom-model"&&restored.Read().Temperature==.2&&restored.Read().TimeoutSeconds==97;
        restored.Close();
        var corporate=new SettingsWindow(owner,new(){Provider="compatible",BaseUrl="http://llm.corp:8080/v1",Model="corporate",Protocol="chat"},"fixture-corporate",client,(_,_)=>{});
        var corporateRead=corporate.Read();corporateRead.Validate("fixture-corporate");
        checks["corporate_http_configured"]=corporateRead.Endpoint("models").AbsoluteUri=="http://llm.corp:8080/v1/models";
        corporate.endpoint.Text="https://llm.corp:8080/v1";
        try{corporate.Read();checks["http_https_origin_guard"]=false;}catch(ModelException){checks["http_https_origin_guard"]=true;}
        corporate.Close();
        panel.Close();
        File.WriteAllText(Path.Combine(directory,"settings-controls.json"),JsonSerializer.Serialize(new{success=checks.Values.All(x=>x),checks},new JsonSerializerOptions{WriteIndented=true}));
        if(checks.Values.Any(x=>!x))throw new InvalidOperationException("Settings controls verification failed");
    }
}
