using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Hudi.Distribution;

namespace HudiPet;

internal static class ConversationVerification
{
    internal static int Run(string directory)
    {
        Directory.CreateDirectory(directory);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Ui.Initialize();
        var checks=new Dictionary<string,bool>();string? error=null;PetWindow? pet=null;
        try{pet=new PetWindow(false,true){ShowInTaskbar=false};pet.Show();pet.VerifyConversation(directory,checks);}
        catch(Exception ex){error=ex.ToString();}
        finally{pet?.Close();}
        bool success=error is null&&checks.Values.All(v=>v);File.WriteAllText(Path.Combine(directory,"conversation-tests.json"),JsonSerializer.Serialize(new{success,checks,error},new JsonSerializerOptions{WriteIndented=true}));
        app.Shutdown();return success?0:1;
    }
}

internal sealed partial class PetWindow
{
    private sealed class ConversationClient:IModelClient
    {
        internal readonly List<ChatMessage[]> Requests=[];
        internal TaskCompletionSource<ModelReply> Pending=new();
        public Task<ModelReply> CompleteAsync(ModelSettings settings,string key,IReadOnlyList<ChatMessage> messages,IProgress<string>? progress,CancellationToken ct)
        {Requests.Add(messages.ToArray());Pending=new();return Pending.Task.WaitAsync(ct);}
        public Task<string[]> ListModelsAsync(ModelSettings settings,string key,CancellationToken ct)=>Task.FromResult(new[]{"fixture"});
    }
    internal void VerifyConversation(string directory,Dictionary<string,bool> checks)
    {
        static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
        {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T item)yield return item;foreach(var nested in Children<T>(child))yield return nested;}}
        chat.Dispose();var client=new ConversationClient();chat=new ChatSession(client);displayedState=ChatState.Idle;
        modelSettings=new(){Model="fixture",UseApiKey=false,KeepHistory=true};apiKey="";BindChatEvents();OpenChat(true);UiVerification.Advance(450);
        var window=chatWindow!;
        TextBox Input()=>Children<TextBox>(window.Body).Single(t=>!t.IsReadOnly);
        ConversationTranscript Transcript()=>window.Transcript!;
        Button Send()=>Children<Button>(window.Body).Single();
        void Pump(){UiVerification.Advance(130);window.UpdateLayout();}
        Input().Text="Привет! Поможешь с планом?";window.HandleComposerKey(Key.Enter,ModifierKeys.None);Pump();
        checks["first_send_keeps_same_window"]=ReferenceEquals(window,chatWindow)&&window.IsVisible&&window.View==ChatView.Answer&&thinkingWindow is null;
        checks["busy_state_shows_question_and_pending_reply"]=Transcript().PlainText.Contains("Привет! Поможешь с планом?")&&Transcript().PlainText.Contains("Печатает");
        Input().Text="Следующий вопрос";Send().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        checks["busy_prevents_duplicate_send"]=client.Requests.Count==1&&!Send().IsEnabled;
        window.ClearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        checks["clear_is_disabled_during_reply"]=!window.ClearButton.IsEnabled&&chat.Busy&&chat.LastQuestion.Length>0;
        client.Pending.SetResult(new ModelReply("Конечно. Сначала выберем главную задачу."));Pump();
        checks["reply_keeps_typed_draft"]=Input().Text=="Следующий вопрос"&&Send().IsEnabled;
        checks["conversation_shows_both_roles"]=Transcript().PlainText.Contains("Вы\nПривет!")&&Transcript().PlainText.Contains(PetName+"\nКонечно.");
        var cards=Transcript().Cards;
        checks["roles_use_separate_opposite_aligned_cards"]=cards.Count==2&&cards[0].HorizontalAlignment==HorizontalAlignment.Right&&cards[1].HorizontalAlignment==HorizontalAlignment.Left;
        checks["message_backgrounds_are_distinct"]=cards[0].Background is SolidColorBrush userFill&&cards[1].Background is SolidColorBrush modelFill&&userFill.Color!=modelFill.Color;
        checks["messages_have_clear_vertical_spacing"]=cards.All(card=>card.Margin.Bottom>=10);
        checks["message_text_remains_selectable"]=Children<TextBox>(Transcript()).All(t=>t.IsReadOnly&&t.Focusable);
        UiVerification.Advance(450);VisualPreview.Save(window,Path.Combine(directory,"separated-messages.png"));
        var separator=Children<Border>(window.Body).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Разделитель переписки");
        double y=separator.TransformToAncestor(window.Body).Transform(new Point()).Y;
        checks["separator_sits_between_reply_and_composer"]=Transcript().TransformToAncestor(window.Body).Transform(new Point(0,Transcript().ActualHeight)).Y<=y&&Input().TransformToAncestor(window.Body).Transform(new Point()).Y>y;
        double oneLine=Input().ActualHeight;
        Input().Text=string.Join('\n',Enumerable.Repeat("Сообщение",12));Pump();
        checks["followup_input_grows_but_stays_bounded"]=Input().ActualHeight>oneLine&&Input().ActualHeight<=88&&window.ActualHeight==320;
        checks["shift_enter_does_not_send"]=!window.HandleComposerKey(Key.Enter,ModifierKeys.Shift)&&client.Requests.Count==1;
        Input().Text="Какую задачу выбрать?";Send().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        checks["followup_sends_previous_context"]=client.Requests.Count==2&&client.Requests[1].Length==3&&client.Requests[1][1].Role=="assistant"&&client.Requests[1][2].Content=="Какую задачу выбрать?";
        client.Pending.SetResult(new ModelReply("Выбери ту, которая приблизит тебя к цели."));Pump();
        checks["second_reply_remains_in_same_conversation"]=ReferenceEquals(window,chatWindow)&&Transcript().PlainText.Contains("главную задачу")&&Transcript().PlainText.Contains("приблизит тебя к цели")&&Input().Text=="";
        checks["older_message_controls_survive_new_replies"]=ReferenceEquals(cards[0],Transcript().Cards[0]);
        VisualPreview.Save(window,Path.Combine(directory,"conversation.png"));
        Input().Text="Ещё вопрос";window.HandleComposerKey(Key.Enter,ModifierKeys.None);Pump();
        client.Pending.SetException(new ModelException("Тестовая ошибка соединения"));Pump();
        checks["failed_request_keeps_history_and_restores_question"]=window.View==ChatView.Answer&&Input().Text=="Ещё вопрос"&&Transcript().PlainText.Contains("Тестовая ошибка")&&Transcript().PlainText.Contains("главную задачу");
        window.HandleComposerKey(Key.Enter,ModifierKeys.None);Pump();client.Pending.SetResult(new ModelReply("Теперь всё готово."));Pump();
        checks["retry_works_in_same_window"]=ReferenceEquals(window,chatWindow)&&Transcript().PlainText.Contains("Теперь всё готово.")&&!Transcript().PlainText.Contains("Тестовая ошибка");
        Input().Text="Подробный ответ";window.HandleComposerKey(Key.Enter,ModifierKeys.None);Pump();
        client.Pending.SetResult(new ModelReply(string.Join('\n',Enumerable.Repeat("Длинный ответ остаётся в своей карточке и прокручивается вместе с перепиской.",20))));Pump();
        checks["long_message_scrolls_above_fixed_input"]=Transcript().ExtentHeight>Transcript().ViewportHeight&&Input().IsVisible&&Input().TransformToAncestor(window.Body).Transform(new Point()).Y>=Transcript().ActualHeight;
        VisualPreview.Save(window,Path.Combine(directory,"long-message.png"));
        Input().Text="Новый разговор";window.ClearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
        checks["clear_removes_messages_and_model_context"]=Transcript().Cards.Count==0&&chat.Messages.Count==0&&chat.Answer==""&&chat.LastQuestion==""&&chat.Error==""&&!chat.Truncated;
        checks["clear_preserves_unsent_draft"]=Input().Text=="Новый разговор"&&!window.ClearButton.IsEnabled;
        window.HandleComposerKey(Key.Enter,ModifierKeys.None);Pump();
        checks["new_request_after_clear_has_no_old_context"]=client.Requests.Last().Length==1&&client.Requests.Last()[0].Content=="Новый разговор";
        client.Pending.SetResult(new ModelReply("Начинаем заново."));Pump();
        Input().Text="Несохранённый черновик";window.Close();OpenChat(true);Pump();
        checks["reopen_keeps_history_and_draft"]=chatWindow!.View==ChatView.Answer&&chatWindow.Draft=="Несохранённый черновик";
    }
}
