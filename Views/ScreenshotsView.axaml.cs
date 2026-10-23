using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ServantLauncher.Views
{
    public partial class ScreenshotsView : UserControl
    {
        public ScreenshotsView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
