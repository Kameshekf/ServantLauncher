using Avalonia.Controls;
using Avalonia.Interactivity;
using ServantLauncher.ViewModels;

namespace ServantLauncher.Views
{
    public partial class ConsoleView : UserControl
    {
        public ConsoleView()
        {
            InitializeComponent();
        }

        private async void OnCopyLogClicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is ConsoleViewModel vm && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                var text = vm.GetFullLogText();
                await clipboard.SetTextAsync(text);
            }
        }
    }
}
