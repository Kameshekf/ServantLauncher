using Avalonia.Controls;
using Avalonia.Interactivity;
using ServantLauncher.ViewModels;

namespace ServantLauncher.Views
{
    public partial class CreateProfileDialog : UserControl
    {
        public CreateProfileDialog()
        {
            InitializeComponent();
        }

        private void OnReleasesClicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is CreateProfileViewModel vm)
            {
                vm.ShowSnapshots = false;
            }
        }

        private void OnSnapshotsClicked(object? sender, RoutedEventArgs e)
        {
            if (DataContext is CreateProfileViewModel vm)
            {
                vm.ShowSnapshots = true;
            }
        }
    }
}
