using System.Drawing;
using System.Windows;

namespace PD2ModManager {
    /// <summary>
    /// Interaction logic for ErrorWindow.xaml
    /// </summary>
    public partial class ErrorWindow : Window {
        
        public ErrorWindow(string text, string caption, Icon icon) {
            InitializeComponent();
            Title = caption;
            lblInfo.Content = text;
            imgIcon.Source = icon.ToImageSource();
        }
        
        private void btnOK_Click(object sender, RoutedEventArgs e) {
            DialogResult = true;
        }

    }
}
