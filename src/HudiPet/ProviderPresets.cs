namespace HudiPet;

// Endpoint/model defaults checked against the providers' official docs on 2026-11-07.
// See docs/model-providers.md. Presets never contain credentials.
internal sealed record ProviderPreset(string Id,string Label,string BaseUrl,string Model,
    string Protocol="chat",bool UseApiKey=true,bool SendTemperature=false,double Temperature=.7,
    int Limit=4096,int Timeout=120,string TokenParameter="max_tokens",string Hint="")
{
    internal ModelSettings Defaults()=>new()
    {
        Provider=Id,BaseUrl=BaseUrl,Model=Model,Protocol=Protocol,UseApiKey=UseApiKey,
        SendTemperature=SendTemperature,Temperature=Temperature,MaxOutputTokens=Limit,
        TimeoutSeconds=Timeout,ChatTokenParameter=TokenParameter,Stream=true,KeepHistory=true
    };
}

internal static class ProviderPresets
{
    internal static readonly ProviderPreset[] All=
    [
        new("openai","OpenAI","https://api.openai.com/v1","gpt-5.4-mini","responses",TokenParameter:"max_completion_tokens",Hint:"Введите API-ключ OpenAI. Модель можно заменить через стрелку справа."),
        new("anthropic","Anthropic · Claude","https://api.anthropic.com/v1","claude-haiku-5-5",Hint:"Введите ключ Claude API, привязанный к рабочему пространству."),
        new("gemini","Google · Gemini","https://generativelanguage.googleapis.com/v1beta/openai","gemini-3.8-flash",Hint:"Введите ключ Gemini API из Google AI Studio."),
        new("deepseek","DeepSeek","https://api.deepseek.com","deepseek-flash",Timeout:180,Hint:"Введите ключ DeepSeek API."),
        new("openrouter","OpenRouter","https://openrouter.ai/api/v1","openrouter/auto",Hint:"Введите ключ OpenRouter. Auto выбирает модель автоматически; можно указать другую."),
        new("xai","xAI · Grok","https://api.x.ai/v1","grok-4.7","responses",SendTemperature:true,Timeout:900,Hint:"Введите ключ API Grok. Для долгих ответов установлен увеличенный тайм-аут."),
        new("groq","Groq","https://api.groq.com/openai/v1","llama-3.3-70b-versatile",SendTemperature:true,TokenParameter:"max_completion_tokens",Hint:"Введите ключ Groq API."),
        new("mistral","Mistral AI","https://api.mistral.ai/v1","mistral-small-latest",SendTemperature:true,Hint:"Введите ключ Mistral AI."),
        new("together","Together AI","https://api.together.ai/v1","MiniMaxAI/MiniMax-M3",Timeout:180,Hint:"Введите ключ Together AI. Доступные модели можно загрузить через стрелку."),
        new("ollama","Ollama · локально","http://localhost:11434/v1","",UseApiKey:false,Timeout:300,Hint:"Запустите Ollama. Нажмите стрелку у модели, чтобы выбрать уже установленную."),
        new("lmstudio","LM Studio · локально","http://localhost:1234/v1","",UseApiKey:false,Timeout:300,Hint:"Включите локальный сервер LM Studio и выберите установленную модель через стрелку."),
        new("compatible","Другой сервер","","",Hint:"Укажите адрес OpenAI-совместимого API, модель и ключ своего сервиса.")
    ];
    internal static int Index(string id)
    {
        int index=Array.FindIndex(All,p=>p.Id==(id=="local"?"ollama":id));return index>=0?index:All.Length-1;
    }
}
