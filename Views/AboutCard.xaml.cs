using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace InFox.Views
{
    public partial class AboutCard : System.Windows.Controls.UserControl
    {
        public AboutCard()
        {
            InitializeComponent();
        }

        private void OnExternalLinkClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string url || string.IsNullOrWhiteSpace(url))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
