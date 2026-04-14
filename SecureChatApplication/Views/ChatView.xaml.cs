using Microsoft.Win32;
using SecureChatApplication.Models;
using System.IO;
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

    private void SaveFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChatMessage { MediaBytes: { } bytes } msg })
        {
            var dialog = new SaveFileDialog
            {
                FileName = msg.FileName ?? "attachment",
                Title = "Save File"
            };

            if (dialog.ShowDialog() == true)
            {
                File.WriteAllBytes(dialog.FileName, bytes);
            }
        }
    }
}

/// <summary>
/// Selects the appropriate message template based on message type and ownership.
/// </summary>
public class MessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? OwnMessageTemplate { get; set; }
    public DataTemplate? OtherMessageTemplate { get; set; }
    public DataTemplate? OwnMediaTemplate { get; set; }
    public DataTemplate? OtherMediaTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is ChatMessage message)
        {
            if (message.IsOwnMessage)
                return message.IsMedia ? OwnMediaTemplate : OwnMessageTemplate;

            return message.IsMedia ? OtherMediaTemplate : OtherMessageTemplate;
        }

        return base.SelectTemplate(item, container);
    }
}
