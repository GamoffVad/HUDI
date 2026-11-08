using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hudi.Distribution;

namespace HudiPet;

internal static class NotesVerification
{
    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;
            foreach(var nested in Children<T>(child))yield return nested;
        }
    }
    internal static void Run(PetWindow pet,string directory)
    {
        var checks=new Dictionary<string,bool>();string fixtures=Path.Combine(directory,"notes-fixture-"+Guid.NewGuid().ToString("N"));
        var store=new NotesStore(fixtures);var note=new PetNote(Guid.NewGuid().ToString("N"),"Идея на завтра\nПопробовать нового питомца 🐾",DateTimeOffset.Now,DateTimeOffset.Now);
        store.Put(note);checks["unicode_survives_reopen"]=new NotesStore(fixtures).Read().Single().Text==note.Text;
        note=note with {Text=note.Text+"\nНе забыть сохранить результат.",Updated=DateTimeOffset.Now};store.Put(note);
        var second=new PetNote(Guid.NewGuid().ToString("N"),"Список дел\n• Закончить проект\n• Сделать перерыв",DateTimeOffset.Now,DateTimeOffset.Now.AddSeconds(1));store.Put(second);
        checks["edited_note_keeps_identity"]=store.Read().Single(n=>n.Id==note.Id).Created==note.Created;
        checks["latest_note_is_first"]=store.Read()[0].Id==second.Id;
        store.Delete(second.Id);checks["delete_affects_only_selected_note"]=store.Read().Single().Id==note.Id;store.Put(second);checks["undo_can_restore_note"]=store.Read().Length==2;
        var empty=new NotesWindow(pet,new NotesStore(Path.Combine(fixtures,"empty")));VisualPreview.Save(empty,Path.Combine(directory,"notes-empty.png"));
        checks["empty_list_is_compact_without_explanation"]=((FrameworkElement)empty.Content).ActualHeight<=60&&!Children<TextBlock>(empty.Body).Any(t=>t.Visibility==Visibility.Visible);
        var plus=Children<Button>(empty.Header).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Новая заметка");
        checks["create_is_only_a_plus"]=plus.Content is System.Windows.Shapes.Path&&plus.ToolTip is null;
        plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));VisualPreview.Save(empty,Path.Combine(directory,"notes-new.png"));
        checks["plus_opens_empty_editor"]=Children<TextBox>(empty.Body).Single().Text.Length==0&&empty.Header.Visibility==Visibility.Collapsed;empty.Close();
        var window=new NotesWindow(pet,store);VisualPreview.Save(window,Path.Combine(directory,"notes-list.png"));window.Edit(note);VisualPreview.Save(window,Path.Combine(directory,"notes-editor.png"));
        var editor=Children<TextBox>(window.Body).Single();editor.Text="Короткая заметка";VisualPreview.Save(window,Path.Combine(directory,"notes-compact.png"));
        double compact=((FrameworkElement)window.Content).ActualHeight;
        checks["note_is_compact_without_footer"]=window.Header.Visibility==Visibility.Collapsed&&compact<=60&&!Children<Button>(window.Body).Any()&&!Children<TextBlock>(window.Body).Any(t=>t.Visibility==Visibility.Visible);
        checks["delete_cross_hidden_without_hover"]=window.DeleteButton.Visibility==Visibility.Collapsed;
        window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});VisualPreview.Save(window,Path.Combine(directory,"notes-hover.png"));
        checks["hover_reveals_delete_cross"]=window.DeleteButton.Visibility==Visibility.Visible&&window.DeleteButton.Width<=18;
        checks["editor_delete_is_centered_without_hint"]=Math.Abs(window.DeleteButton.TransformToAncestor((Visual)window.Content).Transform(new Point(0,window.DeleteButton.ActualHeight/2)).Y-((FrameworkElement)window.Content).ActualHeight/2)<1&&window.DeleteButton.ToolTip is null;
        window.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseLeaveEvent});checks["leaving_hides_delete_cross"]=window.DeleteButton.Visibility==Visibility.Collapsed;
        editor.Text=string.Join('\n',Enumerable.Repeat("Строка заметки",30));VisualPreview.Save(window,Path.Combine(directory,"notes-long.png"));
        checks["long_note_grows_then_scrolls"]=((FrameworkElement)window.Content).ActualHeight>compact+100&&((FrameworkElement)window.Content).ActualHeight<=250&&editor.ExtentHeight>editor.ViewportHeight;
        editor.Text="Коротко";VisualPreview.Save(window,Path.Combine(directory,"notes-shrunk.png"));checks["note_shrinks_when_text_removed"]=Math.Abs(((FrameworkElement)window.Content).ActualHeight-compact)<1;
        editor.Text="Заметка после перезапуска\nТекст сохраняется локально.";
        var frame=new DispatcherFrame();var finish=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(650)};finish.Tick+=(_,_)=>{finish.Stop();frame.Continue=false;};finish.Start();Dispatcher.PushFrame(frame);
        checks["typing_autosaves_without_button"]=new NotesStore(fixtures).Read().Single(n=>n.Id==note.Id).Text==editor.Text;
        window.Opacity=0;window.Show();editor.Text="Последние символы перед закрытием";window.Close();checks["closing_flushes_pending_text"]=new NotesStore(fixtures).Read().Single(n=>n.Id==note.Id).Text=="Последние символы перед закрытием";
        var reopened=new NotesWindow(pet,store);reopened.Edit(store.Read().Single(n=>n.Id==note.Id));
        reopened.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});reopened.DeleteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        checks["ui_delete_persists"]=new NotesStore(fixtures).Read().All(n=>n.Id!=note.Id)&&!reopened.IsClosing;
        VisualPreview.Save(reopened,Path.Combine(directory,"notes-undo.png"));var undo=Children<Button>(reopened.Body).Single(b=>b.Content is string text&&text=="Отменить удаление");undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));checks["ui_undo_persists"]=new NotesStore(fixtures).Read().Any(n=>n.Id==note.Id);
        VisualPreview.Save(reopened,Path.Combine(directory,"notes-list-compact.png"));
        var openRow=Children<Button>(reopened.Body).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)==second.Title);
        var card=(Grid)openRow.Parent;var rowDelete=card.Children.OfType<Button>().Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Удалить заметку");
        checks["list_delete_hidden_until_card_hover"]=rowDelete.Visibility==Visibility.Collapsed;
        card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});checks["list_note_reveals_its_delete_cross"]=rowDelete.Visibility==Visibility.Visible;
        VisualPreview.Save(reopened,Path.Combine(directory,"notes-list-hover.png"));
        checks["list_delete_is_centered_without_hint"]=Math.Abs(rowDelete.TransformToAncestor(card).Transform(new Point(0,rowDelete.ActualHeight/2)).Y-card.ActualHeight/2)<1&&rowDelete.ToolTip is null;
        card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseLeaveEvent});checks["leaving_card_hides_its_delete_cross"]=rowDelete.Visibility==Visibility.Collapsed;
        card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});
        rowDelete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));checks["list_cross_deletes_only_that_note"]=new NotesStore(fixtures).Read().All(n=>n.Id!=second.Id)&&new NotesStore(fixtures).Read().Any(n=>n.Id==note.Id);
        VisualPreview.Save(reopened,Path.Combine(directory,"notes-list-deleted.png"));undo=Children<Button>(reopened.Body).Single(b=>b.Content is string text&&text=="Отменить удаление");undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));reopened.Close();
        string primary=Path.Combine(fixtures,"notes.json");File.WriteAllText(primary,"broken fixture");var recovered=new NotesStore(fixtures);checks["corrupt_primary_recovers_backup"]=recovered.Read().Length>0&&recovered.RecoveredBackup;recovered.Put(note);checks["recovered_store_can_save_again"]=new NotesStore(fixtures).Read().Any(n=>n.Id==note.Id&&n.Text==note.Text);
        string invalid=Path.Combine(fixtures,"unsupported");Directory.CreateDirectory(invalid);File.WriteAllText(Path.Combine(invalid,"notes.json"),"{\"Version\":99,\"Notes\":[]}");
        try{new NotesStore(invalid).Put(note);checks["unknown_format_not_overwritten"]=false;}catch(InvalidDataException){checks["unknown_format_not_overwritten"]=File.ReadAllText(Path.Combine(invalid,"notes.json")).Contains("99");}
        var blocked=new NotesWindow(pet,new NotesStore(invalid));blocked.Edit(null);VisualPreview.Save(blocked,Path.Combine(directory,"notes-storage-error.png"));var unsaved=Children<TextBox>(blocked.Body).Single();unsaved.Text="Не терять этот текст";
        bool saved=blocked.Flush();VisualPreview.Save(blocked,Path.Combine(directory,"notes-storage-error.png"));
        checks["save_failure_remains_visible_and_preserves_text"]=!saved&&unsaved.Text=="Не терять этот текст"&&Children<TextBlock>(blocked.Body).Any(t=>t.Visibility==Visibility.Visible&&t.Text.StartsWith("Не удалось сохранить"));
        unsaved.Text="";blocked.Flush();blocked.Close();
        pet.OpenMenu();var menu=pet.OwnedWindows.OfType<GlassWindow>().Single(w=>w.Title==pet.PetName);VisualPreview.Save(menu,Path.Combine(directory,"context-menu.png"));
        checks["compact_menu"]=menu.Width==270&&menu.ActualHeight<350;
        var notesButton=Children<Button>(menu.Body).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Заметки");notesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var liveNotes=pet.OwnedWindows.OfType<NotesWindow>().Single(w=>w.IsVisible);checks["menu_opens_notes_without_closing_pet"]=pet.IsVisible&&liveNotes.IsVisible;liveNotes.Close();
        pet.QueuePetClick(2);UiVerification.Advance((int)pet.ClickSequenceDelay.TotalMilliseconds+40);
        liveNotes=pet.OwnedWindows.OfType<NotesWindow>().Single(w=>w.IsVisible);
        checks["double_click_opens_notes_without_chat"]=liveNotes.IsVisible&&pet.IsVisible&&!pet.OwnedWindows.OfType<ChatWindow>().Any(w=>w.IsVisible);
        pet.QueuePetClick(2);UiVerification.Advance((int)pet.ClickSequenceDelay.TotalMilliseconds+40);
        checks["double_click_reuses_open_notes"]=pet.OwnedWindows.OfType<NotesWindow>().Count(w=>w.IsVisible)==1&&ReferenceEquals(liveNotes,pet.OwnedWindows.OfType<NotesWindow>().Single(w=>w.IsVisible));
        liveNotes.Close();
        File.WriteAllText(Path.Combine(directory,"notes-checks.json"),JsonSerializer.Serialize(new{success=checks.Values.All(v=>v),checks},new JsonSerializerOptions{WriteIndented=true}));
        if(checks.Values.Any(v=>!v))throw new InvalidOperationException("Notes verification failed");
    }
}

