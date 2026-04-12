using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveCharts;
using LiveCharts.Wpf;

namespace NetworkMonitor.Wpf
{
    public partial class MonitorView : UserControl
    {
        public Func<double, string> YFormatter { get; set; }

        // 赋予初值或可空标记，消除警告
        private CancellationTokenSource? _cts;
        private List<string> _targetIps = new List<string>();
        private Dictionary<string, ChartValues<double>> _ipDataMap = new Dictionary<string, ChartValues<double>>();

        public MonitorView()
        {
            InitializeComponent();
            YFormatter = value => value.ToString("N0") + " ms";
            DataContext = this;
        }

        private void LoadTargets()
        {
            _targetIps.Clear();
            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ip.txt");
                if (File.Exists(filePath))
                {
                    string[] lines = File.ReadAllLines(filePath);
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line)) _targetIps.Add(line.Trim());
                    }
                }
            }
            catch { }

            if (_targetIps.Count == 0)
            {
                _targetIps.Add("www.baidu.com:80");
                _targetIps.Add("8.8.8.8:53");
            }
        }

        private void InitChart()
        {
            SpeedChart.Series.Clear();
            _ipDataMap.Clear();

            foreach (var ip in _targetIps)
            {
                var values = new ChartValues<double>();
                _ipDataMap[ip] = values;

                SpeedChart.Series.Add(new LineSeries
                {
                    Title = ip,
                    Values = values,
                    PointGeometry = null,
                    Fill = Brushes.Transparent,
                    StrokeThickness = 2
                });
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            LoadTargets();
            InitChart();
            TxtNodeCount.Text = _targetIps.Count.ToString();
            TxtMonitorStatus.Text = "运行中";
            TxtMonitorStatus.Foreground = (Brush)FindResource("BrushSuccess");

            BtnStart.IsEnabled = false;
            BtnStop.IsEnabled = true;
            _cts = new CancellationTokenSource();

            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    List<Task> pingTasks = new List<Task>();
                    foreach (var ip in _targetIps)
                    {
                        pingTasks.Add(PingAndUpdateAsync(ip, _cts.Token));
                    }
                    await Task.WhenAll(pingTasks);
                    await Task.Delay(1000, _cts.Token);
                }
            }
            catch (TaskCanceledException) { }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            BtnStart.IsEnabled = true;
            BtnStop.IsEnabled = false;
            TxtMonitorStatus.Text = "已停止";
            TxtMonitorStatus.Foreground = (Brush)FindResource("BrushWarning");
        }

        private async Task PingAndUpdateAsync(string target, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            int port = 80;
            string host = target;

            if (target.Contains(":"))
            {
                var parts = target.Split(':');
                host = parts[0];
                int.TryParse(parts[1], out port);
            }

            var sw = Stopwatch.StartNew();
            bool success = false;
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    var ar = client.BeginConnect(host, port, null, null);
                    success = ar.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(1000));
                    if (success) client.EndConnect(ar);
                }
            }
            catch { success = false; }
            sw.Stop();

            double latency = success ? sw.ElapsedMilliseconds : 1000;

            Dispatcher.Invoke(() =>
            {
                if (_ipDataMap.TryGetValue(target, out var values))
                {
                    values.Add(latency);
                    if (values.Count > 60) values.RemoveAt(0);
                }
            });
        }
    }
}