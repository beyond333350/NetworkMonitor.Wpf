using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LiveCharts;

namespace NetworkMonitor.Wpf
{
    public class LossWarningConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is double loss) return loss >= 1.0;
            return false;
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }

    public partial class TraceView : UserControl
    {
        public ObservableCollection<MtrNode> MtrNodesList { get; set; } = new ObservableCollection<MtrNode>();
        public ChartValues<double> SelectedNodeHistory { get; set; } = new ChartValues<double>();

        private CancellationTokenSource _cts;
        private MtrNode _selectedNode;

        public TraceView()
        {
            InitializeComponent();
            DataContext = this;
            GridMtrNodes.ItemsSource = MtrNodesList;

            // 【核心增加】初始化时读取 ip.txt
            LoadIpList();
        }

        // 读取程序目录下的 ip.txt 填充下拉框
        private void LoadIpList()
        {
            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ip.txt");
                if (File.Exists(filePath))
                {
                    string[] lines = File.ReadAllLines(filePath);
                    List<string> list = new List<string>();
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line)) list.Add(line.Trim());
                    }
                    if (list.Count > 0)
                    {
                        CmbTarget.ItemsSource = list;
                        CmbTarget.SelectedIndex = 0; // 默认选中第一项
                    }
                }
            }
            catch { }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            // 【核心修改】读取 CmbTarget.Text
            string target = CmbTarget.Text.Trim();
            if (string.IsNullOrWhiteSpace(target)) return;

            BtnStart.IsEnabled = false;
            BtnStop.IsEnabled = true;
            CmbTarget.IsEnabled = false; // 测试时禁用下拉框

            _selectedNode = null;
            TxtChartTitle.Text = "📊 正在进行路由发现...";

            Application.Current.Dispatcher.InvokeAsync(() => {
                MtrNodesList.Clear();
                SelectedNodeHistory.Clear();
            });

            _cts = new CancellationTokenSource();

            try
            {
                IPAddress targetIp = null;
                await Task.Run(() => {
                    if (!IPAddress.TryParse(target, out targetIp))
                    {
                        var addresses = Dns.GetHostAddresses(target);
                        if (addresses.Length > 0) targetIp = addresses[0];
                    }
                });

                if (targetIp != null)
                {
                    TxtChartTitle.Text = $"📊 MTR 追踪进行中: {target} [{targetIp}]";
                    await RunMtrEngineAsync(targetIp, _cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // 忽略用户主动取消的报错
            }
            catch (Exception ex)
            {
                if (ex.InnerException is OperationCanceledException || ex.Message.Contains("canceled") || ex.Message.Contains("取消")) return;
                MessageBox.Show($"解析目标地址失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                StopTrace();
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopTrace();
        }

        private void StopTrace()
        {
            _cts?.Cancel();
            BtnStart.IsEnabled = true;
            BtnStop.IsEnabled = false;
            CmbTarget.IsEnabled = true; // 恢复下拉框可用状态

            if (TxtChartTitle.Text.Contains("进行中"))
            {
                TxtChartTitle.Text = TxtChartTitle.Text.Replace("进行中", "已停止");
            }
        }

        private async Task RunMtrEngineAsync(IPAddress targetIp, CancellationToken token)
        {
            int maxHops = 30;
            int timeout = 1000;
            byte[] buffer = Encoding.ASCII.GetBytes("MTR_NetworkMonitor_Test_Packet");

            using (Ping pingSender = new Ping())
            {
                for (int ttl = 1; ttl <= maxHops; ttl++)
                {
                    if (token.IsCancellationRequested) return;

                    PingOptions options = new PingOptions(ttl, true);
                    PingReply reply = null;

                    try
                    {
                        reply = await pingSender.SendPingAsync(targetIp, timeout, buffer, options);
                    }
                    catch { }

                    string ip = "请求超时 (*)";
                    bool isEnd = false;

                    if (reply != null && reply.Status == IPStatus.TtlExpired)
                    {
                        ip = reply.Address.ToString();
                    }
                    else if (reply != null && reply.Status == IPStatus.Success)
                    {
                        ip = reply.Address.ToString();
                        isEnd = true;
                    }

                    Application.Current?.Dispatcher?.Invoke(() => {
                        MtrNode node = new MtrNode { Hop = ttl, IpAddress = ip };
                        MtrNodesList.Add(node);
                    });

                    if (isEnd) break;
                }

                while (!token.IsCancellationRequested)
                {
                    List<MtrNode> safeNodes = null;
                    Application.Current?.Dispatcher?.Invoke(() => {
                        safeNodes = new List<MtrNode>(MtrNodesList);
                    });

                    if (safeNodes == null) break;

                    foreach (var node in safeNodes)
                    {
                        if (token.IsCancellationRequested) break;

                        if (node.IpAddress == "请求超时 (*)") continue;

                        long rtt = -1;
                        bool isSuccess = false;

                        try
                        {
                            var sw = Stopwatch.StartNew();
                            PingReply r = await pingSender.SendPingAsync(node.IpAddress, 1000);
                            sw.Stop();

                            rtt = sw.ElapsedMilliseconds;
                            isSuccess = r.Status == IPStatus.Success;
                        }
                        catch { }

                        Application.Current?.Dispatcher?.Invoke(() => {
                            node.UpdateStats(isSuccess, rtt);

                            if (_selectedNode == node && isSuccess)
                            {
                                SelectedNodeHistory.Add((double)rtt);
                                if (SelectedNodeHistory.Count > 100) SelectedNodeHistory.RemoveAt(0);
                            }
                        });

                        await Task.Delay(20, token);
                    }
                    await Task.Delay(500, token);
                }
            }
        }

        private void GridMtrNodes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridMtrNodes.SelectedItem is MtrNode selected)
            {
                _selectedNode = selected;
                TxtChartTitle.Text = $"📊 [{selected.IpAddress}] 实时延迟波动图";

                Application.Current.Dispatcher.InvokeAsync(() => {
                    SelectedNodeHistory.Clear();
                });
            }
        }
    }

    public class MtrNode : INotifyPropertyChanged
    {
        public int Hop { get; set; }
        public string IpAddress { get; set; }

        private int _sent = 0;
        public int Sent { get { return _sent; } private set { _sent = value; OnPropertyChanged(nameof(Sent)); } }

        private int _received = 0;

        public string LossPercentStr
        {
            get
            {
                if (Sent == 0) return "0.0 %";
                double loss = (double)(Sent - _received) / Sent * 100;
                return $"{loss:F1} %";
            }
        }

        public double LossValue
        {
            get
            {
                if (Sent == 0) return 0;
                return (double)(Sent - _received) / Sent * 100;
            }
        }

        public string LossColor
        {
            get
            {
                if (Sent == 0) return "Gray";
                double loss = (double)(Sent - _received) / Sent * 100;
                return loss >= 1.0 ? "#EF4444" : "#4ADE80";
            }
        }

        private long _lastPing = 0;
        public string LastPingStr => _lastPing == -1 ? "ERR" : $"{_lastPing} ms";

        private long _bestPing = long.MaxValue;
        public string BestPingStr => _bestPing == long.MaxValue ? "-" : $"{_bestPing} ms";

        private long _worstPing = 0;
        public string WorstPingStr => _worstPing == 0 ? "-" : $"{_worstPing} ms";

        private double _totalPing = 0;
        public string AvgPingStr => _received == 0 ? "-" : $"{(_totalPing / _received):F1} ms";

        public void UpdateStats(bool success, long rtt)
        {
            Sent++;
            if (success)
            {
                _received++;
                _lastPing = rtt;
                if (rtt < _bestPing) _bestPing = rtt;
                if (rtt > _worstPing) _worstPing = rtt;
                _totalPing += rtt;
            }
            else
            {
                _lastPing = -1;
            }

            OnPropertyChanged(nameof(Sent));
            OnPropertyChanged(nameof(LossPercentStr));
            OnPropertyChanged(nameof(LossValue));
            OnPropertyChanged(nameof(LossColor));
            OnPropertyChanged(nameof(LastPingStr));
            OnPropertyChanged(nameof(AvgPingStr));
            OnPropertyChanged(nameof(BestPingStr));
            OnPropertyChanged(nameof(WorstPingStr));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}