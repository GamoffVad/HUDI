using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace HudiPet;

internal static class ChatVerification
{
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>respond(request,ct); }
    private static HttpResponseMessage Response(string data,string type="application/json",HttpStatusCode code=HttpStatusCode.OK)=>new(code) { Content=new StringContent(data,Encoding.UTF8,type) };
    internal static async Task<int> Run()
    {
        var passed=new List<string>();
        void Check(bool value,string name) { if(!value)throw new Exception(name);passed.Add(name); }
        string directory=Path.Combine(PetWindow.SettingsDirectory,"verification-v7");Directory.CreateDirectory(directory);
        try
        {
            foreach(var preset in ProviderPresets.All.Where(p=>p.Id!="compatible"))
            {
                var settings=preset.Defaults();settings.Stream=false;if(settings.Model.Length==0)settings.Model="installed-local-model";
                using var presetsClient=new HttpModelClient(new Handler(async(request,ct)=>
                {
                    bool models=request.Content is null;
                    Check(request.RequestUri==settings.Endpoint(models?"models":settings.Protocol=="responses"?"responses":"chat/completions"),preset.Id+" endpoint");
                    bool nativeKey=preset.Id=="anthropic"&&models;
                    Check(settings.UseApiKey?(nativeKey?request.Headers.GetValues("x-api-key").Single()=="preset-fixture-key":request.Headers.Authorization?.Parameter=="preset-fixture-key"):request.Headers.Authorization is null,preset.Id+" authentication");
                    if(preset.Id=="anthropic")Check(request.Headers.GetValues("anthropic-version").Single()=="2023-06-01","Claude model-list version");
                    if(models)return Response(preset.Id=="together"?"[{\"id\":\"test-model\"}]":"{\"data\":[{\"id\":\"test-model\"}]}");
                    using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                    Check(body.RootElement.GetProperty("model").GetString()==settings.Model,preset.Id+" model");
                    Check(body.RootElement.TryGetProperty("temperature",out _)==settings.SendTemperature,preset.Id+" sampling");
                    Check(body.RootElement.GetProperty(settings.Protocol=="responses"?"max_output_tokens":settings.ChatTokenParameter).GetInt32()==settings.MaxOutputTokens,preset.Id+" token limit");
                    return Response(settings.Protocol=="responses"?"{\"status\":\"completed\",\"output\":[{\"content\":[{\"text\":\"Готово\"}]}]}":"{\"choices\":[{\"message\":{\"content\":\"Готово\"},\"finish_reason\":\"stop\"}]}");
                }));
                Check((await presetsClient.ListModelsAsync(settings,settings.UseApiKey?"preset-fixture-key":"",default)).SequenceEqual(new[]{"test-model"}),preset.Id+" model list");
                Check((await presetsClient.CompleteAsync(settings,settings.UseApiKey?"preset-fixture-key":"",[new("user","Привет")],null,default)).Text=="Готово",preset.Id+" reply");
            }
            foreach(string protocol in new[] {"responses","chat"})
            foreach(bool stream in new[] {false,true})
            {
                var settings=new ModelSettings { Model="test-model",Protocol=protocol,Stream=stream };
                string answer="Привет, мир!";
                string data=protocol=="responses"
                    ? stream ? "data: {\"type\":\"response.output_text.delta\",\"delta\":\"Привет, мир!\"}\n\ndata: {\"type\":\"response.completed\"}\n\n" : "{\"status\":\"completed\",\"output\":[{\"content\":[{\"text\":\"Привет, мир!\"}]}]}"
                    : stream ? "data: {\"choices\":[{\"delta\":{\"content\":\"Привет, мир!\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n" : "{\"choices\":[{\"message\":{\"content\":\"Привет, мир!\"},\"finish_reason\":\"stop\"}]}";
                using var client=new HttpModelClient(new Handler(async (request,ct)=>
                {
                    Check(request.Headers.Authorization?.Parameter=="test-secret","authorization");
                    using var payload=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                    Check(!payload.RootElement.TryGetProperty("temperature",out _),"optional temperature omitted");
                    Check(request.RequestUri!.AbsolutePath.EndsWith(protocol=="responses"?"/responses":"/chat/completions"),"route");
                    return Response(data,stream?"text/event-stream":"application/json");
                }));
                Check((await client.CompleteAsync(settings,"test-secret",[new("user","Привет")],null,default)).Text==answer,$"{protocol} stream={stream}");
            }
            using(var failed=new HttpModelClient(new Handler((r,c)=>Task.FromResult(Response("{\"error\":{\"message\":\"bad test-secret\"}}",code:HttpStatusCode.Unauthorized)))))
            {
                try { await failed.CompleteAsync(new(){Model="test"},"test-secret",[],null,default);throw new Exception("401 accepted"); }
                catch(ModelException error) { Check(!error.Message.Contains("test-secret"),"key redacted"); }
                using var session=new ChatSession(failed);bool? result=null;session.Finished+=success=>result=success;
                await session.SendAsync("test",new(){Model="test"},"test-secret");Check(result==false&&session.State==ChatState.Error,"failed request never celebrates");
            }
            using(var broken=new HttpModelClient(new Handler((r,c)=>Task.FromResult(Response("data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n","text/event-stream")))))
            {
                try { await broken.CompleteAsync(new(){Model="test"},"test",[],null,default);throw new Exception("broken stream accepted"); }
                catch(ModelException) { passed.Add("incomplete stream rejected"); }
            }
            using(var waiting=new HttpModelClient(new Handler(async(r,c)=> {await Task.Delay(30000,c);return Response("{}");})))
            {
                using var session=new ChatSession(waiting);bool? result=null;session.Finished+=success=>result=success;
                var sending=session.SendAsync("test",new(){Model="test"},"test");session.Cancel();await sending;
                Check(result==false&&session.State==ChatState.Canceled,"cancellation settles");
            }
            var store=new ModelSettingsStore(directory);store.Save(new(){Model="test"},"secret-for-test");
            Check(store.Load().Key=="secret-for-test","DPAPI roundtrip");
            Check(!File.ReadAllText(Path.Combine(directory,"model-settings.json")).Contains("secret-for-test"),"no plaintext key on disk");
            var disk=JsonSerializer.Deserialize<ModelSettings>(File.ReadAllText(Path.Combine(directory,"model-settings.json")))!;disk.BaseUrl="https://other.example/v1";
            File.WriteAllText(Path.Combine(directory,"model-settings.json"),JsonSerializer.Serialize(disk));Check(store.Load().Key=="","credential origin binding");
            var behavior=new PetBehavior(1);behavior.BeginChatThinking();
            for(int i=0;i<1500;i++)behavior.Update(.02);
            Check(behavior.HoldingThought&&behavior.CurrentAction==PetAction.Think,"thinking held for long request");
            behavior.EndChatThinking(true);for(int i=0;i<2000&&!behavior.SeenActions.Contains(PetAction.Dance);i++)behavior.Update(.02);
            Check(behavior.SeenActions.Contains(PetAction.Dance)&&!behavior.HoldingThought,"thinking exits into celebration");
            var fast=new PetBehavior(2);fast.BeginChatThinking();fast.EndChatThinking(false);fast.Update(.02,false);
            Check(!fast.IsPlaying,"fast cancellation removes queued thinking");
            File.WriteAllText(Path.Combine(directory,"chat.json"),JsonSerializer.Serialize(new{success=true,checks=passed},new JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        catch(Exception error) { File.WriteAllText(Path.Combine(directory,"chat.json"),JsonSerializer.Serialize(new{success=false,error=error.ToString(),checks=passed}));return 1; }
    }
}
