using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NetworkMonitor.Wpf
{
    public partial class MainWindow : Window
    {
        private HomeView _homeView;
        private MonitorView _monitorView;
        private TraceView _traceView;

        public MainWindow()
        {
            InitializeComponent();

            _homeView = new HomeView();
            _monitorView = new MonitorView();
            _traceView = new TraceView();

            // 默认加载主页
            MainContent.Content = _homeView;
            SetActiveNav(BtnNavHome);
        }

        private void BtnNavHome_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = _homeView;
            SetActiveNav(BtnNavHome);
        }

        private void BtnNavMonitor_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = _monitorView;
            SetActiveNav(BtnNavMonitor);
        }

        private void BtnNavTrace_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = _traceView;
            SetActiveNav(BtnNavTrace);
        }

        private void SetActiveNav(Button activeButton)
        {
            var defaultBrush = (Brush)FindResource("BrushNavText");
            var activeBrush = (Brush)FindResource("BrushAccent");

            BtnNavHome.Background = Brushes.Transparent;
            BtnNavMonitor.Background = Brushes.Transparent;
            BtnNavTrace.Background = Brushes.Transparent;

            BtnNavHome.Foreground = defaultBrush;
            BtnNavMonitor.Foreground = defaultBrush;
            BtnNavTrace.Foreground = defaultBrush;

            activeButton.Background = activeBrush;
            activeButton.Foreground = Brushes.White;
        }
    }
}