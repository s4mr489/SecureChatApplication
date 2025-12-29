using SecureChatApplication.Models;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SecureChatApplication.Views;

/// <summary>
/// Interaction logic for ChatView.xaml
/// </summary>
public partial class ChatView : UserControl
{
    public ChatView()
    {
        InitializeComponent();
    }
}

/// <summary>
/// Selects the appropriate message template based on whether it's own or other's message.
/// </summary>
public class MessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? OwnMessageTemplate { get; set; }
    public DataTemplate? OtherMessageTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is ChatMessage message)
        {
            return message.IsOwnMessage ? OwnMessageTemplate : OtherMessageTemplate;
        }
        return base.SelectTemplate(item, container);
    }
}
