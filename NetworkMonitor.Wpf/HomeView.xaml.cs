using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace NetworkMonitor.Wpf
{
    public partial class HomeView : UserControl
    {
        private bool _isRunning = false;
        private Random _randomPortGenerator = new Random();

        public HomeView()
        {
            InitializeComponent();
            LoadDefaultServer();
            LoadIpList();
        }

        private void LoadDefaultServer() => BindTxtToCombo("server.txt", CmbServerAddr);

        private void LoadIpList()
        {
            BindTxtToCombo("ip.txt", CmbIpList);
        }

        private void BindTxtToCombo(string fileName, ComboBox combo)
        {
            try
            {
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                if (File.Exists(filePath))
                {
                    string[] lines = File.ReadAllLines(filePath);
                    List<string> list = new List<string>();
                    foreach (var line in lines) if (!string.IsNullOrWhiteSpace(line)) list.Add(line.Trim());
                    if (list.Count > 0)
                    {
                        combo.ItemsSource = list;
                        combo.SelectedIndex = 0;
                    }
                }
            }
            catch { }
        }

        private async void BtnSysInfo_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;
            _isRunning = true;
            SetButtonsState(false);
            AppendLog("\n>>> 正在探测本机系统与硬件信息...");

            string info = await Task.Run(() =>
            {
                try
                {
                    string pcName = Environment.MachineName;
                    string osRegPath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
                    string osName = GetRegValue(osRegPath, "ProductName", "未知系统");
                    string osBuild = GetRegValue(osRegPath, "CurrentBuild", "0");
                    string osUbr = GetRegValue(osRegPath, "UBR", "0");
                    string osDisplay = GetRegValue(osRegPath, "DisplayVersion", "未知版本");

                    if (int.TryParse(osBuild, out int buildNum) && buildNum >= 22000 && osName.Contains("10"))
                        osName = osName.Replace("10", "11");

                    string fullVersion = $"{osBuild}.{osUbr}";
                    string installDateStr = GetRegValue(osRegPath, "InstallDate", "0");
                    string installDate = "未知";
                    if (long.TryParse(installDateStr, out long unixSeconds))
                    {
                        DateTime dt = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixSeconds).ToLocalTime();
                        installDate = dt.ToString("yyyy-MM-dd");
                    }

                    string cpuRegPath = @"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0";
                    string cpuModel = GetRegValue(cpuRegPath, "ProcessorNameString", "未知 CPU");

                    string serialNumber = RunCmd("wmic bios get serialnumber");
                    if (string.IsNullOrWhiteSpace(serialNumber) || serialNumber.Contains("O.E.M.") || serialNumber == "未知")
                        serialNumber = GetRegValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardSerial", "无");

                    string memoryStr = RunCmd("wmic computersystem get totalphysicalmemory");
                    string memory = "未知";
                    if (long.TryParse(memoryStr, out long memBytes))
                        memory = (memBytes / (1024.0 * 1024 * 1024)).ToString("F2") + " GB";

                    string diskInfo = "";
                    foreach (var drive in DriveInfo.GetDrives())
                        if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                            diskInfo += $"{drive.Name} ({(drive.TotalSize / 1073741824.0):F1} GB)  ";

                    string edgeVer = GetRegValue(@"HKEY_CURRENT_USER\Software\Microsoft\Edge\BLBeacon", "version", "未知");
                    string chromeVer = GetRegValue(@"HKEY_CURRENT_USER\Software\Google\Chrome\BLBeacon", "version", "未知");

                    return $@"----------------------------------------
[计算机名称] {pcName}
[设备序列号] {serialNumber}
[操作系统]   {osName} (版本: {osDisplay} | Build: {fullVersion})
[安装日期]   {installDate}
[处理器 CPU] {cpuModel}
[物理内存]   {memory}
[本地磁盘]   {diskInfo}
[Edge版本]   {edgeVer}
[Chrome版本] {chromeVer}
----------------------------------------";
                }
                catch (Exception ex) { return $"[探测失败] {ex.Message}"; }
            });

            AppendLog(info);
            _isRunning = false;
            SetButtonsState(true);
        }

        private string GetRegValue(string keyPath, string valueName, string defaultValue)
        {
            try { var val = Registry.GetValue(keyPath, valueName, null); return val != null ? val.ToString() : defaultValue; }
            catch { return defaultValue; }
        }

        private string RunCmd(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c " + args) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using (var process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int i = 1; i < lines.Length; i++) if (!string.IsNullOrWhiteSpace(lines[i].Trim())) return lines[i].Trim();
                    return "";
                }
            }
            catch { return ""; }
        }

        private string GetPingTarget() => string.IsNullOrWhiteSpace(CmbIpList.Text) ? "127.0.0.1" : CmbIpList.Text.Trim();

        private async void BtnPing_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;
            _isRunning = true;
            SetButtonsState(false);
            string target = GetPingTarget();
            AppendLog($"\n>>> 开始 PING: {target} ...");

            await Task.Run(() =>
            {
                try
                {
                    using (Ping ping = new Ping())
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            PingReply reply = ping.Send(target, 2000);
                            string result = reply.Status == IPStatus.Success ? $"来自 {reply.Address} 的回复: 字节=32 时间={reply.RoundtripTime}ms" : $"请求超时 ({reply.Status})";
                            Dispatcher.Invoke(() => AppendLog(result));
                            System.Threading.Thread.Sleep(500);
                        }
                    }
                }
                catch (Exception ex) { Dispatcher.Invoke(() => AppendLog($"[Ping 错误] {ex.Message}")); }
            });

            _isRunning = false;
            SetButtonsState(true);
        }

        private async void BtnTcping_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;
            _isRunning = true;
            SetButtonsState(false);
            string target = GetPingTarget();
            int port = 80;
            if (target.Contains(":")) { var parts = target.Split(':'); target = parts[0]; int.TryParse(parts[1], out port); }
            AppendLog($"\n>>> 开始 TCPING: {target}:{port} ...");

            await Task.Run(() =>
            {
                for (int i = 0; i < 4; i++)
                {
                    var sw = Stopwatch.StartNew();
                    bool success = false;
                    try
                    {
                        using (TcpClient client = new TcpClient())
                        {
                            var ar = client.BeginConnect(target, port, null, null);
                            success = ar.AsyncWaitHandle.WaitOne(1000);
                            if (success) client.EndConnect(ar);
                        }
                    }
                    catch { success = false; }
                    sw.Stop();
                    string result = success ? $"连接到 {target}:{port} 成功, 耗时={sw.ElapsedMilliseconds}ms" : $"连接到 {target}:{port} 失败";
                    Dispatcher.Invoke(() => AppendLog(result));
                    System.Threading.Thread.Sleep(500);
                }
            });

            _isRunning = false;
            SetButtonsState(true);
        }

        private string GetTargetIp() => string.IsNullOrWhiteSpace(CmbServerAddr.Text) ? "127.0.0.1" : CmbServerAddr.Text.Trim();

        private async void BtnUploadTest_Click(object sender, RoutedEventArgs e) => await RunIperfTestAsync(false, false);
        private async void BtnDownloadTest_Click(object sender, RoutedEventArgs e) => await RunIperfTestAsync(true, false);
        private async void BtnUploadLoss_Click(object sender, RoutedEventArgs e) => await RunIperfTestAsync(false, true);
        private async void BtnDownloadLoss_Click(object sender, RoutedEventArgs e) => await RunIperfTestAsync(true, true);

        private async Task RunIperfTestAsync(bool isDownload, bool isUdpLossTest)
        {
            _isRunning = true;
            SetButtonsState(false);
            string ip = GetTargetIp();
            int randomPort = _randomPortGenerator.Next(9200, 9241);
            string targetBandwidth = TxtTargetBandwidth.Text.Trim().ToUpper().Replace("M", "");
            if (!int.TryParse(targetBandwidth, out int bw)) bw = 50;

            string testType = (isDownload ? "下载" : "上传") + (isUdpLossTest ? "丢包" : "带宽");
            string args = $"-c {ip} -p {randomPort} -t 10 -f m --forceflush";
            if (isUdpLossTest) args += $" -u -b {bw}M";
            if (isDownload) args += " -R";

            AppendLog($"\n>>> {testType}测试: iperf3 {args}");

            try
            {
                await Task.Run(() =>
                {
                    ProcessStartInfo psi = new ProcessStartInfo { FileName = "iperf3.exe", Arguments = args, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                    using (Process process = new Process { StartInfo = psi })
                    {
                        process.OutputDataReceived += (s, ev) => { if (!string.IsNullOrWhiteSpace(ev.Data)) Dispatcher.Invoke(() => AppendLog(ev.Data)); };
                        process.ErrorDataReceived += (s, ev) => { if (!string.IsNullOrWhiteSpace(ev.Data)) Dispatcher.Invoke(() => AppendLog($"[报错] {ev.Data}")); };
                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();
                    }
                });
            }
            catch (Exception ex) { AppendLog($"[错误] {ex.Message}"); }
            finally { _isRunning = false; SetButtonsState(true); }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;
            TxtLogOutput.Text = "";
            AppendLog(">>> 日志已清空...");
        }

        private void SetButtonsState(bool state)
        {
            BtnUploadTest.IsEnabled = state; BtnDownloadTest.IsEnabled = state;
            BtnUploadLoss.IsEnabled = state; BtnDownloadLoss.IsEnabled = state;
            BtnPing.IsEnabled = state; BtnTcping.IsEnabled = state;
            BtnSysInfo.IsEnabled = state; BtnClearLog.IsEnabled = state;
        }

        private void BtnSpeedTest_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://www.speedtest.cn") { UseShellExecute = true });
        private void BtnUstcTest_Click(object sender, RoutedEventArgs e) => Process.Start(new ProcessStartInfo("https://test.ustc.edu.cn") { UseShellExecute = true });

        private void AppendLog(string message) { TxtLogOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n"); TxtLogOutput.ScrollToEnd(); }
    }
}