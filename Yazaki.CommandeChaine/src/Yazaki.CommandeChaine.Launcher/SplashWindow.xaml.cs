using System.Windows;

namespace Yazaki.CommandeChaine.Launcher;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string status)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetStatus(status));
            return;
        }

        StatusText.Text = status;
    }
}
