using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace HudiPet;

internal sealed class ConversationTranscript:ScrollViewer
{
    private sealed record MessageView(string Role,Border Card,TextBox Text);
    private readonly StackPanel messages=new();
    private readonly List<MessageView> rows=[];
    private readonly string petName;
    internal string PlainText=>string.Join("\n\n",rows.Select(row=>(row.Role=="user"?"Вы":petName)+"\n"+row.Text.Text));
    internal IReadOnlyList<Border> Cards=>rows.Select(r=>r.Card).ToArray();
    internal ConversationTranscript(string petName)
    {
        this.petName=petName;Content=messages;VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;CanContentScroll=false;Focusable=false;
        AutomationProperties.SetName(this,"Переписка с "+petName);
        SizeChanged+=(_,_)=>UpdateWidths();
    }
    private void UpdateWidths()
    {
        double available=ViewportWidth>0?ViewportWidth:ActualWidth;
        foreach(var row in rows)row.Card.MaxWidth=Math.Max(60,available-30);
    }
    private MessageView Create(string role)
    {
        bool user=role=="user";var theme=PetAppearance.Current;
        var body=new StackPanel();
        body.Children.Add(new TextBlock{Text=user?"Вы":petName,FontSize=10,FontWeight=FontWeights.SemiBold,
            Foreground=theme.Ink,Margin=new Thickness(0,0,0,5)});
        var text=Ui.Input("",user?"Сообщение пользователя":"Ответ модели");
        text.IsReadOnly=true;text.AcceptsReturn=true;text.TextWrapping=TextWrapping.Wrap;
        text.BorderThickness=new Thickness(0);text.Background=Brushes.Transparent;text.Foreground=theme.Ink;
        text.CaretBrush=theme.Ink;text.Padding=new Thickness(0);text.FontSize=13;text.MinWidth=0;
        text.VerticalScrollBarVisibility=ScrollBarVisibility.Disabled;text.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        // A single outer scroll viewer scrolls the whole conversation, including
        // long selectable replies, while the composer remains fixed below it.
        text.PreviewMouseWheel+=(_,e)=>{ScrollToVerticalOffset(VerticalOffset-e.Delta/3d);e.Handled=true;};
        body.Children.Add(text);
        var card=new Border{Child=body,CornerRadius=new CornerRadius(0),Padding=new Thickness(10,8,10,9),
            HorizontalAlignment=user?HorizontalAlignment.Right:HorizontalAlignment.Left,
            Margin=user?new Thickness(26,0,0,12):new Thickness(0,0,26,12),
            Background=new SolidColorBrush(PetAppearance.Mix(theme.SurfaceColor,user?theme.AccentColor:theme.TextColor,user?.27:.055)),
            BorderBrush=user?theme.Edge:new SolidColorBrush(PetAppearance.Mix(theme.SurfaceColor,theme.TextColor,.15)),BorderThickness=new Thickness(1)};
        AutomationProperties.SetName(card,user?"Ваше сообщение":"Сообщение "+petName);
        return new(role,card,text);
    }
    internal void UpdateMessages(IReadOnlyList<ChatMessage> values)
    {
        bool follow=VerticalOffset+ViewportHeight>=ExtentHeight-3;
        bool changed=false;
        while(rows.Count>values.Count){messages.Children.RemoveAt(rows.Count-1);rows.RemoveAt(rows.Count-1);changed=true;}
        for(int i=0;i<values.Count;i++)
        {
            var message=values[i];
            if(i==rows.Count){var row=Create(message.Role);rows.Add(row);messages.Children.Add(row.Card);changed=true;}
            else if(rows[i].Role!=message.Role){messages.Children.RemoveAt(i);rows[i]=Create(message.Role);messages.Children.Insert(i,rows[i].Card);changed=true;}
            if(rows[i].Text.Text!=message.Content){rows[i].Text.Text=message.Content;changed=true;}
        }
        if(changed){UpdateWidths();UpdateLayout();if(follow)ScrollToEnd();}
    }
}