internal sealed partial class PetWindow
{
    private sealed class PendingPreviewClient:IModelClient
    {
        internal readonly TaskCompletionSource<ModelReply> Reply=new();
        public Task<ModelReply> CompleteAsync(ModelSettings settings,string key,IReadOnlyList<ChatMessage> messages,IProgress<string>? progress,CancellationToken ct)=>Reply.Task;
        public Task<string[]> ListModelsAsync(ModelSettings settings,string key,CancellationToken ct)=>Task.FromResult(new[]{"fixture"});
    }
    internal void VerifyChatPresentation(string directory)
    {
        chatWindow?.Close();chatWindow=null;chat.Dispose();var client=new PendingPreviewClient();chat=new ChatSession(client);displayedState=ChatState.Idle;BindChatEvents();
        var checks=new Dictionary<string,bool>();var area=ScreenPlacement.WorkArea(this);Top=area.Top+400;
        var request=chat.SendAsync("Тест автоматического показа ответа",new ModelSettings{Model="fixture",UseApiKey=false},"");
        checks["thinking_uses_only_transparent_dots"]=thinkingWindow is {IsVisible:true,AllowsTransparency:true}&&thinkingWindow.Content is StackPanel dots&&dots.Children.Count==3&&chatWindow is null;
        checks["thinking_has_gap_above_pet"]=thinkingWindow is not null&&Top-(thinkingWindow.Top+thinkingWindow.Height)>=11;
        var indicator=thinkingWindow!;UiVerification.Advance(300);
        checks["thinking_uses_generated_images"]=indicator.Dots.Count==3&&indicator.Dots.All(dot=>dot.Source is not null);
        var firstPhases=indicator.Dots.Select(dot=>dot.Opacity).ToArray();
        checks["thinking_dots_move_in_staggered_phases"]=!SystemParameters.ClientAreaAnimation||(firstPhases.Max()-firstPhases.Min()>.05&&indicator.Dots.Any(dot=>((TranslateTransform)((TransformGroup)dot.RenderTransform).Children[1]).Y<-.2));
        VisualPreview.Save(indicator,Path.Combine(directory,"thinking-dots.png"));UiVerification.Advance(180);
        checks["thinking_animation_changes_over_time"]=!SystemParameters.ClientAreaAnimation||indicator.Dots.Select((dot,index)=>Math.Abs(dot.Opacity-firstPhases[index])).Any(change=>change>.02);
        client.Reply.SetResult(new ModelReply("Ответ появился сам — нажимать на питомца не нужно."));
        if(!request.IsCompleted)
        {
            var frame=new DispatcherFrame();var poll=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};int ticks=0;poll.Tick+=(_,_)=>{if(request.IsCompleted||++ticks>100){poll.Stop();frame.Continue=false;}};poll.Start();Dispatcher.PushFrame(frame);
        }
        request.GetAwaiter().GetResult();
        chatWindow?.UpdateLayout();chatWindow?.Place();
        checks["answer_opens_without_click"]=chatWindow is {IsVisible:true,View:ChatView.Answer}&&thinkingWindow is null;
        checks["answer_does_not_take_keyboard_focus"]=chatWindow?.ShowActivated==false;
        var answerClip=((FrameworkElement)chatWindow!.Content).Clip.Bounds;
        checks["automatic_answer_opens_from_circle"]=!SystemParameters.ClientAreaAnimation||(chatWindow.OpeningAnimationRunning&&answerClip.Width<=20&&Math.Abs(answerClip.Width-answerClip.Height)<1);
        VisualPreview.Save(chatWindow,Path.Combine(directory,"automatic-answer-circle.png"));
        UiVerification.Advance(140);
        checks["automatic_answer_expands_with_matte"]=DialogBackdrop.IsMatteApplied(chatWindow)&&(!SystemParameters.ClientAreaAnimation||(chatWindow.Surface.RevealProgress>0&&chatWindow.Surface.RevealProgress<1));
        VisualPreview.Save(chatWindow,Path.Combine(directory,"automatic-answer-expanding.png"));
        UiVerification.Advance(300);
        checks["automatic_answer_finishes_reveal"]=!chatWindow.OpeningAnimationRunning&&chatWindow.Surface.RevealProgress==1&&chatWindow.Surface.Child.Opacity==1;
        checks["answer_has_gap_above_pet"]=chatWindow is not null&&Top-(chatWindow.Top+chatWindow.ActualHeight)>=11;
        checks["thinking_animation_stops_when_answer_arrives"]=indicator.Dots.All(dot=>!dot.HasAnimatedProperties&&((TransformGroup)dot.RenderTransform).Children.All(t=>!t.HasAnimatedProperties));
        if(chatWindow is not null)VisualPreview.Save(chatWindow,Path.Combine(directory,"automatic-answer.png"));
        chatWindow?.Close();chatWindow=null;
        OpenChat(false);
        checks["last_answer_reopens_with_same_animation"]=chatWindow is {View:ChatView.Answer,IsVisible:true}&&(!SystemParameters.ClientAreaAnimation||chatWindow.OpeningAnimationRunning);
        UiVerification.Advance(420);var existingAnswer=chatWindow;OpenChat(false);
        checks["visible_answer_does_not_restart_animation"]=ReferenceEquals(existingAnswer,chatWindow)&&!chatWindow!.OpeningAnimationRunning;
        chatWindow?.Close();chatWindow=null;
        File.WriteAllText(Path.Combine(directory,"chat-presentation.json"),JsonSerializer.Serialize(new{success=checks.Values.All(v=>v),checks},new JsonSerializerOptions{WriteIndented=true}));
        if(checks.Values.Any(v=>!v))throw new InvalidOperationException("Chat presentation verification failed");
    }
}
