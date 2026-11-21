using System.Windows;
using InFox.Services;

namespace InFox.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            DataContext = new
            {
                VersionString = $"v{UpdateService.Instance.GetCurrentVersionString()}"
            };
        }
    }
}
