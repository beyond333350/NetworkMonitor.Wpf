using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace NetworkMonitor.Wpf
{
    public partial class TraceView : UserControl
    {
        private bool _isTracing = false;

        public TraceView()
        {
            InitializeComponent();
            LoadIpList(); // 页面初始化时加载 ip.txt
        }

        // ==========================================
        // 读取 ip.txt 填充到下拉框
        // ==========================================
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
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            list.Add(line.Trim());
                        }
                    }

                    if (list.Count > 0)
                    {
                        CmbTraceIp.ItemsSource = list;
                        CmbTraceIp.SelectedIndex = 0;
                        return;
                    }
                }

                // 兜底数据
                CmbTraceIp.ItemsSource = new List<string> { "www.baidu.com", "8.8.8.8", "114.114.114.114" };
                CmbTraceIp.SelectedIndex = 0;
            }
            catch { }
        }

        // ==========================================
        // 核心：异步执行 Tracert 并实时截获输出
        // ==========================================
        private async void BtnStartTrace_Click(object sender, RoutedEventArgs e)
        {
            if (_isTracing) return;
            _isTracing = true;

            string target = string.IsNullOrWhiteSpace(CmbTraceIp.Text) ? "127.0.0.1" : CmbTraceIp.Text.Trim();

            // 路由跟踪不需要端口，截断处理
            if (target.Contains(":")) target = target.Split(':')[0];

            BtnStartTrace.Content = "跟踪中...";
            BtnStartTrace.IsEnabled = false;

            // 清空旧日志，写入新头
            TxtTraceOutput.Text = $"[{DateTime.Now:HH:mm:ss}] 正在跟踪到达目标 {target} 的路由...\n(注：使用极速模式，跳过DNS解析)\n\n";

            await Task.Run(() =>
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "tracert.exe",
                        Arguments = $"-d {target}", // -d 强制不解析主机名，加速打印
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (Process process = new Process { StartInfo = psi })
                    {
                        process.OutputDataReceived += (s, ev) =>
                        {
                            if (!string.IsNullOrWhiteSpace(ev.Data))
                                Dispatcher.Invoke(() => AppendLog(ev.Data));
                        };

                        process.ErrorDataReceived += (s, ev) =>
                        {
                            if (!string.IsNullOrWhiteSpace(ev.Data))
                                Dispatcher.Invoke(() => AppendLog($"[报错] {ev.Data}"));
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => AppendLog($"[系统错误] 启动 tracert 失败。报错信息: {ex.Message}"));
                }
            });

            _isTracing = false;
            BtnStartTrace.Content = "开始跟踪";
            BtnStartTrace.IsEnabled = true;
            AppendLog("\n<<< 路由跟踪完成。");
        }

        // ==========================================
        // 辅助方法：自动追加并滚动到底部
        // ==========================================
        private void AppendLog(string message)
        {
            TxtTraceOutput.AppendText($"{message}\n");
            TxtTraceOutput.ScrollToEnd();
        }
    }
}