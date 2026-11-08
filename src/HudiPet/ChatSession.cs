using System.IO;
namespace HudiPet;

internal enum ChatState { Idle, Thinking, Answered, Error, Canceled }
internal sealed class ChatSession : IDisposable
{
    private readonly IModelClient client;
    private readonly List<ChatMessage> history = [];
    private CancellationTokenSource? request;
    private int generation;
    internal ChatState State { get; private set; }
    internal bool Busy => State == ChatState.Thinking;
    internal string LastQuestion { get; private set; } = "";
    internal string Answer { get; private set; } = "";
    internal string PartialAnswer { get; private set; } = "";
    internal string Error { get; private set; } = "";
    internal bool Truncated { get; private set; }
    internal event Action? Changed;
    internal event Action? Started;
    internal event Action<bool>? Finished;
    internal ChatSession(IModelClient client) => this.client = client;
    internal void ClearHistory() { if (!Busy) { history.Clear(); Answer = PartialAnswer = LastQuestion = ""; State = ChatState.Idle; Changed?.Invoke(); } }
    internal void Cancel() => request?.Cancel();
    internal async Task SendAsync(string question, ModelSettings settings, string key)
    {
        if (Busy || string.IsNullOrWhiteSpace(question)) return;
        LastQuestion = question.Trim();
        try { settings.Validate(key); }
        catch (ModelException error) { Error = error.Message; State = ChatState.Error; Changed?.Invoke(); return; }
        int run = ++generation;
        using var cancellation = new CancellationTokenSource(); request = cancellation;
        LastQuestion = question.Trim(); PartialAnswer = ""; Error = ""; Truncated = false;
        State = ChatState.Thinking; Started?.Invoke(); Changed?.Invoke();
        var messages = settings.KeepHistory ? history.TakeLast(20).ToList() : [];
        messages.Add(new("user", LastQuestion));
        var progress = new Progress<string>(text => { if (run == generation && Busy) { PartialAnswer = text; Changed?.Invoke(); } });
        bool success = false;
        try
        {
            var reply = await client.CompleteAsync(settings.Copy(), key, messages, progress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            Answer = PartialAnswer = reply.Text; Truncated = reply.Truncated; State = ChatState.Answered;
            if (settings.KeepHistory)
            {
                history.Add(new("user", LastQuestion)); history.Add(new("assistant", Answer));
                while (history.Count > 20 || history.Sum(m => m.Content.Length) > 120_000 && history.Count > 2) history.RemoveRange(0, 2);
            }
            else history.Clear();
            success = true;
        }
        catch (OperationCanceledException) { State = ChatState.Canceled; PartialAnswer = ""; }
        catch (ModelException error) { State = ChatState.Error; Error = error.Message; }
        catch (Exception error) when (error is IOException or System.Net.Http.HttpRequestException) { State = ChatState.Error; Error = "Соединение прервалось. Попробуйте ещё раз."; }
        finally { request = null; generation++; Finished?.Invoke(success); Changed?.Invoke(); }
    }
    public void Dispose() { generation++; request?.Cancel(); }
}
