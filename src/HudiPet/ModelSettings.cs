using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HudiPet;

internal sealed class ModelSettings
{
    internal const string DefaultSystemPrompt="Отвечай ясно и по делу на языке пользователя. Ты — дружелюбный питомец и помощник на рабочем столе.";
    public string Provider { get; set; } = "openai";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Protocol { get; set; } = "responses";
    public string Model { get; set; } = "gpt-5.4-mini";
    public string SystemPrompt { get; set; } = DefaultSystemPrompt;
    public int MaxOutputTokens { get; set; } = 2048;
    public int TimeoutSeconds { get; set; } = 120;
    public bool Stream { get; set; } = true;
    public bool KeepHistory { get; set; } = true;
    public bool UseApiKey { get; set; } = true;
    public bool SendTemperature { get; set; }
    public double Temperature { get; set; } = .7;
    public string ChatTokenParameter { get; set; } = "max_tokens";
    public string ProtectedApiKey { get; set; } = "";
    public string CredentialOrigin { get; set; } = "";
    internal ModelSettings Copy() => (ModelSettings)MemberwiseClone();

    internal Uri Endpoint(string route)
    {
        if (!Uri.TryCreate(BaseUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var address)
            || (address.Scheme != "https" && !(address.Scheme == "http" && address.IsLoopback))
            || !string.IsNullOrEmpty(address.UserInfo) || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
            throw new ModelException("Укажите HTTPS-адрес API. Для локальной модели разрешён http://localhost.");
        return new Uri(address, route);
    }
    internal string Origin => Endpoint("").GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    internal void Validate(string key, bool requireModel = true)
    {
        if(key.Any(char.IsControl)) throw new ModelException("Ключ содержит перенос строки или служебный символ. Вставьте его одной строкой.");
        _ = Endpoint("");
        if (Protocol is not ("responses" or "chat")) throw new ModelException("Выберите формат API.");
        if (requireModel && string.IsNullOrWhiteSpace(Model)) throw new ModelException("Выберите модель в настройках.");
        if (UseApiKey && string.IsNullOrWhiteSpace(key)) throw new ModelException("Добавьте API-ключ в настройках питомца.");
        if (TimeoutSeconds is < 10 or > 1800) throw new ModelException("Тайм-аут должен быть от 10 до 1800 секунд.");
        if (MaxOutputTokens is < 128 or > 32768) throw new ModelException("Лимит ответа: от 128 до 32768 токенов.");
        if (!double.IsFinite(Temperature) || Temperature is < 0 or > 2) throw new ModelException("Температура должна быть от 0 до 2.");
        if (ChatTokenParameter is not ("max_tokens" or "max_completion_tokens")) throw new ModelException("Неверный параметр лимита ответа.");
    }
}

internal sealed class ModelException(string message) : Exception(message);

internal static class SecretStore
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool ok = protect ? CryptProtectData(ref input, "HUDI API", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                              : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException("Windows не смогла защитить или прочитать ключ.");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length); Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero) { Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length); LocalFree(output.Data); }
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
    internal static string Protect(string value) => string.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), true));
    internal static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var bytes = Transform(Convert.FromBase64String(value), false);
        try { return Encoding.UTF8.GetString(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}

internal sealed class ModelSettingsStore
{
    private readonly string path;
    internal string? LoadNotice { get; private set; }
    internal ModelSettingsStore(string? directory = null) => path = Path.Combine(directory ?? PetWindow.SettingsDirectory, "model-settings.json");
    internal (ModelSettings Settings, string Key) Load()
    {
        try
        {
            if (!File.Exists(path)) return (new(), "");
            var value = JsonSerializer.Deserialize<ModelSettings>(File.ReadAllText(path)) ?? new();
            if(value.SystemPrompt=="Ты — HUDI, дружелюбный помощник на рабочем столе. Отвечай ясно и по делу на языке пользователя.")value.SystemPrompt=ModelSettings.DefaultSystemPrompt;
            string key = value.CredentialOrigin == value.Origin ? SecretStore.Unprotect(value.ProtectedApiKey) : "";
            return (value, key);
        }
        catch (Exception error) when (error is IOException or JsonException or CryptographicException or FormatException or ModelException or UnauthorizedAccessException)
        { LoadNotice = "Не удалось прочитать настройки подключения. Введите их заново."; return (new(), ""); }
    }
    internal void Save(ModelSettings value, string key)
    {
        value.Validate(key);
        var stored = value.Copy(); stored.ProtectedApiKey = SecretStore.Protect(key); stored.CredentialOrigin = value.Origin;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
