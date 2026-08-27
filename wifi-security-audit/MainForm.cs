using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WifiSecurityAudit
{
    internal sealed class MainForm : Form
    {
        private readonly Label scoreLabel = new Label();
        private readonly Label verdictLabel = new Label();
        private readonly Label wifiInfoLabel = new Label();
        private readonly Label gatewayLabel = new Label();
        private readonly Label portLabel = new Label();
        private readonly TextBox passwordBox = new TextBox();
        private readonly Label passwordScoreLabel = new Label();
        private readonly Label passwordAdviceLabel = new Label();
        private readonly DataGridView nearbyGrid = new DataGridView();
        private readonly Button scanButton = new Button();
        private WifiSnapshot current;

        private static readonly Color Bg = Color.FromArgb(14, 17, 22);
        private static readonly Color Card = Color.FromArgb(24, 29, 37);
        private static readonly Color Card2 = Color.FromArgb(31, 37, 47);
        private static readonly Color Text = Color.FromArgb(238, 242, 247);
        private static readonly Color Muted = Color.FromArgb(154, 164, 179);
        private static readonly Color Accent = Color.FromArgb(96, 220, 255);
        private static readonly Color Good = Color.FromArgb(112, 220, 156);
        private static readonly Color Warn = Color.FromArgb(255, 198, 98);
        private static readonly Color Bad = Color.FromArgb(255, 112, 118);

        public MainForm()
        {
            Text = "Wi-Fi 安全体检";
            Width = 980;
            Height = 760;
            MinimumSize = new Size(820, 640);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Text;
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(18),
                BackColor = Bg
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 152));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildCurrentCard(), 0, 1);
            root.Controls.Add(BuildPasswordCard(), 0, 2);
            root.Controls.Add(BuildNearbyCard(), 0, 3);

            var footer = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Muted,
                Text = "只做防御性检查：不抓握手、不爆破、不尝试绕过认证。结果是风险评估，不等于绝对安全或绝对可攻破。"
            };
            root.Controls.Add(footer, 0, 4);

            Shown += async delegate { await RunAuditAsync(); };
        }

        private Control BuildHeader()
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
            var title = new Label
            {
                AutoSize = true,
                Location = new Point(0, 2),
                Font = new Font("Segoe UI", 22F, FontStyle.Bold),
                ForeColor = Text,
                Text = "Wi-Fi 安全体检"
            };
            var sub = new Label
            {
                AutoSize = true,
                Location = new Point(3, 47),
                ForeColor = Muted,
                Text = "检查当前无线加密、路由器暴露面和密码强度 · 所有密码分析仅在本机内存完成"
            };
            scanButton.Text = "重新体检";
            scanButton.Size = new Size(112, 38);
            scanButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            scanButton.Location = new Point(800, 18);
            scanButton.FlatStyle = FlatStyle.Flat;
            scanButton.FlatAppearance.BorderColor = Accent;
            scanButton.BackColor = Card2;
            scanButton.ForeColor = Accent;
            scanButton.Click += async delegate { await RunAuditAsync(); };
            p.Resize += delegate { scanButton.Left = Math.Max(0, p.ClientSize.Width - scanButton.Width); };
            p.Controls.Add(title);
            p.Controls.Add(sub);
            p.Controls.Add(scanButton);
            return p;
        }

        private Control BuildCurrentCard()
        {
            var card = CardPanel();
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(15),
                BackColor = Card
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            card.Controls.Add(layout);

            var scorePanel = new Panel { Dock = DockStyle.Fill };
            scoreLabel.Dock = DockStyle.Top;
            scoreLabel.Height = 64;
            scoreLabel.Font = new Font("Segoe UI", 31F, FontStyle.Bold);
            scoreLabel.ForeColor = Accent;
            scoreLabel.TextAlign = ContentAlignment.MiddleCenter;
            scoreLabel.Text = "--";
            verdictLabel.Dock = DockStyle.Fill;
            verdictLabel.TextAlign = ContentAlignment.TopCenter;
            verdictLabel.ForeColor = Muted;
            verdictLabel.Text = "等待检测";
            scorePanel.Controls.Add(verdictLabel);
            scorePanel.Controls.Add(scoreLabel);
            layout.Controls.Add(scorePanel, 0, 0);

            wifiInfoLabel.Dock = DockStyle.Fill;
            wifiInfoLabel.ForeColor = Text;
            wifiInfoLabel.Font = new Font("Segoe UI", 9.5F);
            wifiInfoLabel.Text = "正在读取当前 Wi-Fi…";
            layout.Controls.Add(wifiInfoLabel, 1, 0);

            var right = new Panel { Dock = DockStyle.Fill };
            gatewayLabel.Dock = DockStyle.Top;
            gatewayLabel.Height = 45;
            gatewayLabel.ForeColor = Text;
            gatewayLabel.Text = "网关：检测中…";
            portLabel.Dock = DockStyle.Fill;
            portLabel.ForeColor = Muted;
            portLabel.Text = "管理端口：检测中…";
            right.Controls.Add(portLabel);
            right.Controls.Add(gatewayLabel);
            layout.Controls.Add(right, 2, 0);
            return card;
        }

        private Control BuildPasswordCard()
        {
            var card = CardPanel();
            var title = new Label
            {
                AutoSize = true,
                Location = new Point(16, 13),
                ForeColor = Text,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                Text = "你的 Wi-Fi 密码强度（可选）"
            };
            var note = new Label
            {
                AutoSize = true,
                Location = new Point(16, 39),
                ForeColor = Muted,
                Text = "输入你自己网络的密码做本地评估；本程序不会保存、上传或显示原文。"
            };
            passwordBox.Location = new Point(19, 68);
            passwordBox.Width = 360;
            passwordBox.UseSystemPasswordChar = true;
            passwordBox.BackColor = Color.FromArgb(17, 21, 27);
            passwordBox.ForeColor = Text;
            passwordBox.BorderStyle = BorderStyle.FixedSingle;
            passwordBox.TextChanged += delegate { UpdatePasswordScore(); };

            passwordScoreLabel.Location = new Point(400, 65);
            passwordScoreLabel.Size = new Size(160, 28);
            passwordScoreLabel.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            passwordScoreLabel.ForeColor = Muted;
            passwordScoreLabel.Text = "未输入";

            passwordAdviceLabel.Location = new Point(19, 103);
            passwordAdviceLabel.Size = new Size(880, 40);
            passwordAdviceLabel.ForeColor = Muted;
            passwordAdviceLabel.Text = "建议：至少 16 位，避免手机号、生日、连续数字、重复字符和常见口令。";

            card.Controls.Add(title);
            card.Controls.Add(note);
            card.Controls.Add(passwordBox);
            card.Controls.Add(passwordScoreLabel);
            card.Controls.Add(passwordAdviceLabel);
            card.Resize += delegate
            {
                passwordBox.Width = Math.Min(420, Math.Max(260, card.ClientSize.Width / 2 - 70));
                passwordScoreLabel.Left = passwordBox.Right + 20;
                passwordAdviceLabel.Width = Math.Max(100, card.ClientSize.Width - 38);
            };
            return card;
        }

        private Control BuildNearbyCard()
        {
            var card = CardPanel();
            var title = new Label
            {
                Dock = DockStyle.Top,
                Height = 35,
                Padding = new Padding(14, 8, 0, 0),
                ForeColor = Text,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                Text = "附近可见 Wi-Fi（只读取公开广播信息，不尝试连接）"
            };
            nearbyGrid.Dock = DockStyle.Fill;
            nearbyGrid.BackgroundColor = Card;
            nearbyGrid.BorderStyle = BorderStyle.None;
            nearbyGrid.EnableHeadersVisualStyles = false;
            nearbyGrid.ColumnHeadersDefaultCellStyle.BackColor = Card2;
            nearbyGrid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
            nearbyGrid.DefaultCellStyle.BackColor = Card;
            nearbyGrid.DefaultCellStyle.ForeColor = Text;
            nearbyGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(45, 72, 88);
            nearbyGrid.DefaultCellStyle.SelectionForeColor = Text;
            nearbyGrid.RowHeadersVisible = false;
            nearbyGrid.ReadOnly = true;
            nearbyGrid.AllowUserToAddRows = false;
            nearbyGrid.AllowUserToDeleteRows = false;
            nearbyGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            nearbyGrid.Columns.Add("ssid", "SSID");
            nearbyGrid.Columns.Add("auth", "认证");
            nearbyGrid.Columns.Add("cipher", "加密");
            nearbyGrid.Columns.Add("signal", "信号");
            nearbyGrid.Columns.Add("risk", "风险提示");
            card.Controls.Add(nearbyGrid);
            card.Controls.Add(title);
            return card;
        }

        private Panel CardPanel()
        {
            return new Panel { Dock = DockStyle.Fill, BackColor = Card, Margin = new Padding(0, 4, 0, 8) };
        }

        private async Task RunAuditAsync()
        {
            scanButton.Enabled = false;
            scoreLabel.Text = "…";
            verdictLabel.Text = "检测中";
            wifiInfoLabel.Text = "正在读取当前 Wi-Fi…";
            gatewayLabel.Text = "网关：检测中…";
            portLabel.Text = "管理端口：检测中…";
            nearbyGrid.Rows.Clear();

            try
            {
                current = await Task.Run(() => WifiProbe.GetCurrent());
                var gateway = NetworkProbe.GetDefaultGateway();
                var portsTask = string.IsNullOrWhiteSpace(gateway)
                    ? Task.FromResult(new List<int>())
                    : NetworkProbe.CheckPortsAsync(gateway, new[] { 80, 443, 8080, 8443, 22, 23 }, 350);
                var nearbyTask = Task.Run(() => WifiProbe.GetNearby());

                RenderCurrent(current);
                gatewayLabel.Text = string.IsNullOrWhiteSpace(gateway) ? "网关：未检测到" : "网关：" + gateway;

                var openPorts = await portsTask;
                RenderPorts(openPorts);

                var nearby = await nearbyTask;
                foreach (var n in nearby)
                    nearbyGrid.Rows.Add(n.Ssid, n.Authentication, n.Cipher, n.Signal, RiskText(n.Authentication, n.Cipher));
            }
            catch (Exception ex)
            {
                scoreLabel.Text = "--";
                scoreLabel.ForeColor = Bad;
                verdictLabel.Text = "检测失败";
                wifiInfoLabel.Text = ex.Message;
            }
            finally
            {
                scanButton.Enabled = true;
            }
        }

        private void RenderCurrent(WifiSnapshot w)
        {
            if (w == null || string.IsNullOrWhiteSpace(w.Ssid))
            {
                scoreLabel.Text = "--";
                verdictLabel.Text = "未连接 Wi-Fi";
                wifiInfoLabel.Text = "当前没有检测到已连接的无线网络。请先连接你要体检的家庭 Wi-Fi。";
                return;
            }

            int score = SecurityScore(w.Authentication, w.Cipher);
            scoreLabel.Text = score.ToString();
            scoreLabel.ForeColor = score >= 85 ? Good : score >= 65 ? Warn : Bad;
            verdictLabel.Text = score >= 85 ? "加密配置较强" : score >= 65 ? "存在改进空间" : "高风险配置";
            wifiInfoLabel.Text =
                "SSID：" + w.Ssid + "\r\n" +
                "认证：" + Empty(w.Authentication) + "    加密：" + Empty(w.Cipher) + "\r\n" +
                "信号：" + Empty(w.Signal) + "    频道：" + Empty(w.Channel) + "\r\n" +
                "无线类型：" + Empty(w.RadioType) + "\r\n" +
                "提示：" + RiskText(w.Authentication, w.Cipher);
        }

        private void RenderPorts(List<int> ports)
        {
            if (ports == null || ports.Count == 0)
            {
                portLabel.ForeColor = Good;
                portLabel.Text = "常见管理端口：未发现响应\r\nWPS：Windows 无法可靠读取，请在路由器后台确认已关闭。";
                return;
            }

            portLabel.ForeColor = ports.Contains(23) ? Bad : Warn;
            string detail = "开放端口：" + string.Join(", ", ports);
            if (ports.Contains(23)) detail += "\r\n⚠ Telnet(23) 开放，建议立即关闭。";
            else if (ports.Contains(22)) detail += "\r\nSSH(22) 开放；若非你主动启用，建议关闭。";
            else detail += "\r\n80/443/8080/8443 可能只是路由器管理页面，不等于漏洞。";
            portLabel.Text = detail;
        }

        private void UpdatePasswordScore()
        {
            string p = passwordBox.Text ?? string.Empty;
            if (p.Length == 0)
            {
                passwordScoreLabel.Text = "未输入";
                passwordScoreLabel.ForeColor = Muted;
                passwordAdviceLabel.Text = "建议：至少 16 位，避免手机号、生日、连续数字、重复字符和常见口令。";
                return;
            }

            PasswordAssessment a = PasswordEvaluator.Evaluate(p);
            passwordScoreLabel.Text = a.Score + "/100 · " + a.Level;
            passwordScoreLabel.ForeColor = a.Score >= 80 ? Good : a.Score >= 60 ? Warn : Bad;
            passwordAdviceLabel.Text = a.Advice;
        }

        private static int SecurityScore(string auth, string cipher)
        {
            string a = (auth ?? "").ToLowerInvariant();
            string c = (cipher ?? "").ToLowerInvariant();
            int score;
            if (a.Contains("open") || a.Contains("开放") || a.Contains("无")) score = 5;
            else if (a.Contains("wep")) score = 10;
            else if (a.Contains("wpa3")) score = 96;
            else if (a.Contains("wpa2")) score = 82;
            else if (a.Contains("wpa")) score = 45;
            else score = 55;

            if (c.Contains("tkip")) score -= 25;
            if (c.Contains("wep")) score = Math.Min(score, 10);
            return Math.Max(0, Math.Min(100, score));
        }

        private static string RiskText(string auth, string cipher)
        {
            int s = SecurityScore(auth, cipher);
            string a = (auth ?? "").ToLowerInvariant();
            string c = (cipher ?? "").ToLowerInvariant();
            if (a.Contains("open") || a.Contains("开放")) return "严重：开放网络，没有 Wi-Fi 访问密码保护。";
            if (a.Contains("wep")) return "严重：WEP 已过时，应立即改为 WPA2-AES 或 WPA3。";
            if (c.Contains("tkip")) return "高风险：TKIP 已过时，建议使用 AES/CCMP。";
            if (a.Contains("wpa3")) return "较强：WPA3 配置优先；仍建议关闭 WPS 并使用长密码。";
            if (a.Contains("wpa2")) return "正常：WPA2-AES 仍可用；长随机密码和关闭 WPS 很重要。";
            return s >= 65 ? "中等风险：建议检查路由器安全模式和 WPS。" : "风险较高：建议升级无线安全配置。";
        }

        private static string Empty(string s) { return string.IsNullOrWhiteSpace(s) ? "未知" : s; }
    }

    internal sealed class WifiSnapshot
    {
        public string Ssid;
        public string Bssid;
        public string Authentication;
        public string Cipher;
        public string Signal;
        public string Channel;
        public string RadioType;
    }

    internal static class WifiProbe
    {
        public static WifiSnapshot GetCurrent()
        {
            string text = RunNetsh("wlan show interfaces");
            var w = new WifiSnapshot();
            foreach (string raw in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string line = raw.Trim();
                int idx = line.IndexOf(':');
                if (idx <= 0) continue;
                string key = line.Substring(0, idx).Trim().ToLowerInvariant();
                string value = line.Substring(idx + 1).Trim();

                if (key == "bssid") w.Bssid = value;
                else if (key == "ssid") w.Ssid = value;
                else if (key.Contains("authentication") || key.Contains("身份验证")) w.Authentication = value;
                else if (key == "cipher" || key.Contains("密码")) w.Cipher = value;
                else if (key.Contains("signal") || key.Contains("信号")) w.Signal = value;
                else if (key.Contains("channel") || key.Contains("频道")) w.Channel = value;
                else if (key.Contains("radio type") || key.Contains("无线电类型")) w.RadioType = value;
            }
            return w;
        }

        public static List<WifiSnapshot> GetNearby()
        {
            string text = RunNetsh("wlan show networks mode=bssid");
            var list = new List<WifiSnapshot>();
            WifiSnapshot current = null;

            foreach (string raw in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string line = raw.Trim();
                if (Regex.IsMatch(line, @"^SSID\s+\d+\s*:", RegexOptions.IgnoreCase))
                {
                    int idx = line.IndexOf(':');
                    current = new WifiSnapshot { Ssid = idx >= 0 ? line.Substring(idx + 1).Trim() : "" };
                    list.Add(current);
                    continue;
                }
                if (current == null) continue;
                int c = line.IndexOf(':');
                if (c <= 0) continue;
                string key = line.Substring(0, c).Trim().ToLowerInvariant();
                string value = line.Substring(c + 1).Trim();
                if (key.Contains("authentication") || key.Contains("身份验证")) current.Authentication = value;
                else if (key == "cipher" || key.Contains("密码")) current.Cipher = value;
                else if ((key.Contains("signal") || key.Contains("信号")) && string.IsNullOrWhiteSpace(current.Signal)) current.Signal = value;
            }

            return list.Where(x => !string.IsNullOrWhiteSpace(x.Ssid)).GroupBy(x => x.Ssid).Select(g => g.First()).Take(60).ToList();
        }

        private static string RunNetsh(string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit(5000);
                if (string.IsNullOrWhiteSpace(output) && !string.IsNullOrWhiteSpace(error)) throw new InvalidOperationException(error.Trim());
                return output ?? string.Empty;
            }
        }
    }

    internal static class NetworkProbe
    {
        public static string GetDefaultGateway()
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                try
                {
                    foreach (GatewayIPAddressInformation g in ni.GetIPProperties().GatewayAddresses)
                    {
                        string ip = g.Address == null ? "" : g.Address.ToString();
                        if (!string.IsNullOrWhiteSpace(ip) && ip.Contains(".")) return ip;
                    }
                }
                catch { }
            }
            return string.Empty;
        }

        public static async Task<List<int>> CheckPortsAsync(string host, IEnumerable<int> ports, int timeoutMs)
        {
            var open = new List<int>();
            foreach (int port in ports)
            {
                using (var client = new TcpClient())
                {
                    try
                    {
                        Task connect = client.ConnectAsync(host, port);
                        Task done = await Task.WhenAny(connect, Task.Delay(timeoutMs));
                        if (done == connect && client.Connected) open.Add(port);
                    }
                    catch { }
                }
            }
            return open;
        }
    }

    internal sealed class PasswordAssessment
    {
        public int Score;
        public string Level;
        public string Advice;
    }

    internal static class PasswordEvaluator
    {
        public static PasswordAssessment Evaluate(string p)
        {
            int score = 0;
            var tips = new List<string>();

            if (p.Length >= 20) score += 48;
            else if (p.Length >= 16) score += 40;
            else if (p.Length >= 12) score += 28;
            else if (p.Length >= 8) score += 16;
            else { score += 5; tips.Add("长度太短"); }

            if (p.Any(char.IsLower)) score += 10; else tips.Add("加入小写字母");
            if (p.Any(char.IsUpper)) score += 10; else tips.Add("加入大写字母");
            if (p.Any(char.IsDigit)) score += 10; else tips.Add("加入数字");
            if (p.Any(ch => !char.IsLetterOrDigit(ch))) score += 12; else tips.Add("加入符号");

            string lower = p.ToLowerInvariant();
            string[] weak = { "123456", "12345678", "password", "qwerty", "abcdef", "admin", "wifi", "888888", "666666", "111111" };
            if (weak.Any(lower.Contains)) { score -= 35; tips.Add("包含常见弱口令片段"); }
            if (Regex.IsMatch(lower, @"(.)\1{3,}")) { score -= 18; tips.Add("避免大量重复字符"); }
            if (Regex.IsMatch(lower, @"(?:0123|1234|2345|3456|4567|5678|6789|abcd|qwer)")) { score -= 18; tips.Add("避免连续序列"); }
            if (Regex.IsMatch(lower, @"(?:19|20)\d{2}")) { score -= 8; tips.Add("避免明显年份/生日信息"); }

            score = Math.Max(0, Math.Min(100, score));
            string level = score >= 85 ? "很强" : score >= 70 ? "较强" : score >= 50 ? "一般" : "偏弱";
            string advice = tips.Count == 0
                ? "这组密码结构不错。若路由器支持，配合 WPA3、关闭 WPS，并让路由器管理密码与 Wi-Fi 密码不同。"
                : "建议：" + string.Join("；", tips.Distinct()) + "。优先使用 16 位以上的随机密码或长短语。";

            return new PasswordAssessment { Score = score, Level = level, Advice = advice };
        }
    }
}
