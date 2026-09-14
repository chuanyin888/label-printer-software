using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LabelPrinterApp
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test")
            {
                SelfTest.Run();
                Environment.Exit(0);
            }
            if (args.Length > 1 && args[0] == "--render-test")
            {
                RenderTest.Run(args[1]);
                Environment.Exit(0);
            }
            if (args.Length > 1 && args[0] == "--layout-test")
            {
                LayoutTest.Run(args[1]);
                Environment.Exit(0);
            }
            if (args.Length > 1 && args[0] == "--snapshot")
            {
                Snapshot.Run(args[1]);
                Environment.Exit(0);
            }
            try { SetProcessDPIAware(); } catch { }
            // 启用 TLS 1.2：GitHub、Gitee 等 HTTPS 接口在新版本 .NET 上要求 TLS1.2，旧运行时默认只走 1.0/1.1 会握手失败
            try { System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072; } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => ErrorLog.Show(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                if (ex != null) ErrorLog.Log(ex);
            };
            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                ErrorLog.Show(ex);
            }
        }
    }

    internal static class ErrorLog
    {
        public static string Log(Exception ex)
        {
            if (ex == null) return "";
            string body = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" +
                          ex.GetType().FullName + ": " + ex.Message + "\r\n" + ex.StackTrace;
            string path = "";
            try
            {
                path = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "错误日志.txt");
                File.WriteAllText(path, body, new UTF8Encoding(true));
            }
            catch
            {
                try
                {
                    path = Path.Combine(Path.GetTempPath(), "设备标签打印错误日志.txt");
                    File.WriteAllText(path, body, new UTF8Encoding(true));
                }
                catch { }
            }
            return path;
        }

        public static void Show(Exception ex)
        {
            string path = Log(ex);
            try
            {
                MessageBox.Show("程序遇到错误：" + ex.Message + "\r\n\r\n错误详情已保存到：\r\n" + path +
                                "\r\n\r\n如果反复出现，请把上面的错误信息发给我。", "程序错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }

    // -------------------------------------------------------------------------------------------
    // 设计层：套用 design-taste-frontend 可迁移方法论（统一配色 / 圆角 / 字体，单一生机色强调，
    // 消除“AI 默认味”，保证按钮对比度）。仅作用于窗口 UI，不影响标签打印内容。
    // -------------------------------------------------------------------------------------------
    internal static class Ui
    {
        // 中性底（Slate 系）
        public static Color Back      = Color.FromArgb(241, 243, 245); // 窗口底
        public static Color Card      = Color.FromArgb(255, 255, 255); // 卡片底
        public static Color Border    = Color.FromArgb(214, 221, 227); // 边框
        public static Color Text      = Color.FromArgb(31, 42, 55);    // 主文字
        public static Color TextMuted = Color.FromArgb(95, 107, 122);  // 次要文字
        public static Color TextFaint = Color.FromArgb(148, 163, 179); // 最浅文字
        public static Color RowAlt    = Color.FromArgb(248, 250, 251); // 表格隔行
        public static Color GridHdr   = Color.FromArgb(229, 244, 241); // 表头(浅青)
        public static Color AccentSoft= Color.FromArgb(231, 244, 241); // 选中(浅青)

        // 单项强调色（Teal，全界面唯一强调，不做紫/蓝渐变）
        public static Color Accent      = Color.FromArgb(15, 118, 110);  // teal-700
        public static Color AccentHover = Color.FromArgb(13, 148, 136);  // teal-600
        public static Color AccentDown  = Color.FromArgb(17, 94, 89);    // teal-800

        // 语义状态色（仅用于状态提示，不参与强调）
        public static Color Success    = Color.FromArgb(21, 128, 61);
        public static Color Warning    = Color.FromArgb(180, 83, 9);
        public static Color Danger     = Color.FromArgb(185, 28, 28);
        public static Color DangerHover= Color.FromArgb(254, 242, 242);

        // 中性按钮
        public static Color SecondaryHover = Color.FromArgb(248, 250, 251);
        public static Color SecondaryDown  = Color.FromArgb(238, 242, 244);

        // 字体
        public static Font BaseFont      = new Font("Microsoft YaHei UI", 9F);
        public static Font HeadingFont   = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
        public static Font StatusFont    = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
        public static Font GroupTitleFont= new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);

        // 把旧代码里的任意颜色映射到统一配色
        public static Color Resolve(Color c)
        {
            if (c == Color.Gray || c == Color.DimGray) return TextMuted;
            if (c == Color.DodgerBlue) return Accent;
            if (c == Color.SeaGreen) return Success;
            if (c == Color.DarkOrange) return Warning;
            if (c == Color.Red) return Danger;
            if (c == Color.Black || c == SystemColors.ControlText || c == SystemColors.Control) return Text;
            return c;
        }
    }

    internal enum ButtonKind { Primary, Secondary, Danger }

    // 统一圆角的平面按钮，带 hover/按下反馈，保证文字对比度
    internal class RoundedButton : Button
    {
        public ButtonKind Kind = ButtonKind.Secondary;
        private bool _hover, _pressed;

        public RoundedButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            MouseLeave += (s, e) => { _hover = false; _pressed = false; Invalidate(); };
            MouseDown  += (s, e) => { _pressed = true; Invalidate(); };
            MouseUp    += (s, e) => { _pressed = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            var path = RoundRect(rect, 6);

            Brush bg = null; Pen border = null; Color textColor;
            if (Kind == ButtonKind.Primary)
            {
                bg = new SolidBrush(_pressed ? Ui.AccentDown : (_hover ? Ui.AccentHover : Ui.Accent));
                textColor = Color.White;
            }
            else if (Kind == ButtonKind.Danger)
            {
                bg = new SolidBrush(_pressed ? Ui.DangerHover : (_hover ? Ui.DangerHover : Color.White));
                border = new Pen(Ui.Danger, 1f);
                textColor = Ui.Danger;
            }
            else
            {
                bg = new SolidBrush(_pressed ? Ui.SecondaryDown : (_hover ? Ui.SecondaryHover : Color.White));
                border = new Pen(Ui.Border, 1f);
                textColor = Ui.Text;
            }

            if (!Enabled) { bg = new SolidBrush(Color.FromArgb(232, 236, 239)); textColor = Ui.TextFaint; }
            using (bg)
            {
                g.FillPath(bg, path);
                if (border != null) using (border) g.DrawPath(border, path);
            }
            TextRenderer.DrawText(g, Text, Font, rect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // 现代“卡片式”分组框：白底圆角边框，标题用强调色加粗
    internal class CardGroup : GroupBox
    {
        public CardGroup()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Ui.Card;
            ForeColor = Ui.Text;
            Padding = new Padding(8);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            var path = RoundRect(rect, 8);
            using (var b = new SolidBrush(Ui.Card)) g.FillPath(b, path);
            using (var p = new Pen(Ui.Border, 1f)) g.DrawPath(p, path);
            var tr = new Rectangle(12, 6, Math.Max(0, Width - 30), 16);
            TextRenderer.DrawText(g, Text, Ui.GroupTitleFont, tr, Ui.Accent,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    internal static class SelfTest
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            bool ok = true;

            // 1. QR parse test using the real sample
            const string qr = "http://op.smartont.net/app/download?ssid1=CU_gZUY&password=tp45tths&username=user&pwd=tp45tths&model=GPON&type=ZXHN F677V2&sn=ZTEGCB8E4525&serialnumber=4413D0-01FFFFFFFF011FFF23ZTEGCB8E4525F6&ip=192.168.1.1";
            var p = QRParser.Parse(qr);
            bool p1 = p != null && p.Model == "GPON";
            bool p2 = p != null && p.Type == "ZXHN F677V2";
            bool p3 = p != null && p.SN == "ZTEGCB8E4525";
            sb.AppendLine("parse model=GPON: " + p1);
            sb.AppendLine("parse type=ZXHN F677V2: " + p2);
            sb.AppendLine("parse sn=ZTEGCB8E4525: " + p3);
            ok = ok && p1 && p2 && p3;

            // 2. MAC normalization
            bool m1 = QRParser.NormalizeMac("4413D003B768") == "4413D003B768";
            bool m2 = QRParser.NormalizeMac("44:13:d0:03:b7:68") == "4413D003B768";
            bool m3 = QRParser.NormalizeMac("44-13-D0-03-B7-68") == "4413D003B768";
            sb.AppendLine("mac normalize 12: " + m1);
            sb.AppendLine("mac normalize colon: " + m2);
            sb.AppendLine("mac normalize dash: " + m3);
            ok = ok && m1 && m2 && m3;

            // 3. Code128 roundtrip (encode then decode)
            string[] samples = { "ZTEGCB8E4525", "4413D003B768", "12345678", "ABC-123", "SN123" };
            foreach (var s in samples)
            {
                var codes = Code128.BuildCodes(s);
                string decoded = Code128.Decode(codes);
                bool pass = decoded == s;
                sb.AppendLine("code128 roundtrip " + s + " => " + pass);
                ok = ok && pass;
            }

            // 4. Checksum of known example (wikipedia "Hi" => checksum 33? verify self-consistency instead)
            var c1 = Code128.BuildCodes("Hi");
            sb.AppendLine("code128 checksum for Hi = " + c1[c1.Count - 2] + " (expect 84)");
            ok = ok && c1[c1.Count - 2] == 84;

            bool v1 = Updater.CompareVersion("1.2.0", "1.1.0") > 0;
            bool v2 = Updater.CompareVersion("1.2.0", "1.2.0") == 0;
            bool v3 = Updater.CompareVersion("v1.2.0", "1.1.9") > 0;
            bool v4 = Updater.CompareVersion("1.0.0", "1.1.0") < 0;
            sb.AppendLine("version compare: " + (v1 && v2 && v3 && v4));
            ok = ok && v1 && v2 && v3 && v4;

            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "selftest_result.txt"), sb.ToString() + Environment.NewLine + "SELFTEST " + (ok ? "OK" : "FAIL"));
        }
    }

    internal static class RenderTest
    {
        public static void Run(string path)
        {
            var rec = new DeviceRecord
            {
                Time = DateTime.Now,
                Model = "ZXHN F677V2",
                Type = "GPON",
                SN = "ZTEGCB8E4525",
                MAC = "4413D003B768"
            };
            var items = LayoutItem.DefaultLayout(85, 35);
            List<string> warnings;
            var bmp = LabelRenderer.Render(rec, 85, 35, 203, items, out warnings, false);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            bmp.Dispose();
        }
    }

    internal static class LayoutTest
    {
        public static void Run(string path)
        {
            var sb = new StringBuilder();
            int violations = 0;
            try
            {
                var f = new MainForm();
                f.CreateControl();
                sb.AppendLine("Form client=" + f.ClientSize);
                CheckControls(f, "Form", sb, ref violations);
                f.Dispose();
            }
            catch (Exception ex)
            {
                sb.AppendLine("CREATE FAILED: " + ex.Message);
            }
            sb.Insert(0, "violations=" + violations + Environment.NewLine);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        private static void CheckControls(Control parent, string path, StringBuilder sb, ref int violations)
        {
            foreach (Control child in parent.Controls)
            {
                if (!child.Visible) continue;
                int pr = parent.ClientSize.Width, pb = parent.ClientSize.Height;
                bool bad = child.Left < -1 || child.Top < -1 ||
                           child.Bounds.Right > pr + 1 || child.Bounds.Bottom > pb + 1;
                if (bad && child.Width > 1 && child.Height > 1)
                {
                    violations++;
                    sb.AppendLine("VIOLATION " + path + " / " + child.GetType().Name +
                                  " text='" + child.Text + "' loc=" + child.Location + " size=" + child.Size +
                                  " parentClient=" + new Size(pr, pb));
                }
                var cb = child as ComboBox;
                if (cb != null && cb.Items.Count > 0)
                {
                    int maxW = 0; string longItem = "";
                    foreach (object it in cb.Items)
                    {
                        int w = TextRenderer.MeasureText(it.ToString(), cb.Font).Width;
                        if (w > maxW) { maxW = w; longItem = it.ToString(); }
                    }
                    if (maxW + 22 > cb.Width)
                    {
                        violations++;
                        sb.AppendLine("COMBO_TRUNCATE " + path + " / '" + longItem + "' needed=" + (maxW + 22) + " has=" + cb.Width);
                    }
                }
                var lb = child as Label;
                if (lb != null && !lb.AutoSize && !string.IsNullOrEmpty(lb.Text))
                {
                    int needH = TextRenderer.MeasureText(lb.Text, lb.Font, new Size(lb.Width, 0), TextFormatFlags.WordBreak).Height;
                    if (needH > lb.Height + 2)
                    {
                        violations++;
                        sb.AppendLine("LABEL_TRUNCATE " + path + " / '" + lb.Text + "' needH=" + needH + " has=" + lb.Height);
                    }
                }
                CheckControls(child, path + " / " + child.GetType().Name, sb, ref violations);
            }
        }
    }

    internal static class Snapshot
    {
        public static void Run(string path)
        {
            try
            {
                var f = new MainForm();
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-5000, -5000); // keep off-screen
                f.Show();
                Application.DoEvents();
                int w = Math.Max(1, f.ClientSize.Width);
                int h = Math.Max(1, f.ClientSize.Height);
                // 先把整窗（含标题栏）画出来，再裁出客户区，避免底部被“假裁切”
                var full = new Bitmap(Math.Max(1, f.Width), Math.Max(1, f.Height));
                using (var g = Graphics.FromImage(full)) g.Clear(Color.White);
                f.DrawToBitmap(full, new Rectangle(0, 0, full.Width, full.Height));
                Rectangle cr = f.RectangleToScreen(f.ClientRectangle);
                Rectangle wr = f.Bounds;
                int ox = Math.Max(0, cr.X - wr.X), oy = Math.Max(0, cr.Y - wr.Y);
                var bmp = new Bitmap(w, h);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.DrawImage(full, new Rectangle(0, 0, w, h), new Rectangle(ox, oy, w, h), GraphicsUnit.Pixel);
                }
                full.Dispose();
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
                f.Dispose();
            }
            catch (Exception ex) { try { System.IO.File.WriteAllText(path + ".err", ex.ToString()); } catch { } }
        }
    }

    internal static class CalPreview
    {
        // 自测用：从本机“数据”目录统计每天的记录数
        private static Func<DateTime, int> SampleCounts()
        {
            string dataDir = System.IO.Path.Combine(Application.StartupPath, "数据");
            var counts = new Dictionary<string, int>();
            try
            {
                if (Directory.Exists(dataDir))
                {
                    foreach (var fp in Directory.GetFiles(dataDir, "历史记录_*.csv"))
                    {
                        string day = System.IO.Path.GetFileNameWithoutExtension(fp).Substring("历史记录_".Length);
                        int n = 0;
                        using (var sr = new StreamReader(fp, Encoding.UTF8, true))
                        {
                            while (sr.ReadLine() != null) n++;
                        }
                        if (n > 1) counts[day] = n - 1;
                    }
                }
            }
            catch { }
            return delegate(DateTime d)
            {
                int v;
                return counts.TryGetValue(d.ToString("yyyy-MM-dd"), out v) ? v : 0;
            };
        }

        // 自测：把日历面板直接渲染成 PNG，用于检查“有记录日期浅灰底”的效果
        public static void Run(string path)
        {
            try
            {
                Func<DateTime, int> cp = SampleCounts();
                var p = new CalendarPanel();
                p.CountProvider = cp;
                p.Selected = new DateTime(2026, 9, 12);
                p.ViewMonth = new DateTime(2026, 9, 1);
                var bmp = new Bitmap(p.Width + 2, p.Height + 2);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    p.DrawToBitmap(bmp, new Rectangle(0, 0, p.Width, p.Height));
                }
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
            }
            catch (Exception ex) { try { System.IO.File.WriteAllText(path + ".err", ex.ToString()); } catch { } }
        }

        // 自测：把“点击日期框后弹出的整块日历”渲染成 PNG
        public static void RunPopup(string path)
        {
            try
            {
                var host = new Form();
                host.FormBorderStyle = FormBorderStyle.None;
                host.StartPosition = FormStartPosition.Manual;
                host.Location = new Point(-4000, -4000);
                host.ClientSize = new Size(300, 80);
                var pick = new CalDatePicker();
                pick.CountProvider = SampleCounts();
                pick.Value = new DateTime(2026, 9, 12);
                pick.Location = new Point(12, 20);
                host.Controls.Add(pick);
                host.Show();
                Application.DoEvents();
                pick.ShowDrop();
                Application.DoEvents();
                var pop = pick.PopupForm;
                var bmp = new Bitmap(pop.Width, pop.Height);
                pop.DrawToBitmap(bmp, new Rectangle(0, 0, pop.Width, pop.Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
                // 模拟“点一下 2026-09-05 那一格”，验证能选中并回调
                var panel = pick.PopupForm.Controls[0] as CalendarPanel;
                bool fired = false;
                pick.ValueChanged += (s, e) => fired = true;
                DateTime want = new DateTime(2026, 9, 5);
                for (int r = 0; r < 6 && !fired; r++)
                    for (int c = 0; c < 7 && !fired; c++)
                    {
                        var pt = new Point(1 + c * 34 + 17, 1 + 30 + 22 + r * 27 + 13);
                        if (panel.SimDateAt(pt).Date == want) panel.SimClick(pt);
                    }
                System.IO.File.WriteAllText(path + ".log",
                    "clickedDate=" + want.ToString("yyyy-MM-dd") + " pickerValue=" + pick.Value.ToString("yyyy-MM-dd") +
                    " valueChangedFired=" + fired + " popupVisibleAfterClick=" + pop.Visible);
                pick.HideDrop();
                host.Dispose();
            }
            catch (Exception ex) { try { System.IO.File.WriteAllText(path + ".err", ex.ToString()); } catch { } }
        }
    }

    internal class DeviceRecord
    {
        public DateTime Time;
        public string Model = "";
        public string Type = "";
        public string SN = "";
        public string MAC = "";
        public string RawQR = "";
        public DateTime? PrintTime;
    }

    internal class QRParser
    {
        public string Model;
        public string Type;
        public string SN;
        public string SerialNumber;
        public string SSID;
        public string Password;
        public string Username;
        public string Pwd;
        public string IP;

        public static QRParser Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string t = text.Trim();
            int q = t.IndexOf('?');
            string query = q >= 0 ? t.Substring(q + 1) : t;
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in query.Split('&'))
            {
                if (string.IsNullOrEmpty(pair)) continue;
                int eq = pair.IndexOf('=');
                if (eq < 0) continue;
                string k = pair.Substring(0, eq).Trim();
                string v = pair.Substring(eq + 1).Trim().Replace('+', ' ');
                try { v = Uri.UnescapeDataString(v); } catch { }
                if (k.Length > 0) dict[k] = v;
            }
            if (dict.Count == 0) return null;
            var r = new QRParser();
            r.Model = Get(dict, "model");
            r.Type = Get(dict, "type");
            r.SN = Get(dict, "sn");
            r.SerialNumber = Get(dict, "serialnumber");
            r.SSID = Get(dict, "ssid1", "ssid");
            r.Password = Get(dict, "password");
            r.Username = Get(dict, "username");
            r.Pwd = Get(dict, "pwd");
            r.IP = Get(dict, "ip");
            return r;
        }

        private static string Get(Dictionary<string, string> d, params string[] keys)
        {
            foreach (var k in keys)
                if (d.ContainsKey(k)) return d[k];
            return "";
        }

        public static string NormalizeMac(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var sb = new StringBuilder();
            foreach (char c in text.Trim())
                if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
                    sb.Append(c);
            string mac = sb.ToString().ToUpperInvariant();
            return mac.Length == 12 ? mac : null;
        }
    }

    internal static class Code128
    {
        // Standard Code128 symbol patterns (value -> 11 modules, stop has 13)
        private static readonly string[] P =
        {
            "11011001100","11001101100","11001100110","10010011000","10010001100",
            "10001001100","10011001000","10011000100","10001100100","11001001000",
            "11001000100","11000100100","10110011100","10011011100","10011001110",
            "10111001100","10011101100","10011100110","11001110010","11001011100",
            "11001001110","11011100100","11001110100","11101101110","11101001100",
            "11100101100","11100100110","11101100100","11100110100","11100110010",
            "11011011000","11011000110","11000110110","10100011000","10001011000",
            "10001000110","10110001000","10001101000","10001100010","11010001000",
            "11000101000","11000100010","10110111000","10110001110","10001101110",
            "10111011000","10111000110","10001110110","11101110110","11010001110",
            "11000101110","11011101000","11011100010","11011101110","11101011000",
            "11101000110","11100010110","11101101000","11101100010","11100011010",
            "11101111010","11001000010","11110001010","10100110000","10100001100",
            "10010110000","10010000110","10000101100","10000100110","10110010000",
            "10110000100","10011010000","10011000010","10000110100","10000110010",
            "11000010010","11001010000","11110111010","11000010100","10001111010",
            "10100111100","10010111100","10010011110","10111100100","10011110100",
            "10011110010","11110100100","11110010100","11110010010","11011011110",
            "11011110110","11110110110","10101111000","10100011110","10001011110",
            "10111101000","10111100010","11110101000","11110100010","10111011110",
            "10111101110","11101011110","11110101110","11010000100","11010010000",
            "11010011100","1100011101011"
        };

        private const int StartB = 104;
        private const int StartC = 105;
        private const int Stop = 106;
        private const int QuietModules = 10;

        public static List<int> BuildCodes(string data)
        {
            if (string.IsNullOrEmpty(data)) return new List<int>();
            bool allDigits = data.All(char.IsDigit);
            bool useC = allDigits && data.Length % 2 == 0;
            var codes = new List<int>();
            int i = 0;
            if (useC)
            {
                codes.Add(StartC);
                for (; i < data.Length; i += 2)
                    codes.Add((data[i] - '0') * 10 + (data[i + 1] - '0'));
            }
            else
            {
                codes.Add(StartB);
                for (; i < data.Length; i++)
                {
                    int c = data[i];
                    codes.Add(c < 32 ? c : c - 32);
                }
            }
            int chk = codes[0];
            for (int k = 1; k < codes.Count; k++) chk += codes[k] * k;
            codes.Add(chk % 103);
            codes.Add(Stop);
            return codes;
        }

        public static string PatternFor(List<int> codes)
        {
            var sb = new StringBuilder();
            foreach (int c in codes) sb.Append(P[c]);
            return sb.ToString();
        }

        public static int ModuleCount(string data)
        {
            return PatternFor(BuildCodes(data)).Length + QuietModules * 2;
        }

        public static int Draw(Graphics g, string data, int centerX, int topY, int frameWidth, int height, out int moduleW)
        {
            var codes = BuildCodes(data);
            string pattern = PatternFor(codes);
            int modules = pattern.Length + QuietModules * 2;
            moduleW = Math.Max(1, Math.Min(3, Math.Max(1, frameWidth) / modules));
            int barWidth = modules * moduleW;
            int frameW = Math.Max(frameWidth, barWidth);
            int x0 = centerX - frameW / 2;
            int start = x0 + (frameW - barWidth) / 2 + QuietModules * moduleW;
            for (int m = 0; m < pattern.Length; m++)
                if (pattern[m] == '1')
                    g.FillRectangle(Brushes.Black, start + m * moduleW, topY, moduleW, height);
            return frameW;
        }

        public static string Decode(List<int> codes)
        {
            if (codes.Count < 2) return "";
            int start = codes[0];
            var sb = new StringBuilder();
            if (start == StartC)
            {
                for (int k = 1; k < codes.Count - 2; k++)
                {
                    int v = codes[k];
                    if (v <= 99) sb.Append((v / 10).ToString() + (v % 10).ToString());
                    else if (v <= 102) sb.Append((v - 100).ToString());
                }
            }
            else if (start == StartB)
            {
                for (int k = 1; k < codes.Count - 2; k++)
                {
                    int v = codes[k];
                    if (v <= 95) sb.Append((char)(v + 32));
                    else if (v <= 102) sb.Append((char)(v - 100));
                }
            }
            return sb.ToString();
        }
    }

    internal class LayoutItem
    {
        public string Id;
        public string Name;
        public bool IsBarcode;
        public bool Bold;
        public double Xmm;
        public double Ymm;
        public double FontSizePt;
        public bool Visible = true;
        public double HeightMm = 14;
        public double MaxWidthMm;   // 条码最大宽度约束（0 = 不限制）

        public static List<LayoutItem> DefaultLayout(double W, double H)
        {
            var l = new List<LayoutItem>();
            if (W >= H)
            {
                // 横版（如 85×35）：型号/类型在上方，SN/MAC 条码上下堆叠并横向拉宽，条码线更粗更清晰
                l.Add(new LayoutItem { Id = "model_text", Name = "型号文本", Xmm = W * 0.32, Ymm = H * 0.11, FontSizePt = 10, Visible = true, Bold = true });
                l.Add(new LayoutItem { Id = "type_text", Name = "类型文本", Xmm = W * 0.72, Ymm = H * 0.11, FontSizePt = 10, Visible = true });
                l.Add(new LayoutItem { Id = "sn_barcode", Name = "SN条码", Xmm = W / 2, Ymm = H * 0.26, IsBarcode = true, Visible = true, HeightMm = 7, MaxWidthMm = W * 0.6 });
                l.Add(new LayoutItem { Id = "sn_text", Name = "SN文本", Xmm = W / 2, Ymm = H * 0.49, FontSizePt = 8, Visible = true });
                l.Add(new LayoutItem { Id = "mac_barcode", Name = "MAC条码", Xmm = W / 2, Ymm = H * 0.6, IsBarcode = true, Visible = true, HeightMm = 7, MaxWidthMm = W * 0.6 });
                l.Add(new LayoutItem { Id = "mac_text", Name = "MAC文本", Xmm = W / 2, Ymm = H * 0.83, FontSizePt = 8, Visible = true });
            }
            else
            {
                // 竖版：上下排列
                l.Add(new LayoutItem { Id = "model_text", Name = "型号文本", Xmm = W / 2, Ymm = H * 0.055, FontSizePt = 11, Visible = true, Bold = true });
                l.Add(new LayoutItem { Id = "type_text", Name = "类型文本", Xmm = W / 2, Ymm = H * 0.15, FontSizePt = 10, Visible = true });
                l.Add(new LayoutItem { Id = "sn_barcode", Name = "SN条码", Xmm = W / 2, Ymm = H * 0.24, IsBarcode = true, Visible = true, HeightMm = 14 });
                l.Add(new LayoutItem { Id = "sn_text", Name = "SN文本", Xmm = W / 2, Ymm = H * 0.42, FontSizePt = 8, Visible = true });
                l.Add(new LayoutItem { Id = "mac_barcode", Name = "MAC条码", Xmm = W / 2, Ymm = H * 0.53, IsBarcode = true, Visible = true, HeightMm = 14 });
                l.Add(new LayoutItem { Id = "mac_text", Name = "MAC文本", Xmm = W / 2, Ymm = H * 0.71, FontSizePt = 8, Visible = true });
            }
            return l;
        }
    }

    internal static class LabelRenderer
    {
        public static int MmToPx(double mm, int dpi)
        {
            return (int)Math.Round(mm / 25.4 * dpi);
        }

        public static Font MakeFont(float pt, bool bold)
        {
            try
            {
                return new Font("Microsoft YaHei", pt, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
            }
            catch
            {
                return new Font("Arial", pt, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
            }
        }

        public static string PlaceholderBarcode(LayoutItem it)
        {
            return it.Id == "sn_barcode" ? "SN12345678901" : "1234567890AB";
        }

        public static string TextFor(LayoutItem it, DeviceRecord r, bool placeholder)
        {
            switch (it.Id)
            {
                case "model_text": return "型号：" + (placeholder && string.IsNullOrEmpty(r.Model) ? "ZXHN F677V2" : r.Model);
                case "type_text": return "类型：" + (placeholder && string.IsNullOrEmpty(r.Type) ? "GPON" : r.Type);
                case "sn_text": return "SN：" + (placeholder && string.IsNullOrEmpty(r.SN) ? "SN12345678901" : r.SN);
                case "mac_text": return "MAC：" + (placeholder && string.IsNullOrEmpty(r.MAC) ? "1234567890AB" : r.MAC);
            }
            return "";
        }

        public static string BarcodeDataFor(LayoutItem it, DeviceRecord r)
        {
            return it.Id == "sn_barcode" ? r.SN : r.MAC;
        }

        public static Bitmap Render(DeviceRecord r, double Wmm, double Hmm, int dpi, List<LayoutItem> items, out List<string> warnings, bool placeholder)
        {
            warnings = new List<string>();
            int wPx = MmToPx(Wmm, dpi);
            int hPx = MmToPx(Hmm, dpi);
            var bmp = new Bitmap(Math.Max(1, wPx), Math.Max(1, hPx));
            bmp.SetResolution(dpi, dpi);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.None;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixel;
                foreach (var it in items)
                {
                    if (!it.Visible) continue;
                    if (it.IsBarcode)
                    {
                        string data = BarcodeDataFor(it, r);
                        if (placeholder && string.IsNullOrEmpty(data)) data = PlaceholderBarcode(it);
                        if (string.IsNullOrEmpty(data)) continue;
                        int maxW = wPx - MmToPx(4, dpi) * 2;
                        if (it.MaxWidthMm > 0)
                            maxW = Math.Min(maxW, MmToPx(it.MaxWidthMm, dpi));
                        if (maxW < 30) continue;
                        int h = Math.Max(20, MmToPx(it.HeightMm, dpi));
                        int modules = Code128.ModuleCount(data);
                        int mw;
                        int drawn = Code128.Draw(g, data, MmToPx(it.Xmm, dpi), MmToPx(it.Ymm, dpi), maxW, h, out mw);
                        if (mw < 1 || drawn > maxW)
                            warnings.Add("条码太宽：" + it.Name + "，请换更宽的标签纸或调整布局。");
                    }
                    else
                    {
                        string text = TextFor(it, r, placeholder);
                        if (string.IsNullOrEmpty(text)) continue;
                        using (var f = MakeFont((float)it.FontSizePt, it.Bold))
                        using (var fmt = new StringFormat())
                        {
                            fmt.Alignment = StringAlignment.Center;
                            fmt.LineAlignment = StringAlignment.Near;
                            var sz = g.MeasureString(text, f);
                            var rect = new RectangleF(MmToPx(it.Xmm, dpi) - sz.Width / 2f, MmToPx(it.Ymm, dpi), sz.Width, sz.Height);
                            g.DrawString(text, f, Brushes.Black, rect, fmt);
                        }
                    }
                }
            }
            return bmp;
        }

        public static RectangleF ItemRect(LayoutItem it, DeviceRecord r, double Wmm, double Hmm, int dpi)
        {
            int wPx = MmToPx(Wmm, dpi);
            if (it.IsBarcode)
            {
                string data = BarcodeDataFor(it, r);
                if (string.IsNullOrEmpty(data)) data = PlaceholderBarcode(it);
                int maxW = wPx - MmToPx(4, dpi) * 2;
                if (it.MaxWidthMm > 0)
                    maxW = Math.Min(maxW, MmToPx(it.MaxWidthMm, dpi));
                int modules = Code128.ModuleCount(data);
                int mw = Math.Max(1, Math.Min(3, Math.Max(1, maxW) / modules));
                int barW = modules * mw;
                int w = Math.Max(maxW, barW);
                int h = Math.Max(20, MmToPx(it.HeightMm, dpi));
                return new RectangleF(MmToPx(it.Xmm, dpi) - w / 2f, MmToPx(it.Ymm, dpi), w, h);
            }
            else
            {
                string text = TextFor(it, r, true);
                using (var f = MakeFont((float)it.FontSizePt, it.Bold))
                {
                    using (var bmp = new Bitmap(1, 1))
                    using (var g = Graphics.FromImage(bmp))
                    {
                        var sz = g.MeasureString(text, f);
                        return new RectangleF(MmToPx(it.Xmm, dpi) - sz.Width / 2f, MmToPx(it.Ymm, dpi), sz.Width, sz.Height);
                    }
                }
            }
        }
    }

    internal class AppSettings
    {
        public string Printer = "";
        public double LabelWidthMm = 85;
        public double LabelHeightMm = 35;
        public int Dpi = 203;
        public bool AutoPrint = true;
        public bool ShowModel = true;
        public bool ShowType = true;
        public bool ShowSN = true;
        public bool ShowMAC = true;
        public int LayoutVersion = 6;
        public const string DefaultUpdateUrl = "https://gitee.com/chuanyin888/label-printer-software/raw/master/latest.json";
        public string UpdateUrl = DefaultUpdateUrl;
        public string UpdateToken = "";
        public int BarcodeWidth = 2;   // 0=细  1=中  2=粗
        public bool NasSyncEnabled = false;   // 是否开启 NAS 备份同步
        public string NasPath = "";           // NAS 备份目录
        public bool AutoEnglish = true;       // 打开软件/点扫码框时，自动把中文输入法切到英文（防止模拟按键被输入法吃掉）
        public List<LayoutItem> Layout = LayoutItem.DefaultLayout(85, 35);

        public string Path;

        public void Load()
        {
            if (!File.Exists(Path)) return;
            try
            {
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(Path, Encoding.UTF8))
                {
                    string t = line.Trim();
                    if (t.Length == 0 || t.StartsWith("#") || t.StartsWith("[")) continue;
                    int eq = t.IndexOf('=');
                    if (eq <= 0) continue;
                    d[t.Substring(0, eq).Trim()] = t.Substring(eq + 1).Trim();
                }
                if (d.ContainsKey("printer")) Printer = d["printer"];
                if (d.ContainsKey("labelWidthMm")) LabelWidthMm = ParseD(d["labelWidthMm"], LabelWidthMm);
                if (d.ContainsKey("labelHeightMm")) LabelHeightMm = ParseD(d["labelHeightMm"], LabelHeightMm);
                if (d.ContainsKey("dpi")) Dpi = (int)ParseD(d["dpi"], Dpi);
                if (d.ContainsKey("autoPrint")) AutoPrint = ParseB(d["autoPrint"], AutoPrint);
                if (d.ContainsKey("showModel")) ShowModel = ParseB(d["showModel"], ShowModel);
                if (d.ContainsKey("showType")) ShowType = ParseB(d["showType"], ShowType);
                if (d.ContainsKey("showSN")) ShowSN = ParseB(d["showSN"], ShowSN);
                if (d.ContainsKey("showMAC")) ShowMAC = ParseB(d["showMAC"], ShowMAC);
                bool hasLayoutVersion = d.ContainsKey("layoutVersion");
                if (hasLayoutVersion) LayoutVersion = (int)ParseD(d["layoutVersion"], LayoutVersion);
                bool useSavedLayout = hasLayoutVersion && LayoutVersion >= 6;
                if (d.ContainsKey("updateUrl")) UpdateUrl = d["updateUrl"];
                if (d.ContainsKey("updateToken")) UpdateToken = d["updateToken"];
                if (d.ContainsKey("barcodeWidth")) BarcodeWidth = (int)ParseD(d["barcodeWidth"], BarcodeWidth);
                if (d.ContainsKey("autoEnglish")) AutoEnglish = ParseB(d["autoEnglish"], AutoEnglish);
                if (d.ContainsKey("nasSyncEnabled")) NasSyncEnabled = ParseB(d["nasSyncEnabled"], false);
                if (d.ContainsKey("nasPath")) NasPath = d["nasPath"];
                if (string.IsNullOrWhiteSpace(UpdateUrl)) UpdateUrl = DefaultUpdateUrl;
                LabelWidthMm = Math.Max(5, Math.Min(200, LabelWidthMm));
                LabelHeightMm = Math.Max(5, Math.Min(300, LabelHeightMm));

                var layout = LayoutItem.DefaultLayout(LabelWidthMm, LabelHeightMm);
                if (useSavedLayout)
                {
                    foreach (var it in layout)
                    {
                        string px = "layout." + it.Id + ".x";
                        string py = "layout." + it.Id + ".y";
                        string ps = "layout." + it.Id + ".size";
                        string pv = "layout." + it.Id + ".visible";
                        string ph = "layout." + it.Id + ".height";
                        string pw = "layout." + it.Id + ".maxwidth";
                        if (d.ContainsKey(px)) it.Xmm = ParseD(d[px], it.Xmm);
                        if (d.ContainsKey(py)) it.Ymm = ParseD(d[py], it.Ymm);
                        if (d.ContainsKey(ps)) it.FontSizePt = ParseD(d[ps], it.FontSizePt);
                        if (d.ContainsKey(pv)) it.Visible = ParseB(d[pv], it.Visible);
                        if (d.ContainsKey(ph)) it.HeightMm = ParseD(d[ph], it.HeightMm);
                        if (d.ContainsKey(pw)) it.MaxWidthMm = ParseD(d[pw], it.MaxWidthMm);
                    }
                }
                Layout = layout;
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("printer=" + Printer);
                sb.AppendLine("labelWidthMm=" + LabelWidthMm.ToString("0.##", CultureInfo.InvariantCulture));
                sb.AppendLine("labelHeightMm=" + LabelHeightMm.ToString("0.##", CultureInfo.InvariantCulture));
                sb.AppendLine("dpi=" + Dpi);
                sb.AppendLine("autoPrint=" + (AutoPrint ? "1" : "0"));
                sb.AppendLine("showModel=" + (ShowModel ? "1" : "0"));
                sb.AppendLine("showType=" + (ShowType ? "1" : "0"));
                sb.AppendLine("showSN=" + (ShowSN ? "1" : "0"));
                sb.AppendLine("showMAC=" + (ShowMAC ? "1" : "0"));
                sb.AppendLine("layoutVersion=" + LayoutVersion);
                sb.AppendLine("updateUrl=" + UpdateUrl);
                sb.AppendLine("updateToken=" + UpdateToken);
                sb.AppendLine("barcodeWidth=" + BarcodeWidth);
                sb.AppendLine("nasSyncEnabled=" + (NasSyncEnabled ? "1" : "0"));
                sb.AppendLine("nasPath=" + NasPath);
                sb.AppendLine("autoEnglish=" + (AutoEnglish ? "1" : "0"));
                foreach (var it in Layout)
                {
                    sb.AppendLine("layout." + it.Id + ".x=" + it.Xmm.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.AppendLine("layout." + it.Id + ".y=" + it.Ymm.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.AppendLine("layout." + it.Id + ".size=" + it.FontSizePt.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.AppendLine("layout." + it.Id + ".visible=" + (it.Visible ? "1" : "0"));
                    sb.AppendLine("layout." + it.Id + ".height=" + it.HeightMm.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.AppendLine("layout." + it.Id + ".maxwidth=" + it.MaxWidthMm.ToString("0.##", CultureInfo.InvariantCulture));
                }
                File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }

        private static double ParseD(string s, double def)
        {
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        private static bool ParseB(string s, bool def)
        {
            return s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) ? true : (s == "0" || string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) ? false : def);
        }
    }

    internal class HistoryStore
    {
        private readonly string _path;
        public HistoryStore(string path) { _path = path; }

        // 严格读取：返回 false 表示“文件存在但读不出来”（调用方不要覆盖它，避免把还能抢救的文件清掉）
        public bool TryLoad(out List<DeviceRecord> list)
        {
            list = new List<DeviceRecord>();
            if (!File.Exists(_path)) return true;   // 不存在 = 空，属于正常情况
            string[] lines;
            try { lines = File.ReadAllLines(_path, Encoding.UTF8); }
            catch { return false; }
            for (int i = 1; i < lines.Length; i++)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var f = SplitCsv(lines[i]);
                    if (f.Length < 7) continue;
                    var r = new DeviceRecord();
                    DateTime t;
                    DateTime.TryParse(f[0], out t);
                    r.Time = t;
                    r.Model = f[1];
                    r.Type = f[2];
                    r.SN = f[3];
                    r.MAC = f[4];
                    r.RawQR = f[5];
                    DateTime pt;
                    if (DateTime.TryParse(f[6], out pt)) r.PrintTime = pt;
                    list.Add(r);
                }
                catch { }
            }
            return true;
        }

        public List<DeviceRecord> Load()
        {
            var list = new List<DeviceRecord>();
            if (!File.Exists(_path)) return list;
            string[] lines = null;
            try { lines = File.ReadAllLines(_path, Encoding.UTF8); }
            catch { return list; }
            // 逐行解析：某一条数据格式异常只跳过该条，不拖垮整份历史文件（避免今日计数被清空）
            for (int i = 1; i < lines.Length; i++)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var f = SplitCsv(lines[i]);
                    if (f.Length < 7) continue;
                    var r = new DeviceRecord();
                    DateTime t;
                    DateTime.TryParse(f[0], out t);
                    r.Time = t;
                    r.Model = f[1];
                    r.Type = f[2];
                    r.SN = f[3];
                    r.MAC = f[4];
                    r.RawQR = f[5];
                    DateTime pt;
                    if (DateTime.TryParse(f[6], out pt)) r.PrintTime = pt;
                    list.Add(r);
                }
                catch { }
            }
            return list;
        }

        public void Save(List<DeviceRecord> list)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("时间,型号,类型,SN,MAC,二维码内容,打印时间");
                foreach (var r in list)
                {
                    sb.AppendLine(Escape(r.Time.ToString("yyyy-MM-dd HH:mm:ss")) + "," +
                                  Escape(r.Model) + "," + Escape(r.Type) + "," + Escape(r.SN) + "," +
                                  Escape(r.MAC) + "," + Escape(r.RawQR) + "," +
                                  Escape(r.PrintTime.HasValue ? r.PrintTime.Value.ToString("yyyy-MM-dd HH:mm:ss") : ""));
                }
                File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }

        private static string Escape(string s)
        {
            if (s == null) s = "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static string[] SplitCsv(string line)
        {
            var parts = new List<string>();
            var cur = new StringBuilder();
            bool inQ = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQ)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                        else inQ = false;
                    }
                    else cur.Append(c);
                }
                else
                {
                    if (c == '"') inQ = true;
                    else if (c == ',') { parts.Add(cur.ToString()); cur.Clear(); }
                    else cur.Append(c);
                }
            }
            parts.Add(cur.ToString());
            return parts.ToArray();
        }
    }

    internal static class Updater
    {
      public const string AppVersion = "2.1.1";
        public enum UpdateCheckResult { Error, NoUpdate, UpdateAvailable }

        public static int CompareVersion(string a, string b)
        {
            int[] pa = ParseVersion(a);
            int[] pb = ParseVersion(b);
            for (int i = 0; i < 3; i++)
                if (pa[i] != pb[i]) return pa[i].CompareTo(pb[i]);
            return 0;
        }

        private static int[] ParseVersion(string v)
        {
            var parts = new int[3];
            if (string.IsNullOrEmpty(v)) return parts;
            v = v.Trim().TrimStart('v', 'V');
            var seg = v.Split('.');
            for (int i = 0; i < 3 && i < seg.Length; i++)
            {
                int n; if (int.TryParse(seg[i], out n)) parts[i] = n;
            }
            return parts;
        }

        public static UpdateCheckResult FindNewVersion(string url, string token, string current, out string newVersion, out string downloadUrl, out string notes)
        {
            newVersion = ""; downloadUrl = ""; notes = "";
            if (string.IsNullOrWhiteSpace(url)) url = AppSettings.DefaultUpdateUrl;
            UpdateCheckResult res = TryCheck(url, token, current, ref newVersion, ref downloadUrl, ref notes);
            // 配置的更新源连不上时，回退到默认 Gitee 源，避免在 GitHub 访问受限的网络下误报“无法连接”
            if (res == UpdateCheckResult.Error
                && !string.Equals(url, AppSettings.DefaultUpdateUrl, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(AppSettings.DefaultUpdateUrl))
            {
                newVersion = ""; downloadUrl = ""; notes = "";
                res = TryCheck(AppSettings.DefaultUpdateUrl, "", current, ref newVersion, ref downloadUrl, ref notes);
            }
            return res;
        }

        private static UpdateCheckResult TryCheck(string url, string token, string current, ref string newVersion, ref string downloadUrl, ref string notes)
        {
            if (string.IsNullOrWhiteSpace(url)) return UpdateCheckResult.Error;
            try
            {
                string json;
                if (url.Contains("github.com"))
                {
                    json = GetGitHubReleaseJson(url, token);
                    var obj = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                    if (obj == null) return UpdateCheckResult.Error;
                    newVersion = obj.ContainsKey("tag_name") && obj["tag_name"] != null ? obj["tag_name"].ToString() : "";
                    if (obj.ContainsKey("body") && obj["body"] != null) notes = obj["body"].ToString();
                    var assets = obj.ContainsKey("assets") && obj["assets"] != null ? obj["assets"] as object[] : null;
                    if (assets != null)
                    {
                        foreach (var a in assets)
                        {
                            var ad = a as Dictionary<string, object>;
                            if (ad == null) continue;
                            string name = ad.ContainsKey("name") && ad["name"] != null ? ad["name"].ToString() : "";
                            if (name.ToLowerInvariant().EndsWith(".exe"))
                            {
                                downloadUrl = ad.ContainsKey("browser_download_url") && ad["browser_download_url"] != null ? ad["browser_download_url"].ToString() : "";
                                break;
                            }
                        }
                    }
                }
                else
                {
                    json = Get(url, token);
                    var obj = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                    if (obj == null) return UpdateCheckResult.Error;
                    newVersion = GetS(obj, "version");
                    downloadUrl = GetS(obj, "download");
                    if (downloadUrl == "") downloadUrl = GetS(obj, "url");
                    notes = GetS(obj, "notes");
                }
                if (string.IsNullOrEmpty(newVersion) || string.IsNullOrEmpty(downloadUrl))
                    return UpdateCheckResult.NoUpdate;
                return CompareVersion(newVersion, current) > 0 ? UpdateCheckResult.UpdateAvailable : UpdateCheckResult.NoUpdate;
            }
            catch { return UpdateCheckResult.Error; }
        }

        private static string GetS(Dictionary<string, object> d, string k)
        {
            return d != null && d.ContainsKey(k) && d[k] != null ? d[k].ToString() : "";
        }

        private static string GetGitHubReleaseJson(string url, string token)
        {
            string api = url.TrimEnd('/');
            int idx = api.IndexOf("github.com/");
            string repo = idx >= 0 ? api.Substring(idx + "github.com/".Length) : "";
            api = "https://api.github.com/repos/" + repo + "/releases/latest";
            return Get(api, token);
        }

        private static string Get(string url, string token)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.UserAgent = "LabelPrinterUpdater";
            req.Accept = "application/json";
            req.Timeout = 15000;
            if (!string.IsNullOrEmpty(token)) req.Headers["Authorization"] = "Bearer " + token;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        public static string Download(string url, string token, string destPath)
        {
            using (var wc = new WebClient())
            {
                wc.Headers["User-Agent"] = "LabelPrinterUpdater";
                if (!string.IsNullOrEmpty(token)) wc.Headers["Authorization"] = "Bearer " + token;
                wc.DownloadFile(url, destPath);
            }
            return destPath;
        }

        public static void InstallAndRelaunch(string downloadedExe, string currentExe)
        {
            try
            {
                string cmd = "/c ping -n 2 127.0.0.1 >nul & copy /y \"" + downloadedExe + "\" \"" + currentExe + "\" & start \"\" \"" + currentExe + "\"";
                System.Diagnostics.Process.Start("cmd.exe", cmd);
            }
            catch { }
        }
    }

    // 自绘日历面板：有记录的日期显示“浅灰底”，悬停显示当天数量，点击选择日期
    internal class CalendarPanel : Control
    {
        private DateTime _selected = DateTime.Today;
        private DateTime _viewMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        public DateTime Selected { get { return _selected; } set { _selected = value; Invalidate(); } }
        public DateTime ViewMonth { get { return _viewMonth; } set { _viewMonth = value; Invalidate(); } }
        public Func<DateTime, int> CountProvider;
        public event EventHandler DateClicked;

        private const int CellW = 34, CellH = 27, HeadH = 30, WeekH = 22, FootH = 24;
        private readonly ToolTip _tip = new ToolTip { InitialDelay = 150, ReshowDelay = 60 };
        private DateTime _hover = DateTime.MinValue;

        public CalendarPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Width = CellW * 7 + 2;
            Height = HeadH + WeekH + CellH * 6 + FootH + 2;
            BackColor = Color.White;
        }

        private Rectangle TodayRect { get { return new Rectangle(1, Height - 1 - FootH, 62, FootH); } }

        private Rectangle CellRect(int r, int c) { return new Rectangle(1 + c * CellW, 1 + HeadH + WeekH + r * CellH, CellW, CellH); }

        private bool TryDateAt(Point p, out DateTime d)
        {
            d = DateTime.MinValue;
            int gx = p.X - 1, gy = p.Y - 1 - HeadH - WeekH;
            if (gx < 0 || gy < 0) return false;
            int c = gx / CellW, r = gy / CellH;
            if (c > 6 || r > 5) return false;
            int lead = (int)ViewMonth.DayOfWeek;
            d = ViewMonth.AddDays(-lead + r * 7 + c);
            return true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.White);
            using (var pen = new Pen(Ui.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            int w = Width - 2;
            using (var b = new SolidBrush(Color.FromArgb(247, 249, 250))) g.FillRectangle(b, 1, 1, w, HeadH);
            TextRenderer.DrawText(g, "◀", Font, new Rectangle(6, 1, 24, HeadH), Ui.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "▶", Font, new Rectangle(Width - 30, 1, 24, HeadH), Ui.TextMuted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, ViewMonth.ToString("yyyy年M月"), Ui.HeadingFont, new Rectangle(1, 1, w, HeadH), Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            string[] wk = { "日", "一", "二", "三", "四", "五", "六" };
            for (int i = 0; i < 7; i++)
                TextRenderer.DrawText(g, wk[i], Font, new Rectangle(1 + i * CellW, 1 + HeadH, CellW, WeekH), Ui.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            int lead = (int)ViewMonth.DayOfWeek;
            DateTime start = ViewMonth.AddDays(-lead);
            for (int r = 0; r < 6; r++)
            {
                for (int c = 0; c < 7; c++)
                {
                    DateTime d = start.AddDays(r * 7 + c);
                    var rect = CellRect(r, c);
                    bool has = CountProvider != null && CountProvider(d) > 0;
                    bool inMonth = d.Year == ViewMonth.Year && d.Month == ViewMonth.Month;
                    bool sel = d.Date == Selected.Date;
                    bool hov = d.Date == _hover;
                    if (sel) { using (var b = new SolidBrush(Ui.AccentSoft)) g.FillRectangle(b, rect); }
                    else if (has) { using (var b = new SolidBrush(hov ? Color.FromArgb(210, 219, 228) : Color.FromArgb(222, 229, 236))) g.FillRectangle(b, rect); }
                    else if (hov) { using (var b = new SolidBrush(Color.FromArgb(244, 247, 249))) g.FillRectangle(b, rect); }
                    Color tc = inMonth ? Ui.Text : Color.FromArgb(188, 195, 202);
                    if (sel) tc = Ui.Accent;
                    else if (has && !inMonth) tc = Color.FromArgb(150, 160, 170);
                    TextRenderer.DrawText(g, d.Day.ToString(), Font, rect, tc, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
            }
            // 底部：快捷“今天” + 说明
            int fy = Height - 1 - FootH;
            using (var pen = new Pen(Ui.Border)) g.DrawLine(pen, 1, fy, Width - 2, fy);
            using (var b = new SolidBrush(Color.FromArgb(247, 249, 250))) g.FillRectangle(b, 1, fy + 1, w, FootH - 1);
            var tr = TodayRect;
            if (tr.Contains(PointToClient(Cursor.Position))) { using (var b = new SolidBrush(Ui.AccentSoft)) g.FillRectangle(b, tr); }
            TextRenderer.DrawText(g, "今天", Ui.HeadingFont, tr, Ui.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "浅灰底 = 当天有记录", Font, new Rectangle(tr.Right, fy + 1, Width - 2 - tr.Right, FootH - 1), Ui.TextMuted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            DateTime d;
            if (TryDateAt(e.Location, out d))
            {
                if (d.Date != _hover) { _hover = d.Date; Invalidate(); }
                int n = CountProvider != null ? CountProvider(d) : 0;
                _tip.Show(d.ToString("yyyy-MM-dd") + (n > 0 ? ("：当天 " + n + " 条记录") : "：无记录"), this, e.X + 14, e.Y + 18, 1500);
            }
            else
            {
                if (_hover != DateTime.MinValue) { _hover = DateTime.MinValue; Invalidate(); }
                _tip.Hide(this);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = DateTime.MinValue; _tip.Hide(this); Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            HandleClick(e.Location);
        }

        // 供自测调用：模拟一次鼠标点击
        internal void SimClick(Point p) { HandleClick(p); }
        internal DateTime SimDateAt(Point p) { DateTime d; return TryDateAt(p, out d) ? d : DateTime.MinValue; }

        private void HandleClick(Point loc)
        {
            MouseEventArgs e = new MouseEventArgs(MouseButtons.Left, 1, loc.X, loc.Y, 0);
            if (TodayRect.Contains(e.Location))
            {
                Selected = DateTime.Today;
                ViewMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                Invalidate();
                if (DateClicked != null) DateClicked(this, EventArgs.Empty);
                return;
            }
            if (e.X >= 1 && e.X <= 30 && e.Y >= 1 && e.Y <= 1 + HeadH) { ViewMonth = ViewMonth.AddMonths(-1); Invalidate(); return; }
            if (e.X >= Width - 31 && e.X <= Width - 1 && e.Y >= 1 && e.Y <= 1 + HeadH) { ViewMonth = ViewMonth.AddMonths(1); Invalidate(); return; }
            DateTime d;
            if (TryDateAt(e.Location, out d))
            {
                Selected = d.Date;
                Invalidate();
                if (DateClicked != null) DateClicked(this, EventArgs.Empty);
            }
        }
    }

    // 日期选择控件：点击弹出上面的自绘日历；显示当前选择的日期
    internal class CalDatePicker : Control
    {
        private DateTime _value = DateTime.Today;
        public DateTime Value { get { return _value; } set { _value = value; Invalidate(); } }
        public Func<DateTime, int> CountProvider;
        // 关闭弹层后要把光标还给的控件（一般是扫码输入框，保证扫码不丢）
        public Control FocusAfterClose;
        public event EventHandler ValueChanged;

        private Form _pop;
        private CalendarPanel _panel;

        // 供自测使用
        internal Form PopupForm { get { return _pop; } }

        public CalDatePicker()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 26; Width = 128; Cursor = Cursors.Hand; BackColor = Color.White;
            _panel = new CalendarPanel();
            _panel.DateClicked += (s, e) =>
            {
                Value = _panel.Selected.Date;
                HideDrop();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            };
            _pop = new Form();
            _pop.FormBorderStyle = FormBorderStyle.None;
            _pop.ShowInTaskbar = false;
            _pop.StartPosition = FormStartPosition.Manual;
            _pop.TopMost = true;
            _pop.BackColor = Color.White;
            _pop.ClientSize = new Size(_panel.Width, _panel.Height);
            _panel.Location = new Point(0, 0);
            _pop.Controls.Add(_panel);
            _pop.Deactivate += (s, e) => HideDrop();
            _pop.KeyPreview = true;
            _pop.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) HideDrop(); };
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            ShowDrop();
        }

        internal void ShowDrop()
        {
            try
            {
                _panel.CountProvider = CountProvider;
                _panel.Selected = Value.Date;
                _panel.ViewMonth = new DateTime(Value.Year, Value.Month, 1);
                Point p = PointToScreen(new Point(0, Height));
                Screen scr = Screen.FromControl(this);
                if (p.Y + _pop.Height > scr.WorkingArea.Bottom) p.Y = PointToScreen(new Point(0, 0)).Y - _pop.Height;
                if (p.X + _pop.Width > scr.WorkingArea.Right) p.X = scr.WorkingArea.Right - _pop.Width;
                if (p.X < scr.WorkingArea.Left) p.X = scr.WorkingArea.Left;
                if (p.Y < scr.WorkingArea.Top) p.Y = scr.WorkingArea.Top;
                _pop.Location = p;
                try { if (FindForm() != null) _pop.Show(FindForm()); else _pop.Show(); }
                catch { try { _pop.Show(); } catch { } }
                _pop.BringToFront();
                _pop.Activate();
            }
            catch { }
        }

        internal void HideDrop()
        {
            try
            {
                if (_pop.Visible)
                {
                    _pop.Hide();
                    if (FindForm() != null) FindForm().Activate();
                }
            }
            catch { }
            // 还光标：避免选完日期后扫码枪的输入丢到别处
            try { if (FocusAfterClose != null && FocusAfterClose.CanFocus) FocusAfterClose.Focus(); } catch { }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            base.OnHandleDestroyed(e);
            try { _pop.Dispose(); } catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.White);
            using (var pen = new Pen(Ui.Border)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(e.Graphics, Value.ToString("yyyy-MM-dd"), Font, new Rectangle(7, 0, Width - 28, Height), Ui.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            int x = Width - 21, y = (Height - 12) / 2;
            using (var pen = new Pen(Ui.TextMuted))
            {
                e.Graphics.DrawRectangle(pen, x, y + 2, 14, 11);
                e.Graphics.DrawLine(pen, x, y + 5, x + 14, y + 5);
                e.Graphics.DrawLine(pen, x + 4, y, x + 4, y + 4);
                e.Graphics.DrawLine(pen, x + 10, y, x + 10, y + 4);
            }
        }
    }

    // 由「一体化壳」注入的 NAS 钩子。
    // 单独编译标签打印软件时这些都为 null，功能照常，不影响单文件源码可用。
    internal static class NasHook
    {
        public static Func<bool> IsEnabled;      // 是否已开启 NAS 同步
        public static Func<string> MachineName;  // 本机在 NAS 上的子文件夹名
        public static Action ClearHistory;       // 清空 NAS 上“本机”的历史记录
    }

    // 扫码/识别内容处理结果：没处理 / 已录入一半 / 已完成一台
    internal enum ScanFeed { Ignored, Partial, Completed }

    /// <summary>
    /// 输入法助手：把窗口的输入法切到"英文"。
    /// 为什么需要它：扫码枪/扫码伴侣是"模拟按键"，如果当前是中文输入法，
    /// 字符会进输入法的候选框，工具这边就一个字都收不到（表现为"扫了没反应"）。
    /// 三种手段一起上：① 关掉该窗口的输入法打开状态；② 会话设置成英文/半角；
    /// ③ 给默认 IME 窗口发 IMC_SETCONVERSIONMODE=英文（搜狗、微软拼音都认）。
    /// </summary>
    internal static class ImeHelper
    {
        [DllImport("imm32.dll")] private static extern IntPtr ImmGetContext(IntPtr hWnd);
        [DllImport("imm32.dll")] private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
        [DllImport("imm32.dll")] private static extern bool ImmSetOpenStatus(IntPtr hIMC, bool fOpen);
        [DllImport("imm32.dll")] private static extern bool ImmSetConversionStatus(IntPtr hIMC, int fdwConversion, int fdwSentence);
        [DllImport("imm32.dll")] private static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_IME_CONTROL = 0x0283;
        private const int IMC_SETCONVERSIONMODE = 0x0002;
        private const int IME_CMODE_ALPHANUMERIC = 0x0000;

        /// <summary>把这个窗口的输入法切到英文（失败也只是没切换，不影响别的功能）</summary>
        internal static bool ToEnglish(IntPtr hwnd)
        {
            bool ok = false;
            if (hwnd == IntPtr.Zero) return false;
            try
            {
                IntPtr hIMC = ImmGetContext(hwnd);
                if (hIMC != IntPtr.Zero)
                {
                    try { if (ImmSetOpenStatus(hIMC, false)) ok = true; } catch { }             // 关掉输入法（最干脆）
                    try { if (ImmSetConversionStatus(hIMC, IME_CMODE_ALPHANUMERIC, 0)) ok = true; } catch { }
                    ImmReleaseContext(hwnd, hIMC);
                }
            }
            catch { }
            try
            {
                IntPtr hIme = ImmGetDefaultIMEWnd(hwnd);
                if (hIme != IntPtr.Zero)
                {
                    SendMessage(hIme, WM_IME_CONTROL, (IntPtr)IMC_SETCONVERSIONMODE, (IntPtr)IME_CMODE_ALPHANUMERIC);
                    ok = true;
                }
            }
            catch { }
            return ok;
        }
    }

    /// <summary>
    /// 「光猫扫码伴侣」（modem_scanner.py）的进程管家。
    /// 用途：打开《一体化工具》时自动把它拉起来（窗口先藏起来），
    ///       点「开始识别」就把它的控制台窗口显示出来（工人看日志），
    ///       暂停/继续/停止/换模式通过一个"命令文件"告诉它——不改它的识别逻辑，
    ///       摄像头仍然归它使用。
    /// </summary>
    internal sealed class ScannerCompanion
    {
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

        private Process _proc;
        private readonly string _script;
        private readonly string _cmdFile;
        private readonly List<string> _lines = new List<string>();
        internal string LastError = "";
        internal event Action<string> Line;                 // 每读到一行日志就抛给界面显示

        /// <summary>取最近的日志（给界面"日志"面板用）</summary>
        internal List<string> RecentLines(int max)
        {
            lock (_lines)
            {
                int n = Math.Min(max, _lines.Count);
                return _lines.GetRange(_lines.Count - n, n);
            }
        }

        internal ScannerCompanion(string baseDir)
        {
            _script = Path.Combine(baseDir, "modem_scanner.py");
            _cmdFile = Path.Combine(baseDir, "scanner_cmd.txt");
        }

        internal bool Running
        {
            get { try { return _proc != null && !_proc.HasExited; } catch { return false; } }
        }

        internal int Pid { get { try { return Running ? _proc.Id : 0; } catch { return 0; } } }

        private static string FindPython()
        {
            // 1) PATH 里的 python
            try
            {
                var psi = new ProcessStartInfo("where.exe", "python")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    foreach (var line in (outp ?? "").Split('\n'))
                    {
                        string s = line.Trim();
                        if (s.Length > 4 && s.ToLowerInvariant().EndsWith("python.exe") && File.Exists(s)) return s;
                    }
                }
            }
            catch { }
            // 2) 常见安装位置（装 Python 时默认就在这）
            string lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] cands = new string[]
            {
                Path.Combine(lad, @"Programs\Python\Python311\python.exe"),
                Path.Combine(lad, @"Programs\Python\Python312\python.exe"),
                Path.Combine(lad, @"Programs\Python\Python310\python.exe"),
                @"C:\Python311\python.exe",
                @"C:\Python312\python.exe",
                @"C:\Python310\python.exe"
            };
            foreach (var c in cands) { try { if (File.Exists(c)) return c; } catch { } }
            return "";
        }

        /// <summary>启动扫码伴侣（模板决定这次是二维码模式还是固定型号模式）</summary>
        internal bool Start(string template, out string err)
        {
            err = "";
            try
            {
                if (Running) return true;
                if (!File.Exists(_script))
                {
                    err = "没找到 modem_scanner.py（它要和《一体化工具.exe》放在同一个文件夹）";
                    LastError = err;
                    return false;
                }
                string py = FindPython();
                if (py.Length == 0)
                {
                    err = "没找到 Python（可以用「光猫扫码伴侣_启动.bat」那台的方式，或先装 Python 3.11）";
                    LastError = err;
                    return false;
                }
                try { File.WriteAllText(_cmdFile, "", new UTF8Encoding(false)); } catch { }   // 清掉旧命令
                var psi = new ProcessStartInfo();
                psi.FileName = py;
                psi.Arguments = "-u \"" + _script + "\" --template \"" + template + "\""
                             + " --control \"" + _cmdFile + "\""
                             + " --start-paused"
                             + " --parent-pid " + Process.GetCurrentProcess().Id.ToString()
                             + " --title \"光猫扫码伴侣（一体化工具调用）\"";
                psi.WorkingDirectory = Path.GetDirectoryName(_script);
                psi.UseShellExecute = false;
                // 不要控制台窗口：日志直接读进工具里显示（现代 Windows 的控制台属于 conhost，
                // 拿不到窗口句柄，之前"点开始弹出窗口"就是不生效）
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = new UTF8Encoding(false);
                psi.StandardErrorEncoding = new UTF8Encoding(false);
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                psi.EnvironmentVariables["PYTHONUTF8"] = "1";
                _proc = Process.Start(psi);
                try
                {
                    _proc.OutputDataReceived += (s, e) =>
                    {
                        if (string.IsNullOrEmpty(e.Data)) return;
                        lock (_lines)
                        {
                            _lines.Add(e.Data);
                            if (_lines.Count > 400) _lines.RemoveRange(0, _lines.Count - 400);
                        }
                        try { var h = Line; if (h != null) h(e.Data); } catch { }
                    };
                    _proc.ErrorDataReceived += (s, e) =>
                    {
                        if (string.IsNullOrEmpty(e.Data)) return;
                        try { var h = Line; if (h != null) h("[err] " + e.Data); } catch { }
                    };
                    _proc.BeginOutputReadLine();
                    _proc.BeginErrorReadLine();
                }
                catch { }
                LastError = "";
                return _proc != null;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                LastError = err;
                return false;
            }
        }

        /// <summary>给扫码伴侣发命令：pause / resume / stop / mode=qr / mode=fixed</summary>
        internal void Send(string cmd)
        {
            try { File.WriteAllText(_cmdFile, cmd, new UTF8Encoding(false)); } catch { }
        }

        /// <summary>显示/隐藏它的控制台窗口（等窗口建好，最多等 3 秒）</summary>
        internal bool ShowConsole(bool show)
        {
            try
            {
                if (!Running) return false;
                for (int i = 0; i < 12; i++)
                {
                    _proc.Refresh();
                    IntPtr h = _proc.MainWindowHandle;
                    if (h != IntPtr.Zero)
                    {
                        ShowWindow(h, show ? 9 : 0);        // 9=SW_RESTORE, 0=SW_HIDE
                        if (show) { try { SetForegroundWindow(h); } catch { } }
                        return true;
                    }
                    System.Threading.Thread.Sleep(250);
                }
            }
            catch { }
            return false;
        }

        /// <summary>关掉它（并发命令让它自己先退出，超时就强杀），确保摄像头被释放</summary>
        internal void Kill()
        {
            try
            {
                if (!Running) return;
                Send("stop");
                if (!_proc.WaitForExit(3000))
                {
                    try { _proc.Kill(); } catch { }
                    try { _proc.WaitForExit(1500); } catch { }
                }
            }
            catch { }
            finally
            {
                try { if (_proc != null) _proc.Dispose(); } catch { }
                _proc = null;
            }
        }
    }

    internal class MainForm : Form
    {
        private AppSettings _settings = new AppSettings();
        private string _dataDir;
        private string _settingsPath;
        private readonly List<DeviceRecord> _records = new List<DeviceRecord>();
        // 本次运行中“新加 / 删除”的记录键（保存时以磁盘为准，只应用本机的增删，避免覆盖外部修改/复制进来的历史）
        private readonly HashSet<string> _newKeys = new HashSet<string>();
        private readonly HashSet<string> _deletedKeys = new HashSet<string>();

        private TextBox txtScan, txtModel, txtType, txtSN, txtMAC;
        private TextBox txtNetPrinter;
        private Label lblStatus, lblStep, lblToday;
        private CheckBox chkAuto, chkModel, chkType, chkSN, chkMAC;
        private ComboBox cmbPrinter, cmbDpi, cmbBarWidth;
        private NumericUpDown numW, numH, numFont, numX, numY, numBarcodeH;
        private CheckBox chkItemVisible;
        private Label lblSel;
        private PictureBox picPreview;
        private Button btnResetLayout;
        private RoundedButton _btnScanStart, _btnScanStop;
        private Label _lblScanner;
        private CardGroup _grpScannerLog;
        private TextBox _txtScannerLog;
        private Timer _imeTimer;
        private DataGridView grid;
        private TextBox txtSearch;
        private Label lblCount;
        private CalDatePicker dpHistory;
        private DateTime _filterDate = DateTime.Today;
        private Timer _refreshTimer;
        // NAS 备份同步
        private TextBox txtNasPath;
        private RadioButton radNasOn, radNasOff;
        private Button btnBrowseNas, btnNasSync;
        private Label lblNasStatus;
        private System.Windows.Forms.Timer _nasTimer;
        private bool _nasSyncing;
        private bool _suppressNas;
        private string _searchText = "";
        // 刚刚打印过的那台（防止“摄像头已经打完 + 扫码枪又补扫一次”造成同一条重复打印）
        private string _lastPrintSn = "";
        private string _lastPrintMac = "";
        private DateTime _lastPrintAt = DateTime.MinValue;
        // MAC 是不是"这一台新扫进来的"（没被打印/保存消耗过）。
        // 用途：输入框里会保留上一台的内容，只有"新扫进来的 MAC"才能让二维码补扫时立刻打印，
        // 否则会拿上一台的 MAC 打出错标签。
        private bool _macDirty;

        private bool JustPrinted(string sn, string mac)
        {
            try
            {
                if ((DateTime.Now - _lastPrintAt).TotalSeconds > 20) return false;
                if (!string.IsNullOrEmpty(sn) && !string.IsNullOrEmpty(_lastPrintSn) &&
                    string.Equals(sn.Trim(), _lastPrintSn.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.IsNullOrEmpty(mac) && !string.IsNullOrEmpty(_lastPrintMac) &&
                    string.Equals(mac.Trim(), _lastPrintMac.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        private enum ScanState { AwaitQR, AwaitSN, AwaitMAC }
        private ScanState _scanState = ScanState.AwaitQR;
        private bool _fixedModel;
        private string _lastRawQR = "";

        private LayoutItem _selItem;
        private bool _dragging;
        private bool _moveAll;
        private double _allStartX, _allStartY;
        private double[] _allOrigX, _allOrigY;
        private double _dragOffX, _dragOffY;
        private RectangleF _selRect = RectangleF.Empty;
        private bool _suppressProps;

        // 型号待定（无二维码且历史查不到型号时的中间状态）
        private bool _pendingSnNoModel;

        // 供 OCR 匹配用的型号候选（来自历史记录 + 当前填的）
        internal List<string> ModelCandidates
        {
            get
            {
                var list = new List<string>();
                try
                {
                    foreach (var r in _records)
                        if (!string.IsNullOrWhiteSpace(r.Model) && !list.Contains(r.Model.Trim())) list.Add(r.Model.Trim());
                    string cur = txtModel != null ? txtModel.Text.Trim() : "";
                    if (cur.Length > 0 && !list.Contains(cur)) list.Add(cur);
                }
                catch { }
                return list;
            }
        }

        // EPON 报警：只要检出 EPON 就弹窗报警、不打印、不保存（点掉后继续等下一台）
        internal bool EponAlerted(string source)
        {
            try
            {
                Notify("检测到不支持的设备（EPON / ADSL），不能打印！来源：" + source, Color.Red, false);   // 强制弹窗
                try { Console.Beep(700, 500); } catch { }
                return true;
            }
            catch { return false; }
        }

        // 类型归一：只保留 GPON / EPON 两种
        internal string NormalizeType(string raw, string extraText)
        {
            try
            {
                string all = (raw ?? "") + " " + (extraText ?? "") + " " + txtType.Text;
                if (WinOcr.HasEpon(all)) return "EPON";
                if (WinOcr.HasGpon(all)) return "GPON";
            }
            catch { }
            return raw;
        }

        // 摄像头/OCR 一次填好型号、类型、SN、MAC（无二维码标签走这条路），随后按设置自动打印
        internal bool FillFromOcr(string model, string type, string sn, string mac)
        {
            try
            {
                bool any = false;
                // EPON 直接报警拦下（不打印、不保存）
                if (WinOcr.HasEpon(model + " " + type + " " + txtType.Text + " " + _lastRawQR)) { EponAlerted("标签文字"); return true; }
                type = NormalizeType(type, model + " " + _lastRawQR + " " + txtSN.Text + " " + (System.IO.File.Exists("")?"":"") );
                if (!string.IsNullOrWhiteSpace(model)) { txtModel.Text = model.Trim(); any = true; }
                if (!string.IsNullOrWhiteSpace(type)) { txtType.Text = type.Trim(); any = true; }
                if (!string.IsNullOrWhiteSpace(sn)) { txtSN.Text = sn.Trim(); any = true; }
                if (!string.IsNullOrWhiteSpace(mac)) { txtMAC.Text = mac.Trim(); any = true; }
                if (!any) return false;
                _pendingSnNoModel = false;
                _lastRawQR = "";
                _scanState = ScanState.AwaitMAC;
                bool complete = !string.IsNullOrWhiteSpace(txtSN.Text) && !string.IsNullOrWhiteSpace(txtMAC.Text);
                if (complete && chkAuto.Checked)
                {
                    SetStatus("已从标签文字读出 型号/SN/MAC，正在打印…", Color.SeaGreen);
                    SaveAndPrint(true);
                    return true;
                }
                _scanState = complete ? ScanState.AwaitQR : ScanState.AwaitMAC;
                SetStatus(complete ? "已从标签文字读出 型号/SN/MAC（等待打印）" : "已从标签文字读出部分信息，等待 MAC", Color.DodgerBlue);
                return true;
            }
            catch { return false; }
        }

        // OCR 识别出型号后填入，并自动带出类型
        internal bool FillModelFromOcr(string model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model)) return false;
                string type = "";
                foreach (var r in _records)
                    if (!string.IsNullOrWhiteSpace(r.Model) && r.Model.Trim().Equals(model.Trim(), StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(r.Type)) { type = r.Type; break; }
                txtModel.Text = model.Trim();
                if (!string.IsNullOrEmpty(type)) txtType.Text = type;
                if (_scanState == ScanState.AwaitQR && !string.IsNullOrWhiteSpace(txtSN.Text))
                {
                    _pendingSnNoModel = false;
                    _scanState = ScanState.AwaitMAC;
                    SetStatus("型号已识别（" + model + "），接下来对齐 MAC 条码", Color.DodgerBlue);
                }
                else if (_pendingSnNoModel && !string.IsNullOrWhiteSpace(txtModel.Text))
                {
                    _pendingSnNoModel = false;
                    _scanState = ScanState.AwaitMAC;
                    SetStatus("型号已填（" + model + "），接下来对齐 MAC 条码", Color.DodgerBlue);
                }
                else SetStatus("已填入型号：" + model, Color.SeaGreen);
                return true;
            }
            catch { return false; }
        }

        // 摄像头模式：把打印流程里的模态弹框改成状态提示，避免打断连续识别
        internal bool SuppressDialogs = false;
        internal string LastNotice = "";
        internal event EventHandler NoticeChanged;
        // 打印完成（不管是摄像头读的、扫码枪扫的还是手动点的）——摄像头窗口靠它避免“同一台又打一次”
        internal event EventHandler Printed;

        private void RaisePrinted()
        {
            try { var h = Printed; if (h != null) h(this, EventArgs.Empty); } catch { }
        }

        private void Notify(string msg, Color c, bool modal)
        {
            LastNotice = msg;
            SetStatus(msg, c);
            if (!modal && !SuppressDialogs)
            {
                try { MessageBox.Show(msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); } catch { }
            }
            try { var h = NoticeChanged; if (h != null) h(this, EventArgs.Empty); } catch { }
        }

        public MainForm()
        {
            try
            {
                Init();
            }
            catch (Exception ex)
            {
                ErrorLog.Show(ex);
                try { Environment.Exit(1); } catch { }
            }
        }

        private void Init()
        {
            Text = "设备标签打印软件";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1000, 764);
            MinimumSize = new Size(900, 690);
            Font = Ui.BaseFont;
            StartPosition = FormStartPosition.CenterScreen;

            _dataDir = GetDataDir();
            _settingsPath = Path.Combine(_dataDir, "设置.ini");
            _settings.Path = _settingsPath;
            _settings.Load();
            // 读取“全部”历史（所有按天文件 + 旧的单文件），重开软件能看到以前所有记录，而不是只读今天那一个文件
            _records.AddRange(LoadAllHistory());

            BuildUi2();
            ApplyUi();
            if (dpHistory != null) dpHistory.FocusAfterClose = txtScan;   // 选完日期把光标还给扫码框
            RefreshPrinters(true);
            // 默认显示“最近有记录的那一天”，避免打开时是空的（可用日历悬停看哪天有记录）
            if (dpHistory != null && _records.Count > 0)
            {
                DateTime latest = _records.Max(r => r.Time.Date);
                _filterDate = latest;
                dpHistory.Value = latest;
            }
            LoadHistoryGrid();
            ApplySettingsToUi();
            ApplyChecksToLayout();
            ApplyBarcodeWidthToLayout();
            RestoreLastPrinted();   // 重开软件后恢复上次打印的内容到预览
            UpdatePreview();
            UpdateTodayCount();
            _refreshTimer = new Timer { Interval = 4000 };
            _refreshTimer.Tick += (s, e) => { _refreshTimer.Stop(); RefreshPrinters(false); };
            Shown += (s, e) => { txtScan.Focus(); };
            Shown += (s, e) => StartAutoCheck();
            // 打开工具就自动把「光猫扫码伴侣」拉起来（窗口先藏起来、处于暂停），
            // 工人点「开始识别」才显示它的窗口并开始干活 —— 省掉双击那个 bat 的过程
            Shown += (s, e) =>
            {
                var t = new Timer { Interval = 1200 };
                t.Tick += (s2, e2) =>
                {
                    t.Stop();
                    EnsureCompanion();
                    UpdateScannerUi();
                };
                t.Start();
            };
            FormClosing += (s, e) => { try { if (_companion != null) _companion.Kill(); } catch { } };
            // 打开软件就把输入法切成英文；并且每 3 秒"看一次"——只要光标在扫码框里就保持英文
            Shown += (s, e) =>
            {
                var t = new Timer { Interval = 800 };
                t.Tick += (s2, e2) => { t.Stop(); AutoSwitchToEnglish(); };
                t.Start();
            };
            _imeTimer = new Timer { Interval = 3000 };
            _imeTimer.Tick += (s, e) =>
            {
                try
                {
                    if (!_settings.AutoEnglish) return;
                    if (txtScan != null && txtScan.Focused) AutoSwitchToEnglish();
                    else if (ActiveControl == txtScan) AutoSwitchToEnglish();
                }
                catch { }
            };
            _imeTimer.Start();
            FormClosing += (s, e) => SaveAll();
        }

        // 套用统一主题：窗口底色 + 递归统一各控件配色 / 字体 / 圆角
        private void ApplyUi()
        {
            BackColor = Ui.Back;
            StyleTree(this, true);
        }

        private void StyleTree(Control c, bool isRoot)
        {
            if (c is RoundedButton)
            {
                RoundedButton rb = (RoundedButton)c;
                rb.Kind = ButtonKindFor(rb.Text);
                if (rb.Text == "打印当前设备") rb.Font = Ui.HeadingFont;
                rb.Invalidate();
            }
            else if (c is Button)
            {
                Button b = (Button)c;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.BackColor = Ui.Accent;
                b.ForeColor = Color.White;
            }
            else if (c is GroupBox)
            {
                GroupBox gb = (GroupBox)c;
                gb.BackColor = Ui.Card;
                gb.ForeColor = Ui.Text;
            }
            else if (c is DataGridView)
            {
                StyleGrid((DataGridView)c);
            }
            else if (c is TextBox)
            {
                TextBox tbx = (TextBox)c;
                tbx.BackColor = Color.White;
                tbx.ForeColor = Ui.Text;
                tbx.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (c is ComboBox)
            {
                ComboBox cb = (ComboBox)c;
                cb.BackColor = Color.White; cb.ForeColor = Ui.Text;
            }
            else if (c is NumericUpDown)
            {
                NumericUpDown nud = (NumericUpDown)c;
                nud.ForeColor = Ui.Text;
            }
            else if (c is CheckBox)
            {
                CheckBox chk = (CheckBox)c;
                chk.ForeColor = Ui.Text; chk.BackColor = Color.Transparent;
            }
            else if (c is Label)
            {
                Label lb = (Label)c;
                lb.ForeColor = Ui.Resolve(lb.ForeColor);
                lb.BackColor = Color.Transparent;
            }
            else if (c is TableLayoutPanel || c is FlowLayoutPanel || c is Panel)
            {
                c.BackColor = Color.Transparent;
            }
            else if (c is PictureBox) { /* 保留白色标签预览底 */ }

            foreach (Control ch in c.Controls) StyleTree(ch, false);
        }

        private static ButtonKind ButtonKindFor(string text)
        {
            text = (text ?? "").Trim();
            if (text == "打印当前设备" || text == "连接" || text == "恢复默认排版") return ButtonKind.Primary;
            if (text == "删除选中" || text == "清空全部") return ButtonKind.Danger;
            return ButtonKind.Secondary;
        }

        private static void StyleGrid(DataGridView g)
        {
            g.BackgroundColor = Color.White;
            g.BorderStyle = BorderStyle.None;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.GridColor = Ui.Border;
            g.RowHeadersVisible = false;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            g.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            g.ColumnHeadersHeight = 32;
            var h = g.ColumnHeadersDefaultCellStyle;
            h.BackColor = Ui.GridHdr;
            h.ForeColor = Ui.Accent;
            h.SelectionBackColor = Ui.GridHdr;
            h.SelectionForeColor = Ui.Accent;
            h.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            h.Alignment = DataGridViewContentAlignment.MiddleLeft;
            h.Padding = new Padding(4, 0, 4, 0);
            var d = g.DefaultCellStyle;
            d.BackColor = Color.White;
            d.ForeColor = Ui.Text;
            d.SelectionBackColor = Ui.AccentSoft;
            d.SelectionForeColor = Ui.Text;
            d.Padding = new Padding(4, 2, 4, 2);
            g.AlternatingRowsDefaultCellStyle.BackColor = Ui.RowAlt;
            g.AlternatingRowsDefaultCellStyle.SelectionBackColor = Ui.AccentSoft;
            g.AlternatingRowsDefaultCellStyle.SelectionForeColor = Ui.Text;
            g.RowTemplate.Height = 28;
        }

        private void CheckUpdate(bool interactive)
        {
            if (string.IsNullOrWhiteSpace(_settings.UpdateUrl))
            {
                if (interactive) ConfigureUpdate();
                return;
            }
            try
            {
                string nv, dl, notes;
                var res = Updater.FindNewVersion(_settings.UpdateUrl, _settings.UpdateToken, Updater.AppVersion, out nv, out dl, out notes);
                if (res == Updater.UpdateCheckResult.Error)
                {
                    if (interactive)
                        MessageBox.Show("检查更新失败：无法连接更新服务器，请检查网络后重试。", "更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (res == Updater.UpdateCheckResult.NoUpdate)
                {
                    if (interactive) SetStatus("已是最新版本（v" + Updater.AppVersion + "）", Color.SeaGreen);
                    return;
                }
                if (MessageBox.Show("发现新版本 v" + nv + "（当前 v" + Updater.AppVersion + "）。\n请确认下载并更新。\n\n" + notes, "发现新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    DoUpdate(dl, nv);
            }
            catch (Exception ex)
            {
                if (interactive) MessageBox.Show("检查更新失败：" + ex.Message, "更新", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ConfigureUpdate()
        {
            string url = Microsoft.VisualBasic.Interaction.InputBox("请输入更新地址：\n可以是 GitHub 仓库地址（例如 https://github.com/xxx/标签打印软件），\n或返回 JSON 的更新接口地址。", "配置更新", _settings.UpdateUrl);
            if (string.IsNullOrWhiteSpace(url)) return;
            url = url.Trim();
            string token = Microsoft.VisualBasic.Interaction.InputBox("如有访问令牌（Token）请粘贴，没有可留空：", "配置更新令牌", _settings.UpdateToken);
            _settings.UpdateUrl = url;
            _settings.UpdateToken = token == null ? "" : token.Trim();
            _settings.Save();
            CheckUpdate(true);
        }

        private void StartAutoCheck()
        {
            if (string.IsNullOrWhiteSpace(_settings.UpdateUrl)) return;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string nv, dl, notes;
                    var res = Updater.FindNewVersion(_settings.UpdateUrl, _settings.UpdateToken, Updater.AppVersion, out nv, out dl, out notes);
                    if (res == Updater.UpdateCheckResult.UpdateAvailable && !IsDisposed)
                        BeginInvoke((Action)(() =>
                        {
                            if (MessageBox.Show("发现新版本 v" + nv + "（当前 v" + Updater.AppVersion + "）。是否下载并更新？\n\n" + notes, "发现新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                                DoUpdate(dl, nv);
                        }));
                }
                catch { }
            });
        }

        private void DoUpdate(string downloadUrl, string newVersion)
        {
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), "label_update_" + Guid.NewGuid().ToString("N") + ".exe");
                Updater.Download(downloadUrl, _settings.UpdateToken, tmp);
                SetStatus("正在更新到 v" + newVersion + "，程序将自动重启…", Color.DarkOrange);
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);
                Updater.InstallAndRelaunch(tmp, Application.ExecutablePath);
                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show("下载更新失败：" + ex.Message + "\n请稍后重试。", "更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string GetDataDir()
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            try
            {
                string dir = Path.Combine(exeDir, "数据");
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch
            {
                string fb = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "设备标签打印软件");
                try { Directory.CreateDirectory(fb); return fb; }
                catch { return Path.Combine(Path.GetTempPath(), "设备标签打印软件数据"); }
            }
        }

        private void BuildUiOld()
        {
            // ---------- Left: scan input ----------
            var gLeft = new CardGroup { Text = "扫码录入", Location = new Point(8, 8), Size = new Size(250, 478) };
            Controls.Add(gLeft);

            lblStatus = new Label { Location = new Point(10, 22), Size = new Size(230, 40), Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold), Text = "请扫描设备二维码…", ForeColor = Color.DodgerBlue };
            gLeft.Controls.Add(lblStatus);

            gLeft.Controls.Add(new Label { Text = "扫码输入框（扫完自动识别，无需点击）", Location = new Point(10, 64), Size = new Size(230, 18) });
            txtScan = new TextBox { Location = new Point(10, 82), Size = new Size(230, 30), Font = new Font("Consolas", 12F) };
            txtScan.KeyDown += TxtScan_KeyDown;
            gLeft.Controls.Add(txtScan);

            lblStep = new Label { Location = new Point(10, 116), Size = new Size(230, 34), AutoSize = false, Text = "第 1 步：扫描二维码\n第 2 步：扫描 MAC 条码", ForeColor = Color.Gray };
            gLeft.Controls.Add(lblStep);

            gLeft.Controls.Add(new Label { Text = "当前设备数据（可手动修改）", Location = new Point(10, 158), Size = new Size(230, 20), AutoSize = false, Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold) });

            AddField(gLeft, "型号：", 186, out txtModel);
            AddField(gLeft, "类型：", 226, out txtType);
            AddField(gLeft, "SN：", 266, out txtSN);
            AddField(gLeft, "MAC：", 306, out txtMAC);
            foreach (var tb in new[] { txtModel, txtType, txtSN, txtMAC })
                tb.TextChanged += (s, e) => UpdatePreview();

            chkAuto = new CheckBox { Text = "扫描完成后自动打印并保存", Location = new Point(10, 354), Size = new Size(230, 24), Checked = _settings.AutoPrint };
            chkAuto.CheckedChanged += (s, e) => _settings.AutoPrint = chkAuto.Checked;
            gLeft.Controls.Add(chkAuto);

            var btnPrint = new RoundedButton { Text = "打印当前设备", Location = new Point(10, 384), Size = new Size(230, 34), Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold) };
            btnPrint.Click += (s, e) => SaveAndPrint(false);
            gLeft.Controls.Add(btnPrint);

            var btnSave = new RoundedButton { Text = "仅保存", Location = new Point(10, 424), Size = new Size(110, 28) };
            btnSave.Click += (s, e) => SaveCurrent(false);
            gLeft.Controls.Add(btnSave);

            var btnClear = new RoundedButton { Text = "清空输入", Location = new Point(128, 424), Size = new Size(112, 28) };
            btnClear.Click += (s, e) => { ClearInputs(); };
            gLeft.Controls.Add(btnClear);

            gLeft.Controls.Add(new Label { Text = "提示：历史记录自动保存，随时可重印。", Location = new Point(10, 456), Size = new Size(230, 16), ForeColor = Color.Gray });

            // ---------- Middle: preview ----------
            var gPrev = new CardGroup { Text = "标签预览（鼠标拖动文字/条码可调整位置）", Location = new Point(266, 8), Size = new Size(458, 514), Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
            Controls.Add(gPrev);
            picPreview = new PictureBox { Location = new Point(10, 22), Size = new Size(438, 460), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.Zoom };
            picPreview.MouseDown += Pic_MouseDown;
            picPreview.MouseMove += Pic_MouseMove;
            picPreview.MouseUp += Pic_MouseUp;
            picPreview.Paint += Pic_Paint;
            picPreview.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom;
            gPrev.Controls.Add(picPreview);

            var lblSize = new Label { Location = new Point(10, 490), Size = new Size(438, 16), Anchor = AnchorStyles.Left | AnchorStyles.Bottom | AnchorStyles.Right, ForeColor = Color.Gray, Text = "" };
            _lblSizeHint = lblSize;
            gPrev.Controls.Add(lblSize);

            // ---------- Right: settings ----------
            var gPrinter = new CardGroup { Text = "打印机（可添加网络/局域网打印机）", Location = new Point(732, 8), Size = new Size(260, 136), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            Controls.Add(gPrinter);
            gPrinter.Controls.Add(new Label { Text = "选择打印机：", Location = new Point(8, 18) });
            cmbPrinter = new ComboBox { Location = new Point(8, 38), Size = new Size(232, 26), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbPrinter.SelectedIndexChanged += (s, e) => { if (cmbPrinter.SelectedItem != null) _settings.Printer = cmbPrinter.SelectedItem.ToString(); };
            gPrinter.Controls.Add(cmbPrinter);
            var btnRefresh = new RoundedButton { Text = "刷新", Location = new Point(8, 72), Size = new Size(66, 26) };
            btnRefresh.Click += (s, e) => RefreshPrinters(false);
            gPrinter.Controls.Add(btnRefresh);
            var btnAddNet = new RoundedButton { Text = "添加网络打印机…", Location = new Point(80, 72), Size = new Size(170, 26) };
            btnAddNet.Click += (s, e) => AddNetworkPrinter();
            gPrinter.Controls.Add(btnAddNet);
            txtNetPrinter = new TextBox { Location = new Point(8, 106), Size = new Size(140, 24) };
            gPrinter.Controls.Add(txtNetPrinter);
            var btnConnect = new RoundedButton { Text = "连接", Location = new Point(154, 104), Size = new Size(96, 28) };
            btnConnect.Click += (s, e) => ConnectNetworkPrinter();
            gPrinter.Controls.Add(btnConnect);

            var gSize = new CardGroup { Text = "标签规格（宽/高，单位 mm）", Location = new Point(732, 146), Size = new Size(260, 72), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            Controls.Add(gSize);
            gSize.Controls.Add(new Label { Text = "宽", Location = new Point(8, 20) });
            numW = new NumericUpDown { Location = new Point(40, 16), Size = new Size(64, 24), Minimum = 5, Maximum = 200, DecimalPlaces = 1, Increment = 0.5m };
            gSize.Controls.Add(numW);
            gSize.Controls.Add(new Label { Text = "高", Location = new Point(126, 20) });
            numH = new NumericUpDown { Location = new Point(158, 16), Size = new Size(64, 24), Minimum = 5, Maximum = 300, DecimalPlaces = 1, Increment = 0.5m };
            gSize.Controls.Add(numH);
            gSize.Controls.Add(new Label { Text = "打印精度", Location = new Point(8, 46) });
            cmbDpi = new ComboBox { Location = new Point(56, 42), Size = new Size(140, 26), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbDpi.Items.AddRange(new object[] { "203 dpi", "300 dpi" });
            gSize.Controls.Add(cmbDpi);
            numW.ValueChanged += (s, e) => { _settings.LabelWidthMm = (double)numW.Value; numX.Maximum = (decimal)_settings.LabelWidthMm; ApplyChecksToLayout(); UpdatePreview(); };
            numH.ValueChanged += (s, e) => { _settings.LabelHeightMm = (double)numH.Value; numY.Maximum = (decimal)_settings.LabelHeightMm; UpdatePreview(); };
            cmbDpi.SelectedIndexChanged += (s, e) => { if (cmbDpi.SelectedIndex >= 0) { _settings.Dpi = cmbDpi.SelectedIndex == 0 ? 203 : 300; UpdatePreview(); } };

            var gContent = new CardGroup { Text = "打印内容（可多选）", Location = new Point(732, 224), Size = new Size(260, 98), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            Controls.Add(gContent);
            chkModel = new CheckBox { Text = "型号", Location = new Point(10, 20), Size = new Size(120, 22), Checked = _settings.ShowModel };
            chkType = new CheckBox { Text = "类型", Location = new Point(138, 20), Size = new Size(114, 22), Checked = _settings.ShowType };
            chkSN = new CheckBox { Text = "SN（条码＋文字）", Location = new Point(10, 48), Size = new Size(140, 22), Checked = _settings.ShowSN };
            chkMAC = new CheckBox { Text = "MAC（条码＋文字）", Location = new Point(10, 76), Size = new Size(140, 22), Checked = _settings.ShowMAC };
            foreach (var c in new[] { chkModel, chkType, chkSN, chkMAC })
            {
                c.CheckedChanged += (s, e) => { ApplyChecksToLayout(); UpdatePreview(); };
                gContent.Controls.Add(c);
            }

            var gProp = new CardGroup { Text = "排版属性（选中预览中的元素后编辑）", Location = new Point(732, 328), Size = new Size(260, 198), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            Controls.Add(gProp);
            lblSel = new Label { Location = new Point(8, 10), Size = new Size(234, 20), Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold), Text = "未选择元素" };
            gProp.Controls.Add(lblSel);
            gProp.Controls.Add(new Label { Text = "字体大小(pt)", Location = new Point(8, 36) });
            numFont = new NumericUpDown { Location = new Point(96, 32), Size = new Size(102, 24), Minimum = 4, Maximum = 40, DecimalPlaces = 1, Increment = 0.5m };
            numFont.ValueChanged += (s, e) => { if (!_suppressProps && _selItem != null && !_selItem.IsBarcode) { _selItem.FontSizePt = (double)numFont.Value; UpdatePreview(); } };
            gProp.Controls.Add(numFont);
            gProp.Controls.Add(new Label { Text = "水平位置(mm)", Location = new Point(8, 62) });
            numX = new NumericUpDown { Location = new Point(96, 58), Size = new Size(102, 24), Minimum = 0, Maximum = 200, DecimalPlaces = 1, Increment = 0.1m };
            numX.ValueChanged += (s, e) => { if (!_suppressProps && _selItem != null) { _selItem.Xmm = (double)numX.Value; UpdatePreview(); } };
            gProp.Controls.Add(numX);
            gProp.Controls.Add(new Label { Text = "垂直位置(mm)", Location = new Point(8, 88) });
            numY = new NumericUpDown { Location = new Point(96, 84), Size = new Size(102, 24), Minimum = 0, Maximum = 300, DecimalPlaces = 1, Increment = 0.1m };
            numY.ValueChanged += (s, e) => { if (!_suppressProps && _selItem != null) { _selItem.Ymm = (double)numY.Value; UpdatePreview(); } };
            gProp.Controls.Add(numY);
            gProp.Controls.Add(new Label { Text = "条码高度(mm)", Location = new Point(8, 114) });
            numBarcodeH = new NumericUpDown { Location = new Point(96, 110), Size = new Size(102, 24), Minimum = 5, Maximum = 60, DecimalPlaces = 1, Increment = 0.5m };
            numBarcodeH.ValueChanged += (s, e) => { if (!_suppressProps && _selItem != null && _selItem.IsBarcode) { _selItem.HeightMm = (double)numBarcodeH.Value; UpdatePreview(); } };
            gProp.Controls.Add(numBarcodeH);
            chkItemVisible = new CheckBox { Text = "在标签上显示此元素", Location = new Point(8, 140), Size = new Size(200, 22) };
            chkItemVisible.CheckedChanged += (s, e) => { if (!_suppressProps && _selItem != null) { _selItem.Visible = chkItemVisible.Checked; ApplyChecksToLayout(); UpdatePreview(); } };
            gProp.Controls.Add(chkItemVisible);
            btnResetLayout = new RoundedButton { Text = "恢复默认排版", Location = new Point(8, 166), Size = new Size(150, 26) };
            btnResetLayout.Click += (s, e) => { _settings.Layout = LayoutItem.DefaultLayout(_settings.LabelWidthMm, _settings.LabelHeightMm); ApplyChecksToLayout(); UpdatePreview(); SetStatus("已恢复默认排版", Color.Gray); };
            gProp.Controls.Add(btnResetLayout);

            // ---------- Bottom: history ----------
            var gHist = new CardGroup { Text = "历史记录（自动保存，双击可重印）", Location = new Point(8, 530), Size = new Size(984, 140), Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom };
            Controls.Add(gHist);

            grid = new DataGridView { Location = new Point(12, 22), Size = new Size(780, 92), ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom };
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.Columns.Add("colTime", "时间");
            grid.Columns.Add("colModel", "型号");
            grid.Columns.Add("colType", "类型");
            grid.Columns.Add("colSN", "SN");
            grid.Columns.Add("colMAC", "MAC");
            grid.Columns.Add("colPrintTime", "打印时间");
            grid.ScrollBars = ScrollBars.Vertical;
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) { var tag = grid.Rows[e.RowIndex].Tag as DeviceRecord; if (tag != null) Reprint(tag); } };
            gHist.Controls.Add(grid);

            var btnReprint = new RoundedButton { Text = "重印选中", Location = new Point(802, 22), Size = new Size(170, 24), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnReprint.Click += (s, e) => { var rec = SelectedRecord(); if (rec != null) Reprint(rec); };
            gHist.Controls.Add(btnReprint);
            var btnDel = new RoundedButton { Text = "删除选中", Location = new Point(802, 50), Size = new Size(170, 24), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnDel.Click += (s, e) => DeleteSelected();
            gHist.Controls.Add(btnDel);
            var btnClearAll = new RoundedButton { Text = "清空全部", Location = new Point(802, 78), Size = new Size(170, 24), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnClearAll.Click += (s, e) => ClearAllHistory();
            gHist.Controls.Add(btnClearAll);
            var btnFolder = new RoundedButton { Text = "打开数据文件夹", Location = new Point(802, 106), Size = new Size(170, 24), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnFolder.Click += (s, e) => { try { System.Diagnostics.Process.Start("explorer.exe", _dataDir); } catch { } };
            gHist.Controls.Add(btnFolder);

            lblToday = new Label { Text = "今日已打印：0 台", Location = new Point(12, 112), Size = new Size(300, 20), Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold), ForeColor = Color.SeaGreen };
            gHist.Controls.Add(lblToday);
            var lblHint = new Label { Text = "记录保存在 数据\\历史记录.csv，可用 Excel 打开", Location = new Point(220, 112), Size = new Size(560, 20), Anchor = AnchorStyles.Left | AnchorStyles.Bottom, ForeColor = Color.Gray };
            gHist.Controls.Add(lblHint);
        }

        private void AddField(Control parent, string label, int y, out TextBox box)
        {
            parent.Controls.Add(new Label { Text = label, Location = new Point(10, y), Size = new Size(58, 24) });
            box = new TextBox { Location = new Point(68, y - 2), Size = new Size(172, 24) };
            parent.Controls.Add(box);
        }

        // ---------- 自适应布局（TableLayoutPanel / FlowLayoutPanel，自动适配 DPI 缩放） ----------
        private void BuildUi2()
        {
            SuspendLayout();

            var gHist = BuildHistoryGroup();
            gHist.Dock = DockStyle.Bottom;
            gHist.Height = 214;

            var main = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(8),
                Margin = Padding.Empty
            };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 296));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var leftScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = new Padding(0, 0, 8, 0), Padding = Padding.Empty };
            var gLeft = BuildScanGroup();
            gLeft.Dock = DockStyle.Top;
            gLeft.AutoSize = true;
            gLeft.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            gLeft.Margin = Padding.Empty;
            leftScroll.Controls.Add(gLeft);

            var gPrev = BuildPreviewGroup();
            gPrev.Dock = DockStyle.Fill;
            gPrev.Margin = new Padding(0, 0, 8, 0);

            // 中栏：上面一条"扫码伴侣日志（实时）"，下面是标签预览
            // （日志以前放在左侧扫码卡片里，太窄看不清；放这里宽、够用）
            var midCol = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            midCol.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            midCol.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            midCol.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            BuildScannerLogPanel();
            _grpScannerLog.Dock = DockStyle.Fill;
            _grpScannerLog.Margin = new Padding(0, 0, 8, 6);
            midCol.Controls.Add(_grpScannerLog, 0, 0);
            midCol.Controls.Add(gPrev, 0, 1);

            var rightScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty, Padding = Padding.Empty };
            var rightCol = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(0),
                Margin = Padding.Empty
            };
            for (int i = 0; i < 4; i++) rightCol.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var gp = BuildPrinterGroup(); gp.Dock = DockStyle.Top; gp.Margin = new Padding(0, 0, 0, 6);
            var gs = BuildSizeGroup(); gs.Dock = DockStyle.Top; gs.Margin = new Padding(0, 0, 0, 6);
            var gc = BuildContentGroup(); gc.Dock = DockStyle.Top; gc.Margin = new Padding(0, 0, 0, 6);
            var gprop = BuildPropGroup(); gprop.Dock = DockStyle.Top; gprop.Margin = new Padding(0, 0, 0, 4);
            rightCol.Controls.Add(gp, 0, 0);
            rightCol.Controls.Add(gs, 0, 1);
            rightCol.Controls.Add(gc, 0, 2);
            rightCol.Controls.Add(gprop, 0, 3);
            rightScroll.Controls.Add(rightCol);

            main.Controls.Add(leftScroll, 0, 0);
            main.Controls.Add(midCol, 1, 0);
            main.Controls.Add(rightScroll, 2, 0);

            Controls.Add(main);
            Controls.Add(gHist);
            ResumeLayout();
        }

        private GroupBox BuildScanGroup()
        {
            var g = new CardGroup { Text = "扫码录入", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 22, 10, 14), Margin = Padding.Empty };
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 18, Padding = new Padding(0), Margin = Padding.Empty };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 18; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var modeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
            modeRow.Controls.Add(new Label { Text = "录入方式：", AutoSize = true, Margin = new Padding(0, 4, 6, 0) });
            var cmbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new Padding(0, 2, 0, 6) };
            cmbMode.Items.AddRange(new object[] { "二维码模式", "固定型号模式" });
            cmbMode.SelectedIndexChanged += (s, e) => SetMode(cmbMode.SelectedIndex == 1);
            modeRow.Controls.Add(cmbMode);
            t.Controls.Add(modeRow, 0, 0); t.SetColumnSpan(modeRow, 2);

            // 摄像头识别：开始/暂停、停止（选好录入方式后直接开，不用另开 Python 脚本）
            var scanRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4), Padding = Padding.Empty };
            _btnScanStart = new RoundedButton { Text = "开始识别", Width = 118, Height = 30, Margin = new Padding(0, 0, 6, 0) };
            _btnScanStart.Click += (s, e) => ScannerStartPause();
            scanRow.Controls.Add(_btnScanStart);
            _btnScanStop = new RoundedButton { Text = "停止", Width = 66, Height = 30, Margin = new Padding(0, 0, 8, 0) };
            _btnScanStop.Click += (s, e) => ScannerStop();
            scanRow.Controls.Add(_btnScanStop);
            t.Controls.Add(scanRow, 0, 1); t.SetColumnSpan(scanRow, 2);

            // 状态单独占一行：跟按钮挤在一行时放不下会被裁成一条细边（之前的界面问题）
            _lblScanner = new Label { Text = "摄像头识别：已停止", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(0, 0, 0, 6) };
            t.Controls.Add(_lblScanner, 0, 2); t.SetColumnSpan(_lblScanner, 2);

            lblStatus = new Label { Text = "请扫描设备二维码…", AutoSize = true, Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold), ForeColor = Color.DodgerBlue, Margin = new Padding(0, 0, 0, 8) };
            t.Controls.Add(lblStatus, 0, 3); t.SetColumnSpan(lblStatus, 2);

            var lblScanHint = new Label { Text = "扫码输入框（扫完自动识别，无需点击）", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
            t.Controls.Add(lblScanHint, 0, 4); t.SetColumnSpan(lblScanHint, 2);

            txtScan = new TextBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 12F), Margin = new Padding(0, 2, 0, 8) };
            txtScan.KeyDown += TxtScan_KeyDown;
            // 光标一进扫码框就把中文输入法切成英文（不然模拟按键会被输入法吃掉，草显示"没反应"）
            txtScan.GotFocus += (s, e) => AutoSwitchToEnglish();
            t.Controls.Add(txtScan, 0, 5); t.SetColumnSpan(txtScan, 2);

            lblStep = new Label { Text = "第 1 步：扫描二维码\n第 2 步：扫描 MAC 条码", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(0, 0, 0, 6) };
            t.Controls.Add(lblStep, 0, 6); t.SetColumnSpan(lblStep, 2);

            var lblHeader = new Label { Text = "当前设备数据（可手动修改）", AutoSize = true, Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold), Margin = new Padding(0, 4, 0, 6) };
            t.Controls.Add(lblHeader, 0, 7); t.SetColumnSpan(lblHeader, 2);

            AddFieldRow(t, "型号：", 8, out txtModel);
            AddFieldRow(t, "类型：", 9, out txtType);
            AddFieldRow(t, "SN：", 10, out txtSN);
            AddFieldRow(t, "MAC：", 11, out txtMAC);
            foreach (var tb in new[] { txtModel, txtType, txtSN, txtMAC })
                tb.TextChanged += (s, e) =>
                {
                    // 没有二维码时：手动填上型号后自动继续去对齐 MAC
                    if (_pendingSnNoModel && !string.IsNullOrWhiteSpace(txtModel.Text))
                    {
                        _pendingSnNoModel = false;
                        _scanState = ScanState.AwaitMAC;
                        SetStatus("型号已填，接下来对齐 MAC 条码", Color.DodgerBlue);
                    }
                    UpdatePreview();
                };
            // 只差"型号/类型"的标签：填完型号一离开输入框，就把这一台接着打完（不用再点按钮）
            txtModel.Leave += (s, e) => AutoFinishAfterModelFilled();
            txtType.Leave += (s, e) => AutoFinishAfterModelFilled();

            chkAuto = new CheckBox { Text = "扫描完成后自动打印并保存", AutoSize = true, Checked = _settings.AutoPrint, Margin = new Padding(0, 2, 0, 6) };
            chkAuto.CheckedChanged += (s, e) => _settings.AutoPrint = chkAuto.Checked;
            t.Controls.Add(chkAuto, 0, 12); t.SetColumnSpan(chkAuto, 2);

            var btnPrint = new RoundedButton { Text = "打印当前设备", Dock = DockStyle.Fill, Height = 34, Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) };
            btnPrint.Click += (s, e) => SaveAndPrint(false);
            t.Controls.Add(btnPrint, 0, 13); t.SetColumnSpan(btnPrint, 2);

            var btnSave = new RoundedButton { Text = "仅保存", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
            btnSave.Click += (s, e) => SaveCurrent(false);
            t.Controls.Add(btnSave, 0, 14);
            var btnClear = new RoundedButton { Text = "清空输入", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 0) };
            btnClear.Click += (s, e) => ClearInputs();
            t.Controls.Add(btnClear, 1, 14);

            // 摄像头 / 高拍仪识别（免驱 UVC 设备；识别到二维码+MAC 就自动打印）
            var btnCam = new RoundedButton { Text = "摄像头 / 高拍仪识别…", Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            btnCam.Click += (s, e) => OpenCameraForm();
            t.Controls.Add(btnCam, 0, 15); t.SetColumnSpan(btnCam, 2);

            // 底部提示：限制在列宽内自动换行，避免文字超出卡片被右侧/底部裁切
            var lblHintBottom = new Label
            {
                Text = "提示：历史记录自动保存，\n随时可重印。",
                AutoSize = false,
                Dock = DockStyle.Fill,
                AutoEllipsis = false,
                ForeColor = Color.Gray,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 4, 0, 2)
            };
            // 自动切英文输入法（防止扫码伴侣的模拟按键被中文输入法吃掉）
            var imeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0), Padding = Padding.Empty };
            var chkEnglish = new CheckBox
            {
                Text = "自动切英文输入法",
                AutoSize = true,
                Checked = _settings.AutoEnglish,
                Margin = new Padding(0, 2, 8, 0)
            };
            chkEnglish.CheckedChanged += (s, e) =>
            {
                _settings.AutoEnglish = chkEnglish.Checked;
                if (chkEnglish.Checked) AutoSwitchToEnglish();
                else if (_lblImeState != null) { _lblImeState.Text = "输入法：未自动切换"; _lblImeState.ForeColor = Color.Gray; }
            };
            imeRow.Controls.Add(chkEnglish);
            _lblImeState = new Label { Text = "输入法：未自动切换", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(0, 6, 0, 0) };
            imeRow.Controls.Add(_lblImeState);
            t.Controls.Add(imeRow, 0, 17); t.SetColumnSpan(imeRow, 2);
            t.RowStyles[17] = new RowStyle(SizeType.Absolute, 26);

            t.Controls.Add(lblHintBottom, 0, 16); t.SetColumnSpan(lblHintBottom, 2);
            t.RowStyles[16] = new RowStyle(SizeType.Absolute, 36);   // 预留两行高度，确保不被截断

            g.Controls.Add(t);
            cmbMode.SelectedIndex = 0;
            return g;
        }

        private void AddFieldRow(TableLayoutPanel t, string label, int row, out TextBox box)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 5, 8, 5) }, 0, row);
            box = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 6) };
            t.Controls.Add(box, 1, row);
        }

        /// <summary>
        /// 「扫码伴侣日志（实时）」面板：放在中栏标签预览的上方（宽，能看清）。
        /// 点「开始识别」后自动出现，里面是扫码伴侣的运行日志（以前那个黑窗口里的内容）。
        /// </summary>
        private void BuildScannerLogPanel()
        {
            _grpScannerLog = new CardGroup
            {
                Text = "扫码伴侣日志（实时）",
                Dock = DockStyle.Fill,
                Padding = new Padding(6, 18, 6, 6),
                Margin = new Padding(0, 0, 8, 6),
                Visible = false
            };
            _txtScannerLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,                     // 自动换行（长行不再截断，方便看日志）
                Font = new Font("Consolas", 9F),
                BackColor = Color.FromArgb(250, 250, 250)
            };
            _grpScannerLog.Controls.Add(_txtScannerLog);
        }

        private GroupBox BuildPreviewGroup()
        {
            var g = new CardGroup { Text = "标签预览（鼠标拖动文字/条码可调整位置）", Dock = DockStyle.Fill, Padding = new Padding(8, 20, 8, 8), Margin = Padding.Empty };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(0), Margin = Padding.Empty };
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            picPreview = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 0, 6) };
            picPreview.MouseDown += Pic_MouseDown;
            picPreview.MouseMove += Pic_MouseMove;
            picPreview.MouseUp += Pic_MouseUp;
            picPreview.Paint += Pic_Paint;
            t.Controls.Add(picPreview, 0, 0);
            _lblSizeHint = new Label { AutoSize = true, ForeColor = Color.Gray, Margin = Padding.Empty, Text = "" };
            t.Controls.Add(_lblSizeHint, 0, 1);
            g.Controls.Add(t);
            return g;
        }

        private GroupBox BuildPrinterGroup()
        {
            var g = new CardGroup { Text = "打印机（可添加网络/局域网打印机）", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 22, 10, 10), Margin = Padding.Empty };
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 4, Padding = new Padding(0), Margin = Padding.Empty };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            for (int i = 0; i < 4; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var lsel = new Label { Text = "选择打印机：", AutoSize = true, Margin = new Padding(0, 2, 0, 6) };
            t.Controls.Add(lsel, 0, 0); t.SetColumnSpan(lsel, 2);

            cmbPrinter = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 8) };
            cmbPrinter.SelectedIndexChanged += (s, e) => { if (cmbPrinter.SelectedItem != null) _settings.Printer = cmbPrinter.SelectedItem.ToString(); };
            t.Controls.Add(cmbPrinter, 0, 1); t.SetColumnSpan(cmbPrinter, 2);

            var btnAddNet = new RoundedButton { Text = "添加网络打印机…", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            btnAddNet.Click += (s, e) => AddNetworkPrinter();
            t.Controls.Add(btnAddNet, 0, 2); t.SetColumnSpan(btnAddNet, 2);

            txtNetPrinter = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
            t.Controls.Add(txtNetPrinter, 0, 3);
            var btnCol = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty, Padding = Padding.Empty };
            var btnRefresh = new RoundedButton { Text = "刷新", Width = 78, Height = 28, Margin = new Padding(0, 0, 0, 4) };
            btnRefresh.Click += (s, e) => RefreshPrinters(false);
            var btnConnect = new RoundedButton { Text = "连接", Width = 78, Height = 28, Margin = Padding.Empty };
            btnConnect.Click += (s, e) => ConnectNetworkPrinter();
            btnCol.Controls.Add(btnRefresh);
            btnCol.Controls.Add(btnConnect);
            t.Controls.Add(btnCol, 1, 3);

            g.Controls.Add(t);
            return g;
        }

        private GroupBox BuildSizeGroup()
        {
            var g = new CardGroup { Text = "标签规格", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 22, 10, 10), Margin = Padding.Empty };
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, RowCount = 3, Padding = new Padding(0), Margin = Padding.Empty };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            t.Controls.Add(new Label { Text = "宽", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 10) }, 0, 0);
            numW = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 5, Maximum = 200, DecimalPlaces = 1, Increment = 0.5m, Margin = new Padding(0, 6, 8, 10) };
            t.Controls.Add(numW, 1, 0);
            t.Controls.Add(new Label { Text = "高", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 10) }, 2, 0);
            numH = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 5, Maximum = 300, DecimalPlaces = 1, Increment = 0.5m, Margin = new Padding(0, 6, 0, 10) };
            t.Controls.Add(numH, 3, 0);

            t.Controls.Add(new Label { Text = "打印精度", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 8, 2) }, 0, 1);
            cmbDpi = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 2) };
            cmbDpi.Items.AddRange(new object[] { "203 dpi", "300 dpi" });
            t.Controls.Add(cmbDpi, 1, 1); t.SetColumnSpan(cmbDpi, 2);

            t.Controls.Add(new Label { Text = "条码粗细", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 4, 8, 0) }, 0, 2);
            cmbBarWidth = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
            cmbBarWidth.Items.AddRange(new object[] { "细", "中", "粗" });
            cmbBarWidth.SelectedIndex = _settings.BarcodeWidth;
            cmbBarWidth.SelectedIndexChanged += (s, e) => { if (cmbBarWidth.SelectedIndex >= 0) { _settings.BarcodeWidth = cmbBarWidth.SelectedIndex; ApplyBarcodeWidthToLayout(); _settings.Save(); UpdatePreview(); } };
            t.Controls.Add(cmbBarWidth, 1, 2); t.SetColumnSpan(cmbBarWidth, 2);

            numW.ValueChanged += (s, e) => { _settings.LabelWidthMm = (double)numW.Value; numX.Maximum = (decimal)_settings.LabelWidthMm; ApplyChecksToLayout(); ApplyBarcodeWidthToLayout(); UpdatePreview(); };
            numH.ValueChanged += (s, e) => { _settings.LabelHeightMm = (double)numH.Value; numY.Maximum = (decimal)_settings.LabelHeightMm; UpdatePreview(); };
            cmbDpi.SelectedIndexChanged += (s, e) => { if (cmbDpi.SelectedIndex >= 0) { _settings.Dpi = cmbDpi.SelectedIndex == 0 ? 203 : 300; UpdatePreview(); } };

            g.Controls.Add(t);
            return g;
        }

        private GroupBox BuildContentGroup()
        {
            var g = new CardGroup { Text = "打印内容（可多选）", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10, 22, 10, 10), Margin = Padding.Empty };
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 3, Padding = new Padding(0), Margin = Padding.Empty };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            chkModel = new CheckBox { Text = "型号", AutoSize = true, Checked = _settings.ShowModel, Margin = new Padding(0, 2, 8, 8) };
            chkType = new CheckBox { Text = "类型", AutoSize = true, Checked = _settings.ShowType, Margin = new Padding(0, 2, 0, 8) };
            chkSN = new CheckBox { Text = "SN（条码＋文字）", AutoSize = true, Checked = _settings.ShowSN, Margin = new Padding(0, 0, 0, 8) };
            chkMAC = new CheckBox { Text = "MAC（条码＋文字）", AutoSize = true, Checked = _settings.ShowMAC, Margin = new Padding(0, 0, 0, 2) };
            t.Controls.Add(chkModel, 0, 0);
            t.Controls.Add(chkType, 1, 0);
            t.Controls.Add(chkSN, 0, 1); t.SetColumnSpan(chkSN, 2);
            t.Controls.Add(chkMAC, 0, 2); t.SetColumnSpan(chkMAC, 2);
            foreach (var c in new[] { chkModel, chkType, chkSN, chkMAC })
                c.CheckedChanged += (s, e) => { ApplyChecksToLayout(); UpdatePreview(); };
            g.Controls.Add(t);
            return g;
        }

        private GroupBox BuildPropGroup()
        {
            var g = new CardGroup { Text = "排版属性（选中预览中的元素后编辑）", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 20, 8, 8), Margin = Padding.Empty };
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 8, Padding = new Padding(0), Margin = Padding.Empty };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            for (int i = 0; i < 8; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            lblSel = new Label { Text = "未选择元素", AutoSize = true, Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6) };
            t.Controls.Add(lblSel, 0, 0); t.SetColumnSpan(lblSel, 2);

            t.Controls.Add(new Label { Text = "字体大小(pt)", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 5, 8, 6) }, 0, 1);
            numFont = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 4, Maximum = 40, DecimalPlaces = 1, Increment = 0.5m, Margin = new Padding(0, 3, 0, 6) };
            t.Controls.Add(numFont, 1, 1);

            t.Controls.Add(new Label { Text = "水平位置(mm)", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 5, 8, 6) }, 0, 2);
            numX = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = 200, DecimalPlaces = 1, Increment = 0.1m, Margin = new Padding(0, 3, 0, 6) };
            t.Controls.Add(numX, 1, 2);

            t.Controls.Add(new Label { Text = "垂直位置(mm)", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 5, 8, 6) }, 0, 3);
            numY = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = 300, DecimalPlaces = 1, Increment = 0.1m, Margin = new Padding(0, 3, 0, 6) };
            t.Controls.Add(numY, 1, 3);

            t.Controls.Add(new Label { Text = "条码高度(mm)", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 5, 8, 6) }, 0, 4);
            numBarcodeH = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 5, Maximum = 60, DecimalPlaces = 1, Increment = 0.5m, Margin = new Padding(0, 3, 0, 6) };
            t.Controls.Add(numBarcodeH, 1, 4);

            chkItemVisible = new CheckBox { Text = "在标签上显示此元素", AutoSize = true, Margin = new Padding(0, 3, 0, 6) };
            t.Controls.Add(chkItemVisible, 0, 5); t.SetColumnSpan(chkItemVisible, 2);

            var chkMoveAll = new CheckBox { Text = "整体移动（一起移动所有元素）", AutoSize = true, Margin = new Padding(0, 2, 0, 4) };
            chkMoveAll.CheckedChanged += (s, e) => { _moveAll = chkMoveAll.Checked; if (!_moveAll) _dragging = false; };
            t.Controls.Add(chkMoveAll, 0, 6); t.SetColumnSpan(chkMoveAll, 2);

            btnResetLayout = new RoundedButton { Text = "恢复默认排版", Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 2) };
            btnResetLayout.Click += (s, e) => { _settings.Layout = LayoutItem.DefaultLayout(_settings.LabelWidthMm, _settings.LabelHeightMm); ApplyChecksToLayout(); UpdatePreview(); SetStatus("已恢复默认排版", Color.Gray); };
            t.Controls.Add(btnResetLayout, 0, 7); t.SetColumnSpan(btnResetLayout, 2);

            numFont.ValueChanged += (s, e) => { if (!_suppressProps && _selItem != null && !_selItem.IsBarcode) { _selItem.FontSizePt = (double)numFont.Value; UpdatePreview(); } };
            numX.ValueChanged += (s, e) => { if (!_suppressProps && _selItem != null) { _selItem.Xmm = (double)numX.Value; UpdatePreview(); } };
            numY.ValueChanged += (s, e) => { if (!_suppressProps && _selItem != null) { _selItem.Ymm = (double)numY.Value; UpdatePreview(); } };
            numBarcodeH.ValueChanged += (s, e) => { if (!_suppressProps && _selItem != null && _selItem.IsBarcode) { _selItem.HeightMm = (double)numBarcodeH.Value; UpdatePreview(); } };
            chkItemVisible.CheckedChanged += (s, e) => { if (!_suppressProps && _selItem != null) { _selItem.Visible = chkItemVisible.Checked; ApplyChecksToLayout(); UpdatePreview(); } };

            g.Controls.Add(t);
            return g;
        }

        private GroupBox BuildNasGroup()
        {
            var g = new CardGroup { Text = "NAS 数据备份（每 5 分钟同步）", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 20, 8, 8), Margin = Padding.Empty };
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, RowCount = 4, Padding = new Padding(0), Margin = Padding.Empty };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            for (int i = 0; i < 4; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // 第 0 行：开关 有 / 无
            var toggleRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
            toggleRow.Controls.Add(new Label { Text = "开启同步：", AutoSize = true, Margin = new Padding(0, 4, 6, 0) });
            radNasOn = new RadioButton { Text = "有", AutoSize = true, Margin = new Padding(0, 4, 10, 0) };
            radNasOff = new RadioButton { Text = "无", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            radNasOn.CheckedChanged += (s, e) => { if (!_suppressNas) { _settings.NasSyncEnabled = radNasOn.Checked; _settings.Save(); ApplyNasState(); if (radNasOn.Checked) { StartNasBackup(); NasSyncOnce(); } } };
            radNasOff.CheckedChanged += (s, e) => { if (!_suppressNas) { _settings.NasSyncEnabled = radNasOn.Checked; _settings.Save(); ApplyNasState(); } };
            toggleRow.Controls.Add(radNasOn);
            toggleRow.Controls.Add(radNasOff);
            t.Controls.Add(toggleRow, 0, 0); t.SetColumnSpan(toggleRow, 3);

            // 第 1 行：NAS 目录 + 浏览
            t.Controls.Add(new Label { Text = "NAS 目录：", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 6, 0) }, 0, 1);
            txtNasPath = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 2, 0, 6) };
            t.Controls.Add(txtNasPath, 1, 1);
            btnBrowseNas = new RoundedButton { Text = "浏览", Dock = DockStyle.Fill, Height = 28, Margin = new Padding(2, 0, 0, 6) };
            btnBrowseNas.Click += (s, e) => BrowseNasPath();
            t.Controls.Add(btnBrowseNas, 2, 1);

            // 第 2 行：立即同步
            btnNasSync = new RoundedButton { Text = "立即同步", Dock = DockStyle.Left, Width = 92, Height = 28, Margin = new Padding(0, 2, 0, 4) };
            btnNasSync.Click += (s, e) => NasSyncOnce();
            t.Controls.Add(btnNasSync, 0, 2); t.SetColumnSpan(btnNasSync, 3);

            // 第 3 行：同步状态
            lblNasStatus = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(0, 4, 0, 2) };
            t.Controls.Add(lblNasStatus, 0, 3); t.SetColumnSpan(lblNasStatus, 3);

            g.Controls.Add(t);
            _suppressNas = true;
            radNasOn.Checked = _settings.NasSyncEnabled;
            radNasOff.Checked = !_settings.NasSyncEnabled;
            _suppressNas = false;
            ApplyNasState();
            return g;
        }

        private void ApplyNasState()
        {
            bool on = _settings.NasSyncEnabled;
            _suppressNas = true;
            if (radNasOn != null) radNasOn.Checked = on;
            if (radNasOff != null) radNasOff.Checked = !on;
            _suppressNas = false;
            if (txtNasPath != null) { txtNasPath.Text = _settings.NasPath; txtNasPath.ReadOnly = !on; txtNasPath.Enabled = on; }
            if (btnBrowseNas != null) btnBrowseNas.Enabled = on;
            if (btnNasSync != null) btnNasSync.Enabled = on;
            if (lblNasStatus != null)
            {
                if (!on) lblNasStatus.Text = "未开启 NAS 同步";
                else if (string.IsNullOrWhiteSpace(_settings.NasPath)) lblNasStatus.Text = "请选择 NAS 目录";
                else
                {
                    string machine = Environment.MachineName;
                    if (string.IsNullOrWhiteSpace(machine)) machine = "本机";
                    lblNasStatus.Text = "已开启·同步到「" + machine + "」子文件夹";
                }
                lblNasStatus.ForeColor = Ui.Resolve(lblNasStatus.ForeColor);
            }
        }

        private void BrowseNasPath()
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择 NAS 备份目录";
                dlg.ShowNewFolderButton = true;
                if (!string.IsNullOrWhiteSpace(_settings.NasPath) && Directory.Exists(_settings.NasPath))
                    dlg.SelectedPath = _settings.NasPath;
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _settings.NasPath = dlg.SelectedPath;
                    _settings.Save();
                    txtNasPath.Text = _settings.NasPath;
                    ApplyNasState();
                    NasSyncOnce();
                }
            }
        }

        private void StartNasBackup()
        {
            if (_nasTimer == null)
            {
                _nasTimer = new System.Windows.Forms.Timer { Interval = 5 * 60 * 1000 };
                _nasTimer.Tick += (s, e) => { _nasTimer.Stop(); NasSyncOnce(); _nasTimer.Start(); };
                _nasTimer.Start();
            }
            if (_settings.NasSyncEnabled && !string.IsNullOrWhiteSpace(_settings.NasPath))
            {
                // 启动后约 5 秒先做一次初始同步
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    System.Threading.Thread.Sleep(5000);
                    if (!IsDisposed) try { BeginInvoke((Action)NasSyncOnce); } catch { }
                });
            }
        }

        private void NasSyncOnce()
        {
            if (_nasSyncing) return;
            if (!_settings.NasSyncEnabled || string.IsNullOrWhiteSpace(_settings.NasPath)) return;
            _nasSyncing = true;
            string localDir = _dataDir;
            string nasDir = _settings.NasPath.Trim();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try { DoNasSync(localDir, nasDir); }
                finally { _nasSyncing = false; }
            });
        }

        private void DoNasSync(string localDir, string nasDir)
        {
            string statusText; Color sc = Color.SeaGreen;
            try
            {
                if (!Directory.Exists(nasDir)) Directory.CreateDirectory(nasDir);
                // 多机共用 NAS 时，按机器名建子文件夹，避免同名历史文件互相覆盖
                string machine = Environment.MachineName;
                if (string.IsNullOrWhiteSpace(machine)) machine = "本机";
                string targetDir = Path.Combine(nasDir, machine);
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);
                var files = Directory.GetFiles(localDir, "历史记录_*.csv");
                int copied = 0, same = 0, failed = 0;
                foreach (var lf in files)
                {
                    string name = Path.GetFileName(lf);
                    string df = Path.Combine(targetDir, name);
                    try
                    {
                        bool need = !File.Exists(df) || File.GetLastWriteTime(lf) > File.GetLastWriteTime(df).AddSeconds(1);
                        if (need) { File.Copy(lf, df, true); copied++; } else same++;
                    }
                    catch { failed++; }
                }
                int total = copied + same + failed;
                statusText = total == 0 ? "同步完成：暂无历史文件" : "同步完成：更新 " + copied + "，已最新 " + same + (failed > 0 ? "，失败 " + failed : "");
                if (failed > 0) sc = Color.DarkOrange;
            }
            catch (Exception ex)
            {
                string m = ex.Message ?? "";
                statusText = "同步失败：" + (m.Length > 40 ? m.Substring(0, 40) : m);
                sc = Color.Red;
            }
            NotifyNasStatus(statusText, sc);
        }

        private void NotifyNasStatus(string text, Color c)
        {
            if (IsDisposed || lblNasStatus == null) return;
            try { BeginInvoke((Action)(() => { if (lblNasStatus != null) { lblNasStatus.Text = text; lblNasStatus.ForeColor = Ui.Resolve(c); } })); } catch { }
        }

        private GroupBox BuildHistoryGroup()
        {
            var g = new CardGroup { Text = "历史记录（自动保存，双击可重印）", Dock = DockStyle.Fill, Padding = new Padding(8, 20, 8, 8), Margin = Padding.Empty };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(0), Margin = Padding.Empty };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var searchRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
            searchRow.Controls.Add(new Label { Text = "查找(SN/MAC)：", AutoSize = true, Margin = new Padding(0, 3, 6, 6) });
            txtSearch = new TextBox { Width = 240, Margin = new Padding(0, 0, 8, 6), Anchor = AnchorStyles.Left };
            txtSearch.TextChanged += (s, e) => { _searchText = txtSearch.Text.Trim(); LoadHistoryGrid(); };
            searchRow.Controls.Add(txtSearch);
            searchRow.Controls.Add(new Label { Text = "可输完整 SN/MAC 或后几位", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(0, 3, 0, 6) });
            searchRow.Controls.Add(new Label { Text = "日期：", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(20, 3, 4, 6) });
            dpHistory = new CalDatePicker { Value = DateTime.Today, Margin = new Padding(0, 0, 0, 6) };
            dpHistory.CountProvider = HistCountForDate;
            dpHistory.ValueChanged += (s, e) => { _filterDate = dpHistory.Value.Date; LoadHistoryGrid(); };
            searchRow.Controls.Add(dpHistory);
            t.Controls.Add(searchRow, 0, 0); t.SetColumnSpan(searchRow, 2);

            grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, ScrollBars = ScrollBars.Vertical, Margin = new Padding(0, 0, 8, 8) };
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.Columns.Add("colTime", "时间");
            grid.Columns.Add("colModel", "型号");
            grid.Columns.Add("colType", "类型");
            grid.Columns.Add("colSN", "SN");
            grid.Columns.Add("colMAC", "MAC");
            grid.Columns.Add("colPrintTime", "打印时间");
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) { var tag = grid.Rows[e.RowIndex].Tag as DeviceRecord; if (tag != null) Reprint(tag); } };
            grid.CellToolTipTextNeeded += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == 0)
                {
                    var rec = grid.Rows[e.RowIndex].Tag as DeviceRecord;
                    if (rec != null)
                    {
                        int dayCount = _records.Count(r => r.Time.Date == rec.Time.Date);
                        e.ToolTipText = rec.Time.ToString("yyyy-MM-dd") + " 当天共 " + dayCount + " 条记录";
                    }
                }
            };
            t.Controls.Add(grid, 0, 1);

            var btnCol = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty, Padding = Padding.Empty };
            var btnReprint = new RoundedButton { Text = "重印选中", Width = 170, Height = 22, Margin = new Padding(0, 0, 0, 1) };
            btnReprint.Click += (s, e) => { var rec = SelectedRecord(); if (rec != null) Reprint(rec); };
            var btnDel = new RoundedButton { Text = "删除选中", Width = 170, Height = 22, Margin = new Padding(0, 0, 0, 1) };
            btnDel.Click += (s, e) => DeleteSelected();
            var btnClearAll = new RoundedButton { Text = "清空全部", Width = 170, Height = 22, Margin = new Padding(0, 0, 0, 1) };
            btnClearAll.Click += (s, e) => ClearAllHistory();
            var btnFolder = new RoundedButton { Text = "打开数据文件夹", Width = 170, Height = 22, Margin = new Padding(0, 0, 0, 1) };
            btnFolder.Click += (s, e) => { try { System.Diagnostics.Process.Start("explorer.exe", _dataDir); } catch { } };
            btnCol.Controls.Add(btnReprint);
            btnCol.Controls.Add(btnDel);
            btnCol.Controls.Add(btnClearAll);
            btnCol.Controls.Add(btnFolder);
            t.Controls.Add(btnCol, 1, 1);

            var lblRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
            lblToday = new Label { Text = "今日已打印：0 台", AutoSize = true, Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold), ForeColor = Color.SeaGreen, Margin = new Padding(0, 2, 24, 0) };
            lblRow.Controls.Add(lblToday);
            lblCount = new Label { Text = "共 0 条记录", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 2, 24, 0) };
            lblRow.Controls.Add(lblCount);
            lblRow.Controls.Add(new Label { Text = "记录按天保存：数据\\历史记录_日期.csv（每天一个新文件）", AutoSize = true, ForeColor = Color.Gray });
            t.Controls.Add(lblRow, 0, 2); t.SetColumnSpan(lblRow, 2);

            g.Controls.Add(t);
            return g;
        }

        // 仅自测：切到固定型号模式并填入型号/类型
        internal void TestSetFixedModel(string model, string type)
        {
            try { SetMode(true); txtModel.Text = model; txtType.Text = type; } catch { }
        }

        // 仅自测：模拟点「开始识别」/「暂停」/「停止」
        internal void TestScannerStart() { ScannerStartPause(); }
        internal void TestScannerStop() { ScannerStop(); }
        internal string TestStatusText() { try { return lblStatus == null ? "" : lblStatus.Text; } catch { return ""; } }
        internal void TestSetModel(string model, string type)
        {
            try { if (txtModel != null) txtModel.Text = model; if (txtType != null && type != null) txtType.Text = type; }
            catch { }
        }
        internal string TestFocused()
        {
            try
            {
                var c = ActiveControl;
                if (c == null) return "（无）";
                if (c == txtScan) return "扫码输入框 ✓";
                return c.GetType().Name + "：" + c.Text;
            }
            catch { return ""; }
        }
        internal string TestHistoryTail(int n)
        {
            try
            {
                var sb = new StringBuilder();
                int start = Math.Max(0, _records.Count - n);
                for (int i = start; i < _records.Count; i++)
                    sb.AppendLine(_records[i].Time.ToString("MM-dd HH:mm") + "  " + _records[i].Model + " / " + _records[i].SN + " / " + _records[i].MAC);
                return sb.ToString();
            }
            catch { return ""; }
        }
        internal string TestImeSwitch()
        {
            try
            {
                bool ok = ImeHelper.ToEnglish(Handle);
                if (txtScan != null && txtScan.Handle != IntPtr.Zero) ok = ImeHelper.ToEnglish(txtScan.Handle) || ok;
                return ok ? "已执行输入法切英文（API 返回成功）" : "执行了，但系统/输入法没回应（可能本来就没装中文输入法）";
            }
            catch (Exception ex) { return "切输入法出错：" + ex.Message; }
        }
        internal string TestScannerLog()
        {
            try
            {
                // 优先返回"扫码伴侣"抓到的日志（现在日志显示在工具里）
                if (_companion != null)
                {
                    var ls = _companion.RecentLines(40);
                    if (ls.Count > 0) return string.Join("\r\n", ls.ToArray());
                }
                return _camForm != null && !_camForm.IsDisposed ? _camForm.TestLog() : "（没有日志）";
            }
            catch { return ""; }
        }
        internal string TestScannerState()
        {
            try
            {
                string pid = (_companion != null && _companion.Running) ? ("进程PID=" + _companion.Pid) : "进程=没在跑";
                return (ScannerRunning ? (ScannerPaused ? "已暂停" : "运行中") : "已停止")
                     + " | 按钮=" + (_btnScanStart == null ? "" : _btnScanStart.Text)
                     + " | 状态标签=" + (_lblScanner == null ? "" : _lblScanner.Text)
                     + " | " + pid;
            }
            catch { return ""; }
        }

        // 打开「摄像头 / 高拍仪识别」窗口
        private CameraForm _camForm;

        // ============ 主界面上的「开始识别 / 暂停 / 停止」============
        // 打开《一体化工具》时自动把「光猫扫码伴侣」(modem_scanner.py) 拉起来（窗口先藏着）；
        // 点「开始识别」= 显示它的窗口 + 让它开始识别；再点 = 暂停；「停止」= 让它退出。
        // 摄像头归扫码伴侣使用（和双击那个 bat 完全一样），工具这边只负责发命令、收数据。
        private ScannerCompanion _companion;
        private bool _compStarted;          // 工人点过「开始」了吗
        private bool _compPaused;           // 现在是不是暂停状态

        internal string CompanionTemplate
        {
            get { return _fixedModel ? "{sn}\\t{mac}\\n" : "{qr}\\n{mac}\\n"; }
        }

        // ============ 自动把输入法切成英文（防止模拟按键被中文输入法吃掉）============
        private Label _lblImeState;
        private DateTime _lastImeSwitch = DateTime.MinValue;

        private void AutoSwitchToEnglish()
        {
            try
            {
                if (!_settings.AutoEnglish) return;
                if (Handle == IntPtr.Zero) return;
                ImeHelper.ToEnglish(Handle);
                // 扫码输入框也单独切一次（有些输入法是按窗口记状态的）
                if (txtScan != null && txtScan.Handle != IntPtr.Zero) ImeHelper.ToEnglish(txtScan.Handle);
                _lastImeSwitch = DateTime.Now;
                if (_lblImeState != null)
                {
                    _lblImeState.Text = "输入法：已自动切英文（勾选项可关）";
                    _lblImeState.ForeColor = Color.SeaGreen;
                }
            }
            catch { }
        }

        /// <summary>拉起扫码伴侣（第一次会把窗口藏起来、处于暂停状态）</summary>
        internal void EnsureCompanion()
        {
            try
            {
                if (ShellApp.Globals.TestHarness) return;           // 只有命令行自测才跳过；"测试模式"勾选不影响扫码伴侣
                if (_companion == null) _companion = new ScannerCompanion(Application.StartupPath);
                if (_companion.Running) return;
                string err;
                if (_companion.Start(CompanionTemplate, out err))
                {
                    _companion.Line += OnCompanionLine;              // 它的日志 → 显示在工具里
                    _compPaused = true;
                    _compStarted = false;
                    SetStatus("扫码伴侣已就绪（它用摄像头识别；点「开始识别」开始干活）", Color.DodgerBlue);
                }
                else
                {
                    SetStatus("扫码伴侣没能启动：" + err, Color.Red);
                }
            }
            catch (Exception ex) { SetStatus("启动扫码伴侣出错：" + ex.Message, Color.Red); }
        }

        // 扫码伴侣的一行日志 → 追加到"扫码伴侣日志"面板（自动滚到底）
        private void OnCompanionLine(string line)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((MethodInvoker)delegate
                {
                    try
                    {
                        // 有些版本会带 ANSI 颜色码（?[90m 之类），去掉再显示，日志才干净
                        string show = System.Text.RegularExpressions.Regex.Replace(
                            line ?? "", "\x1B\\[[0-9;]*[A-Za-z]", "");
                        if (show.Trim().Length == 0) return;
                        if (_txtScannerLog == null) return;
                        if (_grpScannerLog != null && !_grpScannerLog.Visible) _grpScannerLog.Visible = true;
                        _txtScannerLog.AppendText(show + "\r\n");
                        string[] ls = _txtScannerLog.Lines;
                        if (ls.Length > 400)
                        {
                            var sb = new StringBuilder();
                            for (int i = ls.Length - 300; i < ls.Length; i++) sb.AppendLine(ls[i]);
                            _txtScannerLog.Text = sb.ToString();
                        }
                        _txtScannerLog.SelectionStart = _txtScannerLog.TextLength;
                        _txtScannerLog.ScrollToCaret();
                    }
                    catch { }
                });
            }
            catch { }
        }

        internal bool ScannerRunning
        {
            get { return _compStarted && _companion != null && _companion.Running; }
        }

        internal bool ScannerPaused
        {
            get { return _compPaused; }
        }

        /// <summary>开始 / 暂停（再点一次继续）——控制的是光猫扫码伴侣</summary>
        internal void ScannerStartPause()
        {
            try
            {
                if (ShellApp.Globals.TestHarness) { SetStatus("（自动化自测）跳过启动扫码伴侣", Color.DarkOrange); return; }
                if (_companion == null) _companion = new ScannerCompanion(Application.StartupPath);
                if (!_companion.Running)
                {
                    EnsureCompanion();
                    if (!_companion.Running) { UpdateScannerUi(); return; }
                }
                _companion.Send("mode=" + (_fixedModel ? "fixed" : "qr"));   // 模式跟界面上的选择走
                if (!_compStarted)
                {
                    _companion.Send("resume");
                    _compStarted = true; _compPaused = false;
                    if (_grpScannerLog != null) _grpScannerLog.Visible = true;   // 点开始就把日志面板显示出来
                    SetStatus(_fixedModel
                        ? "扫码伴侣已开始（固定型号模式：读 SN + MAC 条码）"
                        : "扫码伴侣已开始（二维码模式：读二维码 + MAC 条码）", Color.SeaGreen);
                }
                else if (_compPaused)
                {
                    _companion.Send("resume");
                    _compPaused = false;
                    SetStatus("扫码伴侣已继续", Color.SeaGreen);
                }
                else
                {
                    _companion.Send("pause");
                    _compPaused = true;
                    SetStatus("扫码伴侣已暂停（窗口还在，点「继续」恢复）", Color.DarkOrange);
                }
                UpdateScannerUi();
                // 点完按钮后把光标还给扫码输入框：扫码伴侣是"模拟按键"，光标不在这个框里就白敲
                try { if (txtScan != null) { txtScan.Focus(); ActiveControl = txtScan; } } catch { }
            }
            catch (Exception ex)
            {
                MessageBox.Show("操作扫码伴侣失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>停止：让扫码伴侣退出（释放摄像头）</summary>
        internal void ScannerStop()
        {
            try
            {
                if (ShellApp.Globals.TestHarness) { SetStatus("（自动化自测）没有扫码伴侣", Color.DarkOrange); return; }
                if (_companion != null && _companion.Running)
                {
                    _companion.Send("stop");
                    System.Threading.Thread.Sleep(300);
                    if (_companion.Running) _companion.Kill();
                }
                _compStarted = false; _compPaused = false;
                SetStatus("扫码伴侣已停止（摄像头已释放）", Color.Gray);
            }
            catch { }
            UpdateScannerUi();
            try { if (txtScan != null) { txtScan.Focus(); ActiveControl = txtScan; } } catch { }
        }

        private void UpdateScannerUi()
        {
            try
            {
                bool on = ScannerRunning;
                bool paused = ScannerPaused;
                if (_btnScanStart != null)
                    _btnScanStart.Text = !on ? "开始识别" : (paused ? "继续" : "暂停");
                if (_btnScanStop != null)
                    _btnScanStop.Enabled = on || (_companion != null && _companion.Running);
                if (_lblScanner != null)
                {
                    bool ready = _companion != null && _companion.Running;
                    _lblScanner.Text = !on ? (ready ? "扫码伴侣：已就绪（未开始）" : "扫码伴侣：已停止")
                                           : (paused ? "扫码伴侣：已暂停" : "扫码伴侣：运行中");
                    _lblScanner.ForeColor = !on ? Color.Gray : (paused ? Color.DarkOrange : Color.SeaGreen);
                }
            }
            catch { }
        }

        private void OpenCameraForm()
        {
            try
            {
                // 摄像头只能被一个程序占用：扫码伴侣正在用的时候先提醒一下
                if (_companion != null && _companion.Running && _compStarted && !_compPaused)
                {
                    if (MessageBox.Show("扫码伴侣正在使用摄像头。\n要打开这个完整识别窗口（用于对焦/调参数），" +
                        "需要先停止扫码伴侣。\n\n现在就停止扫码伴侣并打开完整窗口吗？",
                        "提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return;
                    ScannerStop();
                }
                if (_camForm != null && !_camForm.IsDisposed)
                {
                    _camForm.Activate();
                    return;
                }
                _camForm = new CameraForm(this);
                _camForm.FormClosed += (s, e) => { _camForm = null; };
                try { _camForm.Show(); } catch { }
            }
            catch (Exception ex)
            {
                string logPath = ErrorLog.Log(ex);
                _camForm = null;
                MessageBox.Show("打开摄像头识别窗口失败：" + ex.Message +
                    (string.IsNullOrEmpty(logPath) ? "" : ("\n\n详细信息已写入：" + logPath)), "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void TxtScan_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            string s = txtScan.Text.Trim();
            if (string.IsNullOrEmpty(s)) return;
            txtScan.Clear();
            ProcessScanText(s, false);
        }

        // "只差型号"的标签（没有大二维码、历史里也查不到）：工人把型号填好、光标一离开，
        // 就把这一台接着打完/存好（不用再点「打印当前设备」）
        private void AutoFinishAfterModelFilled()
        {
            try
            {
                if (!chkAuto.Checked) return;
                if (string.IsNullOrWhiteSpace(txtModel.Text)) return;
                if (string.IsNullOrWhiteSpace(txtSN.Text) || string.IsNullOrWhiteSpace(txtMAC.Text)) return;
                if (chkType.Checked && string.IsNullOrWhiteSpace(txtType.Text)) return;
                SaveAndPrint(true);
            }
            catch { }
        }

        // 扫码内容处理：扫码枪与「摄像头识别」共用同一套流程（含型号库对比、强制录入、自动打印）
        // 返回 Ignored=没处理，Partial=已录入一半，Completed=已完成一台
        internal ScanFeed ProcessScanText(string input, bool fromCamera)
        {
            if (input == null) return ScanFeed.Ignored;
            string s = input.Trim();
            if (s.Length == 0) return ScanFeed.Ignored;

            // 不管当前在等什么：只要扫进来的是“设备二维码”，就按二维码处理。
            // 这样摄像头只读到条码、二维码读不清时，可以用扫码枪补扫二维码把这一台补齐。
            if (!_fixedModel)
            {
                var pq = QRParser.Parse(s);
                if (pq != null && (!string.IsNullOrEmpty(pq.SN) || !string.IsNullOrEmpty(pq.Model) || !string.IsNullOrEmpty(pq.Type)))
                {
                    if (WinOcr.HasBadTech(s + " " + pq.Model)) { EponAlerted("二维码"); return ScanFeed.Ignored; }
                    _lastRawQR = s;
                    txtModel.Text = pq.Type;
                    txtType.Text = pq.Model;
                    txtSN.Text = pq.SN;
                    _scanState = ScanState.AwaitMAC;
                    _pendingSnNoModel = false;
                    // 只看"这一台"：SN 相同，或（新扫进来的）MAC 相同 → 才算刚打过，避免拿上一台的 MAC 误判
                    bool just = false;
                    try
                    {
                        if (!string.IsNullOrEmpty(pq.SN) && !string.IsNullOrEmpty(_lastPrintSn) &&
                            string.Equals(pq.SN.Trim(), _lastPrintSn.Trim(), StringComparison.OrdinalIgnoreCase)) just = true;
                        else if (_macDirty && !string.IsNullOrWhiteSpace(txtMAC.Text) && !string.IsNullOrEmpty(_lastPrintMac) &&
                            string.Equals(txtMAC.Text.Trim(), _lastPrintMac.Trim(), StringComparison.OrdinalIgnoreCase)
                            && (DateTime.Now - _lastPrintAt).TotalSeconds < 20) just = true;
                    }
                    catch { }
                    if (just)
                    {
                        SetStatus("这台刚刚已经打印过了（要再打一张请点「打印当前设备」）", Color.DarkOrange);
                        if (!fromCamera) txtScan.Focus();
                        return ScanFeed.Partial;
                    }
                    if (!string.IsNullOrWhiteSpace(txtMAC.Text) && chkAuto.Checked)
                    {
                        // 关键：输入框里会保留"上一台"的内容。只有这个 MAC 是这一台新扫进来的
                        // （_macDirty）才允许立刻打印；否则会拿上一台的 MAC 打出错标签。
                        if (_macDirty)
                        {
                            SetStatus("二维码已补扫，数据齐了，正在打印…", Color.SeaGreen);
                            SaveAndPrint(true);
                            return ScanFeed.Completed;
                        }
                    }
                    SetStatus("已识别二维码，请扫描 MAC 条码…", Color.DodgerBlue);
                    if (!fromCamera) txtScan.Focus();
                    return ScanFeed.Partial;
                }
            }

            if (_fixedModel)
            {
                if (_scanState == ScanState.AwaitSN)
                {
                    if (QRParser.NormalizeMac(s) != null)
                    {
                        SetStatus("这是 MAC，请先扫 SN 条码", Color.Red);
                        return ScanFeed.Ignored;
                    }
                    txtSN.Text = s;
                    _scanState = ScanState.AwaitMAC;
                    SetStatus("请扫描 MAC 条码…", Color.DodgerBlue);
                    if (!fromCamera) txtScan.Focus();
                    return ScanFeed.Partial;
                }
                string macf = QRParser.NormalizeMac(s);
                if (macf == null)
                {
                    SetStatus("MAC 格式不正确（应为 12 位字母数字），请重扫", Color.Red);
                    return ScanFeed.Ignored;
                }
                txtMAC.Text = macf;
                _macDirty = true;                     // 新扫进来的 MAC
                _scanState = ScanState.AwaitSN;
                if (chkAuto.Checked)
                {
                    SaveAndPrint(true);
                    return ScanFeed.Completed;
                }
                else
                {
                    SetStatus("数据已录入，点击“打印当前设备”或“仅保存”", Color.DarkOrange);
                    if (!fromCamera) txtScan.Focus();
                    return ScanFeed.Completed;
                }
            }

            if (_scanState == ScanState.AwaitQR)
            {
                var p = QRParser.Parse(s);
                if (p == null || (string.IsNullOrEmpty(p.Type) && string.IsNullOrEmpty(p.Model) && string.IsNullOrEmpty(p.SN)))
                {
                    // 不是二维码：有些标签**根本没有大二维码**，只有 SN / MAC 两个条码。
                    // 这时把第一段当成 SN 收下：型号优先用历史记录反查（同批设备），查不到就提示手填。
                    bool looksSn = QRParser.NormalizeMac(s) == null && WinOcr.LooksLikeSn(s);
                    if (looksSn)
                    {
                        txtSN.Text = s.Trim();
                        _lastRawQR = "";
                        _pendingSnNoModel = false;
                        string m2, t2;
                        if (GuessModelFromHistory(s, out m2, out t2) && !string.IsNullOrEmpty(m2))
                        {
                            txtModel.Text = m2;
                            if (!string.IsNullOrEmpty(t2)) txtType.Text = t2;
                            _scanState = ScanState.AwaitMAC;
                            SetStatus("这台没有二维码：已按 SN 反查出型号 " + m2 + "，接下来扫 MAC 条码", Color.DodgerBlue);
                        }
                        else
                        {
                            _scanState = ScanState.AwaitMAC;
                            // 型号框里已经有值（上一次手填的）→ 直接沿用，同一批设备就不用每台都填
                            if (!string.IsNullOrWhiteSpace(txtModel.Text))
                            {
                                _pendingSnNoModel = false;
                                SetStatus("这台没有二维码：型号沿用「" + txtModel.Text.Trim() + "」，接下来扫 MAC 条码",
                                          Color.DodgerBlue);
                            }
                            else
                            {
                                _pendingSnNoModel = true;
                                SetStatus("这台没有二维码、历史里也查不到型号：请手工填一次「型号/类型」，"
                                    + "再扫 MAC 条码（填过之后同类设备会自动沿用；也可以切成固定型号模式）",
                                    Color.DarkOrange);
                            }
                        }
                        if (!fromCamera) txtScan.Focus();
                        return ScanFeed.Partial;
                    }
                    SetStatus("未识别出二维码内容，请重新扫描", Color.Red);
                    return ScanFeed.Ignored;
                }
                _lastRawQR = s;
                txtModel.Text = p.Type;
                txtType.Text = p.Model;
                txtSN.Text = p.SN;
                _scanState = ScanState.AwaitMAC;
                SetStatus("已识别二维码，请扫描 MAC 条码…", Color.DodgerBlue);
                if (!fromCamera) txtScan.Focus();
                return ScanFeed.Partial;
            }

            // AwaitMAC
            string mac = QRParser.NormalizeMac(s);
            if (mac == null)
            {
                SetStatus("MAC 格式不正确（应为 12 位字母数字），请重扫", Color.Red);
                return ScanFeed.Ignored;
            }
            txtMAC.Text = mac;
            _macDirty = true;                         // 新扫进来的 MAC
            if (chkAuto.Checked)
            {
                SaveAndPrint(true);
                return ScanFeed.Completed;
            }
            else
            {
                _scanState = ScanState.AwaitQR;
                SetStatus("数据已录入，点击“打印当前设备”或“仅保存”", Color.DarkOrange);
                if (!fromCamera) txtScan.Focus();
                return ScanFeed.Completed;
            }
        }

        // 供「摄像头识别」使用：当前是否固定型号模式 / 是否需要 MAC / 当前等待什么
        internal bool IsFixedModelMode { get { return _fixedModel; } }
        internal bool NeedsMac { get { return chkMAC == null || chkMAC.Checked || !_fixedModel; } }
        internal string ScanStateText
        {
            get
            {
                if (_fixedModel) return _scanState == ScanState.AwaitSN ? "等待SN" : "等待MAC";
                return _scanState == ScanState.AwaitQR ? "等待二维码" : "等待MAC";
            }
        }

        private void SaveAndPrint(bool clearAfter)
        {
            var rec = CurrentRecord();
            string verr = ValidateForPrint();
            if (verr != null)
            {
                Notify(verr, Color.Red, true);
                return;
            }
            // ★ 核对历史记录：这台已经录过就弹窗提示"重复录入"，不打印也不保存
            if (CheckDuplicate(rec)) return;
            bool testMode = ShellApp.Globals.TestMode;
            if (!testMode && cmbPrinter.SelectedItem == null)
            {
                Notify("没有可用打印机，请先在右侧选择打印机。", Color.Red, true);
                return;
            }

            List<string> warnings;
            var bmp = LabelRenderer.Render(rec, _settings.LabelWidthMm, _settings.LabelHeightMm, _settings.Dpi, _settings.Layout, out warnings, false);
            bool printed = PrintBitmap(rec, cmbPrinter.Text);
            bmp.Dispose();
            if (!printed) return;

            rec.PrintTime = DateTime.Now;
            bool dup = AddRecord(rec);
            _lastPrintSn = rec.SN ?? "";
            _lastPrintMac = rec.MAC ?? "";
            _lastPrintAt = DateTime.Now;
            _macDirty = false;          // 这个 MAC 已经被这张标签用掉了，下一台必须重新扫
            RaisePrinted();
            // 记录“最近打印”，供重开软件后恢复预览
            SaveLastPrinted(rec);
            _scanState = _fixedModel ? ScanState.AwaitSN : ScanState.AwaitQR;
            if (clearAfter)
            {
                // 保留最后打印内容在输入框与预览，只重置扫描状态，便于下一台扫码覆盖
                SetStatus(testMode
                    ? (dup ? "测试模式：已模拟打印并保存（注意：该设备之前已录入过）·已保留最后预览" : "测试模式：已模拟打印并保存，等待下一台…（已保留最后预览）")
                    : (dup ? "已打印并保存（注意：该设备之前已录入过）·已保留最后预览" : "已打印并保存，等待下一台…（已保留最后预览）"), dup ? Color.Red : (testMode ? Color.DarkOrange : Color.SeaGreen));
                txtScan.Focus();
                UpdatePreview();
            }
            else
            {
                SetStatus(testMode
                    ? (dup ? "测试模式：已模拟打印并保存（注意：该设备之前已录入过）" : "测试模式：已模拟打印并保存")
                    : (dup ? "已打印并保存（注意：该设备之前已录入过）" : "已打印并保存"), dup ? Color.Red : (testMode ? Color.DarkOrange : Color.SeaGreen));
                UpdatePreview();
            }
            UpdateTodayCount();
        }

        // 校验将要打印的元素：勾选要打印的内容，若对应内容缺失则提示并禁止打印
        // 核对历史记录：SN 或 MAC 命中就返回 true（并弹"重复录入"提示），调用方不要再打印/保存
        private bool CheckDuplicate(DeviceRecord rec)
        {
            try
            {
                if (rec == null) return false;
                string sn = (rec.SN ?? "").Trim();
                string mac = (rec.MAC ?? "").Trim();
                if (sn.Length == 0 && mac.Length == 0) return false;
                DeviceRecord old = null;
                foreach (var r in _records)
                {
                    bool hit = (sn.Length > 0 && !string.IsNullOrEmpty(r.SN) &&
                                string.Equals(r.SN.Trim(), sn, StringComparison.OrdinalIgnoreCase))
                            || (mac.Length > 0 && !string.IsNullOrEmpty(r.MAC) &&
                                string.Equals(r.MAC.Trim(), mac, StringComparison.OrdinalIgnoreCase));
                    if (hit) { old = r; break; }
                }
                if (old == null) return false;                 // 历史里没有 → 正常录入

                try { Console.Beep(600, 400); } catch { }
                string msg = "重复录入！这台设备之前已经录入过了，本次不会打印、也不会保存。\n\n"
                    + "型号：" + (string.IsNullOrEmpty(old.Model) ? "-" : old.Model) + "\n"
                    + "类型：" + (string.IsNullOrEmpty(old.Type) ? "-" : old.Type) + "\n"
                    + "SN：" + (string.IsNullOrEmpty(old.SN) ? "-" : old.SN) + "\n"
                    + "MAC：" + (string.IsNullOrEmpty(old.MAC) ? "-" : old.MAC) + "\n"
                    + "上次录入：" + old.Time.ToString("yyyy-MM-dd HH:mm:ss") + "\n\n"
                    + "如果只是要补打一张标签：请在下面「历史记录」里选中这一条，点「重印选中」。";
                // 自动化自测时不弹模态框（否则自测会卡住）；正常运行时（哪怕勾了测试模式）都要弹出来提醒工人
                if (!ShellApp.Globals.TestHarness)
                {
                    try { MessageBox.Show(msg, "重复录入", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                    catch { }
                }
                SetStatus("重复录入：" + (string.IsNullOrEmpty(rec.SN) ? rec.MAC : rec.SN)
                    + "（已在 " + old.Time.ToString("MM-dd HH:mm") + " 录入过，跳过）", Color.Red);
                try { if (txtScan != null) { txtScan.Focus(); ActiveControl = txtScan; } } catch { }
                return true;
            }
            catch { return false; }
        }

        // 没有二维码时：用 SN 去历史记录里反查“型号 / 类型”（先精确后前缀），典型现场是同一批型号
        internal bool GuessModelFromHistory(string sn, out string model, out string type)
        {
            model = ""; type = "";
            try
            {
                if (string.IsNullOrWhiteSpace(sn)) return false;
                string s = sn.Trim().ToUpperInvariant();
                foreach (var r in _records)
                {
                    if (!string.IsNullOrEmpty(r.SN) && r.SN.Trim().ToUpperInvariant() == s && !string.IsNullOrEmpty(r.Model))
                    { model = r.Model; type = r.Type; return true; }
                }
                string p6 = s.Length >= 6 ? s.Substring(0, 6) : s;
                string p4 = s.Length >= 4 ? s.Substring(0, 4) : s;
                foreach (var r in _records)
                {
                    if (string.IsNullOrEmpty(r.SN) || string.IsNullOrEmpty(r.Model)) continue;
                    string rs = r.SN.Trim().ToUpperInvariant();
                    if ((p6.Length >= 4 && rs.StartsWith(p6)) || (p4.Length >= 4 && rs.StartsWith(p4)))
                    { model = r.Model; type = r.Type; return true; }
                }
            }
            catch { }
            return false;
        }

        // 摄像头模式专用：没扫到二维码时，用 SN 条码反查型号并直接进入“等 MAC”状态
        // 摄像头一次读到“整台设备”时用：二维码（型号/类型/SN）+ 条码（SN / MAC）一次性填好，完整就打印
        // 摄像头模式下设备被拿开：清掉“上一台”的临时状态（文本框里保留最后打印的内容，方便对照）
        internal void CamDeviceGone()
        {
            _pendingSnNoModel = false;
        }

        // 仅自测：看看输入框里现在是什么（验证“换了一台不会留着上一台的 MAC”）
        internal string TestInputs()
        {
            try
            {
                return "型号=" + txtModel.Text + " 类型=" + txtType.Text + " SN=" + txtSN.Text + " MAC=" + txtMAC.Text;
            }
            catch { return ""; }
        }

        // 与扫码枪流程等价（同样的校验、同样的 EPON 拦截、同样的自动打印开关）
        internal ScanFeed FeedDevice(string qrText, List<string> codes, bool guessFromHistory)
        {
            try
            {
                string codeAll = codes == null ? "" : string.Join(" ", codes.ToArray());
                // ① EPON / ADSL 一律报警拦下（不打印、不保存）
                if (WinOcr.HasBadTech((qrText ?? "") + " " + codeAll))
                {
                    EponAlerted("二维码/标签");
                    return ScanFeed.Ignored;
                }

                string model = null, type = null, sn = null, rawQr = null;
                if (!string.IsNullOrEmpty(qrText))
                {
                    var p = QRParser.Parse(qrText);
                    if (p != null && (!string.IsNullOrEmpty(p.SN) || !string.IsNullOrEmpty(p.Model) || !string.IsNullOrEmpty(p.Type)))
                    {
                        rawQr = qrText;
                        model = p.Type;      // 二维码里的 type= 是型号（与扫码枪一致）
                        type = p.Model;      // 二维码里的 model= 是技术类型（GPON/EPON）
                        sn = p.SN;
                    }
                }

                string mac = null, snCode = null;
                if (codes != null)
                    foreach (var c in codes)
                    {
                        if (string.IsNullOrWhiteSpace(c)) continue;
                        string t = c.Trim();
                        string m = QRParser.NormalizeMac(t);
                        if (m != null && (string.IsNullOrEmpty(sn) || !string.Equals(m, sn.Trim(), StringComparison.OrdinalIgnoreCase)))
                        {
                            if (mac == null) mac = m;
                            continue;
                        }
                        if (m != null) continue;      // 和二维码里的 SN 相同：只是 SN 条码
                        if (snCode == null && WinOcr.LooksLikeSn(t)) snCode = t;
                    }

                // ★ 换了一台就把上一台留下的内容清掉，再按本帧读到的填。
                //   以前只“补写”读到的字段：读不到 MAC 时文本框里还留着上一台的 MAC，
                //   四台设备就会打成同一个 MAC。现在以“本帧读到的”为准，读不到就留空（不打印、继续等）。
                {
                    string idSn = !string.IsNullOrEmpty(sn) ? sn.Trim().ToUpperInvariant()
                                : (!string.IsNullOrEmpty(snCode) ? snCode.Trim().ToUpperInvariant() : "");
                    string idMac = mac ?? "";
                    bool newDevice;
                    if (idSn.Length > 0)
                        newDevice = !string.Equals(idSn, (txtSN.Text ?? "").Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase);
                    else if (idMac.Length > 0)
                        newDevice = !string.Equals(idMac, (txtMAC.Text ?? "").Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase);
                    else
                        newDevice = false;                    // 分不清是哪台：不动已有内容，也不打印（下面会走 Partial）
                    if (newDevice)
                    {
                        if (!_fixedModel) { txtModel.Text = ""; txtType.Text = ""; }
                        txtSN.Text = "";
                        txtMAC.Text = "";
                    }
                }

                bool fixedMode = _fixedModel;
                if (fixedMode)
                {
                    // 固定型号模式：型号/类型是界面上手填固定的，这里只用条码补 SN / MAC
                    string snf = snCode;
                    if (string.IsNullOrEmpty(snf))
                        foreach (var c in codes) if (!string.IsNullOrWhiteSpace(c) && QRParser.NormalizeMac(c) == null) { snf = c.Trim(); break; }
                    // 换一台同样先清掉上一台的 SN / MAC
                    if (!string.IsNullOrEmpty(snf) &&
                        !string.Equals(snf.Trim().ToUpperInvariant(), (txtSN.Text ?? "").Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase))
                    { txtSN.Text = ""; txtMAC.Text = ""; }
                    else if (string.IsNullOrEmpty(snf) && !string.IsNullOrEmpty(mac) &&
                        !string.Equals(mac, (txtMAC.Text ?? "").Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase))
                    { txtSN.Text = ""; txtMAC.Text = ""; }
                    if (!string.IsNullOrEmpty(snf)) txtSN.Text = snf;
                    if (!string.IsNullOrEmpty(mac)) { txtMAC.Text = mac; _macDirty = true; }
                    _lastRawQR = "";
                    bool done1 = !string.IsNullOrWhiteSpace(txtSN.Text) && !string.IsNullOrWhiteSpace(txtMAC.Text);
                    if (done1 && chkAuto.Checked) { SaveAndPrint(true); return ScanFeed.Completed; }
                    _scanState = done1 ? (chkAuto.Checked ? ScanState.AwaitSN : ScanState.AwaitSN) : ScanState.AwaitMAC;
                    if (done1) { SetStatus("数据已录入，点击“打印当前设备”或“仅保存”", Color.DarkOrange); return ScanFeed.Completed; }
                    SetStatus("固定型号：已读条码，等 SN / MAC…", Color.DodgerBlue);
                    return ScanFeed.Partial;
                }

                // ② 没有二维码：用 SN 条码反查型号（和原来的逻辑一样，可勾选关闭）
                bool hasModel = false, hasType = false;
                if (!string.IsNullOrEmpty(model)) { txtModel.Text = model.Trim(); hasModel = true; }
                if (!string.IsNullOrEmpty(type)) { txtType.Text = type.Trim(); hasType = true; }
                if (string.IsNullOrEmpty(sn) && !string.IsNullOrEmpty(snCode)) sn = snCode;
                if (!string.IsNullOrEmpty(sn)) txtSN.Text = sn.Trim();
                if (!string.IsNullOrEmpty(mac)) { txtMAC.Text = mac; _macDirty = true; }
                if (!string.IsNullOrEmpty(rawQr)) _lastRawQR = rawQr;

                if (!hasModel && !string.IsNullOrWhiteSpace(txtSN.Text) && guessFromHistory)
                {
                    string m2, t2;
                    if (GuessModelFromHistory(txtSN.Text, out m2, out t2))
                    {
                        if (!string.IsNullOrEmpty(m2)) { txtModel.Text = m2; hasModel = true; }
                        if (!string.IsNullOrEmpty(t2)) { txtType.Text = t2; hasType = true; }
                    }
                }

                // ③ 按“打印内容”的勾选判断是否已经齐了
                bool ok = true;
                if (chkModel.Checked && string.IsNullOrWhiteSpace(txtModel.Text)) ok = false;
                if (chkType.Checked && string.IsNullOrWhiteSpace(txtType.Text)) ok = false;
                if (chkSN.Checked && string.IsNullOrWhiteSpace(txtSN.Text)) ok = false;
                if (chkMAC.Checked && string.IsNullOrWhiteSpace(txtMAC.Text)) ok = false;

                if (ok)
                {
                    _pendingSnNoModel = false;
                    if (JustPrinted(txtSN.Text, txtMAC.Text))
                    {
                        SetStatus("这台刚刚已经打印过了（要再打一张请点「打印当前设备」）", Color.DarkOrange);
                        _scanState = ScanState.AwaitQR;
                        return ScanFeed.Completed;
                    }
                    if (chkAuto.Checked)
                    {
                        SetStatus("已读全 型号/类型/SN/MAC，正在打印…", Color.SeaGreen);
                        SaveAndPrint(true);
                        return ScanFeed.Completed;
                    }
                    _scanState = ScanState.AwaitQR;
                    SetStatus("已读全数据（等待打印）", Color.SeaGreen);
                    return ScanFeed.Completed;
                }

                _scanState = ScanState.AwaitMAC;
                _pendingSnNoModel = !string.IsNullOrWhiteSpace(txtSN.Text) && string.IsNullOrWhiteSpace(txtModel.Text);
                SetStatus("已读到部分信息（" + (string.IsNullOrWhiteSpace(txtModel.Text) ? "缺型号 " : "") +
                          (string.IsNullOrWhiteSpace(txtMAC.Text) ? "缺 MAC 条码" : "") +
                          "），把设备放稳或点「一键找焦点」让条码更清楚…（不会拿上一台的 MAC 打印）", Color.DodgerBlue);
                return ScanFeed.Partial;
            }
            catch { return ScanFeed.Ignored; }
        }

        // 摄像头模式专用：没扫到二维码时，用 SN 条码反查型号并直接进入“等 MAC”状态
        internal bool FeedSnInsteadOfQr(string sn)
        {
            try
            {
                if (_scanState != ScanState.AwaitQR) return false;
                if (string.IsNullOrWhiteSpace(sn)) return false;
                string model, type;
                if (!GuessModelFromHistory(sn, out model, out type))
                {
                    // 历史里查不到：先把 SN 收下，等型号（OCR 识别或手动填写）后再继续
                    txtSN.Text = sn.Trim();
                    _lastRawQR = "";
                    _pendingSnNoModel = true;
                    SetStatus("没有二维码，也没查到型号：请点『识别型号文字(OCR)』或手动填型号，然后对齐 MAC", Color.DarkOrange);
                    return true;
                }
                txtModel.Text = model;
                txtType.Text = type;
                txtSN.Text = sn.Trim();
                _lastRawQR = "";
                _scanState = ScanState.AwaitMAC;
                SetStatus("没有二维码：已用 SN 反查出型号 " + model + "，接下来对齐 MAC 条码", Color.DodgerBlue);
                return true;
            }
            catch { return false; }
        }

        private string ValidateForPrint()
        {
            bool any = chkModel.Checked || chkType.Checked || chkSN.Checked || chkMAC.Checked;
            if (!any) return "请至少勾选一个要打印的内容（型号 / 类型 / SN / MAC）。";
            if (chkModel.Checked && string.IsNullOrWhiteSpace(txtModel.Text))
                return "型号为空，无法打印，请先填写或扫描型号。";
            if (chkType.Checked && string.IsNullOrWhiteSpace(txtType.Text))
                return "类型为空，无法打印，请先填写或扫描类型。";
            if (chkSN.Checked && string.IsNullOrWhiteSpace(txtSN.Text))
                return "SN 为空，无法打印，请先扫描 SN。";
            if (chkMAC.Checked && string.IsNullOrWhiteSpace(txtMAC.Text))
                return "MAC 为空，无法打印，请先扫描 MAC。";
            return null;
        }

        private void SaveCurrent(bool clearAfter)
        {
            var rec = CurrentRecord();
            if (string.IsNullOrWhiteSpace(rec.SN) && string.IsNullOrWhiteSpace(rec.MAC))
            {
                MessageBox.Show("SN 和 MAC 都为空，无法保存。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            bool dup = AddRecord(rec);
            RaisePrinted();
            _scanState = ScanState.AwaitQR;
            if (clearAfter) ClearInputs();
            else _macDirty = false;     // 仅保存也算"这个 MAC 用过了"
            SetStatus(dup ? "已保存（注意：该设备之前已录入过）" : "已保存到历史记录", dup ? Color.Red : Color.SeaGreen);
        }

        private void Reprint(DeviceRecord rec)
        {
            bool testMode = ShellApp.Globals.TestMode;
            if (!testMode && cmbPrinter.SelectedItem == null)
            {
                MessageBox.Show("没有可用打印机，请先选择打印机。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            List<string> warnings;
            var bmp = LabelRenderer.Render(rec, _settings.LabelWidthMm, _settings.LabelHeightMm, _settings.Dpi, _settings.Layout, out warnings, false);
            bool printed = PrintBitmap(rec, cmbPrinter.Text);
            bmp.Dispose();
            if (!printed) return;
            rec.PrintTime = DateTime.Now;
            SaveAll();
            LoadHistoryGrid();
            UpdateTodayCount();
            SetStatus(testMode ? ("测试模式：已模拟重印并保存：" + rec.SN) : ("已重印：" + rec.SN), testMode ? Color.DarkOrange : Color.SeaGreen);
        }

        private bool PrintBitmap(DeviceRecord rec, string printerName)
        {
            // 全局测试模式：不实际输出到打印机，但“模拟打印成功”，后续保存/计数照常进行
            if (ShellApp.Globals.TestMode)
            {
                System.Threading.Thread.Sleep(400);
                return true;
            }
            try
            {
                using (var pd = new PrintDocument())
                {
                    pd.PrinterSettings.PrinterName = printerName;
                    pd.DefaultPageSettings.Landscape = false;
                    pd.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
                    pd.OriginAtMargins = true;
                    try
                    {
                        int pw = (int)Math.Round(_settings.LabelWidthMm / 25.4 * 100);
                        int ph = (int)Math.Round(_settings.LabelHeightMm / 25.4 * 100);
                        pd.DefaultPageSettings.PaperSize = new PaperSize("Label", pw, ph);
                    }
                    catch { }
                    pd.PrintPage += (s, e) =>
                    {
                        var g = e.Graphics;
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.SmoothingMode = SmoothingMode.None;
                        g.CompositingQuality = CompositingQuality.HighSpeed;
                        int dpi = (int)Math.Round(g.DpiX > 0 ? g.DpiX : _settings.Dpi);
                        List<string> w;
                        using (var rbmp = LabelRenderer.Render(rec, _settings.LabelWidthMm, _settings.LabelHeightMm, dpi, _settings.Layout, out w, false))
                        {
                            g.DrawImage(rbmp, e.MarginBounds);
                        }
                        e.HasMorePages = false;
                    };
                    pd.Print();
                }
                return true;
            }
            catch (Exception ex)
            {
                Notify("打印失败：" + ex.Message + "（请检查打印机是否开机、驱动与标签纸尺寸）", Color.Red, true);
                return false;
            }
        }

        private DeviceRecord CurrentRecord()
        {
            return new DeviceRecord
            {
                Time = DateTime.Now,
                Model = txtModel.Text.Trim(),
                Type = txtType.Text.Trim(),
                SN = txtSN.Text.Trim(),
                MAC = txtMAC.Text.Trim(),
                RawQR = _lastRawQR
            };
        }

        private bool AddRecord(DeviceRecord rec)
        {
            bool dup = !string.IsNullOrEmpty(rec.SN) && !string.IsNullOrEmpty(rec.MAC) &&
                       _records.Any(r => r.SN == rec.SN && r.MAC == rec.MAC);
            _records.Insert(0, rec);
            _newKeys.Add(RecKey(rec));
            // 刚扫的记录属于今天：切回“今天”，保证能看到
            if (_filterDate.Date != DateTime.Today)
            {
                _filterDate = DateTime.Today;
                if (dpHistory != null) dpHistory.Value = DateTime.Today;
            }
            SaveAll();
            LoadHistoryGrid();
            return dup;
        }

        private DeviceRecord SelectedRecord()
        {
            if (grid.SelectedRows.Count == 0) return null;
            return grid.SelectedRows[0].Tag as DeviceRecord;
        }

        private void DeleteSelected()
        {
            var rec = SelectedRecord();
            if (rec == null) return;
            _deletedKeys.Add(RecKey(rec));
            AddTombstone(rec);          // 记下“这条被删了”，避免下次 NAS 同步又合并回来
            _records.Remove(rec);
            SaveAll();
            LoadHistoryGrid();
        }

        private void LoadHistoryGrid()
        {
            grid.SuspendLayout();
            grid.Rows.Clear();
            DateTime day = _filterDate.Date;
            List<DeviceRecord> list = _records.Where(r => r.Time.Date == day).ToList();
            if (!string.IsNullOrEmpty(_searchText))
                list = list.Where(r =>
                    (!string.IsNullOrEmpty(r.SN) && r.SN.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(r.MAC) && r.MAC.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            foreach (var r in list)
            {
                int idx = grid.Rows.Add(
                    r.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                    r.Model,
                    r.Type,
                    r.SN,
                    r.MAC,
                    r.PrintTime.HasValue ? r.PrintTime.Value.ToString("yyyy-MM-dd HH:mm:ss") : "");
                grid.Rows[idx].Tag = r;
            }
            grid.ResumeLayout();
            if (lblCount != null)
            {
                string ds = _filterDate.ToString("yyyy-MM-dd");
                lblCount.Text = string.IsNullOrEmpty(_searchText)
                    ? ("历史共 " + _records.Count + " 条 · " + ds + " 当天 " + list.Count + " 条")
                    : ("历史共 " + _records.Count + " 条 · " + ds + " 匹配 " + list.Count + " 条");
            }
        }

        // 供日期控件“悬停某天”显示当天记录数
        private int HistCountForDate(DateTime d)
        {
            DateTime day = d.Date;
            return _records.Count(r => r.Time.Date == day);
        }

        // 自测：打开日历 → 模拟点击目标日期 → 看表格是否只剩那天
        internal string TestDateFilter(DateTime target)
        {
            if (dpHistory == null) return "no picker";
            dpHistory.ShowDrop();
            Application.DoEvents();
            var panel = dpHistory.PopupForm.Controls[0] as CalendarPanel;
            if (panel == null) return "no panel";
            bool clicked = false;
            for (int r = 0; r < 6 && !clicked; r++)
                for (int c = 0; c < 7 && !clicked; c++)
                {
                    var pt = new Point(1 + c * 34 + 17, 1 + 30 + 22 + r * 27 + 13);
                    if (panel.SimDateAt(pt).Date == target.Date) { panel.SimClick(pt); clicked = true; }
                }
            Application.DoEvents();
            // 翻月箭头
            string m0 = panel.ViewMonth.ToString("yyyy-MM");
            panel.SimClick(new Point(14, 15));
            string mPrev = panel.ViewMonth.ToString("yyyy-MM");
            panel.SimClick(new Point(panel.Width - 14, 15));
            string mBack = panel.ViewMonth.ToString("yyyy-MM");
            // “今天”按钮
            panel.SimClick(new Point(30, panel.Height - 12));
            Application.DoEvents();
            string afterToday = dpHistory.Value.ToString("yyyy-MM-dd") + "/" + _filterDate.ToString("yyyy-MM-dd");
            return "want=" + target.ToString("yyyy-MM-dd") + " clicked=" + clicked +
                   " picker=" + dpHistory.Value.ToString("yyyy-MM-dd") +
                   " filter=" + _filterDate.ToString("yyyy-MM-dd") +
                   " rows=" + grid.Rows.Count + " | " + lblCount.Text +
                   " | monthPrev=" + m0 + "→" + mPrev + "→" + mBack + " today=" + afterToday;
        }

        private void UpdateTodayCount()
        {
            // 按每条记录的“录入日期”统计今日数量：每条记录都有 Time，且重新打开后能读回，
            // 避免旧数据/个别记录没有“打印时间”时导致今日计数归零
            int n = _records.Count(r => r.Time.Date == DateTime.Today);
            lblToday.Text = "今日已打印：" + n + " 台";
        }

        private void ClearInputs()
        {
            if (!_fixedModel)
            {
                txtModel.Text = "";
                txtType.Text = "";
            }
            txtSN.Text = "";
            txtMAC.Text = "";
            _macDirty = false;
            _lastRawQR = "";
            _scanState = _fixedModel ? ScanState.AwaitSN : ScanState.AwaitQR;
            SetStatus(_fixedModel ? "请扫描 SN 条码…" : "请扫描设备二维码…", Color.DodgerBlue);
            txtScan.Focus();
            UpdatePreview();
        }

        // 采集样本时把当前识别到的字段一起写下来（半自动标注）
        internal string CurrentFieldsText()
        {
            try
            {
                return "型号=" + txtModel.Text.Trim() + "\t类型=" + txtType.Text.Trim() +
                       "\tSN=" + txtSN.Text.Trim() + "\tMAC=" + txtMAC.Text.Trim();
            }
            catch { return ""; }
        }

        private string LastPrintPath { get { return Path.Combine(_dataDir, "最近打印.txt"); } }

        private void SaveLastPrinted(DeviceRecord rec)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("model=" + (rec.Model ?? ""));
                sb.AppendLine("type=" + (rec.Type ?? ""));
                sb.AppendLine("sn=" + (rec.SN ?? ""));
                sb.AppendLine("mac=" + (rec.MAC ?? ""));
                sb.AppendLine("rawqr=" + (rec.RawQR ?? ""));
                File.WriteAllText(LastPrintPath, sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }

        private bool RestoreLastPrinted()
        {
            if (!File.Exists(LastPrintPath)) return false;
            try
            {
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(LastPrintPath, Encoding.UTF8))
                {
                    string t = line.Trim();
                    if (t.Length == 0 || t.StartsWith("#")) continue;
                    int eq = t.IndexOf('=');
                    if (eq <= 0) continue;
                    d[t.Substring(0, eq).Trim()] = t.Substring(eq + 1).Trim();
                }
                string sn, mac;
                d.TryGetValue("sn", out sn); d.TryGetValue("mac", out mac);
                if (string.IsNullOrWhiteSpace(sn) && string.IsNullOrWhiteSpace(mac)) return false;
                string model, type, rawqr;
                d.TryGetValue("model", out model); d.TryGetValue("type", out type); d.TryGetValue("rawqr", out rawqr);
                txtModel.Text = model ?? "";
                txtType.Text = type ?? "";
                txtSN.Text = sn ?? "";
                txtMAC.Text = mac ?? "";
                _macDirty = true;                  // 恢复"最近打印"内容时也算新数据，便于重打
                _lastRawQR = rawqr ?? "";
                _scanState = _fixedModel ? ScanState.AwaitSN : ScanState.AwaitQR;
                SetStatus(_fixedModel ? "已恢复上次打印内容，扫描下一台" : "已恢复上次打印内容，扫描下一台", Color.DodgerBlue);
                return true;
            }
            catch { return false; }
        }

        private void SetMode(bool fixedModel)
        {
            _fixedModel = fixedModel;
            _scanState = fixedModel ? ScanState.AwaitSN : ScanState.AwaitQR;
            txtSN.Text = "";
            txtMAC.Text = "";
            _lastRawQR = "";
            if (lblStep != null)
                lblStep.Text = fixedModel
                    ? "第 1 步：填写型号/类型\n第 2 步：扫描 SN 条码\n第 3 步：扫描 MAC 条码"
                    : "第 1 步：扫描二维码\n第 2 步：扫描 MAC 条码";
            SetStatus(fixedModel ? "请填写型号/类型，然后扫描 SN 条码…" : "请扫描设备二维码…", Color.DodgerBlue);
            if (txtScan != null) txtScan.Focus();
            // 录入方式变了：如果扫码伴侣正在跑，让它跟着换模式（二维码 / 固定型号）
            try
            {
                if (_companion != null && _companion.Running)
                {
                    _companion.Send("mode=" + (fixedModel ? "fixed" : "qr"));
                    SetStatus(fixedModel
                        ? "扫码伴侣已切到固定型号模式（读 SN + MAC 条码）"
                        : "扫码伴侣已切到二维码模式（读二维码 + MAC 条码）", Color.SeaGreen);
                }
            }
            catch { }
            UpdatePreview();
        }

        private void SetStatus(string text, Color c)
        {
            if (lblStatus == null) return;
            lblStatus.Text = text;
            lblStatus.ForeColor = Ui.Resolve(c);
        }

        private void ApplySettingsToUi()
        {
            _suppressProps = true;
            numW.Value = Math.Max(numW.Minimum, Math.Min(numW.Maximum, (decimal)_settings.LabelWidthMm));
            numH.Value = Math.Max(numH.Minimum, Math.Min(numH.Maximum, (decimal)_settings.LabelHeightMm));
            numX.Maximum = (decimal)_settings.LabelWidthMm;
            numY.Maximum = (decimal)_settings.LabelHeightMm;
            cmbDpi.SelectedIndex = _settings.Dpi >= 300 ? 1 : 0;
            chkAuto.Checked = _settings.AutoPrint;
            chkModel.Checked = _settings.ShowModel;
            chkType.Checked = _settings.ShowType;
            chkSN.Checked = _settings.ShowSN;
            chkMAC.Checked = _settings.ShowMAC;
            _suppressProps = false;
            if (!string.IsNullOrEmpty(_settings.Printer))
            {
                int i = cmbPrinter.Items.IndexOf(_settings.Printer);
                if (i >= 0) cmbPrinter.SelectedIndex = i;
            }
        }

        private void RefreshPrinters(bool preselectSaved)
        {
            string prev = cmbPrinter.Text;
            cmbPrinter.Items.Clear();
            try
            {
                foreach (string p in PrinterSettings.InstalledPrinters)
                    cmbPrinter.Items.Add(p);
            }
            catch { }
            if (cmbPrinter.Items.Count > 0)
            {
                int idx = -1;
                if (preselectSaved && !string.IsNullOrEmpty(_settings.Printer))
                    idx = cmbPrinter.Items.IndexOf(_settings.Printer);
                if (idx < 0) idx = cmbPrinter.Items.IndexOf(prev);
                if (idx < 0) idx = cmbPrinter.Items.IndexOf("TSC TTP-244 Pro");
                if (idx < 0) idx = 0;
                cmbPrinter.SelectedIndex = idx;
            }
        }

        private void AddNetworkPrinter()
        {
            try
            {
                System.Diagnostics.Process.Start("rundll32.exe", "printui.dll,PrintUIEntry /il");
                SetStatus("正在打开添加打印机窗口，添加完成后点“刷新”", Color.DarkOrange);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开添加打印机窗口：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ConnectNetworkPrinter()
        {
            string addr = txtNetPrinter.Text.Trim();
            if (addr.Length == 0)
            {
                MessageBox.Show("请输入网络打印机地址，例如：\\\\192.168.1.100\\打印机名", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtNetPrinter.Focus();
                return;
            }
            if (!addr.StartsWith("\\\\")) addr = "\\\\" + addr;
            try
            {
                System.Diagnostics.Process.Start("rundll32.exe", "printui.dll,PrintUIEntry /in /n \"" + addr + "\"");
                SetStatus("正在连接网络打印机…稍后自动刷新列表", Color.DarkOrange);
                _refreshTimer.Stop();
                _refreshTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("连接失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ApplyChecksToLayout()
        {
            foreach (var it in _settings.Layout)
            {
                switch (it.Id)
                {
                    case "model_text": it.Visible = chkModel.Checked; break;
                    case "type_text": it.Visible = chkType.Checked; break;
                    case "sn_barcode":
                    case "sn_text": it.Visible = chkSN.Checked; break;
                    case "mac_barcode":
                    case "mac_text": it.Visible = chkMAC.Checked; break;
                }
            }
        }

        private void ApplyBarcodeWidthToLayout()
        {
            double W = _settings.LabelWidthMm;
            double maxw;
            if (_settings.BarcodeWidth == 0) maxw = Math.Max(20, W * 0.35);
            else if (_settings.BarcodeWidth == 1) maxw = Math.Max(20, W * 0.6);
            else maxw = Math.Max(20, W - 10);
            foreach (var it in _settings.Layout)
                if (it.IsBarcode) it.MaxWidthMm = maxw;
        }

        private Bitmap _labelBitmap;
        private Label _lblSizeHint;

        private void UpdatePreview()
        {
            try
            {
                var rec = CurrentRecord();
                List<string> warnings;
                var old = _labelBitmap;
                _labelBitmap = LabelRenderer.Render(rec, _settings.LabelWidthMm, _settings.LabelHeightMm, _settings.Dpi, _settings.Layout, out warnings, true);
                if (picPreview != null) picPreview.Image = _labelBitmap;
                if (old != null) old.Dispose();
                int wpx = LabelRenderer.MmToPx(_settings.LabelWidthMm, _settings.Dpi);
                int hpx = LabelRenderer.MmToPx(_settings.LabelHeightMm, _settings.Dpi);
                if (_lblSizeHint != null)
                    _lblSizeHint.Text = "标签尺寸：" + _settings.LabelWidthMm.ToString("0.#") + " × " + _settings.LabelHeightMm.ToString("0.#") + " mm（" + wpx + "×" + hpx + " px @ " + _settings.Dpi + " dpi）";
                if (warnings.Count > 0)
                    SetStatus(warnings[0], Color.Red);
            }
            catch (Exception ex)
            {
                ErrorLog.Log(ex);
            }
            if (picPreview != null) picPreview.Invalidate();
        }

        private void Pic_MouseDown(object sender, MouseEventArgs e)
        {
            if (_labelBitmap == null) return;
            var p = PreviewToLabel(e.Location);
            if (p == null) { SelectItem(null); return; }
            if (_moveAll)
            {
                _dragging = true;
                _allStartX = p.Value.X / PxPerMm();
                _allStartY = p.Value.Y / PxPerMm();
                _allOrigX = new double[_settings.Layout.Count];
                _allOrigY = new double[_settings.Layout.Count];
                for (int i = 0; i < _settings.Layout.Count; i++)
                {
                    _allOrigX[i] = _settings.Layout[i].Xmm;
                    _allOrigY[i] = _settings.Layout[i].Ymm;
                }
                picPreview.Cursor = Cursors.SizeAll;
                return;
            }
            LayoutItem hit = null;
            for (int i = _settings.Layout.Count - 1; i >= 0; i--)
            {
                var it = _settings.Layout[i];
                if (!it.Visible) continue;
                var r = LabelRenderer.ItemRect(it, CurrentRecord(), _settings.LabelWidthMm, _settings.LabelHeightMm, _settings.Dpi);
                if (r.Contains(p.Value))
                {
                    hit = it;
                    break;
                }
            }
            if (hit == null)
            {
                SelectItem(null);
                return;
            }
            SelectItem(hit);
            _dragging = true;
            _dragOffX = p.Value.X / PxPerMm() - hit.Xmm;
            _dragOffY = p.Value.Y / PxPerMm() - hit.Ymm;
            picPreview.Cursor = Cursors.SizeAll;
        }

        private void Pic_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging && _moveAll)
            {
                var p = PreviewToLabel(e.Location);
                if (p != null)
                {
                    double mmPerPx = 1.0 / PxPerMm();
                    double dx = p.Value.X * mmPerPx - _allStartX;
                    double dy = p.Value.Y * mmPerPx - _allStartY;
                    for (int i = 0; i < _settings.Layout.Count; i++)
                    {
                        var it = _settings.Layout[i];
                        it.Xmm = Math.Max(0, Math.Min(_settings.LabelWidthMm, _allOrigX[i] + dx));
                        it.Ymm = Math.Max(0, Math.Min(_settings.LabelHeightMm, _allOrigY[i] + dy));
                    }
                    UpdatePreview();
                }
                return;
            }
            if (_dragging && _selItem != null)
            {
                var p = PreviewToLabel(e.Location);
                if (p != null)
                {
                    double mmPerPx = 1.0 / PxPerMm();
                    _selItem.Xmm = Math.Max(0, Math.Min(_settings.LabelWidthMm, p.Value.X * mmPerPx - _dragOffX));
                    _selItem.Ymm = Math.Max(0, Math.Min(_settings.LabelHeightMm, p.Value.Y * mmPerPx - _dragOffY));
                    BindProps(_selItem);
                    UpdatePreview();
                }
            }
            else if (_labelBitmap != null)
            {
                var p = PreviewToLabel(e.Location);
                bool over = false;
                if (p != null)
                {
                    for (int i = _settings.Layout.Count - 1; i >= 0; i--)
                    {
                        var it = _settings.Layout[i];
                        if (!it.Visible) continue;
                        if (LabelRenderer.ItemRect(it, CurrentRecord(), _settings.LabelWidthMm, _settings.LabelHeightMm, _settings.Dpi).Contains(p.Value)) { over = true; break; }
                    }
                }
                picPreview.Cursor = over ? Cursors.Hand : Cursors.Default;
            }
        }

        private void Pic_MouseUp(object sender, MouseEventArgs e)
        {
            _dragging = false;
            picPreview.Cursor = Cursors.Default;
        }

        private PointF? PreviewToLabel(Point p)
        {
            if (_labelBitmap == null) return null;
            int cw = picPreview.ClientSize.Width, ch = picPreview.ClientSize.Height;
            double zoom = Math.Min((double)cw / _labelBitmap.Width, (double)ch / _labelBitmap.Height);
            double dw = _labelBitmap.Width * zoom, dh = _labelBitmap.Height * zoom;
            double ox = (cw - dw) / 2.0, oy = (ch - dh) / 2.0;
            double lx = (p.X - ox) / zoom, ly = (p.Y - oy) / zoom;
            if (lx < 0 || ly < 0 || lx > _labelBitmap.Width || ly > _labelBitmap.Height) return null;
            return new PointF((float)lx, (float)ly);
        }

        private double PxPerMm()
        {
            return _settings.Dpi / 25.4;
        }

        private void SelectItem(LayoutItem it)
        {
            _selItem = it;
            _selRect = it == null ? RectangleF.Empty : LabelRenderer.ItemRect(it, CurrentRecord(), _settings.LabelWidthMm, _settings.LabelHeightMm, _settings.Dpi);
            BindProps(it);
            picPreview.Invalidate();
        }

        private void BindProps(LayoutItem it)
        {
            _suppressProps = true;
            if (it == null)
            {
                lblSel.Text = "未选择元素";
                numFont.Enabled = false;
                numX.Enabled = false;
                numY.Enabled = false;
                numBarcodeH.Enabled = false;
                chkItemVisible.Enabled = false;
            }
            else
            {
                lblSel.Text = it.Name;
                numFont.Enabled = !it.IsBarcode;
                numFont.Value = Math.Max(numFont.Minimum, Math.Min(numFont.Maximum, (decimal)it.FontSizePt));
                numX.Enabled = true;
                numX.Value = Math.Max(numX.Minimum, Math.Min(numX.Maximum, (decimal)it.Xmm));
                numY.Enabled = true;
                numY.Value = Math.Max(numY.Minimum, Math.Min(numY.Maximum, (decimal)it.Ymm));
                numBarcodeH.Enabled = it.IsBarcode;
                numBarcodeH.Value = Math.Max(numBarcodeH.Minimum, Math.Min(numBarcodeH.Maximum, (decimal)it.HeightMm));
                chkItemVisible.Enabled = true;
                chkItemVisible.Checked = it.Visible;
            }
            _suppressProps = false;
        }

        private void Pic_Paint(object sender, PaintEventArgs e)
        {
            if (_selItem == null || _selRect.IsEmpty) return;
            // draw selection rectangle overlaid on preview coordinates
            int cw = picPreview.ClientSize.Width, ch = picPreview.ClientSize.Height;
            double zoom = Math.Min((double)cw / _labelBitmap.Width, (double)ch / _labelBitmap.Height);
            double dw = _labelBitmap.Width * zoom, dh = _labelBitmap.Height * zoom;
            double ox = (cw - dw) / 2.0, oy = (ch - dh) / 2.0;
            float x = (float)(ox + _selRect.X * zoom);
            float y = (float)(oy + _selRect.Y * zoom);
            float w = (float)(_selRect.Width * zoom);
            float h = (float)(_selRect.Height * zoom);
            using (var pen = new Pen(Color.Red, 1.5f) { DashStyle = DashStyle.Dash })
                e.Graphics.DrawRectangle(pen, x, y, w, h);
        }

        private void SaveAll()
        {
            _settings.Save();
            SaveHistoryByDay();
        }

        // 读取全部历史：所有按天文件（历史记录_yyyy-MM-dd.csv）+ 旧的单文件（历史记录.csv），去重后按时间倒序
        private List<DeviceRecord> LoadAllHistory()
        {
            var all = new List<DeviceRecord>();
            try
            {
                if (!Directory.Exists(_dataDir)) return all;
                var files = new List<string>();
                foreach (var file in Directory.GetFiles(_dataDir, "历史记录_*.csv"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    string ds = name.Substring(name.LastIndexOf('_') + 1);
                    DateTime d;
                    if (DateTime.TryParseExact(ds, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                        files.Add(file);   // 只读“按天”文件，跳过“历史记录_旧归档.csv”之类
                }
                string legacy = Path.Combine(_dataDir, "历史记录.csv");
                if (File.Exists(legacy)) files.Add(legacy);
                foreach (var file in files)
                {
                    try { all.AddRange(new HistoryStore(file).Load()); } catch { }
                }
                // 去重（同一台可能因历史迁移在多个文件里出现）+ 按时间倒序
                var seen = new HashSet<string>();
                var uniq = new List<DeviceRecord>();
                foreach (var r in all)
                {
                    string key = r.Time.ToString("yyyy-MM-dd HH:mm:ss") + "|" + (r.SN ?? "") + "|" + (r.MAC ?? "");
                    if (seen.Add(key)) uniq.Add(r);
                }
                // 本机删除过的记录不再显示（避免重开软件后“删了又回来”）
                var tomb = LoadTombstones();
                if (tomb.Count > 0) uniq.RemoveAll(delegate(DeviceRecord r) { return tomb.Contains(RecKey(r)); });
                uniq.Sort((a, b) => b.Time.CompareTo(a.Time));
                all = uniq;
            }
            catch { }
            return all;
        }

        private static string RecKey(DeviceRecord r)
        {
            if (r == null) return "";
            return r.Time.ToString("yyyy-MM-dd HH:mm:ss") + "|" + (r.SN ?? "") + "|" + (r.MAC ?? "");
        }

        // ---------- 删除标记（墓碑）：本机删掉的记录，不要再被 NAS 合并回来 ----------
        private string TombstonePath { get { return Path.Combine(_dataDir, "删除记录.txt"); } }

        private HashSet<string> LoadTombstones()
        {
            var set = new HashSet<string>();
            try
            {
                string p = TombstonePath;
                if (File.Exists(p))
                    foreach (var line in File.ReadAllLines(p, Encoding.UTF8))
                    {
                        string t = line.Trim();
                        if (t.Length > 0 && t.IndexOf('|') > 0) set.Add(t);
                    }
            }
            catch { }
            return set;
        }

        private void AddTombstone(DeviceRecord r)
        {
            try
            {
                string k = RecKey(r);
                if (string.IsNullOrEmpty(k)) return;
                if (!Directory.Exists(_dataDir)) Directory.CreateDirectory(_dataDir);
                File.AppendAllText(TombstonePath, k + "\r\n", new UTF8Encoding(false));
            }
            catch { }
        }

        // 按“录入日期”把记录写回各自的按天文件。
        // 以“磁盘现有内容”为准，只应用本机本次运行的新增/删除，避免把外部修改或复制进来的历史文件覆盖掉。
        private void SaveHistoryByDay()
        {
            try
            {
                if (!Directory.Exists(_dataDir)) Directory.CreateDirectory(_dataDir);

                var disk = LoadAllHistory();
                var seen = new HashSet<string>();
                var merged = new List<DeviceRecord>();
                foreach (var r in disk)
                {
                    string k = RecKey(r);
                    if (_deletedKeys.Contains(k)) continue;   // 本机删掉的，跳过
                    if (seen.Add(k)) merged.Add(r);           // 磁盘内容优先（外部修改/复制进来的以磁盘为准）
                }
                foreach (var r in _records)
                {
                    string k = RecKey(r);
                    if (_deletedKeys.Contains(k)) continue;
                    if (_newKeys.Contains(k) && seen.Add(k)) merged.Add(r);   // 只补本机新增的
                }

                var groups = new Dictionary<DateTime, List<DeviceRecord>>();
                foreach (var r in merged)
                {
                    DateTime d = r.Time.Date;
                    if (!groups.ContainsKey(d)) groups[d] = new List<DeviceRecord>();
                    groups[d].Add(r);
                }
                foreach (var kv in groups)
                    new HistoryStore(Path.Combine(_dataDir, "历史记录_" + kv.Key.ToString("yyyy-MM-dd") + ".csv")).Save(kv.Value);

                // 某天已经没有记录了：只有当“文件里的每一条都是被明确删掉的”才清空该文件。
                // 文件读不出来、或里面还有没被删除的记录，一律保持原样——绝不清空，
                // 否则一旦本机历史出问题（文件损坏/读不到），会把仅存的数据连同 NAS 备份一起抹掉。
                var tomb2 = LoadTombstones();
                foreach (var file in Directory.GetFiles(_dataDir, "历史记录_*.csv"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    string ds = name.Substring(name.LastIndexOf('_') + 1);
                    DateTime d;
                    if (!DateTime.TryParseExact(ds, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
                    if (groups.ContainsKey(d.Date)) continue;
                    List<DeviceRecord> rows;
                    if (!new HistoryStore(file).TryLoad(out rows)) continue;
                    if (rows.Count == 0) continue;
                    bool allDeleted = true;
                    foreach (var r in rows)
                    {
                        string k = RecKey(r);
                        if (!_deletedKeys.Contains(k) && !tomb2.Contains(k)) { allDeleted = false; break; }
                    }
                    if (allDeleted) new HistoryStore(file).Save(new List<DeviceRecord>());
                }
            }
            catch { }
        }

        // 清空全部历史：本机 +（可选）NAS 上本机那份；带二次确认，避免误删
        private void ClearAllHistory()
        {
            if (MessageBox.Show("确定清空全部历史记录？此操作不可恢复。", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            bool nasOn = false;
            string mn = "";
            try
            {
                nasOn = NasHook.IsEnabled != null && NasHook.IsEnabled();
                if (nasOn && NasHook.MachineName != null) mn = NasHook.MachineName();
            }
            catch { }

            bool clearNas = false;
            if (nasOn)
            {
                var r = MessageBox.Show(
                    "是否同时清空 NAS 上本机（" + mn + "）的历史记录？\n\n" +
                    "是：NAS 也一起清空（彻底删除）。\n" +
                    "否：只清空本机，NAS 备份保留——下次同步会把 NAS 里的记录合并回来。",
                    "NAS 备份", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                clearNas = (r == DialogResult.Yes);
            }

            _records.Clear();
            _newKeys.Clear();
            _deletedKeys.Clear();
            DeleteAllHistoryFiles();
            try { if (File.Exists(TombstonePath)) File.Delete(TombstonePath); } catch { }   // 全清了，删除标记也没意义了
            if (clearNas)
            {
                bool ok = false;
                try { if (NasHook.ClearHistory != null) { NasHook.ClearHistory(); ok = true; } } catch { }
                SetStatus(ok ? "已清空本机与 NAS 的历史记录" : "已清空本机历史（NAS 未清空）", ok ? Color.SeaGreen : Color.DarkOrange);
            }
            LoadHistoryGrid();
            UpdateTodayCount();
        }

        // 供壳程序在“NAS 合并回来数据”后刷新界面
        internal int TestHistoryCount { get { return _records.Count; } }
        internal int TestGridRows { get { return grid == null ? -1 : grid.Rows.Count; } }

        public void ReloadHistoryFromDisk()
        {
            try
            {
                _records.Clear();
                _records.AddRange(LoadAllHistory());
                LoadHistoryGrid();
                UpdateTodayCount();
            }
            catch { }
        }

        // 清空全部历史文件（按天文件 + 旧的单文件）
        private void DeleteAllHistoryFiles()
        {
            try
            {
                if (!Directory.Exists(_dataDir)) return;
                foreach (var file in Directory.GetFiles(_dataDir, "历史记录_*.csv"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    string ds = name.Substring(name.LastIndexOf('_') + 1);
                    DateTime d;
                    if (DateTime.TryParseExact(ds, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                        { try { File.Delete(file); } catch { } }
                }
                string legacy = Path.Combine(_dataDir, "历史记录.csv");
                if (File.Exists(legacy)) { try { File.Delete(legacy); } catch { } }
            }
            catch { }
        }

        // 嵌入到壳程序后，原 FormClosing 不触发，改由壳在关闭时调用本方法保存
        public void Shutdown()
        {
            try { _settings.Save(); } catch { }
            try { SaveHistoryByDay(); } catch { }
            try { if (_refreshTimer != null) _refreshTimer.Stop(); } catch { }
            try { if (_nasTimer != null) _nasTimer.Stop(); } catch { }
        }
    }

    // ==================== 摄像头 / 高拍仪识别 ====================

    // Windows 自带的视频采集接口（Video for Windows / avicap32）：
    // 免驱 UVC 摄像头、高拍仪都能用，不需要厂商 SDK、不需要外置 DLL。
    internal static class VfwCamera
    {
        private const int WM_CAP_START = 0x400;
        private const int WM_CAP_SET_CALLBACK_FRAME = WM_CAP_START + 5;
        private const int WM_CAP_DRIVER_CONNECT = WM_CAP_START + 10;
        private const int WM_CAP_DRIVER_DISCONNECT = WM_CAP_START + 11;
        private const int WM_CAP_DRIVER_GET_NAME = WM_CAP_START + 12;
        private const int WM_CAP_EDIT_COPY = WM_CAP_START + 30;
        private const int WM_CAP_DLG_VIDEOFORMAT = WM_CAP_START + 41;
        private const int WM_CAP_DLG_VIDEOSOURCE = WM_CAP_START + 42;
        private const int WM_CAP_GET_VIDEOFORMAT = WM_CAP_START + 44;
        private const int WM_CAP_SET_VIDEOFORMAT = WM_CAP_START + 45;
        private const int WM_CAP_SET_PREVIEW = WM_CAP_START + 50;
        private const int WM_CAP_SET_PREVIEWRATE = WM_CAP_START + 52;
        private const int WM_CAP_SET_SCALE = WM_CAP_START + 53;
        private const int WM_CAP_GRAB_FRAME_NOSTOP = WM_CAP_START + 61;
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;

        // 仅自测用：假装有一个可用的摄像头（脱离真机验证整套识别流程）
        internal static bool TestFakeDevice = false;
        internal static string TestFakeImage = "";

        // ---------- 驱动帧回调取图（比“复制到剪贴板”可靠得多，很多高拍仪不支持后者） ----------
        [StructLayout(LayoutKind.Sequential)]
        public struct VIDEOHDR
        {
            public IntPtr lpData;
            public int dwBufferLength;
            public int dwBytesUsed;
            public int dwTimeCaptured;
            public IntPtr dwUser;
            public int dwFlags;
            public IntPtr dwReserved1;
            public IntPtr dwReserved2;
            public IntPtr dwReserved3;
            public IntPtr dwReserved4;
        }

        public delegate IntPtr FrameProc(IntPtr hWnd, IntPtr lpVHdr);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, FrameProc lParam);

        private static FrameProc _frameProc;          // 必须保持引用，否则会被 GC 回收导致崩溃
        private static int _frameW, _frameH, _frameBits;
        private static int _framesSeen;
        private static bool _wantFrame;
        private static Bitmap _frameBmp;

        public static int FrameCallbacksSeen { get { return _framesSeen; } }
        public static bool WantFrame { set { _wantFrame = value; } }

        public static void HookFrameCallback(IntPtr h, int width, int height, int bitCount)
        {
            try
            {
                _frameW = width; _frameH = height; _frameBits = bitCount <= 0 ? 24 : bitCount;
                _framesSeen = 0;
                _frameProc = new FrameProc(OnFrame);
                SendMessage(h, WM_CAP_SET_CALLBACK_FRAME, IntPtr.Zero, _frameProc);
            }
            catch { }
        }

        public static void UnhookFrameCallback(IntPtr h)
        {
            try { SendMessage(h, WM_CAP_SET_CALLBACK_FRAME, IntPtr.Zero, (FrameProc)null); } catch { }
            _frameProc = null;
            try { if (_frameBmp != null) { _frameBmp.Dispose(); _frameBmp = null; } } catch { }
        }

        // 取最近一次回调拿到的画面（调用方自己负责 Dispose）
        public static Bitmap TakeLastFrame()
        {
            try
            {
                if (_frameBmp == null) return null;
                var copy = new Bitmap(_frameBmp);
                return copy;
            }
            catch { return null; }
        }

        private static IntPtr OnFrame(IntPtr hWnd, IntPtr lpVHdr)
        {
            try
            {
                _framesSeen++;
                if (!_wantFrame || _frameW <= 0 || _frameH <= 0) return IntPtr.Zero;
                var vh = (VIDEOHDR)Marshal.PtrToStructure(lpVHdr, typeof(VIDEOHDR));
                if (vh.lpData == IntPtr.Zero) return IntPtr.Zero;
                int bits = _frameBits >= 32 ? 32 : (_frameBits >= 24 ? 24 : 16);
                int stride = ((_frameW * bits + 31) / 32) * 4;
                int rowBytes = _frameW * (bits / 8);
                if (_frameBmp == null || _frameBmp.Width != _frameW || _frameBmp.Height != _frameH)
                {
                    if (_frameBmp != null) _frameBmp.Dispose();
                    _frameBmp = new Bitmap(_frameW, _frameH, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                }
                var bd = _frameBmp.LockBits(new Rectangle(0, 0, _frameW, _frameH),
                    System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                try
                {
                    byte[] row = new byte[Math.Max(rowBytes, stride)];
                    for (int y = 0; y < _frameH; y++)
                    {
                        IntPtr src = (IntPtr)(vh.lpData.ToInt64() + (long)(_frameH - 1 - y) * stride);   // DIB 是自下而上
                        Marshal.Copy(src, row, 0, Math.Min(row.Length, rowBytes));
                        IntPtr dst = (IntPtr)(bd.Scan0.ToInt64() + (long)y * bd.Stride);
                        Marshal.Copy(row, 0, dst, rowBytes);
                    }
                }
                finally { _frameBmp.UnlockBits(bd); }
                _wantFrame = false;
            }
            catch { }
            return IntPtr.Zero;
        }

        [DllImport("avicap32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr capCreateCaptureWindowA(string lpszWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, int nID);

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, StringBuilder lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        public static IntPtr Create(IntPtr parent, int w, int h)
        {
            if (TestFakeDevice) return (IntPtr)12345;
            try { return capCreateCaptureWindowA("printer_cam", WS_CHILD | WS_VISIBLE, 0, 0, Math.Max(1, w), Math.Max(1, h), parent, 0); }
            catch { return IntPtr.Zero; }
        }

        public static bool Connect(IntPtr h, int index)
        {
            if (TestFakeDevice) return true;
            if (h == IntPtr.Zero) return false;
            try { return SendMessage(h, WM_CAP_DRIVER_CONNECT, (IntPtr)index, IntPtr.Zero) != IntPtr.Zero; }
            catch { return false; }
        }

        public static void Disconnect(IntPtr h) { if (TestFakeDevice) return; try { if (h != IntPtr.Zero) SendMessage(h, WM_CAP_DRIVER_DISCONNECT, IntPtr.Zero, IntPtr.Zero); } catch { } }
        public static void Destroy(IntPtr h) { if (TestFakeDevice) return; try { if (h != IntPtr.Zero) DestroyWindow(h); } catch { } }
        public static void Move(IntPtr h, int x, int y, int w, int hh) { if (TestFakeDevice) return; try { if (h != IntPtr.Zero) MoveWindow(h, x, y, Math.Max(1, w), Math.Max(1, hh), true); } catch { } }
        public static void ToBottom(IntPtr h) { if (TestFakeDevice) return; try { if (h != IntPtr.Zero) SetWindowPos(h, (IntPtr)1, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); } catch { } }

        public static void StartPreview(IntPtr h, int rateMs)
        {
            if (TestFakeDevice) return;
            try
            {
                SendMessage(h, WM_CAP_SET_CALLBACK_FRAME, IntPtr.Zero, IntPtr.Zero);
                SendMessage(h, WM_CAP_SET_SCALE, (IntPtr)1, IntPtr.Zero);
                SendMessage(h, WM_CAP_SET_PREVIEWRATE, (IntPtr)Math.Max(30, rateMs), IntPtr.Zero);
                SendMessage(h, WM_CAP_SET_PREVIEW, (IntPtr)1, IntPtr.Zero);
            }
            catch { }
        }

        // 关掉驱动自带的预览窗口（改成我们自己画帧，画面与识别内容完全一致、且可左右翻转）
        public static void StopPreview(IntPtr h)
        {
            if (TestFakeDevice) return;
            try { if (h != IntPtr.Zero) SendMessage(h, WM_CAP_SET_PREVIEW, IntPtr.Zero, IntPtr.Zero); } catch { }
        }

        public static void ShowFormatDialog(IntPtr h) { try { if (h != IntPtr.Zero) SendMessage(h, WM_CAP_DLG_VIDEOFORMAT, IntPtr.Zero, IntPtr.Zero); } catch { } }
        public static void ShowSourceDialog(IntPtr h) { try { if (h != IntPtr.Zero) SendMessage(h, WM_CAP_DLG_VIDEOSOURCE, IntPtr.Zero, IntPtr.Zero); } catch { } }

        // 取一帧画面（抓帧 + 复制到剪贴板再取出，兼容所有 UVC 设备）
        public static Bitmap Grab(IntPtr h)
        {
            if (TestFakeDevice)
            {
                try
                {
                    // 注意：必须做成“不依赖流”的独立位图，否则流关掉后 GDI+ 再裁剪/克隆会报“内存不足”
                    byte[] bytes = File.ReadAllBytes(TestFakeImage);
                    using (var ms = new MemoryStream(bytes, false))
                    using (var img = Image.FromStream(ms, false, false))
                        return new Bitmap(img);
                }
                catch { return null; }
            }
            if (h == IntPtr.Zero) return null;
            try
            {
                SendMessage(h, WM_CAP_GRAB_FRAME_NOSTOP, IntPtr.Zero, IntPtr.Zero);
                SendMessage(h, WM_CAP_EDIT_COPY, IntPtr.Zero, IntPtr.Zero);
                for (int i = 0; i < 12; i++)          // 大分辨率下剪贴板写入比较慢，多等一会儿
                {
                    try
                    {
                        if (Clipboard.ContainsImage())
                        {
                            using (var img = Clipboard.GetImage())
                            {
                                if (img != null) return new Bitmap(img);
                            }
                        }
                    }
                    catch { }
                    System.Threading.Thread.Sleep(90);
                }
            }
            catch { }
            return null;
        }

        // 当前视频格式（分辨率）——用来判断画质够不够读码
        public static Size FrameSize(IntPtr h)
        {
            if (TestFakeDevice) return new Size(1280, 720);
            IntPtr p = IntPtr.Zero;
            try
            {
                if (h == IntPtr.Zero) return Size.Empty;
                const int size = 4096;
                p = Marshal.AllocHGlobal(size);
                Marshal.Copy(new byte[size], 0, p, size);
                SendMessage(h, WM_CAP_GET_VIDEOFORMAT, (IntPtr)Marshal.SizeOf(typeof(BITMAPINFOHEADER)), p);
                var bih = (BITMAPINFOHEADER)Marshal.PtrToStructure(p, typeof(BITMAPINFOHEADER));
                if (bih.biWidth > 0 && bih.biHeight != 0) return new Size(bih.biWidth, Math.Abs(bih.biHeight));
            }
            catch { }
            finally { try { if (p != IntPtr.Zero) Marshal.FreeHGlobal(p); } catch { } }
            return Size.Empty;
        }

        // 枚举摄像头（免驱 UVC 设备都会出现在这里）
        private static readonly int[,] WantSizes = new int[,] { { 1920, 1080 }, { 1280, 720 }, { 1024, 768 }, { 800, 600 }, { 640, 480 } };

        // 试着把视频格式切到更高的分辨率（免驱驱动默认常常只给 640×480）
        public static Size TrySetBestFormat(IntPtr h, out string note)
        {
            Size cur = FrameSize(h);
            note = "";
            if (TestFakeDevice) { note = "已是 " + cur.Width + "×" + cur.Height + "（模拟）"; return cur; }
            if (h == IntPtr.Zero) { note = "设备未打开"; return cur; }
            try
            {
                for (int i = 0; i < WantSizes.GetLength(0); i++)
                {
                    int w = WantSizes[i, 0], hh = WantSizes[i, 1];
                    if (cur.Width >= w) { note = "已是 " + cur.Width + "×" + cur.Height; return cur; }
                    if (TrySetFormat(h, w, hh))
                    {
                        Size now = FrameSize(h);
                        if (now.Width >= w) { note = "已自动切换到 " + now.Width + "×" + now.Height; return now; }
                    }
                }
            }
            catch { }
            note = "驱动不支持更高的视频格式（当前 " + cur.Width + "×" + cur.Height + "）。";
            return cur;
        }

        // 仅自测用：直接设置视频格式
        internal static bool TrySetFormatForTest(IntPtr h, int w, int hh) { return TrySetFormat(h, w, hh); }

        // 当前格式的位深（帧回调需要用它算行宽）
        public static int FrameBits(IntPtr h)
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                if (h == IntPtr.Zero) return 24;
                const int size = 4096;
                p = Marshal.AllocHGlobal(size);
                Marshal.Copy(new byte[size], 0, p, size);
                SendMessage(h, WM_CAP_GET_VIDEOFORMAT, (IntPtr)Marshal.SizeOf(typeof(BITMAPINFOHEADER)), p);
                var bih = (BITMAPINFOHEADER)Marshal.PtrToStructure(p, typeof(BITMAPINFOHEADER));
                return bih.biBitCount <= 0 ? 24 : bih.biBitCount;
            }
            catch { return 24; }
            finally { try { if (p != IntPtr.Zero) Marshal.FreeHGlobal(p); } catch { } }
        }

        private static bool TrySetFormat(IntPtr h, int w, int hh)
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                const int size = 4096;
                p = Marshal.AllocHGlobal(size);
                Marshal.Copy(new byte[size], 0, p, size);
                SendMessage(h, WM_CAP_GET_VIDEOFORMAT, (IntPtr)Marshal.SizeOf(typeof(BITMAPINFOHEADER)), p);
                var bih = (BITMAPINFOHEADER)Marshal.PtrToStructure(p, typeof(BITMAPINFOHEADER));
                bih.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bih.biWidth = w;
                bih.biHeight = hh;
                int bytes = Math.Max(1, bih.biBitCount / 8);
                bih.biSizeImage = w * hh * bytes;
                Marshal.StructureToPtr(bih, p, false);
                SendMessage(h, WM_CAP_SET_VIDEOFORMAT, (IntPtr)Marshal.SizeOf(typeof(BITMAPINFOHEADER)), p);
                Size now = FrameSize(h);
                return now.Width >= w;
            }
            catch { return false; }
            finally { try { if (p != IntPtr.Zero) Marshal.FreeHGlobal(p); } catch { } }
        }

        public static List<string> ListDevices(IntPtr parent)
        {
            var list = new List<string>();
            if (TestFakeDevice) { list.Add("测试摄像头（模拟）"); return list; }
            IntPtr h = IntPtr.Zero;
            try
            {
                h = Create(parent, 2, 2);
                if (h == IntPtr.Zero) return list;
                for (int i = 0; i < 12; i++)
                {
                    if (!Connect(h, i)) break;
                    string name = "";
                    try
                    {
                        var sb = new StringBuilder(160);
                        SendMessage(h, WM_CAP_DRIVER_GET_NAME, (IntPtr)i, sb);
                        name = sb.ToString().Trim();
                    }
                    catch { }
                    list.Add(name.Length == 0 ? ("摄像头 " + (i + 1)) : name);
                    Disconnect(h);
                }
            }
            catch { }
            finally { Destroy(h); }
            return list;
        }
    }

    // 画面解码与结果归类（二维码内容 / MAC 条码 / 其它条码）
    // 带位置信息的条码（用它和标签上印的字对齐，判断这条码是哪一项）
    internal class CodeHit { public string Text = ""; public Rectangle Box = Rectangle.Empty; }

    internal static class CameraDetect
    {
        private static ZXing.BarcodeReader _qr;
        private static ZXing.BarcodeReader _bc;
        private static ZXing.BarcodeReader _qrHard;
        private static ZXing.BarcodeReader _bcHard;

        private static ZXing.BarcodeReader Make(bool qr, bool hard)
        {
            var r = new ZXing.BarcodeReader();
            r.AutoRotate = true;
            r.Options.TryInverted = true;
            r.Options.TryHarder = hard;
            r.Options.PossibleFormats = qr
                ? new List<ZXing.BarcodeFormat> { ZXing.BarcodeFormat.QR_CODE }
                : new List<ZXing.BarcodeFormat>
                  {
                      ZXing.BarcodeFormat.CODE_128, ZXing.BarcodeFormat.CODE_39, ZXing.BarcodeFormat.CODE_93,
                      ZXing.BarcodeFormat.EAN_13, ZXing.BarcodeFormat.EAN_8, ZXing.BarcodeFormat.ITF, ZXing.BarcodeFormat.UPC_A
                  };
            return r;
        }

        private static ZXing.BarcodeReader QrReader(bool hard)
        {
            if (hard)
            {
                if (_qrHard == null) _qrHard = Make(true, true);
                return _qrHard;
            }
            if (_qr == null) _qr = Make(true, false);
            return _qr;
        }

        private static ZXing.BarcodeReader BarcodeReader1D(bool hard)
        {
            if (hard)
            {
                if (_bcHard == null) _bcHard = Make(false, true);
                return _bcHard;
            }
            if (_bc == null) _bc = Make(false, false);
            return _bc;
        }

        public static Rectangle RoiRect(Size s, int percent)
        {
            if (percent >= 100 || percent <= 0) return new Rectangle(0, 0, s.Width, s.Height);
            int w = Math.Max(1, s.Width * percent / 100);
            int h = Math.Max(1, s.Height * percent / 100);
            return new Rectangle((s.Width - w) / 2, (s.Height - h) / 2, w, h);
        }

        private static void Add(ZxingTexts keep, ZXing.BarcodeReader r, Bitmap img)
        {
            try
            {
                var res = r.DecodeMultiple(img);
                if (res == null) return;
                foreach (var one in res)
                {
                    if (one == null) continue;
                    string t = (one.Text ?? "").Trim();
                    if (t.Length > 0 && !keep.Texts.Contains(t)) keep.Texts.Add(t);
                }
            }
            catch { }
        }

        private class ZxingTexts { public List<string> Texts = new List<string>(); }

        // 解出画面里的所有码（二维码 + 一维码），只在指定区域内找。
        // 先“快速模式”扫（快），需要的那一类没扫到才上“加强模式”（准但慢）。
        public static List<string> DecodeTexts(Bitmap bmp, int roiPercent, bool wantQr, bool wantBarcode)
        {
            var keep = new ZxingTexts();
            if (bmp == null) return keep.Texts;
            Rectangle roi = RoiRect(bmp.Size, roiPercent);
            Bitmap work = bmp;
            bool cloned = false;
            try
            {
                if (roi.Width < bmp.Width || roi.Height < bmp.Height)
                {
                    work = bmp.Clone(roi, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    cloned = true;
                }
                var qrHit = new ZxingTexts();
                var bcHit = new ZxingTexts();
                if (wantQr) { Add(qrHit, QrReader(false), work); Merge(keep, qrHit); }
                if (wantBarcode) { Add(bcHit, BarcodeReader1D(false), work); Merge(keep, bcHit); }
                if (wantQr && qrHit.Texts.Count == 0) Add(keep, QrReader(true), work);
                if (wantBarcode && bcHit.Texts.Count == 0) Add(keep, BarcodeReader1D(true), work);
            }
            catch { }
            finally { if (cloned) { try { work.Dispose(); } catch { } } }
            return keep.Texts;
        }

        public static List<string> DecodeTexts(Bitmap bmp, int roiPercent)
        {
            return DecodeTexts(bmp, roiPercent, true, true);
        }

        // 指定矩形区域（像素）识别
        // 解出条码并带位置（用于和标签上印的字对齐）
        public static List<CodeHit> DecodeHits(Bitmap bmp, Rectangle roi)
        {
            var hits = new List<CodeHit>();
            if (bmp == null) return hits;
            if (roi.Width < 16 || roi.Height < 16) roi = new Rectangle(0, 0, bmp.Width, bmp.Height);
            Bitmap work = null;
            try
            {
                work = bmp.Clone(roi, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                AddHits(hits, BarcodeReader1D(false), work, roi.X, roi.Y);
                AddHits(hits, QrReader(false), work, roi.X, roi.Y);
                if (hits.Count == 0)
                {
                    AddHits(hits, BarcodeReader1D(true), work, roi.X, roi.Y);
                    AddHits(hits, QrReader(true), work, roi.X, roi.Y);
                }
            }
            catch { }
            finally { if (work != null) { try { work.Dispose(); } catch { } } }
            return hits;
        }

        private static void AddHits(List<CodeHit> hits, ZXing.BarcodeReader r, Bitmap img, int ox, int oy)
        {
            try
            {
                var res = r.DecodeMultiple(img);
                if (res == null) return;
                foreach (var one in res)
                {
                    if (one == null) continue;
                    string txt = (one.Text ?? "").Trim();
                    if (txt.Length == 0) continue;
                    bool dup = false;
                    foreach (var h in hits) if (h.Text == txt) { dup = true; break; }
                    if (dup) continue;
                    var box = new Rectangle(ox, oy, img.Width, img.Height);
                    try
                    {
                        var pts = one.ResultPoints;
                        if (pts != null && pts.Length > 0)
                        {
                            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                            foreach (var p in pts)
                            {
                                if (p.X < minX) minX = p.X;
                                if (p.X > maxX) maxX = p.X;
                                if (p.Y < minY) minY = p.Y;
                                if (p.Y > maxY) maxY = p.Y;
                            }
                            int x = ox + (int)Math.Max(0, minX), y = oy + (int)Math.Max(0, minY);
                            int w = Math.Max(20, (int)(maxX - minX));
                            int h2 = Math.Max(8, (int)(maxY - minY));
                            box = new Rectangle(x, y, w, h2);
                        }
                    }
                    catch { }
                    hits.Add(new CodeHit { Text = txt, Box = box });
                }
            }
            catch { }
        }

        public static List<string> DecodeRect(Bitmap bmp, Rectangle roi, bool wantQr, bool wantBarcode)
        {
            var keep = new ZxingTexts();
            if (bmp == null) return keep.Texts;
            if (roi.Width < 16 || roi.Height < 16) roi = new Rectangle(0, 0, bmp.Width, bmp.Height);
            if (roi.X < 0) roi.X = 0;
            if (roi.Y < 0) roi.Y = 0;
            if (roi.Right > bmp.Width) roi.Width = bmp.Width - roi.X;
            if (roi.Bottom > bmp.Height) roi.Height = bmp.Height - roi.Y;
            Bitmap work = null;
            try
            {
                work = bmp.Clone(roi, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                // 大图先缩一半做“快速扫描”（二维码在 250 像素左右时缩一半仍然能读），快很多
                Bitmap small = null;
                try
                {
                    if (work.Width > 1400)
                    {
                        small = new Bitmap(work, new Size(work.Width / 2, work.Height / 2));
                        var q2 = new ZxingTexts();
                        if (wantQr) { Add(q2, QrReader(false), small); Merge(keep, q2); }
                        var b2 = new ZxingTexts();
                        if (wantBarcode) { Add(b2, BarcodeReader1D(false), small); Merge(keep, b2); }
                        bool doneQr = !wantQr || q2.Texts.Count > 0;
                        bool doneBc = !wantBarcode || b2.Texts.Count > 0;
                        if (doneQr && doneBc) return keep.Texts;   // 缩图就都读到了，直接返回
                    }
                }
                catch { }
                finally { if (small != null) { try { small.Dispose(); } catch { } } }
                var qrHit = new ZxingTexts();
                var bcHit = new ZxingTexts();
                if (wantQr) { Add(qrHit, QrReader(false), work); Merge(keep, qrHit); }
                if (wantBarcode) { Add(bcHit, BarcodeReader1D(false), work); Merge(keep, bcHit); }
                if (wantQr && qrHit.Texts.Count == 0) Add(keep, QrReader(true), work);
                if (wantBarcode && bcHit.Texts.Count == 0) Add(keep, BarcodeReader1D(true), work);
            }
            catch { }
            finally { if (work != null) { try { work.Dispose(); } catch { } } }
            return keep.Texts;
        }

        private static void Merge(ZxingTexts to, ZxingTexts from)
        {
            foreach (string t in from.Texts) if (!to.Texts.Contains(t)) to.Texts.Add(t);
        }

        // 画面亮度统计：用来判断“抓到的画面是不是纯色/全黑”（取帧失败）
        public static void Brightness(Bitmap bmp, out double avg, out double dev)
        {
            avg = 0; dev = 0;
            if (bmp == null) return;
            try
            {
                double sum = 0, sum2 = 0; int n = 0;
                int stepX = Math.Max(1, bmp.Width / 160), stepY = Math.Max(1, bmp.Height / 120);
                for (int y = 0; y < bmp.Height; y += stepY)
                    for (int x = 0; x < bmp.Width; x += stepX)
                    {
                        Color c = bmp.GetPixel(x, y);
                        double v = (c.R + c.G + c.B) / 3.0;
                        sum += v; sum2 += v * v; n++;
                    }
                if (n > 0)
                {
                    avg = sum / n;
                    double var = sum2 / n - avg * avg;
                    dev = var > 0 ? Math.Sqrt(var) : 0;
                }
            }
            catch { }
        }

        // 归类：二维码内容、MAC、以及其它条码文本（固定型号模式下的 SN 条码）
        public static void Classify(List<string> texts, out string qrText, out string mac, out string otherText)
        {
            qrText = null; mac = null; otherText = null;
            if (texts == null) return;
            foreach (string t in texts)
            {
                if (string.IsNullOrEmpty(t)) continue;
                string m = QRParser.NormalizeMac(t);
                if (m != null) { if (mac == null) mac = m; continue; }
                if (qrText == null)
                {
                    var p = QRParser.Parse(t);
                    if (p != null && (!string.IsNullOrEmpty(p.SN) || !string.IsNullOrEmpty(p.Model) || !string.IsNullOrEmpty(p.Type)))
                        qrText = t;
                }
                // 固定型号模式要的是 SN 条码：跳过网址类内容（二维码常常就是个网址）
                bool looksUrl = t.StartsWith("http", StringComparison.OrdinalIgnoreCase) || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
                if (otherText == null && !looksUrl) otherText = t;
            }
        }

        // ================= 速度优先的一枪识别（一次解出二维码 + 条码） =================
        // 现场慢在哪：① 每帧把二维码、条码分两轮扫；② 一维码在整幅 4200×3100 上解（1~2 秒）。
        // 这里改成：
        //   ① 二维码、条码同时开两条线解（多核并行），一次帧就把“型号/类型/SN + MAC”全拿到；
        //   ② 一维码先用“行剖面”定位到条码所在横带，再只在窄条上解（几十毫秒搞定）；
        //   ③ 关掉 AutoRotate / TryInverted 这类“重扫一遍整幅”的开关（原来白花 0.7~1 秒）；
        //   ④ 记住上一台设备用到的二维码框、条码窗口，下一台先在这些位置找，命中就更快。
        private static ZXing.BarcodeReader _qrOnce, _qrOnceRot, _bcOnce;

        private static ZXing.BarcodeReader QrOnce(bool rotate)
        {
            if (!rotate)
            {
                if (_qrOnce == null)
                {
                    var r = new ZXing.BarcodeReader();
                    r.AutoRotate = false;            // 不旋转重扫
                    r.Options.TryInverted = false;   // 不反色重扫
                    r.Options.TryHarder = true;      // 标签上的小二维码要加强模式
                    r.Options.PossibleFormats = new List<ZXing.BarcodeFormat> { ZXing.BarcodeFormat.QR_CODE };
                    _qrOnce = r;
                }
                return _qrOnce;
            }
            if (_qrOnceRot == null)
            {
                var r2 = new ZXing.BarcodeReader();
                r2.AutoRotate = true;                // 兜底：摄像头装歪了才会用到
                r2.Options.TryInverted = true;
                r2.Options.TryHarder = true;
                r2.Options.PossibleFormats = new List<ZXing.BarcodeFormat> { ZXing.BarcodeFormat.QR_CODE };
                _qrOnceRot = r2;
            }
            return _qrOnceRot;
        }

        private static ZXing.BarcodeReader BcOnce()
        {
            if (_bcOnce == null)
            {
                var r = new ZXing.BarcodeReader();
                r.AutoRotate = false;
                r.Options.TryInverted = false;
                r.Options.TryHarder = true;          // 只在窄横带上解，加强模式也很快
                r.Options.PossibleFormats = new List<ZXing.BarcodeFormat>
                {
                    ZXing.BarcodeFormat.CODE_128, ZXing.BarcodeFormat.CODE_39, ZXing.BarcodeFormat.CODE_93,
                    ZXing.BarcodeFormat.EAN_13, ZXing.BarcodeFormat.ITF, ZXing.BarcodeFormat.EAN_8, ZXing.BarcodeFormat.UPC_A
                };
                _bcOnce = r;
            }
            return _bcOnce;
        }

        // 画面指纹：64×48 采样，1~2ms。画面没动就不重复识别（用户要求：和上一台一样就不要重复提取填写）
        public const int FpCols = 64;
        public const int FpRows = 48;

        // 把画面缩成 64×48 的“亮度格子”（每格 0~15），用来判断画面有没有变
        public static void FrameCells(Bitmap bmp, Rectangle roi, byte[] cells)
        {
            if (bmp == null || cells == null) return;
            try
            {
                if (roi.Width < 16 || roi.Height < 16) roi = new Rectangle(0, 0, bmp.Width, bmp.Height);
                var bd = bmp.LockBits(roi, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    int stride = bd.Stride;
                    int rows = FpRows, cols = FpCols;
                    for (int gy = 0; gy < rows; gy++)
                    {
                        int yy = gy * roi.Height / rows;
                        for (int gx = 0; gx < cols; gx++)
                        {
                            int xx = gx * roi.Width / cols;
                            int off = yy * stride + xx * 3;
                            int v = (System.Runtime.InteropServices.Marshal.ReadByte(bd.Scan0, off) * 114 +
                                     System.Runtime.InteropServices.Marshal.ReadByte(bd.Scan0, off + 1) * 587 +
                                     System.Runtime.InteropServices.Marshal.ReadByte(bd.Scan0, off + 2) * 299) / 1000;
                            cells[gy * cols + gx] = (byte)(v >> 4);      // 16 级亮度
                        }
                    }
                }
                finally { bmp.UnlockBits(bd); }
            }
            catch { }
        }

        // 两个格子数组差多少（0~1）。相机噪点会造成少量格子变化，所以按比例判断
        public static double CellDiff(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return 1;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                int d = a[i] - b[i];
                if (d < 0) d = -d;
                if (d >= 2) diff++;
            }
            return (double)diff / a.Length;
        }

        private static int Luma(byte[] buf, int p)
        {
            return (buf[p] * 114 + buf[p + 1] * 587 + buf[p + 2] * 299) / 1000;
        }

        private static bool HasMac(List<CodeHit> hits)
        {
            foreach (var h in hits) if (QRParser.NormalizeMac(h.Text) != null) return true;
            return false;
        }

        // 一维码：先解“上一台用过的窗口”；没拿到就跑“行扫描”找条码行（每行取一条整宽横窗去解）。
        // 现场验证：条码那一行的“水平总变差”最大，取前几行去解，2~3 个窗口就能命中，几十毫秒。
        private static List<CodeHit> ScanBarcodes(Bitmap crop, List<Rectangle> hints, bool needSn, bool deepFallback, out List<Rectangle> used, out string diag)
        {
            var hits = new List<CodeHit>();
            used = new List<Rectangle>();
            long profMs = 0, decMs = 0;
            int w = crop.Width, h = crop.Height;
            var r = BcOnce();
            var cand = new List<Rectangle>();
            if (hints != null)
                foreach (var hh in hints)
                {
                    if (hh.Width < 40 || hh.Height < 8) continue;
                    if (hh.X < 0 || hh.Y < 0 || hh.Right > w || hh.Bottom > h) continue;
                    cand.Add(hh);
                }

            // 1) 先用“上一台的位置”解（设备没挪窝时最快，1~2 个窗口就够）
            for (int i = 0; i < cand.Count; i++) DecodeWindow(crop, cand[i], r, hits, used, ref decMs);
            if (SatisfiedBc(hits, needSn))
            {
                diag = "上次位置 解码" + decMs + "ms 命中" + hits.Count;
                return hits;
            }

            // 2) 条码定位：先按列打分找“条码所在的横向区间”，再在区间里按行打分找条码行
            var sw = Stopwatch.StartNew();
            var cand2 = BarcodeCandidates(crop);
            profMs = sw.ElapsedMilliseconds;
            int tried = 0;
            foreach (var win in cand2)
            {
                if (tried >= 16) break;
                bool dup = false;
                foreach (var c in cand) if (Math.Abs(c.Y - win.Y) < 36 && Math.Abs(c.X - win.X) < 120) { dup = true; break; }
                if (dup) continue;
                cand.Add(win); tried++;
                DecodeWindow(crop, win, r, hits, used, ref decMs);
                if (SatisfiedBc(hits, needSn)) break;
            }

            // 3) 兜底：快路什么都没读到（或读不全）时，退回“整幅 + 旋转/反色”的老办法，慢但保险
            bool deep = false;
            if (deepFallback && !SatisfiedBc(hits, needSn))
            {
                deep = true;
                var sw3 = Stopwatch.StartNew();
                try
                {
                    var all = DecodeHits(crop, new Rectangle(0, 0, w, h));
                    foreach (var g in all)
                    {
                        bool dup2 = false;
                        foreach (var ex in hits) if (ex.Text == g.Text) { dup2 = true; break; }
                        if (!dup2) hits.Add(g);
                    }
                }
                catch { }
                decMs += sw3.ElapsedMilliseconds;
            }
            diag = "定位" + profMs + "ms 解码" + decMs + "ms 候选" + cand.Count + " 命中" + hits.Count + (deep ? " 整幅兜底" : "");
            return hits;
        }

        // 条码要的“两类内容”：MAC（12 位十六进制）+（固定型号模式下还要）SN 条码
        private static bool SatisfiedBc(List<CodeHit> hits, bool needSn)
        {
            int mac = 0, other = 0;
            foreach (var h in hits)
            {
                if (QRParser.NormalizeMac(h.Text) != null) mac++;
                else other++;
            }
            if (mac == 0) return false;
            return !needSn || other > 0;
        }

        private static void DecodeWindow(Bitmap crop, Rectangle rect, ZXing.BarcodeReader r, List<CodeHit> hits, List<Rectangle> used, ref long decMs)
        {
            if (rect.Width < 40 || rect.Height < 8) return;
            if (rect.X < 0 || rect.Y < 0 || rect.Right > crop.Width || rect.Bottom > crop.Height) return;
            var sw = Stopwatch.StartNew();
            List<CodeHit> got = null;
            try
            {
                using (var piece = crop.Clone(rect, PixelFormat.Format24bppRgb))
                    got = AddHitsFast(piece, 0, 0, r);
            }
            catch { }
            decMs += sw.ElapsedMilliseconds;
            if (got == null || got.Count == 0) return;
            foreach (var g in got)
            {
                bool dup = false;
                foreach (var ex in hits) if (ex.Text == g.Text) { dup = true; break; }
                if (!dup) hits.Add(g);
            }
            if (!used.Contains(rect)) used.Add(rect);
        }

        // 条码所在的横向区间宽度（真实标签上的条码宽度约 800px，这里给足余量）
        private const int SegWidth = 1100;

        // 条码定位：① 按列打分（“竖直方向一致 + 水平有边”= 条码纹路）找出条码横向区间；
        //           ② 在每个区间里按同样的特征给行打分，取最高的几行 → 得到若干候选窗口
        public static List<Rectangle> BarcodeCandidates(Bitmap crop)
        {
            var outp = new List<Rectangle>();
            if (crop == null) return outp;
            int w = crop.Width, h = crop.Height;
            if (w < 60 || h < 40) return outp;
            System.Drawing.Imaging.BitmapData bd = null;
            try
            {
                bd = crop.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                int stride = bd.Stride;
                byte[] buf = new byte[stride * h];
                System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
                long[] colScore = ColumnScore(buf, stride, w, h);
                // 用滑动窗口给“区间”打分（谁最像条码区）
                int step = Math.Max(120, SegWidth / 6);
                var segs = new List<int[]>();
                for (int sx = 0; sx < w; sx += step)
                {
                    int x1 = Math.Min(w, sx + SegWidth);
                    long s = 0;
                    for (int x = sx; x < x1; x += 2) s += colScore[x];
                    segs.Add(new int[] { sx, (int)Math.Min(int.MaxValue, s / 1000) });
                    if (x1 >= w) break;
                }
                segs.Sort(delegate (int[] a, int[] b) { return b[1].CompareTo(a[1]); });
                int segUsed = 0;
                foreach (var seg in segs)
                {
                    if (segUsed >= 4) break;
                    bool dupSeg = false;
                    foreach (var r0 in outp)
                        if (Math.Abs(r0.X + SegWidth / 2 - (seg[0] + SegWidth / 2)) < SegWidth / 2) { dupSeg = true; break; }
                    if (dupSeg) continue;
                    segUsed++;
                    var rows = RowScore(buf, stride, w, h, seg[0], SegWidth, 4, 60);
                    foreach (int cy in rows)
                    {
                        int x0 = Math.Max(0, seg[0] - 120), x1 = Math.Min(w, seg[0] + SegWidth + 120);
                        int y0 = Math.Max(0, cy - 30), y1 = Math.Min(h - 1, cy + 30);
                        if (x1 - x0 < 40 || y1 - y0 < 12) continue;
                        outp.Add(new Rectangle(x0, y0, x1 - x0, y1 - y0 + 1));
                    }
                }
            }
            catch { }
            finally { if (bd != null) { try { crop.UnlockBits(bd); } catch { } } }
            return outp;
        }

        // 给“一键找焦点”用：数一数这一帧到底能读出几个码（二维码 + 一维码）。
        // 比“清晰度”这种打分靠谱得多——糊的对焦值一个码都读不出来。
        public static int CountCodes(Bitmap bmp, Rectangle roi)
        {
            int n = 0;
            if (bmp == null) return 0;
            try
            {
                if (roi.Width < 16 || roi.Height < 16) roi = new Rectangle(0, 0, bmp.Width, bmp.Height);
                using (var crop = bmp.Clone(roi, PixelFormat.Format24bppRgb))
                {
                    string d1, d2;
                    List<Rectangle> bands;
                    var qr = ScanQr(crop, Rectangle.Empty, true, out d1);       // 顺带把“倒着放”也算进去
                    var bc = ScanBarcodes(crop, null, true, false, out bands, out d2);
                    n = qr.Count + bc.Count;
                }
            }
            catch { }
            return n;
        }

        // 每列的“条码分”：竖直方向一致（同一根条子）+ 水平方向有边（条子边界）
        private static long[] ColumnScore(byte[] buf, int stride, int w, int h)
        {
            long[] score = new long[w];
            for (int y = 4; y < h - 12; y += 4)
            {
                int row = y * stride, row2 = (y + 8) * stride;
                for (int x = 2; x < w; x += 2)
                {
                    int v = Luma(buf, row + x * 3);
                    int v2 = Luma(buf, row2 + x * 3);
                    int dv = v - v2; if (dv < 0) dv = -dv;
                    if (dv >= 22) continue;
                    int v0 = Luma(buf, row + (x - 2) * 3);
                    int dh = v - v0; if (dh < 0) dh = -dh;
                    if (dh > 20) score[x] += dh;
                }
            }
            return score;
        }

        // 某个横向区间内，每行的“条码分”，取最高的若干行（行间至少隔 minSep）
        private static List<int> RowScore(byte[] buf, int stride, int w, int h, int x0, int sw, int count, int minSep)
        {
            var outp = new List<int>();
            if (sw < 60) sw = w;
            int xa = Math.Max(2, x0), xb = Math.Min(w, x0 + sw);
            if (xb - xa < 60) return outp;
            long[] e = new long[h];
            for (int y = 2; y < h - 12; y += 2)
            {
                int row = y * stride, row2 = (y + 8) * stride;
                long s = 0;
                for (int x = xa; x < xb; x += 2)
                {
                    int v = Luma(buf, row + x * 3);
                    int v2 = Luma(buf, row2 + x * 3);
                    int dv = v - v2; if (dv < 0) dv = -dv;
                    if (dv >= 22) continue;
                    int v0 = Luma(buf, row + (x - 2) * 3);
                    int dh = v - v0; if (dh < 0) dh = -dh;
                    if (dh > 20) s += dh;
                }
                e[y] = s;
            }
            var idx = new List<int>();
            for (int i = 2; i < h - 12; i += 2) idx.Add(i);
            idx.Sort(delegate (int a, int b) { return e[b].CompareTo(e[a]); });
            foreach (int y in idx)
            {
                if (e[y] <= 0) break;
                bool tooClose = false;
                foreach (int q in outp) if (Math.Abs(q - y) < minSep) { tooClose = true; break; }
                if (!tooClose) outp.Add(y);
                if (outp.Count >= count) break;
            }
            return outp;
        }

        private static List<CodeHit> AddHitsFast(Bitmap img, int ox, int oy, ZXing.BarcodeReader r)
        {
            var hits = new List<CodeHit>();
            try
            {
                var res = r.DecodeMultiple(img);
                if (res == null) return hits;
                foreach (var one in res)
                {
                    if (one == null) continue;
                    string txt = (one.Text ?? "").Trim();
                    if (txt.Length == 0) continue;
                    bool dup = false;
                    foreach (var h in hits) if (h.Text == txt) { dup = true; break; }
                    if (dup) continue;
                    var box = new Rectangle(ox, oy, img.Width, img.Height);
                    try
                    {
                        var pts = one.ResultPoints;
                        if (pts != null && pts.Length > 0)
                        {
                            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                            foreach (var p in pts)
                            {
                                if (p.X < minX) minX = p.X;
                                if (p.X > maxX) maxX = p.X;
                                if (p.Y < minY) minY = p.Y;
                                if (p.Y > maxY) maxY = p.Y;
                            }
                            int x = ox + (int)Math.Max(0, minX), y = oy + (int)Math.Max(0, minY);
                            int w2 = Math.Max(20, (int)(maxX - minX));
                            int h2 = Math.Max(8, (int)(maxY - minY));
                            if (x + w2 > ox + img.Width) w2 = ox + img.Width - x;
                            if (y + h2 > oy + img.Height) h2 = oy + img.Height - y;
                            if (w2 > 8 && h2 > 4) box = new Rectangle(x, y, w2, h2);
                        }
                    }
                    catch { }
                    hits.Add(new CodeHit { Text = txt, Box = box });
                }
            }
            catch { }
            return hits;
        }

        private static List<CodeHit> ScanQr(Bitmap crop, Rectangle hint, bool allowRotate, out string diag)
        {
            var hits = new List<CodeHit>();
            long ms1 = 0, ms2 = 0, ms3 = 0;
            try
            {
                // 提示框稍微放大一点（设备摆放会有点位移），仍然比整块小很多
                var h2 = hint;
                if (h2.Width >= 40 && h2.Height >= 40)
                {
                    int padX = Math.Max(24, h2.Width / 4), padY = Math.Max(24, h2.Height / 4);
                    h2 = Rectangle.FromLTRB(Math.Max(0, h2.Left - padX), Math.Max(0, h2.Top - padY),
                                            Math.Min(crop.Width, h2.Right + padX), Math.Min(crop.Height, h2.Bottom + padY));
                }
                if (h2.Width >= 40 && h2.Height >= 40 && h2.Right <= crop.Width && h2.Bottom <= crop.Height)
                {
                    var sw = Stopwatch.StartNew();
                    using (var piece = crop.Clone(h2, PixelFormat.Format24bppRgb))
                    {
                        var got = AddHitsFast(piece, h2.X, h2.Y, QrOnce(allowRotate));
                        foreach (var g in got) if (!hits.Contains(g)) hits.Add(g);
                    }
                    ms1 = sw.ElapsedMilliseconds;
                }
                // 提示框里找到的不一定是“设备二维码”（标签上可能还有别的小二维码），
                // 只有拿到能解析出型号/类型/SN 的才算数，否则仍然整块再找一遍
                if (!HasDeviceQr(hits))
                {
                    var sw = Stopwatch.StartNew();
                    var got = AddHitsFast(crop, 0, 0, QrOnce(false));
                    foreach (var g in got) if (!hits.Contains(g)) hits.Add(g);
                    ms2 = sw.ElapsedMilliseconds;
                }
                // 兜底：设备被倒着放 / 侧着放时（转了 180°、90°），正着扫是解不出来的。
                // 自己把图翻过来再解一次，比 ZXing 自带的旋转重扫快。
                if (!HasDeviceQr(hits) && allowRotate)
                {
                    var sw = Stopwatch.StartNew();
                    ScanQrRotated(crop, hits, RotateFlipType.Rotate180FlipNone);
                    if (!HasDeviceQr(hits)) ScanQrRotated(crop, hits, RotateFlipType.Rotate90FlipNone);
                    if (!HasDeviceQr(hits)) ScanQrRotated(crop, hits, RotateFlipType.Rotate270FlipNone);
                    ms3 = sw.ElapsedMilliseconds;
                }
            }
            catch { }
            diag = "提示框 " + ms1 + "ms 整块 " + ms2 + "ms" + (ms3 > 0 ? (" 翻转兜底 " + ms3 + "ms") : "");
            return hits;
        }

        // 把画面转过去解二维码，并把解到的位置换算回原图坐标
        private static void ScanQrRotated(Bitmap crop, List<CodeHit> hits, RotateFlipType flip)
        {
            try
            {
                using (var rot = (Bitmap)crop.Clone())
                {
                    rot.RotateFlip(flip);
                    var got = AddHitsFast(rot, 0, 0, QrOnce(false));
                    foreach (var g in got)
                    {
                        int bx, by, bw = g.Box.Width, bh = g.Box.Height;
                        if (flip == RotateFlipType.Rotate180FlipNone)
                        {
                            bx = crop.Width - (g.Box.X + bw);
                            by = crop.Height - (g.Box.Y + bh);
                        }
                        else if (flip == RotateFlipType.Rotate90FlipNone)
                        {
                            // 顺时针 90°：x' = H-1-y, y' = x
                            bx = g.Box.Y;
                            by = crop.Width - (g.Box.X + bw);
                            bw = g.Box.Height; bh = g.Box.Width;
                        }
                        else
                        {
                            // 逆时针 90°（Rotate270FlipNone）：x' = y, y' = W-1-x
                            bx = crop.Height - (g.Box.Y + g.Box.Height);
                            by = g.Box.X;
                            bw = g.Box.Height; bh = g.Box.Width;
                        }
                        if (bx < 0) bx = 0;
                        if (by < 0) by = 0;
                        if (bx + bw > crop.Width) bw = crop.Width - bx;
                        if (by + bh > crop.Height) bh = crop.Height - by;
                        bool dup = false;
                        foreach (var e2 in hits) if (e2.Text == g.Text) { dup = true; break; }
                        if (!dup && bw > 8 && bh > 4)
                            hits.Add(new CodeHit { Text = g.Text, Box = new Rectangle(bx, by, bw, bh) });
                    }
                }
            }
            catch { }
        }

        // 是不是“设备二维码”：能解析出型号 / 类型 / SN（标签上别的小二维码不算）
        private static bool HasDeviceQr(List<CodeHit> hits)
        {
            if (hits == null) return false;
            foreach (var h in hits)
            {
                try
                {
                    var p = QRParser.Parse(h.Text);
                    if (p != null && (!string.IsNullOrEmpty(p.SN) || !string.IsNullOrEmpty(p.Model) || !string.IsNullOrEmpty(p.Type)))
                        return true;
                }
                catch { }
            }
            return false;
        }

        /// <summary>一次读取整台设备：二维码（型号/类型/SN）+ 条码（SN / MAC），两条线并行</summary>
        internal class FrameRead
        {
            public string QrText;
            public List<string> Codes = new List<string>();      // 一维码读到的内容
            public Rectangle QrBox = Rectangle.Empty;            // 二维码位置（ROI 内坐标，给下一帧当提示）
            public List<Rectangle> Bands = new List<Rectangle>(); // 条码窗口（ROI 内坐标）
            public string Diag = "";
            public long Ms;
            public bool GoodQr;                                  // 这个二维码能给出型号/类型/SN
            public bool HasQr { get { return !string.IsNullOrEmpty(QrText); } }
        }

        public static FrameRead ReadFrame(Bitmap bmp, Rectangle roi, Rectangle qrHint, List<Rectangle> bandHints,
                                          bool needSn, bool allowRotate, bool deepFallback, bool cheapOnly)
        {
            var fr = new FrameRead();
            var swAll = Stopwatch.StartNew();
            if (bmp == null) { fr.Diag = "无画面"; return fr; }
            if (roi.Width < 16 || roi.Height < 16) roi = new Rectangle(0, 0, bmp.Width, bmp.Height);
            if (roi.X < 0) roi.X = 0;
            if (roi.Y < 0) roi.Y = 0;
            if (roi.Right > bmp.Width) roi.Width = bmp.Width - roi.X;
            if (roi.Bottom > bmp.Height) roi.Height = bmp.Height - roi.Y;
            Bitmap bcWork = null, qrWork = null;
            try
            {
                string cloneErr = "";
                try { bcWork = bmp.Clone(roi, PixelFormat.Format24bppRgb); } catch (Exception ex) { cloneErr = "c1:" + ex.Message; }
                try { qrWork = bmp.Clone(roi, PixelFormat.Format24bppRgb); } catch (Exception ex) { cloneErr += " c2:" + ex.Message; }
                List<CodeHit> bcHits = null, qrHits = null;
                List<Rectangle> bands = null;
                string bcDiag = "", qrDiag = "";
                // 两条线并行：二维码（重）与条码定位+解码（轻）互不等待
                var t1 = new System.Threading.Thread(delegate ()
                {
                    try { bcHits = bcWork == null ? new List<CodeHit>() : ScanBarcodes(bcWork, bandHints, needSn, deepFallback, out bands, out bcDiag); }
                    catch (Exception ex) { bcHits = new List<CodeHit>(); bcDiag = "ERR " + ex.Message; }
                });
                var t2 = new System.Threading.Thread(delegate ()
                {
                    // cheapOnly：这一帧和上一帧一样、而且上一帧什么都没读到 → 只做便宜的一维码重查，不再重复扫二维码
                    // （但走“整幅兜底”时仍然要扫二维码，兜底才兜得住）
                    try { qrHits = (qrWork == null || (cheapOnly && !deepFallback)) ? new List<CodeHit>() : ScanQr(qrWork, qrHint, allowRotate, out qrDiag); }
                    catch (Exception ex) { qrHits = new List<CodeHit>(); qrDiag = "ERR " + ex.Message; }
                });
                try { t1.IsBackground = true; t2.IsBackground = true; t1.Start(); t2.Start(); t1.Join(); t2.Join(); }
                catch { }
                if (qrHits != null)
                {
                    // 多个二维码：大的优先（标签上大的通常是设备二维码，小的是登录/参考码）
                    try
                    {
                        qrHits.Sort(delegate (CodeHit a, CodeHit b)
                        {
                            long aa = (long)Math.Max(0, a.Box.Width) * Math.Max(0, a.Box.Height);
                            long bb = (long)Math.Max(0, b.Box.Width) * Math.Max(0, b.Box.Height);
                            return bb.CompareTo(aa);
                        });
                    }
                    catch { }
                    foreach (var h in qrHits)
                    {
                        bool good = false;
                        try
                        {
                            var pp = QRParser.Parse(h.Text);
                            good = pp != null && (!string.IsNullOrEmpty(pp.SN) || !string.IsNullOrEmpty(pp.Model) || !string.IsNullOrEmpty(pp.Type));
                        }
                        catch { }
                        // 优先用“设备二维码”（能给出型号/类型/SN 的那个）
                        if (fr.QrText == null || (good && !fr.GoodQr))
                        {
                            fr.QrText = h.Text;
                            fr.QrBox = h.Box;
                            fr.GoodQr = good;
                        }
                    }
                }
                if (bcHits != null)
                    foreach (var h in bcHits) if (!fr.Codes.Contains(h.Text)) fr.Codes.Add(h.Text);
                if (bands != null) fr.Bands = bands;
                fr.Diag = "区域 " + roi.X + "," + roi.Y + "," + roi.Width + "," + roi.Height +
                          " 克隆" + (bcWork != null ? "1" : "0") + (qrWork != null ? "1" : "0") + cloneErr +
                          " 二维码[" + qrDiag + "] 条码[" + bcDiag + "]";
            }
            catch (Exception ex) { fr.Diag = "ERR " + ex.Message; }
            finally
            {
                if (bcWork != null) { try { bcWork.Dispose(); } catch { } }
                if (qrWork != null) { try { qrWork.Dispose(); } catch { } }
            }
            swAll.Stop();
            fr.Ms = swAll.ElapsedMilliseconds;
            return fr;
        }
    }

    // 摄像头 / 高拍仪识别窗口：看到二维码 + MAC 条码就自动录入并打印（复用主界面扫码流程）
    internal class CameraForm : Form
    {
        private MainForm _owner;                 // 注意：不是 readonly —— 紧凑模式复用同一个窗体实例，初始化放在 Init() 里
        private string _cfgPath;
        private readonly Dictionary<string, string> _cfg = new Dictionary<string, string>();
        private bool _loading;

        private ComboBox _cmbCam, _cmbRoi;
        private NumericUpDown _numInterval, _numStable;
        private Panel _preview;
        private PictureBox _pic;
        private Bitmap _disp;                 // 自己渲染的预览画面（画面=识别内容，可左右翻转）
        private CheckBox _chkMirror;
        private bool _decoding;
        private DateTime _lastDecodeAt = DateTime.MinValue;
        private bool _autoDumped;
        private Size _lastFrameSize = Size.Empty;
        private DateTime _noCodeAt = DateTime.Now;
        private Label _lblStatus, _lblResult, _lblInfo;
        private TextBox _log;
        private CheckBox _chkBeep;
        private CheckBox _chkGuess;
        private CheckBox _chkWinOcr;
        private RoundedButton _btnOpen;

        private Timer _timer;
        private IntPtr _cam = IntPtr.Zero;
        private bool _on;
        private DShowCamera _ds;                  // DirectShow 取流（主用；免驱 VFW 在很多高拍仪上只能取到黑帧）
        private ComboBox _cmbRes;
        private CheckBox _chkAutoFocus;
        private NumericUpDown _numFocus;
        private RoundedButton _btnFindFocus;
        private bool _focusSupported;
        private int _focusMin, _focusMax = 1023, _focusStep = 1, _focusDef;
        // 识别区域（按画面百分比），支持在预览上拖拽框选
        private double _roiX = 20, _roiY = 20, _roiW = 60, _roiH = 60;   // 默认中间 60%（可在预览上拖拽框选更小的标签区域）
        private bool _useCustomRoi;
        private bool _dragRoi;
        private Point _dragFrom, _dragTo;
        private double _sharp;
        private bool _ocrTried;
        private Rectangle _autoRoi = Rectangle.Empty;
        private DateTime _autoRoiAt = DateTime.MinValue;
        private string _pendingSig = "";
        private int _sameCount;
        private int _clearCount;
        private bool _locked;
        // 速度优先：画面指纹 + 上一台的位置提示 + 去重（和上一台一样就不重复提取填写）
        private int _fpState;                       // 0=这帧还没识别 1=已识别出结果 2=识别过但画面里没有码
        private DateTime _nextRetryAt = DateTime.MinValue;
        private DateTime _lastReadAt = DateTime.MinValue;
        private RectangleF _qrHintPct = RectangleF.Empty;
        private readonly List<RectangleF> _bandHintPct = new List<RectangleF>();
        private Size _readRoiSize = Size.Empty;
        private readonly HashSet<string> _devKeys = new HashSet<string>();   // 镜头下这台设备的“身份钥匙”（可能在多帧里陆续读到）
        private string _printedKey = "";                                    // 这台已经打印过的那把钥匙
        private bool _camSeen;
        private bool _qrOkForDevice;      // 镜头下这台已经读到过二维码
        private bool _rotateTried;        // 这台已经做过“翻转兜底”，一台只做一次
        private int _emptyTries;
        private int _partialTries;
        private int _sigRun;
        private long _lastReadMs;
        private DateTime _lastDiagAt = DateTime.MinValue;
        private byte[] _fpPrev = new byte[CameraDetect.FpCols * CameraDetect.FpRows];
        private byte[] _fpNow = new byte[CameraDetect.FpCols * CameraDetect.FpRows];
        private DateTime _lastRotateAt = DateTime.MinValue;

        private int _frames;
        private DateTime _fpsTime = DateTime.Now;
        private double _fps;
        private int _lastDecodeMs;
        private DateTime _slowLoggedAt = DateTime.MinValue;

        // 紧凑模式：主界面上「开始/暂停/停止」用它——只有一个小预览窗 + 一行状态，跟着主窗口走
        internal bool CompactMode;
        internal bool Paused;                                    // 暂停：还出画面，但不识别、不录入
        internal bool ScannerOn { get { return _on; } }

        public CameraForm(MainForm owner)
        {
            Init(owner, false);
        }

        public CameraForm(MainForm owner, bool compact)
        {
            Init(owner, compact);
        }

        private void Init(MainForm owner, bool compact)
        {
            _owner = owner;
            CompactMode = compact;
            _cfgPath = Path.Combine(Application.StartupPath, "camera.ini");
            if (compact)
            {
                Text = "摄像头预览";
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = false;
                ClientSize = new Size(360, 250);
                MinimumSize = new Size(240, 180);
                StartPosition = FormStartPosition.Manual;
            }
            else
            {
                Text = "摄像头 / 高拍仪识别（自动识别二维码 + MAC 条码）";
                ClientSize = new Size(960, 760);
                MinimumSize = new Size(780, 620);
                StartPosition = FormStartPosition.CenterScreen;
            }
            KeyPreview = true;
            BuildUi();
            LoadCfg();
            // 定时器必须先建好：下面 RefreshDevices 可能会自动打开摄像头，而打开流程会用到它
            _timer = new Timer();
            _timer.Interval = 150;      // 预览刷新节奏（识别间隔另由“识别间隔(ms)”控制）
            _timer.Tick += (s, e) => TickOnce();
            FormClosing += (s, e) => CloseCamera();
            if (compact && _owner != null)
            {
                // 小预览窗跟着主窗口走
                _owner.Move += OnOwnerMoved;
                _owner.Resize += OnOwnerMoved;
                _followTimer = new Timer();
                _followTimer.Interval = 400;
                _followTimer.Tick += (s, e) => FollowOwner();
                _followTimer.Start();
            }
            RefreshDevices(true);
            if (compact) FollowOwner();
        }

        private Timer _followTimer;

        private void OnOwnerMoved(object sender, EventArgs e)
        {
            FollowOwner();
        }

        internal void FollowOwnerNow() { FollowOwner(); }

        // 贴在主窗口右下角（并保证不跑出屏幕）
        private Rectangle _lastOwnerBounds = Rectangle.Empty;
        private void FollowOwner()
        {
            try
            {
                if (!CompactMode || _owner == null || _owner.IsDisposed || !_owner.Visible) return;
                var r = _owner.Bounds;                       // 屏幕坐标
                // 只有主窗口真的动了才重新贴过去；这样工人可以自己把小预览窗拖到顺手的位置
                if (r == _lastOwnerBounds) return;
                _lastOwnerBounds = r;
                int x = r.Right - Width - 14;
                int y = r.Bottom - Height - 14;
                var scr = Screen.FromControl(_owner).WorkingArea;
                x = Math.Max(scr.Left + 4, Math.Min(x, scr.Right - Width - 4));
                y = Math.Max(scr.Top + 4, Math.Min(y, scr.Bottom - Height - 4));
                if (Location.X != x || Location.Y != y) Location = new Point(x, y);
            }
            catch { }
        }

        // ---------- 界面 ----------
        private void BuildUi()
        {
            // 紧凑模式（主界面上的开始/暂停/停止用）：只留一个小预览 + 一行状态，别的都不要
            if (CompactMode)
            {
                var root2 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(3) };
                root2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                root2.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                root2.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
                root2.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
                _preview = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 28, 32) };
                _preview.Resize += (s, e) => LayoutCamera();
                _pic = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 28, 32), SizeMode = PictureBoxSizeMode.Normal };
                _pic.MouseDown += Pic_MouseDown2;
                _pic.MouseMove += Pic_MouseMove2;
                _pic.MouseUp += Pic_MouseUp2;
                _preview.Controls.Add(_pic);
                root2.Controls.Add(_preview, 0, 0);
                _lblStatus = new Label
                {
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold),
                    ForeColor = Color.DodgerBlue
                };
                root2.Controls.Add(_lblStatus, 0, 1);
                _lblResult = new Label
                {
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = Color.DimGray
                };
                root2.Controls.Add(_lblResult, 0, 2);
                // 下面这些控件在紧凑模式下不显示，但代码里会用到（日志、信息行），建好留着
                _lblInfo = new Label { Visible = false, AutoSize = false, Width = 10, Height = 10 };
                _log = new TextBox { Multiline = true, Visible = false, Width = 10, Height = 10 };
                // 摄像头/识别参数这些控件在紧凑模式不显示，但 LoadCfg/SaveCfg/识别循环会用到，
                // 所以照样建出来（不可见），否则会报"未将对象引用设置到对象的实例"
                _cmbCam = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Visible = false, Width = 10 };
                _cmbRoi = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Visible = false, Width = 10 };
                _cmbRoi.Items.AddRange(new object[] { "自定义（拖拽框选）", "整幅画面", "中间 90%", "中间 80%", "中间 60%", "中间 40%" });
                _cmbRes = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Visible = false, Width = 10 };
                _cmbRes.Items.AddRange(new object[] { "自动（推荐）", "流畅 1920", "标准 2592", "高清 4208", "低 1280" });
                _numInterval = new NumericUpDown { Minimum = 120, Maximum = 2000, Increment = 50, Value = 150, Visible = false, Width = 10 };
                _numInterval.ValueChanged += (s, e) => { if (_timer != null) _timer.Interval = (int)_numInterval.Value; };
                _numStable = new NumericUpDown { Minimum = 1, Maximum = 5, Value = 1, Visible = false, Width = 10 };
                _numFocus = new NumericUpDown { Minimum = 0, Maximum = 1023, Value = 0, Visible = false, Width = 10 };
                _chkBeep = new CheckBox { Visible = false, Width = 10 };
                _chkMirror = new CheckBox { Visible = false, Width = 10 };
                _chkGuess = new CheckBox { Visible = false, Width = 10 };
                _chkWinOcr = new CheckBox { Visible = false, Width = 10 };
                _chkAutoFocus = new CheckBox { Visible = false, Width = 10 };
                _btnFindFocus = new RoundedButton { Visible = false, Width = 10 };
                _btnOpen = new RoundedButton { Visible = false, Width = 10 };
                Controls.Add(root2);
                return;
            }

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // 必须指定：否则整列会被子控件撑宽，预览区被挤坏
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));

            var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            bar.Controls.Add(new Label { Text = "摄像头：", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
            _cmbCam = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, Margin = new Padding(0, 3, 6, 4) };
            bar.Controls.Add(_cmbCam);
            var btnRefresh = new RoundedButton { Text = "刷新", Width = 66, Height = 26, Margin = new Padding(0, 3, 6, 4) };
            btnRefresh.Click += (s, e) => RefreshDevices(false);
            bar.Controls.Add(btnRefresh);
            _btnOpen = new RoundedButton { Text = "打开摄像头", Width = 108, Height = 26, Margin = new Padding(0, 3, 6, 4) };
            _btnOpen.Click += (s, e) => ToggleCamera();
            bar.Controls.Add(_btnOpen);
            bar.Controls.Add(new Label { Text = "清晰度：", AutoSize = true, Margin = new Padding(8, 7, 4, 0) });
            _cmbRes = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new Padding(0, 3, 6, 4) };
            _cmbRes.Items.AddRange(new object[] { "自动（推荐）", "流畅 1920", "标准 2592", "高清 4208", "低 1280" });
            _cmbRes.SelectedIndexChanged += (s, e) => SaveCfg();
            bar.Controls.Add(_cmbRes);
            root.Controls.Add(bar, 0, 0);

            _preview = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 28, 32) };
            _preview.Resize += (s, e) => LayoutCamera();
            _pic = new PictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 28, 32), SizeMode = PictureBoxSizeMode.Normal };
            _pic.MouseDown += Pic_MouseDown2;
            _pic.MouseMove += Pic_MouseMove2;
            _pic.MouseUp += Pic_MouseUp2;
            _preview.Controls.Add(_pic);
            root.Controls.Add(_preview, 0, 1);

            var opt = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            opt.Controls.Add(new Label { Text = "识别区域：", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
            _cmbRoi = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new Padding(0, 3, 14, 4) };
            _cmbRoi.Items.AddRange(new object[] { "自定义（拖拽框选）", "整幅画面", "中间 90%", "中间 80%", "中间 60%", "中间 40%" });
            _cmbRoi.SelectedIndexChanged += (s, e) => { if (_loading) return; ApplyRoiPreset(_cmbRoi.SelectedIndex); SaveCfg(); };
            opt.Controls.Add(_cmbRoi);
            opt.Controls.Add(new Label { Text = "识别间隔(ms)：", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
            _numInterval = new NumericUpDown { Minimum = 120, Maximum = 2000, Increment = 50, Value = 300, Width = 70, Margin = new Padding(0, 3, 14, 4) };
            _numInterval.ValueChanged += (s, e) => { if (_timer != null) _timer.Interval = (int)_numInterval.Value; SaveCfg(); };
            opt.Controls.Add(_numInterval);
            opt.Controls.Add(new Label { Text = "防抖帧数：", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
            _numStable = new NumericUpDown { Minimum = 1, Maximum = 5, Value = 2, Width = 50, Margin = new Padding(0, 3, 14, 4) };
            _numStable.ValueChanged += (s, e) => SaveCfg();
            opt.Controls.Add(_numStable);
            _chkBeep = new CheckBox { Text = "提示音", AutoSize = true, Checked = true, Margin = new Padding(0, 6, 14, 0) };
            _chkBeep.CheckedChanged += (s, e) => SaveCfg();
            opt.Controls.Add(_chkBeep);
            _chkWinOcr = new CheckBox { Text = "使用Windows OCR", AutoSize = true, Checked = true, Margin = new Padding(0, 6, 14, 0) };
            _chkWinOcr.CheckedChanged += (s, e) => SaveCfg();
            opt.Controls.Add(_chkWinOcr);
            _chkGuess = new CheckBox { Text = "无二维码时用SN反查型号", AutoSize = true, Checked = true, Margin = new Padding(0, 6, 14, 0) };
            _chkGuess.CheckedChanged += (s, e) => SaveCfg();
            opt.Controls.Add(_chkGuess);
            _chkMirror = new CheckBox { Text = "画面左右翻转", AutoSize = true, Margin = new Padding(0, 6, 14, 0) };
            _chkMirror.CheckedChanged += (s, e) => SaveCfg();
            opt.Controls.Add(_chkMirror);
            opt.Controls.Add(new Label { Text = "对焦：", AutoSize = true, Margin = new Padding(12, 7, 4, 0) });
            _chkAutoFocus = new CheckBox { Text = "自动", AutoSize = true, Checked = true, Margin = new Padding(0, 6, 6, 0) };
            _chkAutoFocus.CheckedChanged += (s, e) =>
            {
                if (_loading || _ds == null) return;
                if (_chkAutoFocus.Checked) { _ds.SetFocus((int)_numFocus.Value, true); Log("对焦：自动"); }
                else { _ds.SetFocus((int)_numFocus.Value, false); Log("对焦：手动定焦 " + (int)_numFocus.Value); }
                SaveCfg();
            };
            opt.Controls.Add(_chkAutoFocus);
            _numFocus = new NumericUpDown { Minimum = 0, Maximum = 1023, Value = 0, Width = 70, Margin = new Padding(0, 3, 6, 4) };
            _numFocus.ValueChanged += (s, e) =>
            {
                if (_loading || _ds == null || _chkAutoFocus.Checked) return;
                _ds.SetFocus((int)_numFocus.Value, false);
                SaveCfg();
            };
            opt.Controls.Add(_numFocus);
            _btnFindFocus = new RoundedButton { Text = "一键找焦点（只做一次）", Width = 150, Height = 26, Margin = new Padding(0, 3, 6, 4) };
            _btnFindFocus.Click += (s, e) => FindBestFocus();
            opt.Controls.Add(_btnFindFocus);
            var btnOcr = new RoundedButton { Text = "识别型号文字(OCR)", Width = 150, Height = 26, Margin = new Padding(0, 3, 6, 4) };
            btnOcr.Click += (s, e) => DoOcrOnce(true);
            opt.Controls.Add(btnOcr);
            var btnAutoRoi = new RoundedButton { Text = "自动框选识别区域", Width = 150, Height = 26, Margin = new Padding(0, 3, 6, 4) };
            btnAutoRoi.Click += (s, e) => AutoFrameRoi();
            opt.Controls.Add(btnAutoRoi);
            var btnRearm = new RoundedButton { Text = "重新识别", Width = 86, Height = 26, Margin = new Padding(0, 3, 6, 4) };
            btnRearm.Click += (s, e) =>
            {
                _locked = false; _clearCount = 0; _pendingSig = ""; _sameCount = 0;
                _devKeys.Clear(); _printedKey = ""; _camSeen = false;
                _sigRun = 0; _fpState = 0; _partialTries = 0; _emptyTries = 0;
                _nextRetryAt = DateTime.MinValue;
                Log("已解除锁定并清掉“上一台”的记忆，可重新识别（同一台也能再打一次）");
            };
            opt.Controls.Add(btnRearm);
            var btnSnap = new RoundedButton { Text = "保存当前画面…", Width = 126, Height = 26, Margin = new Padding(0, 3, 6, 4) };
            btnSnap.Click += (s, e) => SaveSnapshot();
            opt.Controls.Add(btnSnap);
            root.Controls.Add(opt, 0, 2);

            var info = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            // 注意：这里必须限宽（AutoSize=false + 省略号），否则长文本会把整个窗口布局撑宽，预览区被挤坏
            _lblStatus = new Label { Text = "就绪", AutoSize = false, Width = 300, Height = 20, AutoEllipsis = true, Font = new Font("Microsoft YaHei", 9.5F, FontStyle.Bold), ForeColor = Color.DodgerBlue, Margin = new Padding(0, 4, 12, 2) };
            info.Controls.Add(_lblStatus);
            _lblResult = new Label { Text = "识别结果：—", AutoSize = false, Width = 340, Height = 20, AutoEllipsis = true, ForeColor = Color.DimGray, Margin = new Padding(0, 4, 12, 2) };
            info.Controls.Add(_lblResult);
            _lblInfo = new Label { Text = "", AutoSize = false, Width = 262, Height = 20, AutoEllipsis = true, ForeColor = Color.Gray, Margin = new Padding(0, 4, 0, 2) };
            info.Controls.Add(_lblInfo);
            root.Controls.Add(info, 0, 3);

            _log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9F), BackColor = Color.White, Margin = new Padding(0, 4, 0, 0) };
            root.Controls.Add(_log, 0, 4);

            Controls.Add(root);
        }

        // ---------- 配置 ----------
        private string GetCfg(string k) { string v; return _cfg.TryGetValue(k, out v) ? v : ""; }

        private void LoadCfg()
        {
            _loading = true;
            try
            {
                if (File.Exists(_cfgPath))
                    foreach (var line in File.ReadAllLines(_cfgPath, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        _cfg[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
            }
            catch { }
            int interval = 150, stable = 1, roi = 90;   // 速度优先：默认 150ms 一轮、确认 1 次就干活
            int.TryParse(GetCfg("interval"), out interval);
            int.TryParse(GetCfg("stable"), out stable);
            int.TryParse(GetCfg("roi"), out roi);
            if (interval < 120 || interval > 2000) interval = 150;
            if (stable < 1 || stable > 5) stable = 2;
            if (roi <= 0 || roi > 100) roi = 90;
            _numInterval.Value = interval;
            _numStable.Value = stable;
            _chkBeep.Checked = GetCfg("beep") != "0";
            _chkMirror.Checked = GetCfg("mirror") == "1";
            if (_chkGuess != null) _chkGuess.Checked = GetCfg("guess") != "0";
            if (_chkWinOcr != null) _chkWinOcr.Checked = GetCfg("winocr") != "0";
            int roiIdx = 0;
            bool hasRoiCfg = int.TryParse(GetCfg("roi"), out roiIdx);
            double rx, ry, rw, rh;
            double.TryParse(GetCfg("roiX"), out rx); double.TryParse(GetCfg("roiY"), out ry);
            double.TryParse(GetCfg("roiW"), out rw); double.TryParse(GetCfg("roiH"), out rh);
            if (rw > 1 && rw <= 100 && rh > 1 && rh <= 100) { _roiX = rx; _roiY = ry; _roiW = rw; _roiH = rh; }
            if (!hasRoiCfg) roiIdx = 1;                    // 从没配置过 → 默认"整幅画面"（比中间 60% 保险，不会切掉条码）
            else if (roiIdx < 0 || roiIdx > 5) roiIdx = RoiIndexFromPercent(roi);
            _cmbRoi.SelectedIndex = roiIdx;
            ApplyRoiPreset(roiIdx);
            if (roiIdx == 0 && rw > 1) { _roiX = rx; _roiY = ry; _roiW = rw; _roiH = rh; }   // 自定义：恢复上次框
            int resIdx = 0;
            int.TryParse(GetCfg("res"), out resIdx);
            if (_cmbRes != null && resIdx >= 0 && resIdx < _cmbRes.Items.Count) _cmbRes.SelectedIndex = resIdx;
            _loading = false;
        }

        private void SetCfg(string k, string v) { _cfg[k] = v; }

        private void SaveCfg()
        {
            if (_loading) return;
            try
            {
                string dev = _cmbCam != null && _cmbCam.SelectedItem != null ? _cmbCam.SelectedItem.ToString() : GetCfg("device");
                SetCfg("device", dev);
                SetCfg("roi", (_cmbRoi == null ? 0 : _cmbRoi.SelectedIndex).ToString());
                SetCfg("roiX", _roiX.ToString("0.##"));
                SetCfg("roiY", _roiY.ToString("0.##"));
                SetCfg("roiW", _roiW.ToString("0.##"));
                SetCfg("roiH", _roiH.ToString("0.##"));
                SetCfg("res", (_cmbRes == null ? 0 : _cmbRes.SelectedIndex).ToString());
                SetCfg("interval", ((int)_numInterval.Value).ToString());
                SetCfg("stable", ((int)_numStable.Value).ToString());
                SetCfg("beep", _chkBeep.Checked ? "1" : "0");
                SetCfg("mirror", _chkMirror.Checked ? "1" : "0");
                SetCfg("guess", (_chkGuess != null && _chkGuess.Checked) ? "1" : "0");
                SetCfg("winocr", (_chkWinOcr != null && _chkWinOcr.Checked) ? "1" : "0");
                SetCfg("autofocus", (_chkAutoFocus != null && _chkAutoFocus.Checked) ? "1" : "0");
                var sb = new StringBuilder();
                foreach (var kv in _cfg) if (!string.IsNullOrEmpty(kv.Key)) sb.AppendLine(kv.Key + "=" + kv.Value);
                File.WriteAllText(_cfgPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        private static int RoiIndexFromPercent(int p)
        {
            if (p >= 100) return 0;
            if (p >= 90) return 1;
            if (p >= 80) return 2;
            if (p >= 60) return 3;
            return 4;
        }

        private int RoiPercent()
        {
            switch (_cmbRoi == null ? 1 : _cmbRoi.SelectedIndex)
            {
                case 0: return 100;
                case 1: return 90;
                case 2: return 80;
                case 3: return 60;
                case 4: return 40;
            }
            return 90;
        }

        // 识别区域：按画面百分比计算实际矩形（可在预览上拖拽框选）
        private void ApplyRoiPreset(int idx)
        {
            switch (idx)
            {
                case 0: _useCustomRoi = true; break;                                   // 自定义：保持当前框
                case 1: _useCustomRoi = false; _roiX = 0; _roiY = 0; _roiW = 100; _roiH = 100; break;
                case 2: _useCustomRoi = false; _roiX = 5; _roiY = 5; _roiW = 90; _roiH = 90; break;
                case 3: _useCustomRoi = false; _roiX = 10; _roiY = 10; _roiW = 80; _roiH = 80; break;
                case 4: _useCustomRoi = false; _roiX = 20; _roiY = 20; _roiW = 60; _roiH = 60; break;
                case 5: _useCustomRoi = false; _roiX = 30; _roiY = 30; _roiW = 40; _roiH = 40; break;
            }
        }

        // 自动识别区域：用 1/4 缩略图快速找出所有码，取“最左上→最右下”的包围盒（含下方印字），自动适配大/小标签
        private Rectangle EffectiveRoi(Size frame)
        {
            // 自动区域（每台设备只算一次）；没有就退回设定区域
            if (_autoRoi.Width > 8 && _autoRoi.Height > 8) return _autoRoi;
            return CurrentRoi(frame);
        }

        private void UpdateAutoRoi(Bitmap bmp)
        {
            try
            {
                if (bmp == null) return;
                if (_autoRoi.Width > 8) return;
                int sw = Math.Max(200, bmp.Width / 6), sh = Math.Max(150, bmp.Height / 6);
                using (var small = new Bitmap(bmp, new Size(sw, sh)))
                {
                    var hits = CameraDetect.DecodeHits(small, new Rectangle(0, 0, sw, sh));
                    if (hits.Count == 0) { _autoRoi = Rectangle.Empty; _autoRoiAt = DateTime.Now; return; }
                    int x1 = int.MaxValue, y1 = int.MaxValue, x2 = 0, y2 = 0;
                    foreach (var h in hits)
                    {
                        if (h.Box.Left < x1) x1 = h.Box.Left;
                        if (h.Box.Top < y1) y1 = h.Box.Top;
                        if (h.Box.Right > x2) x2 = h.Box.Right;
                        if (h.Box.Bottom > y2) y2 = h.Box.Bottom;
                    }
                    double k = (double)bmp.Width / sw;
                    int padX = (int)Math.Max(20, (x2 - x1) * k * 0.06);
                    int padTop = (int)Math.Max(20, (y2 - y1) * k * 0.10);
                    int padBottom = (int)Math.Max(60, (y2 - y1) * k * 0.40);
                    int rx = (int)(x1 * k) - padX, ry = (int)(y1 * k) - padTop;
                    int rw = (int)((x2 - x1) * k) + padX * 2, rh = (int)((y2 - y1) * k) + padTop + padBottom;
                    if (rw < 120) rw = Math.Min(bmp.Width, 240);
                    if (rh < 90) rh = Math.Min(bmp.Height, 180);
                    if (rx < 0) rx = 0;
                    if (ry < 0) ry = 0;
                    if (rx + rw > bmp.Width) rw = bmp.Width - rx;
                    if (ry + rh > bmp.Height) rh = bmp.Height - ry;
                    if ((long)rw * rh * 100 >= (long)bmp.Width * bmp.Height * 12)
                    {
                        _autoRoi = new Rectangle(rx, ry, rw, rh);
                        Log("自动识别区域已锁定：" + rw + "×" + rh + "（占全幅 " + ((long)rw * rh * 100 / ((long)bmp.Width * bmp.Height)) + "%）");
                    }
                    else { _autoRoi = Rectangle.Empty; Log("自动区域太小，改用设定区域"); }
                    _autoRoiAt = DateTime.Now;
                }
            }
            catch { }
        }

        private Rectangle CurrentRoi(Size frame)
        {
            int x = (int)Math.Round(frame.Width * _roiX / 100.0);
            int y = (int)Math.Round(frame.Height * _roiY / 100.0);
            int w = (int)Math.Round(frame.Width * _roiW / 100.0);
            int h = (int)Math.Round(frame.Height * _roiH / 100.0);
            if (w < 32) w = Math.Min(32, frame.Width);
            if (h < 32) h = Math.Min(32, frame.Height);
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x + w > frame.Width) x = Math.Max(0, frame.Width - w);
            if (y + h > frame.Height) y = Math.Max(0, frame.Height - h);
            return new Rectangle(x, y, w, h);
        }

        // ---------- 摄像头 ----------
        private void RefreshDevices(bool autoOpen)
        {
            string keep = _cmbCam.SelectedItem as string;
            var list = VfwCamera.ListDevices(Handle);
            _cmbCam.Items.Clear();
            foreach (var n in list) _cmbCam.Items.Add(n);
            if (_cmbCam.Items.Count == 0)
            {
                SetStatusText("没有检测到摄像头（插上免驱 USB 摄像头/高拍仪后点“刷新”）", Color.Red);
                return;
            }
            string want = GetCfg("device");
            int idx = 0;
            if (!string.IsNullOrEmpty(keep)) { int k = _cmbCam.Items.IndexOf(keep); if (k >= 0) idx = k; }
            if (!string.IsNullOrEmpty(want)) { int k = _cmbCam.Items.IndexOf(want); if (k >= 0) idx = k; }
            _cmbCam.SelectedIndex = idx;
            var names = new List<string>();
            foreach (var it in _cmbCam.Items) names.Add(it.ToString());
            Log("检测到摄像头：" + string.Join("；", names.ToArray()));
            SetStatusText("已找到 " + _cmbCam.Items.Count + " 个摄像头，正在打开…", Color.DodgerBlue);
            if (autoOpen && !_on)
            {
                try { ToggleCamera(); }
                catch (Exception ex) { Log("自动打开摄像头失败：" + ex.Message); SetStatusText("自动打开失败，请手动点“打开摄像头”", Color.Red); }
            }
        }

        // 目标取流宽度（按“清晰度”下拉选择）
        private int WantWidth()
        {
            switch (_cmbRes == null ? 0 : _cmbRes.SelectedIndex)
            {
                case 1: return 1920;
                case 2: return 2592;
                case 3: return 4208;
                case 4: return 1280;
            }
            return 9999;   // 自动：取设备支持的最大分辨率（读小二维码需要像素，识别区域框小一点即可保证速度）
        }

        private void ToggleCamera()
        {
            if (_on) { CloseCamera(); return; }
            if (_cmbCam.SelectedIndex < 0) { SetStatusText("请先选择摄像头", Color.Red); return; }
            string name = _cmbCam.SelectedItem as string;

            // 主用 DirectShow（免驱 VFW 在不少高拍仪上只能取到黑帧）
            string msg = "";
            bool ok = false;
            // 自测：假装有一台摄像头，用一张固定图片跑完整流程（不碰真硬件）
            if (VfwCamera.TestFakeDevice)
            {
                bool fcOk = VfwCamera.Connect(Handle, 0);
                if (fcOk)
                {
                    _cam = (IntPtr)12345;      // 模拟模式下不真的建采集窗（Disconnect/Destroy 在模拟模式下是空操作）
                    ok = true;
                    Log("自测模式：使用模拟摄像头（图片 " + VfwCamera.TestFakeImage + "）");
                }
            }
            try
            {
                if (!ok)
                {
                    _ds = new DShowCamera();
                    DShowCamera.Log = delegate(string s) { Log("  " + s); };
                    ok = _ds.Open(_cmbCam.SelectedIndex, WantWidth(), out msg);
                }
            }
            catch (Exception ex) { msg = ex.Message; ok = false; }
            if (!ok)
            {
                Log("DirectShow 打开失败：" + msg);
                if (_ds != null) { try { _ds.Close(); } catch { } _ds = null; }
                SetStatusText("打开失败：" + msg + "（可先关掉“相机”或高拍仪自带软件）", Color.Red);
                return;
            }
            DShowCamera.Log = null;
            if (_owner != null)
            {
                _owner.SuppressDialogs = true;      // 摄像头模式下不弹模态框，避免打断连续识别
                _owner.NoticeChanged += OnOwnerNotice;
                _owner.Printed += OnOwnerPrinted;
            }
            _on = true;
            _btnOpen.Text = "关闭摄像头";
            _autoDumped = false;
            _lastFrameSize = Size.Empty;
            _lastDecodeAt = DateTime.MinValue;
            Size sz = _ds != null ? _ds.FrameSize : VfwCamera.FrameSize(_cam);
            Log("已打开：" + name + "（" + sz.Width + "×" + sz.Height + "）");
            Log("视频格式：" + msg);
            Log("使用建议：① 点「一键找焦点」把对焦锁定；② 把设备放好后在预览上拖拽框选标签区域（框小一点识别更快更准），这些设置都会自动记住");

            // 对焦：能控就启用，并套用上次定焦值
            int fmin = 0, fmax = 0, fstep = 1, fdef = 0, fcaps = 0;
            _focusSupported = _ds != null && _ds.GetFocusRange(out fmin, out fmax, out fstep, out fdef, out fcaps);
            if (_focusSupported)
            {
                _focusMin = fmin; _focusMax = fmax; _focusStep = fstep <= 0 ? 1 : fstep; _focusDef = fdef;
                _numFocus.Minimum = Math.Max(0, fmin);
                _numFocus.Maximum = Math.Max(fmin, fmax);
                if (_numFocus.Increment <= 0) _numFocus.Increment = 1;
                int saved;
                bool hasSaved = int.TryParse(GetCfg("focus_" + name), out saved) && saved >= fmin && saved <= fmax;
                bool autoF = GetCfg("autofocus") != "0";
                _loading = true;
                _chkAutoFocus.Checked = !hasSaved && autoF;
                _numFocus.Value = hasSaved ? saved : (fdef >= fmin && fdef <= fmax ? fdef : fmin);
                _loading = false;
                if (hasSaved) { _chkAutoFocus.Checked = false; _ds.SetFocus(saved, false); Log("对焦：已套用上次锁定的定焦值 " + saved + "（不用再调，直接开始干活）"); }
                else
                {
                    _ds.SetFocus((int)_numFocus.Value, _chkAutoFocus.Checked);
                    Log("对焦：跟随硬件（自动=" + _chkAutoFocus.Checked + "）");
                    // 没有记住的定焦值：自动找一次最清晰的对焦并锁定（高拍仪每次打开都会把对焦复位成糊的默认值）
                    Log("第一次使用：自动执行一次对焦搜索并锁定（约 10 秒，之后会记住，不用再调）…");
                    SetStatusText("正在自动对焦（约 10 秒）…", Color.DodgerBlue);
                    Application.DoEvents();
                    FindBestFocus();
                }
                Log("对焦范围 " + fmin + "~" + fmax + "（默认 " + fdef + "）。建议点「一键找焦点」自动找一个清晰值并锁定");
            }
            else
            {
                _chkAutoFocus.Enabled = false; _numFocus.Enabled = false; _btnFindFocus.Enabled = false;
                Log("这台设备不支持软件调对焦（用它的自动对焦即可）");
            }

            _locked = false; _clearCount = 0; _pendingSig = ""; _sameCount = 0;
            SetStatusText("摄像头已打开，把设备放到镜头下…", Color.SeaGreen);
            SaveCfg();
            if (_timer != null) _timer.Start();
        }

        // 一扫，找最清晰的对焦值并锁定（只做一次，之后一直用这个值，来一台读一台更快更稳）
        private void FindBestFocus()
        {
            if (_ds == null || !_on) { SetStatusText("请先打开摄像头", Color.DarkOrange); return; }
            if (!_focusSupported) { SetStatusText("这台设备不支持软件调对焦", Color.DarkOrange); return; }
            try
            {
                _timer.Stop();
                int steps = 12;
                int best = _focusMin; double bestSh = -1;
                Log("--- 开始自动对焦（约 10 秒）---");
                for (int i = 0; i <= steps; i++)
                {
                    int v = _focusMin + (_focusMax - _focusMin) * i / steps;
                    // 跳过对焦范围最前端（那里几乎一定是糊的，之前就误锁过 0）
                    if (i == 0) continue;
                    _ds.SetFocus(v, false);
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(350);
                    using (var b = _ds.Grab())
                    {
                        if (b == null) continue;
                        double sh = DShowCamera.Sharpness(b, EffectiveRoi(b.Size));
                        // 关键：先看“这一档能不能真读出码”，读得出码的一律赢过只是“看着清楚”的档
                        int codes = 0;
                        try { codes = CameraDetect.CountCodes(b, EffectiveRoi(b.Size)); } catch { }
                        double score = codes * 1000.0 + sh;
                        Log("对焦 " + v + " → 清晰度 " + sh.ToString("0.0") +
                            (codes > 0 ? ("　读出 " + codes + " 个码") : ""));
                        if (score > bestSh) { bestSh = score; best = v; }
                    }
                }
                // 在最佳值附近再细扫一遍
                int span = Math.Max(1, (_focusMax - _focusMin) / steps);
                int bestCodes = 0;
                for (int v = Math.Max(_focusMin, best - span); v <= Math.Min(_focusMax, best + span); v += Math.Max(1, span / 4))
                {
                    _ds.SetFocus(v, false);
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(250);
                    using (var b = _ds.Grab())
                    {
                        if (b == null) continue;
                        double sh = DShowCamera.Sharpness(b, EffectiveRoi(b.Size));
                        int codes = 0;
                        try { codes = CameraDetect.CountCodes(b, EffectiveRoi(b.Size)); } catch { }
                        double score = codes * 1000.0 + sh;
                        Log("  细扫 对焦 " + v + " → 清晰度 " + sh.ToString("0.0") +
                            (codes > 0 ? ("　读出 " + codes + " 个码") : ""));
                        if (score > bestSh) { bestSh = score; best = v; bestCodes = codes; }
                    }
                }
                _loading = true;
                _chkAutoFocus.Checked = false;
                _numFocus.Value = Math.Min(_numFocus.Maximum, Math.Max(_numFocus.Minimum, best));
                _loading = false;
                _ds.SetFocus(best, false);
                SetCfg("focus_" + (_cmbCam.SelectedItem as string), best.ToString());
                SetCfg("autofocus", "0");
                SaveCfg();
                Log("已锁定对焦值 " + best + "（评分 " + bestSh.ToString("0") +
                    (bestCodes > 0 ? ("，能读出 " + bestCodes + " 个码") : "，当前画面里没读到码（可放上设备后点「一键找焦点」再定一次）") + "），已记住");
                SetStatusText("已锁定对焦 " + best + "，可以开始识别了", Color.SeaGreen);
            }
            catch (Exception ex) { Log("自动对焦失败：" + ex.Message); }
            finally { if (_on && _timer != null) _timer.Start(); }
        }

        internal void CloseCamera()
        {
            try { if (_timer != null) _timer.Stop(); } catch { }
            try { if (_followTimer != null) _followTimer.Stop(); } catch { }
            try { if (_ds != null) { _ds.Close(); _ds = null; } } catch { }
            try { if (_owner != null) { _owner.SuppressDialogs = false; _owner.NoticeChanged -= OnOwnerNotice; _owner.Printed -= OnOwnerPrinted; } } catch { }
            try { if (_owner != null) { _owner.Move -= OnOwnerMoved; _owner.Resize -= OnOwnerMoved; } } catch { }
            try { if (_cam != IntPtr.Zero) { VfwCamera.Disconnect(_cam); VfwCamera.Destroy(_cam); } } catch { }
            _cam = IntPtr.Zero; _on = false;
            try { if (_disp != null) { _disp.Dispose(); _disp = null; } } catch { }
            try { if (_btnOpen != null) _btnOpen.Text = "打开摄像头"; } catch { }
            SetStatusText("摄像头已关闭", Color.Gray);
            try { if (_lblInfo != null) _lblInfo.Text = ""; } catch { }
            try { SaveCfg(); } catch { }
        }

        private void LayoutCamera()
        {
            try { if (_cam != IntPtr.Zero) VfwCamera.Move(_cam, 0, 0, _preview.ClientSize.Width, _preview.ClientSize.Height); } catch { }
        }

        // ---------- 识别循环 ----------
        private void TickOnce()
        {
            if (!_on) return;
            if (_ds == null && _cam == IntPtr.Zero) return;   // DShow 模式用 _ds，VFW 模式才看 _cam
            if (_owner == null || _owner.IsDisposed) { CloseCamera(); return; }
            Bitmap bmp = _ds != null ? _ds.Grab() : VfwCamera.Grab(_cam);
            if (bmp == null) { SetStatusText("抓取画面中…", Color.DodgerBlue); return; }
            try
            {
                _frames++;
                DateTime now = DateTime.Now;
                if ((now - _fpsTime).TotalSeconds >= 2)
                {
                    _fps = _frames / Math.Max(0.5, (now - _fpsTime).TotalSeconds);
                    _frames = 0; _fpsTime = now;
                }
                if (!_autoDumped) { _autoDumped = true; AutoDump(bmp); }
                if ((DateTime.Now - _autoRoiAt).TotalSeconds > 30) UpdateAutoRoi(bmp);   // 仅记录参考，不参与识别
                try { _sharp = DShowCamera.Sharpness(bmp, EffectiveRoi(bmp.Size)); } catch { }
                if (bmp.Size != _lastFrameSize)
                {
                    if (_lastFrameSize.Width > 0)
                        Log("抓帧尺寸变化：" + _lastFrameSize.Width + "×" + _lastFrameSize.Height +
                            " → " + bmp.Width + "×" + bmp.Height);
                    _lastFrameSize = bmp.Size;
                }
                ShowFrame(bmp);

                // 暂停：画面照出（工人能看到预览），但不识别、不录入
                if (Paused)
                {
                    SetStatusText("已暂停（画面还在，识别已停）· 点「继续」恢复", Color.DarkOrange);
                    return;
                }

                // ---------- 识别：一帧一次拿全（二维码 + 条码），画面没变就不重复识别 ----------
                Rectangle roiRect = EffectiveRoi(bmp.Size);
                CameraDetect.FrameCells(bmp, roiRect, _fpNow);
                double diff = CameraDetect.CellDiff(_fpPrev, _fpNow);
                bool frameChanged = diff >= 0.05;          // 噪点不算变化，换了设备才算
                if (frameChanged)
                {
                    byte[] tmp = _fpPrev; _fpPrev = _fpNow; _fpNow = tmp;
                }
                if (frameChanged)
                {
                    _fpState = 0;          // 画面变了：允许识别
                    _partialTries = 0;
                    if (_locked) { _locked = false; Log("画面已变化，可以识别下一台"); }
                }
                bool due = (now - _lastDecodeAt).TotalMilliseconds >= (double)_numInterval.Value;
                bool wantAttempt;
                if (_decoding) wantAttempt = false;
                else if (frameChanged || _fpState == 0) wantAttempt = due;
                else if (_fpState == 1) wantAttempt = false;                 // 这台已经识别过了：不再重复提取
                else wantAttempt = now >= _nextRetryAt;                      // 画面里没有码：慢速重试（且只跑便宜的那半边）

                if (wantAttempt)
                {
                    _lastDecodeAt = now;
                    bool cheapOnly = (frameChanged == false && _fpState == 2);   // 画面没变又没读到：只做便宜的重查
                    Bitmap forDecode = null;
                    try { forDecode = (Bitmap)bmp.Clone(); } catch { }
                    if (forDecode != null)
                    {
                        _decoding = true;
                        Bitmap work = forDecode;
                        Rectangle roi2 = roiRect;
                        _readRoiSize = roi2.Size;
                        Rectangle qrHint = PctToPixels(_qrHintPct, roi2.Size);
                        var bandHints = PctListToPixels(_bandHintPct, roi2.Size);
                        bool needSn = _owner != null && _owner.IsFixedModelMode;
                        // 兜底：这台还没读到二维码、而且设备已经放稳了 → 把画面转 180°/90° 再解一次（一台只做一次）
                        bool allowRotate = !_qrOkForDevice && !_rotateTried && !frameChanged;
                        if (allowRotate) { _rotateTried = true; Log("还没读到二维码：做一次“翻转兜底”重扫（倒着放/侧着放也能读）"); }
                        // 兜底：读不全（或好几次什么都没读到）时，设备也放稳了，就退回“整幅 + 旋转/反色”的老办法，
                        // 慢但保险——宁可多等 1~2 秒，也不能拿上一台的 MAC 去打印
                        bool deepFallback = (_partialTries >= 1 || _emptyTries >= 3) && !frameChanged;
                        DateTime t0 = DateTime.Now;
                        System.Threading.ThreadPool.QueueUserWorkItem(delegate
                        {
                            CameraDetect.FrameRead fr = null;
                            try { fr = CameraDetect.ReadFrame(work, roi2, qrHint, bandHints, needSn, allowRotate, deepFallback, cheapOnly); }
                            catch { }
                            int ms = (int)(DateTime.Now - t0).TotalMilliseconds;
                            try { work.Dispose(); } catch { }
                            try
                            {
                                if (IsDisposed || !IsHandleCreated) { _decoding = false; return; }
                                BeginInvoke((MethodInvoker)delegate
                                {
                                    _decoding = false;
                                    _lastDecodeMs = ms;
                                    try { HandleFrameRead(fr); } catch { }
                                    UpdateInfo();
                                });
                            }
                            catch { _decoding = false; }
                        });
                    }
                }
            }
            catch { }
            finally { try { bmp.Dispose(); } catch { } }
        }

        // ---------- 位置提示（按 ROI 百分比记住上一台的位置，下一台先在这些位置找） ----------
        private static Rectangle PctToPixels(RectangleF pct, Size roi)
        {
            if (roi.Width < 8 || roi.Height < 8) return Rectangle.Empty;
            if (pct.Width <= 0 || pct.Height <= 0) return Rectangle.Empty;
            int x = (int)Math.Round(pct.X * roi.Width), y = (int)Math.Round(pct.Y * roi.Height);
            int w = (int)Math.Round(pct.Width * roi.Width), h = (int)Math.Round(pct.Height * roi.Height);
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (w < 8) w = 8;
            if (h < 8) h = 8;
            if (x + w > roi.Width) w = roi.Width - x;
            if (y + h > roi.Height) h = roi.Height - y;
            if (w < 8 || h < 8) return Rectangle.Empty;
            return new Rectangle(x, y, w, h);
        }

        private static RectangleF PixelsToPct(Rectangle r, Size roi)
        {
            if (roi.Width < 8 || roi.Height < 8 || r.Width < 8 || r.Height < 8) return RectangleF.Empty;
            return new RectangleF((float)r.X / roi.Width, (float)r.Y / roi.Height,
                                  (float)r.Width / roi.Width, (float)r.Height / roi.Height);
        }

        private List<Rectangle> PctListToPixels(List<RectangleF> list, Size roi)
        {
            var outp = new List<Rectangle>();
            if (list == null) return outp;
            foreach (var p in list)
            {
                var r = PctToPixels(p, roi);
                if (r.Width > 0) outp.Add(r);
            }
            return outp;
        }

        // 一台设备的“身份钥匙”：镜头下这台设备可能只会读到其中一部分（有时只读到二维码、有时只读到条码），
        // 所以只要**任意一把钥匙**和当前这台对得上，就认为还是同一台（避免同一台被当成新的一台重复打印）。
        private static List<string> DeviceKeys(CameraDetect.FrameRead fr)
        {
            var keys = new List<string>();
            try
            {
                if (fr == null) return keys;
                string sn = "", mac = "";
                if (fr.HasQr)
                {
                    var p = QRParser.Parse(fr.QrText);
                    if (p != null && !string.IsNullOrEmpty(p.SN)) sn = p.SN.Trim().ToUpperInvariant();
                    else if (!string.IsNullOrEmpty(p.SN)) sn = p.SN.Trim().ToUpperInvariant();
                }
                foreach (var c in fr.Codes)
                {
                    string m = QRParser.NormalizeMac(c);
                    if (m != null) { if (mac.Length == 0) mac = m; continue; }
                }
                if (sn.Length > 0) keys.Add("SN:" + sn);
                if (mac.Length > 0) keys.Add("MAC:" + mac);
                if (keys.Count == 0)
                {
                    if (fr.HasQr) keys.Add("QR:" + fr.QrText.Trim().ToUpperInvariant());
                    else foreach (var c in fr.Codes) if (!string.IsNullOrEmpty(c)) { keys.Add("B:" + c.Trim().ToUpperInvariant()); break; }
                }
            }
            catch { }
            return keys;
        }

        private static string Shorten2(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }

        // 一帧读到的东西：二维码（型号/类型/SN）+ 条码（SN / MAC）一次填好；
        // “和上一台内容一样”就不重复提取填写
        private void HandleFrameRead(CameraDetect.FrameRead fr)
        {
            if (fr == null) { _fpState = 2; _nextRetryAt = DateTime.Now.AddMilliseconds(800); return; }
            _lastReadMs = fr.Ms;
            _lastReadAt = DateTime.Now;

            // 记住这一帧里二维码 / 条码的位置，下一帧先在这些位置找（快很多）
            if (_readRoiSize.Width > 8)
            {
                if (fr.HasQr && fr.QrBox.Width > 8)
                    _qrHintPct = PixelsToPct(fr.QrBox, _readRoiSize);
                if (fr.Bands != null && fr.Bands.Count > 0)
                {
                    _bandHintPct.Clear();
                    for (int i = 0; i < fr.Bands.Count && i < 8; i++)
                    {
                        var pct = PixelsToPct(fr.Bands[i], _readRoiSize);
                        if (pct.Width > 0) _bandHintPct.Add(pct);
                    }
                }
            }

            bool anyCode = fr.HasQr || fr.Codes.Count > 0;
            if (!anyCode)
            {
                _fpState = 2;                                    // 这帧没东西：慢速重试即可
                _nextRetryAt = DateTime.Now.AddMilliseconds(1500);
                if ((DateTime.Now - _lastDiagAt).TotalSeconds > 1.5)
                {
                    _lastDiagAt = DateTime.Now;
                    Log("这帧没读到码（一帧 " + fr.Ms + "ms）：" + fr.Diag);
                }
                _emptyTries++;
                _clearCount++;
                if (_devKeys.Count > 0 || _printedKey.Length > 0 || _camSeen)
                {
                    _devKeys.Clear();                            // 设备已拿开：下一台（哪怕同一台）重新识别
                    _printedKey = ""; _camSeen = false;
                    _partialTries = 0;
                    _qrOkForDevice = false; _rotateTried = false;
                    _qrHintPct = RectangleF.Empty;
                    _bandHintPct.Clear();
                    if (_owner != null) { try { _owner.CamDeviceGone(); } catch { } }
                }
                if (_locked && _clearCount >= 2) { _locked = false; Log("画面已清空，等待下一台…"); }
                if (_locked) SetStatusText("打印完成，请把设备拿开（拿开后自动继续）", Color.DarkOrange);
                else if (_lastFrameSize.Width > 0 && _lastFrameSize.Width < 1200 && (DateTime.Now - _noCodeAt).TotalSeconds > 3)
                    SetStatusText("没识别到：把设备凑近一点（当前抓帧 " + _lastFrameSize.Width + "×" + _lastFrameSize.Height + "）", Color.OrangeRed);
                else SetStatusText("等待识别…（把设备放到镜头下）", Color.DodgerBlue);
                return;
            }

            _clearCount = 0; _noCodeAt = DateTime.Now; _emptyTries = 0;
            string show = (fr.HasQr ? Shorten2(fr.QrText, 36) : "（无二维码）");
            if (fr.Codes.Count > 0) show += " ／ 条码 " + Shorten2(string.Join(" / ", fr.Codes.ToArray()), 40);
            _lblResult.Text = "识别结果：" + show;

            if (_locked)
            {
                _fpState = 1;
                SetStatusText("打印完成，请把设备拿开（拿开后自动继续）", Color.DarkOrange);
                return;
            }

            var keys = DeviceKeys(fr);
            bool sameDevice = false;
            foreach (var k in keys) if (_devKeys.Contains(k)) { sameDevice = true; break; }
            if (keys.Count > 0 && !sameDevice)
            {
                _devKeys.Clear();                                // 换了一台：重新记钥匙
                _printedKey = "";
                _qrOkForDevice = false; _rotateTried = false;
                if (_camSeen) Log("换了一台（" + keys[0] + "）");
                _camSeen = true;
            }
            if (fr.HasQr) _qrOkForDevice = true;
            foreach (var k in keys) _devKeys.Add(k);
            if (sameDevice && _printedKey.Length > 0)
            {
                _fpState = 1;                                    // 同一台：不再提取、不再填写、不再打印
                SetStatusText("与上一台相同，已跳过（不重复提取填写）", Color.Gray);
                Log("与上一台相同（" + _printedKey + "），跳过提取与填写");
                return;
            }
            string sig = keys.Count > 0 ? keys[0] : "";
            if (sig.Length > 0)
            {
                if (sig == _pendingSig) _sigRun++;
                else { _pendingSig = sig; _sigRun = 1; }
                if (_sigRun < (int)_numStable.Value)
                {
                    SetStatusText("已读到（" + Shorten2(sig, 30) + "），确认 " + _sigRun + "/" + (int)_numStable.Value + " …", Color.DodgerBlue);
                    _fpState = 0;
                    _nextRetryAt = DateTime.Now.AddMilliseconds(Math.Max(60, (int)_numInterval.Value));
                    return;
                }
            }

            bool guess = _chkGuess == null || _chkGuess.Checked;
            ScanFeed res = _owner != null ? _owner.FeedDevice(fr.QrText, fr.Codes, guess) : ScanFeed.Ignored;
            if (res == ScanFeed.Completed)
            {
                _printedKey = sig;
                _fpState = 1;
                _locked = true; _clearCount = 0; _partialTries = 0;
                Log("一次读完一台：" + show + "　用时 " + fr.Ms + "ms　" + fr.Diag);
                if (_chkBeep.Checked) Beep(true);
            }
            else if (res == ScanFeed.Partial)
            {
                _fpState = 0;
                _partialTries++;
                _nextRetryAt = DateTime.Now.AddMilliseconds(_partialTries >= 2 ? 120 : 260);
                if (_partialTries == 1 || (DateTime.Now - _lastDiagAt).TotalSeconds > 3)
                {
                    _lastDiagAt = DateTime.Now;
                    Log("读到一部分：" + show + "　用时 " + fr.Ms + "ms　" + fr.Diag);
                }
            }
            else
            {
                _fpState = 0;
                _partialTries++;
                _nextRetryAt = DateTime.Now.AddMilliseconds(400);
                SetStatusText("读到内容但不完整（" + Shorten2(show, 40) + "）", Color.DarkOrange);
                Log("内容未被接受：" + show + "　" + fr.Diag);
                if (_chkBeep.Checked) Beep(false);
            }
        }

        // 自己画预览：画面 = 正在识别的那一帧（可左右翻转），并画出识别区域框
        private void ShowFrame(Bitmap bmp)
        {
            if (bmp == null || _pic == null || _preview == null) return;
            Size cs = _pic.ClientSize;                       // 用预览框自己的尺寸，避免被布局瞬时值带偏
            if (cs.Width < 16 || cs.Height < 16) return;
            int w = cs.Width, h = cs.Height;
            // 尺寸只差几个像素时不要重建（否则画面会一宽一窄地跳）
            if (_disp == null || Math.Abs(_disp.Width - w) > 3 || Math.Abs(_disp.Height - h) > 3)
            {
                try { if (_disp != null) _disp.Dispose(); } catch { }
                _disp = new Bitmap(w, h);
                try { _pic.Image = _disp; } catch { }
            }
            try
            {
                using (var g = Graphics.FromImage(_disp))
                {
                    g.Clear(Color.FromArgb(24, 28, 32));
                    double s = Math.Min((double)w / bmp.Width, (double)h / bmp.Height);
                    int dw = Math.Max(1, (int)(bmp.Width * s)), dh = Math.Max(1, (int)(bmp.Height * s));
                    int dx = (w - dw) / 2, dy = (h - dh) / 2;
                    bool mirror = _chkMirror != null && _chkMirror.Checked;
                    if (mirror)
                    {
                        g.TranslateTransform(w, 0);
                        g.ScaleTransform(-1, 1);
                        g.DrawImage(bmp, new Rectangle(w - dx - dw, dy, dw, dh));
                        g.ResetTransform();
                    }
                    else
                    {
                        g.DrawImage(bmp, new Rectangle(dx, dy, dw, dh));
                    }
                    // 识别区域框（按百分比映射到显示区域）
                    int rw = Math.Max(2, (int)(dw * _roiW / 100.0)), rh = Math.Max(2, (int)(dh * _roiH / 100.0));
                    int rx = dx + (int)(dw * _roiX / 100.0), ry = dy + (int)(dh * _roiY / 100.0);
                    using (var pen = new Pen(Color.FromArgb(255, 196, 0), 2))
                        g.DrawRectangle(pen, rx, ry, rw - 1, rh - 1);
                    if (_dragRoi)
                    {
                        int gx1 = Math.Min(_dragFrom.X, _dragTo.X), gy1 = Math.Min(_dragFrom.Y, _dragTo.Y);
                        int gw = Math.Abs(_dragTo.X - _dragFrom.X), gh = Math.Abs(_dragTo.Y - _dragFrom.Y);
                        using (var pen = new Pen(Color.FromArgb(0, 200, 120), 2))
                            g.DrawRectangle(pen, gx1, gy1, gw, gh);
                    }
                }
                _pic.Invalidate();
            }
            catch { }
        }

        // 打开摄像头后自动存一张“抓到的画面”，方便排查“预览有画面但识别不到”的问题
        private void AutoDump(Bitmap bmp)
        {
            try
            {
                string path = Path.Combine(Application.StartupPath, "摄像头画面_自动.png");
                using (var copy = new Bitmap(bmp)) copy.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                Log("已自动保存抓到的画面：" + path + "（" + bmp.Width + "×" + bmp.Height + "）");
                double avg, dev;
                SampleStats(bmp, out avg, out dev);
                Log("画面亮度 平均 " + avg.ToString("0") + " / 波动 " + dev.ToString("0") +
                    (dev < 6 ? "  ← 画面几乎是纯色，说明这种取帧方式在这台摄像头上取不到有效图像" : ""));
            }
            catch { }
        }

        private static void SampleStats(Bitmap bmp, out double avg, out double dev)
        {
            CameraDetect.Brightness(bmp, out avg, out dev);
        }

        private void UpdateInfo()
        {
            try
            {
                Size sz = VfwCamera.FrameSize(_cam);
                string grabTxt = _lastFrameSize.Width > 0 ? (_lastFrameSize.Width + "×" + _lastFrameSize.Height) : "—";
                _lblInfo.Text = "抓帧 " + grabTxt + " · 约 " + _fps.ToString("0.0") +
                                " 帧/秒 · 一帧读完 " + _lastReadMs + "ms（含抓帧共 " + _lastDecodeMs + "ms） · 清晰度 " + _sharp.ToString("0");
                if (_lastReadMs > 1200 && (DateTime.Now - _slowLoggedAt).TotalSeconds > 15)
                {
                    _slowLoggedAt = DateTime.Now;    // 最多每 15 秒提示一次
                    Log("识别较慢（一帧 " + _lastReadMs + "ms）：可把识别区域缩小一点，或把“识别间隔”调大");
                }
            }
            catch { }
        }

        private void HandleTexts(List<string> texts)
        {
            bool any = texts != null && texts.Count > 0;
            if (!any)
            {
                _pendingSig = ""; _sameCount = 0;
                _clearCount++;
                if (_locked && _clearCount >= 2) { _locked = false; _ocrTried = false; _autoRoi = Rectangle.Empty; Log("画面已清空，等待下一台…（自动区域已重置）"); }
                // 画面里没有二维码/条码时：自动用 OCR 读一次标签文字（每台只试一次）
                if (!_locked && _owner != null && _owner.ScanStateText == "等待二维码" && !_ocrTried &&
                    (DateTime.Now - _noCodeAt).TotalSeconds > 2.5 && _owner.ModelCandidates.Count > 0)
                {
                    _ocrTried = true;
                    Log("（画面里没有二维码）自动用文字识别读取型号/SN/MAC…");
                    DoOcrOnce(false);
                    return;
                }
                if (_locked)
                    SetStatusText("打印完成，请把设备拿开（拿开后自动继续）", Color.DarkOrange);
                else if (_lastFrameSize.Width > 0 && _lastFrameSize.Width < 1200 && (DateTime.Now - _noCodeAt).TotalSeconds > 3)
                    SetStatusText("没识别到：把设备凑近一点，让二维码占满识别框的 1/4 以上（当前抓帧 " +
                        _lastFrameSize.Width + "×" + _lastFrameSize.Height + "）", Color.OrangeRed);
                else
                    SetStatusText("等待识别…（把设备放到镜头下）", Color.DodgerBlue);
                return;
            }
            _clearCount = 0;
            _noCodeAt = DateTime.Now;

            string qrText, mac, other;
            CameraDetect.Classify(texts, out qrText, out mac, out other);
            string show = string.Join(" / ", texts.ToArray());
            if (show.Length > 60) show = show.Substring(0, 60) + "…";
            _lblResult.Text = "识别结果：" + show;

            if (_locked) { SetStatusText("打印完成，请把设备拿开（拿开后自动继续）", Color.DarkOrange); return; }

            string state = _owner.ScanStateText;
            string candidate = null;
            if (state == "等待二维码") candidate = qrText;
            else if (state == "等待SN") candidate = other;
            else candidate = mac;

            if (candidate == null)
            {
                // 没二维码时：用 SN 条码去历史记录里反查型号/类型（可勾选关闭）
                if (state == "等待二维码" && other != null && (_chkGuess == null || _chkGuess.Checked))
                {
                    if (_owner.FeedSnInsteadOfQr(other))
                    {
                        Log("没有二维码：用 SN " + Shorten(other) + " 反查到型号并继续");
                        _pendingSig = ""; _sameCount = 0;
                        if (_chkBeep.Checked) Beep(true);
                        return;
                    }
                }
                _pendingSig = ""; _sameCount = 0;
                SetStatusText(state + "…（还没识别到需要的内容）", Color.DarkOrange);
                return;
            }

            if (candidate == _pendingSig) _sameCount++;
            else { _pendingSig = candidate; _sameCount = 1; }
            SetStatusText(state + "…（连续 " + _sameCount + "/" + (int)_numStable.Value + " 帧）", Color.DodgerBlue);
            if (_sameCount < (int)_numStable.Value) return;

            _pendingSig = ""; _sameCount = 0;
            ScanFeed res = _owner.ProcessScanText(candidate, true);
            if (res == ScanFeed.Completed)
            {
                _locked = true; _clearCount = 0;
                Log("已完成一台（" + Shorten(candidate) + "）");
                if (_chkBeep.Checked) Beep(true);
            }
            else if (res == ScanFeed.Partial)
            {
                Log("已识别：" + Shorten(candidate));
            }
            else
            {
                Log("内容未被接受：" + Shorten(candidate));
                if (_chkBeep.Checked) Beep(false);
            }
        }

        private static string Shorten(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= 60 ? s : (s.Substring(0, 60) + "…");
        }

        private void Beep(bool ok)
        {
            try { if (ok) Console.Beep(1200, 90); else Console.Beep(420, 170); } catch { }
        }

        // ---------- 在预览上拖拽框选识别区域 ----------
        private static List<string> codes2ForText(List<CodeHit> hits)
        {
            var list = new List<string>();
            foreach (var h in hits) list.Add(h.Text);
            return list;
        }

        private void OnOwnerNotice(object sender, EventArgs e)
        {
            try
            {
                string msg = _owner != null ? _owner.LastNotice : "";
                if (!string.IsNullOrEmpty(msg)) { SetStatusText(msg, Color.Red); Log("提示：" + msg); }
            }
            catch { }
        }

        // 这台已经打印过了（哪怕是扫码枪补扫二维码 / 手动点的打印）：镜头这边不再重复识别打印
        private void OnOwnerPrinted(object sender, EventArgs e)
        {
            try
            {
                if (_printedKey.Length == 0 && _devKeys.Count > 0)
                    foreach (var k in _devKeys) { _printedKey = k; break; }
                if (_printedKey.Length == 0) _printedKey = "已打印";
                _fpState = 1;
                _locked = true; _clearCount = 0; _partialTries = 0;
                Log("这台已经打印完成（" + _printedKey + "），镜头这边不再重复提取打印");
            }
            catch { }
        }

        private void Pic_MouseDown2(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _dragRoi = true;
            _dragFrom = new Point(Math.Max(0, Math.Min(_pic.ClientSize.Width, e.X)),
                                  Math.Max(0, Math.Min(_pic.ClientSize.Height, e.Y)));
            _dragTo = _dragFrom;
            try { _pic.Capture = true; } catch { }
        }

        private void Pic_MouseMove2(object sender, MouseEventArgs e)
        {
            if (!_dragRoi) return;
            _dragTo = new Point(Math.Max(0, Math.Min(_pic.ClientSize.Width, e.X)),
                                Math.Max(0, Math.Min(_pic.ClientSize.Height, e.Y)));
        }

        private void Pic_MouseUp2(object sender, MouseEventArgs e)
        {
            if (!_dragRoi) return;
            _dragRoi = false;
            try { _pic.Capture = false; } catch { }
            int x1 = Math.Min(_dragFrom.X, _dragTo.X), y1 = Math.Min(_dragFrom.Y, _dragTo.Y);
            int x2 = Math.Max(_dragFrom.X, _dragTo.X), y2 = Math.Max(_dragFrom.Y, _dragTo.Y);
            if (x2 - x1 < 20 || y2 - y1 < 20) { SetStatusText("框太小了，请重新拖拽框选", Color.DarkOrange); return; }
            int dw, dh, dx, dy;
            ComputeImageRect(out dx, out dy, out dw, out dh);
            if (dw <= 0 || dh <= 0) return;
            _roiX = Math.Max(0, Math.Min(100, (x1 - dx) * 100.0 / dw));
            _roiY = Math.Max(0, Math.Min(100, (y1 - dy) * 100.0 / dh));
            _roiW = Math.Max(2, Math.Min(100 - _roiX, (x2 - x1) * 100.0 / dw));
            _roiH = Math.Max(2, Math.Min(100 - _roiY, (y2 - y1) * 100.0 / dh));
            _useCustomRoi = true;
            _loading = true;
            if (_cmbRoi != null) _cmbRoi.SelectedIndex = 0;   // 切到“自定义”
            _loading = false;
            SaveCfg();
            Log("识别区域已框选：X " + _roiX.ToString("0") + "%  Y " + _roiY.ToString("0") +
                "%  宽 " + _roiW.ToString("0") + "%  高 " + _roiH.ToString("0") + "%");
            SetStatusText("识别区域已框选并记住", Color.SeaGreen);
        }

        private void ComputeImageRect(out int dx, out int dy, out int dw, out int dh)
        {
            dx = dy = 0; dw = dh = 0;
            try
            {
                if (_lastFrameSize.Width <= 0 || _pic == null) return;
                int w = Math.Max(1, _pic.ClientSize.Width), h = Math.Max(1, _pic.ClientSize.Height);
                double s = Math.Min((double)w / _lastFrameSize.Width, (double)h / _lastFrameSize.Height);
                dw = Math.Max(1, (int)(_lastFrameSize.Width * s));
                dh = Math.Max(1, (int)(_lastFrameSize.Height * s));
                dx = (w - dw) / 2; dy = (h - dh) / 2;
            }
            catch { }
        }

        private void SetStatusText(string text, Color c)
        {
            try { if (_lblStatus != null) { _lblStatus.Text = text; _lblStatus.ForeColor = c; } } catch { }
        }

        private void Log(string msg)
        {
            try
            {
                _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + msg + "\r\n");
                string[] lines = _log.Lines;
                if (lines.Length > 300)
                {
                    var sb = new StringBuilder();
                    for (int i = lines.Length - 200; i < lines.Length; i++) sb.AppendLine(lines[i]);
                    _log.Text = sb.ToString();
                }
                _log.SelectionStart = _log.TextLength;
                _log.ScrollToCaret();
            }
            catch { }
        }

        // 仅自测：返回窗口日志文本
        internal string TestLog() { try { return _log == null ? "" : _log.Text; } catch { return ""; } }
        internal bool TestCameraOn { get { return _on; } }
        internal void TestFindFocus() { try { FindBestFocus(); } catch { } }
        internal void TestOcr() { try { DoOcrOnce(false); } catch { } }
        internal string TestFrameInfo()
        {
            try { return (_lastFrameSize.Width + "×" + _lastFrameSize.Height); } catch { return ""; }
        }
        // 仅自测：立刻抓一帧并按当前识别区域解码，把结果和图片都写出来
        internal string TestDecodeNow()
        {
            try
            {
                if (_ds == null) return "摄像头未打开";
                using (var b = _ds.Grab())
                {
                    if (b == null) return "抓帧失败";
                    var roi = CurrentRoi(b.Size);
                    var texts = CameraDetect.DecodeRect(b, roi, true, true);
                    string p = Path.Combine(Application.StartupPath, "测试抓帧.png");
                    try { b.Save(p, System.Drawing.Imaging.ImageFormat.Png); } catch { }
                    return "帧 " + b.Width + "×" + b.Height + "  识别区域 " + roi.X + "," + roi.Y + "," + roi.Width + "," + roi.Height +
                           "  解出 " + texts.Count + " 个：" + string.Join(" | ", texts.ToArray());
                }
            }
            catch (Exception ex) { return "ERR " + ex.Message; }
        }
        // 仅自测：把“自己渲染的预览画面”存成 PNG，检查缩放/居中/识别框/镜像是否正确
        internal bool TestSaveDisp(string path)
        {
            try
            {
                if (_disp == null) return false;
                using (var copy = new Bitmap(_disp)) copy.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                return true;
            }
            catch { return false; }
        }

        // OCR：识别画面里的型号文字，并与历史型号库比对后自动填入
        private void DoOcrOnce(bool manual)
        {
            if (_ds == null || !_on) { if (manual) SetStatusText("请先打开摄像头", Color.DarkOrange); return; }
            if (!WinOcr.Available) { if (manual) SetStatusText("这台系统没有可用的自带 OCR（需要 Win10 及以上）", Color.DarkOrange); return; }
            try
            {
                if (manual) SetStatusText("正在识别文字…", Color.DodgerBlue);
                using (var b = _ds.Grab())
                {
                    if (b == null) { if (manual) SetStatusText("抓帧失败", Color.Red); return; }
                    Rectangle roi = CurrentRoi(b.Size);
                    using (var crop = b.Clone(roi, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                    {
                        // 文字放大 2 倍再识别（小字更准）
                        Bitmap ocrIn = crop;
                        bool scaled = false;
                        try
                        {
                            // 整块不放大（放大留给条码下方的小横带去用），这里能省掉数倍 OCR 时间
                            scaled = false;
                        }
                        catch { }
                        // 按用户要求：整块区域不再做文字识别（只用二维码 + 条码）
                        string text = null;
                        if (scaled && ocrIn != null) { try { ocrIn.Dispose(); } catch { } }
                        // 本版本已按需求去掉文字识别（ONNX 也一并移除以缩小体积），这里直接给出明确提示，不再报错
                        if (string.IsNullOrEmpty(text))
                        {
                            Log("本版本未启用文字识别（只用二维码 + 一维码）。要识别标签文字请用「固定型号模式」或让扫码枪补扫。");
                            if (manual) SetStatusText("本版本未启用 OCR（只用二维码/条码识别）", Color.DarkOrange);
                            return;
                        }
                        // ① 用“条码位置 ↔ 旁边印的字”判定字段（多个 SN/MAC：SN=GPON SN、MAC=PON MAC）
                        // ① 每个条码下方印的那行字单独 OCR，用它判定这条码是哪一项
                        var hitsAll = CameraDetect.DecodeHits(b, roi);
                        // 识别区域里没找到条码（比如框太小、只圈住了型号）：自动用整幅再找一次兜底
                        if (hitsAll.Count == 0 && (roi.Width < b.Width || roi.Height < b.Height))
                        {
                            var allHits = CameraDetect.DecodeHits(b, new Rectangle(0, 0, b.Width, b.Height));
                            if (allHits.Count > 0)
                            {
                                Log("识别区域里没有条码，已自动用整幅兜底（建议把黄框框住整个标签，含条码）");
                                hitsAll = allHits;
                            }
                        }
                        string snG = null, snS = null, macP = null, macM = null;
                        string detail2 = "";
                        int ocrCount = 0;
                        foreach (var hh in hitsAll)
                        {
                            if (ocrCount++ >= 6) break;
                            try
                            {
                                // 1D 条码只给出两端点（没有高度），所以按条码宽度估算下方文字带
                                int gap = Math.Max(6, hh.Box.Width / 60);
                                int stripH = Math.Max(60, hh.Box.Width / 6);
                                var strip = new Rectangle(hh.Box.Left - hh.Box.Width / 12, hh.Box.Bottom + gap,
                                                          (int)(hh.Box.Width * 1.25), stripH);
                                if (strip.X < 0) strip.X = 0;
                                if (strip.Y < 0) strip.Y = 0;
                                if (strip.Right > b.Width) strip.Width = b.Width - strip.X;
                                if (strip.Bottom > b.Height) strip.Height = b.Height - strip.Y;
                                if (strip.Width < 20 || strip.Height < 10) continue;
                                using (var sc = b.Clone(strip, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                                using (var big = (sc.Width > 200) ? new Bitmap(sc, new Size(sc.Width * 2, sc.Height * 2)) : new Bitmap(sc))
                                {
                                    string cap = null;   // 按用户要求：不再对条码下方的字做 OCR
                                    // 同一张小行图，两种引擎直接对比（ONNX 用于识别"一行文字"）
                                    try
                                    {
                                        var swO = System.Diagnostics.Stopwatch.StartNew();
                                    string capOnnx = null;
                                        if (!string.IsNullOrEmpty(capOnnx) && capOnnx.Trim().Length >= 4) cap = capOnnx;   // 行识别首选 ONNX
                                        swO.Stop();
                                        Log("  ONNX(" + swO.ElapsedMilliseconds + "ms)：" + (string.IsNullOrEmpty(capOnnx) ? "（无）" : capOnnx) +
                                            "   WindowsOCR：" + (string.IsNullOrEmpty(cap) ? "（无）" : cap));
                                    }
                                    catch { }
                                    string key = WinOcr.NormalizeKey(cap);
                                    detail2 += "[" + hh.Text + " ← " + (cap ?? "?") + "] ";
                                    if (key.Contains("GPONSN")) { if (snG == null) snG = hh.Text; }
                                    else if (key.Contains("PONMAC")) { if (macP == null) macP = hh.Text; }
                                    else if (key.Contains("MAC")) { if (macM == null) macM = hh.Text; }
                                    else if (key.Contains("SN")) { if (snS == null) snS = hh.Text; }
                                }
                            }
                            catch { }
                        }
                        if (!string.IsNullOrEmpty(detail2)) Log("条码与标签文字的对应：" + detail2);
                        string m2, t2, sn2, mac2;
                        sn2 = snG != null ? snG : snS;
                        mac2 = macP != null ? macP : macM;
                        m2 = null; t2 = null;
                        {
                            var codes = CameraDetect.DecodeRect(crop, new Rectangle(0, 0, crop.Width, crop.Height), false, true);
                            string mm, tt, ss, aa;
                            WinOcr.ExtractFields(text, codes, out mm, out tt, out ss, out aa);
                            m2 = mm; t2 = tt;
                            if (sn2 == null) sn2 = ss;
                            if (mac2 == null) mac2 = aa;
                        }
                        // 型号/类型兜底：把标签整块（条码包围盒向左扩一倍、上下各扩半倍）再 OCR 一次
                        if (string.IsNullOrEmpty(m2) && hitsAll.Count > 0)
                        {
                            try
                            {
                                int lx1 = int.MaxValue, ly1 = int.MaxValue, lx2 = 0, ly2 = 0;
                                foreach (var hx in hitsAll)
                                {
                                    if (hx.Box.Left < lx1) lx1 = hx.Box.Left;
                                    if (hx.Box.Top < ly1) ly1 = hx.Box.Top;
                                    if (hx.Box.Right > lx2) lx2 = hx.Box.Right;
                                    if (hx.Box.Bottom > ly2) ly2 = hx.Box.Bottom;
                                }
                                int bw = Math.Max(50, lx2 - lx1), bh = Math.Max(30, ly2 - ly1);
                                var lb = new Rectangle(lx1 - bw, ly1 - bh / 2, bw * 2 + bw / 4, bh * 2);
                                if (lb.X < 0) { lb.Width += lb.X; lb.X = 0; }
                                if (lb.Y < 0) { lb.Height += lb.Y; lb.Y = 0; }
                                if (lb.Right > b.Width) lb.Width = b.Width - lb.X;
                                if (lb.Bottom > b.Height) lb.Height = b.Height - lb.Y;
                                if (lb.Width > 40 && lb.Height > 30)
                                {
                                    using (var lc = b.Clone(lb, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                                    using (var lg = (lc.Width > 300) ? new Bitmap(lc, new Size(lc.Width * 2, lc.Height * 2)) : new Bitmap(lc))
                                    {
                                        string t3 = WinOcr.Recognize(lg);
                                        string mm3, tt3, ss3, aa3;
                                        WinOcr.ExtractFields(t3, codes2ForText(hitsAll), out mm3, out tt3, out ss3, out aa3);
                                        if (!string.IsNullOrEmpty(mm3)) { m2 = mm3; Log("型号（标签整块 OCR）：" + mm3); }
                                        if (!string.IsNullOrEmpty(tt3)) { t2 = tt3; Log("类型（标签整块 OCR）：" + tt3); }
                                    }
                                }
                            }
                            catch { }
                        }
                        // 类型统一成 GPON / EPON；EPON、ADSL 直接报警拦下
                        if (WinOcr.HasBadTech(text)) { Log("检测到 EPON/ADSL 设备，报警并拦下"); _owner.EponAlerted("标签文字"); return; }
                        if (WinOcr.HasGpon(text)) t2 = "GPON";
                        // 不用 OCR 时：按条码顺序定字段——最下面那条是 GPON SN，它上面那条 12 位十六进制是 PON MAC
                        try
                        {
                            if ((string.IsNullOrEmpty(sn2) || string.IsNullOrEmpty(mac2)) && hitsAll.Count > 0)
                            {
                                var sorted = new List<CodeHit>(hitsAll);
                                sorted.Sort(delegate(CodeHit c1, CodeHit c2) { return c1.Box.Top.CompareTo(c2.Box.Top); });
                                for (int si = sorted.Count - 1; si >= 0 && string.IsNullOrEmpty(sn2); si--)
                                {
                                    string tx = (sorted[si].Text ?? "").Trim();
                                    if (tx.Length >= 10 && WinOcr.NormalizeMacText(tx) == null && WinOcr.LooksLikeSn(tx)) sn2 = tx;
                                }
                                for (int mi = sorted.Count - 1; mi >= 0 && string.IsNullOrEmpty(mac2); mi--)
                                    mac2 = WinOcr.NormalizeMacText(sorted[mi].Text);
                                if (!string.IsNullOrEmpty(sn2) || !string.IsNullOrEmpty(mac2))
                                    Log("按条码顺序取字段：SN=" + (sn2 ?? "-") + "  MAC=" + (mac2 ?? "-"));
                            }
                        }
                        catch { }
                        sn2 = WinOcr.CleanSn(sn2);
                        // 用 ONNX 逐行读标签区域（型号/类型那几行），Windows OCR 读得不干净时用它补上
                        try
                        {
                            if (hitsAll.Count > 0 && (string.IsNullOrEmpty(m2) || m2.Length < 4))
                            {
                                int bx1 = int.MaxValue, by1 = int.MaxValue, bx2 = 0, by2 = 0;
                                foreach (var hx in hitsAll)
                                {
                                    if (hx.Box.Left < bx1) bx1 = hx.Box.Left;
                                    if (hx.Box.Top < by1) by1 = hx.Box.Top;
                                    if (hx.Box.Right > bx2) bx2 = hx.Box.Right;
                                    if (hx.Box.Bottom > by2) by2 = hx.Box.Bottom;
                                }
                                int bw = Math.Max(60, bx2 - bx1), bh = Math.Max(40, by2 - by1);
                                var lb = new Rectangle(bx1 - bw * 3 / 2, by1 - bh / 3, bw * 3, bh * 2);
                                if (lb.X < 0) { lb.Width += lb.X; lb.X = 0; }
                                if (lb.Y < 0) { lb.Height += lb.Y; lb.Y = 0; }
                                if (lb.Right > b.Width) lb.Width = b.Width - lb.X;
                                if (lb.Bottom > b.Height) lb.Height = b.Height - lb.Y;
                                int strips = 12, stripH = Math.Max(20, lb.Height / strips);
                                string onnxLines = "";
                                for (int k = 0; k < strips; k++)
                                {
                                    var st = new Rectangle(lb.X, lb.Y + k * stripH, lb.Width, stripH);
                                    if (st.Bottom > b.Height || st.Height < 8) break;
                                    using (var sc2 = b.Clone(st, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
                                    using (var big2 = (sc2.Width > 200) ? new Bitmap(sc2, new Size(sc2.Width * 2, sc2.Height * 2)) : new Bitmap(sc2))
                                    {
                                        string ln2 = PaddleOcrOnnx.Recognize(big2);
                                        if (!string.IsNullOrEmpty(ln2) && ln2.Trim().Length >= 3) onnxLines += ln2 + " ";
                                    }
                                }
                                if (onnxLines.Length > 0)
                                {
                                    Log("ONNX 逐行读到：" + onnxLines);
                                    string mm4, tt4, ss4, aa4;
                                    WinOcr.ExtractFields(onnxLines, null, out mm4, out tt4, out ss4, out aa4);
                                    if (!string.IsNullOrEmpty(mm4) && (string.IsNullOrEmpty(m2) || m2.Length < 4)) m2 = mm4;
                                    if (!string.IsNullOrEmpty(tt4) && string.IsNullOrEmpty(t2)) t2 = tt4;
                                    if (WinOcr.HasGpon(onnxLines)) t2 = "GPON";
                                    if (WinOcr.HasBadTech(onnxLines)) { Log("ONNX 读到 EPON/ADSL，报警拦下"); _owner.EponAlerted("标签文字(ONNX)"); return; }
                                }
                            }
                        }
                        catch { }
                        if (!string.IsNullOrEmpty(m2) || !string.IsNullOrEmpty(sn2) || !string.IsNullOrEmpty(mac2))
                        {
                            Log("按标签文字取到：型号=" + (m2 ?? "-") + "  类型=" + (t2 ?? "-") + "  SN=" + (sn2 ?? "-") + "  MAC=" + (mac2 ?? "-"));
                            _owner.FillFromOcr(m2, t2, sn2, mac2);
                            if (_chkBeep.Checked) Beep(true);
                            return;
                        }
                        string best = WinOcr.BestMatch(text, _owner.ModelCandidates);
                        if (!string.IsNullOrEmpty(best))
                        {
                            _owner.FillModelFromOcr(best);
                            Log("OCR 匹配到已有型号：" + best);
                            if (manual) SetStatusText("OCR 已识别型号：" + best, Color.SeaGreen);
                            if (_chkBeep.Checked) Beep(true);
                        }
                        else
                        {
                            var toks = WinOcr.ExtractTokens(text);
                            string cand = null;
                            foreach (var tk in toks)
                                if (tk.Length >= 5 && char.IsLetter(tk[0]) && !tk.ToUpperInvariant().StartsWith("SN")) { cand = tk; break; }
                            if (cand != null)
                            {
                                _owner.FillModelFromOcr(cand);
                                Log("OCR 取到型号候选：" + cand + "（历史里没有过，已先填上，确认无误后会被记住）");
                                if (manual) SetStatusText("OCR 取到型号：" + cand, Color.SeaGreen);
                            }
                            else
                            {
                                Log("OCR：没匹配到型号（把识别区域框到型号那一行会更准）");
                                if (manual) SetStatusText("OCR 没匹配到型号", Color.DarkOrange);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Log("OCR 失败：" + ex.Message); if (manual) SetStatusText("OCR 失败：" + ex.Message, Color.Red); }
        }

        // 采集样本：存整帧 PNG + 同名字段文本（为训练 YOLO 攒数据）
        // 自动框选：抓一帧，找出条码/二维码，把它们的外框（含下方印字）设成识别区域
        private void AutoFrameRoi()
        {
            if (_ds == null || !_on) { SetStatusText("请先打开摄像头", Color.DarkOrange); return; }
            try
            {
                SetStatusText("正在自动框选…", Color.DodgerBlue);
                Application.DoEvents();
                using (var b = _ds.Grab())
                {
                    if (b == null) { SetStatusText("抓帧失败", Color.Red); return; }
                    var hits = CameraDetect.DecodeHits(b, new Rectangle(0, 0, b.Width, b.Height));
                    if (hits.Count == 0) { SetStatusText("没找到条码/二维码，先把设备放好再点", Color.DarkOrange); Log("自动框选：整幅没找到条码"); return; }
                    int x1 = int.MaxValue, y1 = int.MaxValue, x2 = 0, y2 = 0;
                    foreach (var h in hits)
                    {
                        if (h.Box.Left < x1) x1 = h.Box.Left;
                        if (h.Box.Top < y1) y1 = h.Box.Top;
                        if (h.Box.Right > x2) x2 = h.Box.Right;
                        if (h.Box.Bottom > y2) y2 = h.Box.Bottom;
                    }
                    int bw = Math.Max(40, x2 - x1), bh = Math.Max(30, y2 - y1);
                    int left = Math.Max(0, x1 - bw / 4), top = Math.Max(0, y1 - bh / 2);
                    int right = Math.Min(b.Width, x2 + bw / 4), bottom = Math.Min(b.Height, y2 + bh);   // 下方留出印字
                    _roiX = left * 100.0 / b.Width;
                    _roiY = top * 100.0 / b.Height;
                    _roiW = (right - left) * 100.0 / b.Width;
                    _roiH = (bottom - top) * 100.0 / b.Height;
                    if (_roiW < 15) { _roiW = 15; _roiX = Math.Max(0, _roiX - 5); }
                    if (_roiH < 15) { _roiH = 15; _roiY = Math.Max(0, _roiY - 5); }
                    _loading = true;
                    if (_cmbRoi != null) _cmbRoi.SelectedIndex = 0;   // 切到“自定义”
                    _loading = false;
                    SaveCfg();
                    Log("自动框选完成：X " + _roiX.ToString("0") + "%  Y " + _roiY.ToString("0") + "%  宽 " + _roiW.ToString("0") +
                        "%  高 " + _roiH.ToString("0") + "%（识别区域越小越快）");
                    SetStatusText("已自动框选 " + _roiW.ToString("0") + "%×" + _roiH.ToString("0") + "%，识别会明显变快", Color.SeaGreen);
                }
            }
            catch (Exception ex) { Log("自动框选失败：" + ex.Message); }
        }

        private void SaveSample()
        {
            if (_ds == null || !_on) { SetStatusText("请先打开摄像头", Color.DarkOrange); return; }
            try
            {
                using (var b = _ds.Grab())
                {
                    if (b == null) { SetStatusText("抓帧失败", Color.Red); return; }
                    string dir = Path.Combine(Application.StartupPath, "数据", "样本");
                    Directory.CreateDirectory(dir);
                    string name = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    b.Save(Path.Combine(dir, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    string fields = _owner != null ? _owner.CurrentFieldsText() : "";
                    File.WriteAllText(Path.Combine(dir, name + ".txt"),
                        fields + Environment.NewLine + "自动区域=" + _autoRoi + Environment.NewLine, new UTF8Encoding(false));
                    Log("已采集样本：" + name + ".png  " + fields);
                    SetStatusText("样本已保存到 数据\\样本\\", Color.SeaGreen);
                }
            }
            catch (Exception ex) { Log("采集样本失败：" + ex.Message); }
        }

        // 保存当前画面（带识别区域框），便于排查识别问题
        private void SaveSnapshot()
        {
            if (_disp == null) { MessageBox.Show("还没有画面，请先打开摄像头。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "PNG 图片|*.png";
                dlg.FileName = "摄像头画面_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    using (var copy = new Bitmap(_disp))
                    {
                        using (var g = Graphics.FromImage(copy))
                        {
                            Rectangle r = CurrentRoi(copy.Size);
                            using (var pen = new Pen(Color.FromArgb(255, 196, 0), Math.Max(2, copy.Width / 400)))
                                g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
                        }
                        copy.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Log("已保存画面：" + dlg.FileName);
                }
                catch (Exception ex) { MessageBox.Show("保存失败：" + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
        }
    }
}
