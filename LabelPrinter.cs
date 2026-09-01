using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
                var bmp = new Bitmap(w, h);
                // fill background white then draw
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                }
                f.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
                f.Dispose();
            }
            catch { }
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
        public const string DefaultUpdateUrl = "https://github.com/chuanyin888/label-printer-software";
        public string UpdateUrl = DefaultUpdateUrl;
        public string UpdateToken = "";
        public int BarcodeWidth = 2;   // 0=细  1=中  2=粗
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

        public List<DeviceRecord> Load()
        {
            var list = new List<DeviceRecord>();
            if (!File.Exists(_path)) return list;
            try
            {
                var lines = File.ReadAllLines(_path, Encoding.UTF8);
                for (int i = 1; i < lines.Length; i++)
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
            }
            catch { }
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
        public const string AppVersion = "1.2.8";

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

        public static bool FindNewVersion(string url, string token, string current, out string newVersion, out string downloadUrl, out string notes)
        {
            newVersion = ""; downloadUrl = ""; notes = "";
            if (string.IsNullOrWhiteSpace(url)) return false;
            try
            {
                string json;
                if (url.Contains("github.com"))
                {
                    json = GetGitHubReleaseJson(url, token);
                    var obj = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
                    if (obj == null) return false;
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
                    if (obj == null) return false;
                    newVersion = GetS(obj, "version");
                    downloadUrl = GetS(obj, "download");
                    if (downloadUrl == "") downloadUrl = GetS(obj, "url");
                    notes = GetS(obj, "notes");
                }
                return !string.IsNullOrEmpty(newVersion) && !string.IsNullOrEmpty(downloadUrl) &&
                       CompareVersion(newVersion, current) > 0;
            }
            catch { return false; }
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

    internal class MainForm : Form
    {
        private AppSettings _settings = new AppSettings();
        private string _dataDir;
        private string _historyPath;
        private string _settingsPath;
        private HistoryStore _history;
        private readonly List<DeviceRecord> _records = new List<DeviceRecord>();

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
        private DataGridView grid;
        private TextBox txtSearch;
        private Label lblCount;
        private Timer _refreshTimer;
        private string _searchText = "";

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
            ClientSize = new Size(1000, 680);
            MinimumSize = new Size(900, 660);
            Font = new Font("Microsoft YaHei", 9F);
            StartPosition = FormStartPosition.CenterScreen;

            _dataDir = GetDataDir();
            _historyPath = Path.Combine(_dataDir, "历史记录_" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv");
            _settingsPath = Path.Combine(_dataDir, "设置.ini");
            _settings.Path = _settingsPath;
            _settings.Load();
            // 首次：若今天的日期文件不存在但存在旧的单文件，则把旧文件更名为今天的日期文件（一次性迁移，避免重复并入每一天）
            if (!File.Exists(_historyPath))
            {
                string legacy = Path.Combine(_dataDir, "历史记录.csv");
                if (File.Exists(legacy))
                    try { File.Move(legacy, _historyPath); } catch { }
            }
            _history = new HistoryStore(_historyPath);
            _records.AddRange(_history.Load());

            BuildUi2();
            RefreshPrinters(true);
            LoadHistoryGrid();
            ApplySettingsToUi();
            ApplyChecksToLayout();
            ApplyBarcodeWidthToLayout();
            UpdatePreview();
            UpdateTodayCount();
            _refreshTimer = new Timer { Interval = 4000 };
            _refreshTimer.Tick += (s, e) => { _refreshTimer.Stop(); RefreshPrinters(false); };
            Shown += (s, e) => { txtScan.Focus(); };
            Shown += (s, e) => StartAutoCheck();
            FormClosing += (s, e) => SaveAll();
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
                var has = Updater.FindNewVersion(_settings.UpdateUrl, _settings.UpdateToken, Updater.AppVersion, out nv, out dl, out notes);
                if (!has)
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
                    var has = Updater.FindNewVersion(_settings.UpdateUrl, _settings.UpdateToken, Updater.AppVersion, out nv, out dl, out notes);
                    if (has && !IsDisposed)
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
            var gLeft = new GroupBox { Text = "扫码录入", Location = new Point(8, 8), Size = new Size(250, 478) };
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

            var btnPrint = new Button { Text = "打印当前设备", Location = new Point(10, 384), Size = new Size(230, 34), Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold) };
            btnPrint.Click += (s, e) => SaveAndPrint(false);
            gLeft.Controls.Add(btnPrint);

            var btnSave = new Button { Text = "仅保存", Location = new Point(10, 424), Size = new Size(110, 28) };
            btnSave.Click += (s, e) => SaveCurrent(false);
            gLeft.Controls.Add(btnSave);

            var btnClear = new Button { Text = "清空输入", Location = new Point(128, 424), Size = new Size(112, 28) };
            btnClear.Click += (s, e) => { ClearInputs(); };
            gLeft.Controls.Add(btnClear);

            gLeft.Controls.Add(new Label { Text = "提示：历史记录自动保存，随时可重印。", Location = new Point(10, 456), Size = new Size(230, 16), ForeColor = Color.Gray });

            // ---------- Middle: preview ----------
            var gPrev = new GroupBox { Text = "标签预览（鼠标拖动文字/条码可调整位置）", Location = new Point(266, 8), Size = new Size(458, 514), Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right };
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
            var gPrinter = new GroupBox { Text = "打印机（可添加网络/局域网打印机）", Location = new Point(732, 8), Size = new Size(260, 136), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            Controls.Add(gPrinter);
            gPrinter.Controls.Add(new Label { Text = "选择打印机：", Location = new Point(8, 18) });
            cmbPrinter = new ComboBox { Location = new Point(8, 38), Size = new Size(232, 26), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbPrinter.SelectedIndexChanged += (s, e) => { if (cmbPrinter.SelectedItem != null) _settings.Printer = cmbPrinter.SelectedItem.ToString(); };
            gPrinter.Controls.Add(cmbPrinter);
            var btnRefresh = new Button { Text = "刷新", Location = new Point(8, 72), Size = new Size(66, 26) };
            btnRefresh.Click += (s, e) => RefreshPrinters(false);
            gPrinter.Controls.Add(btnRefresh);
            var btnAddNet = new Button { Text = "添加网络打印机…", Location = new Point(80, 72), Size = new Size(170, 26) };
            btnAddNet.Click += (s, e) => AddNetworkPrinter();
            gPrinter.Controls.Add(btnAddNet);
            txtNetPrinter = new TextBox { Location = new Point(8, 106), Size = new Size(140, 24) };
            gPrinter.Controls.Add(txtNetPrinter);
            var btnConnect = new Button { Text = "连接", Location = new Point(154, 104), Size = new Size(96, 28) };
            btnConnect.Click += (s, e) => ConnectNetworkPrinter();
            gPrinter.Controls.Add(btnConnect);

            var gSize = new GroupBox { Text = "标签规格（宽/高，单位 mm）", Location = new Point(732, 146), Size = new Size(260, 72), Anchor = AnchorStyles.Top | AnchorStyles.Right };
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

            var gContent = new GroupBox { Text = "打印内容（可多选）", Location = new Point(732, 224), Size = new Size(260, 98), Anchor = AnchorStyles.Top | AnchorStyles.Right };
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

            var gProp = new GroupBox { Text = "排版属性（选中预览中的元素后编辑）", Location = new Point(732, 328), Size = new Size(260, 198), Anchor = AnchorStyles.Top | AnchorStyles.Right };
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
            btnResetLayout = new Button { Text = "恢复默认排版", Location = new Point(8, 166), Size = new Size(150, 26) };
            btnResetLayout.Click += (s, e) => { _settings.Layout = LayoutItem.DefaultLayout(_settings.LabelWidthMm, _settings.LabelHeightMm); ApplyChecksToLayout(); UpdatePreview(); SetStatus("已恢复默认排版", Color.Gray); };
            gProp.Controls.Add(btnResetLayout);

            // ---------- Bottom: history ----------
            var gHist = new GroupBox { Text = "历史记录（自动保存，双击可重印）", Location = new Point(8, 530), Size = new Size(984, 140), Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right | AnchorStyles.Bottom };
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

            var btnReprint = new Button { Text = "重印选中", Location = new Point(802, 22), Size = new Size(170, 24), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnReprint.Click += (s, e) => { var rec = SelectedRecord(); if (rec != null) Reprint(rec); };
            gHist.Controls.Add(btnReprint);
            var btnDel = new Button { Text = "删除选中", Location = new Point(802, 50), Size = new Size(170, 24), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnDel.Click += (s, e) => DeleteSelected();
            gHist.Controls.Add(btnDel);
            var btnClearAll = new Button { Text = "清空全部", Location = new Point(802, 78), Size = new Size(170, 24), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnClearAll.Click += (s, e) => { if (MessageBox.Show("确定清空全部历史记录？此操作不可恢复。", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) { _records.Clear(); LoadHistoryGrid(); SaveAll(); } };
            gHist.Controls.Add(btnClearAll);
            var btnFolder = new Button { Text = "打开数据文件夹", Location = new Point(802, 106), Size = new Size(170, 24), Anchor = AnchorStyles.Top | AnchorStyles.Right };
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
            gHist.Height = 170;

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

            var gLeft = BuildScanGroup();
            gLeft.Dock = DockStyle.Fill;
            gLeft.Margin = new Padding(0, 0, 8, 0);

            var gPrev = BuildPreviewGroup();
            gPrev.Dock = DockStyle.Fill;
            gPrev.Margin = new Padding(0, 0, 8, 0);

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
            var gp = BuildPrinterGroup(); gp.Dock = DockStyle.Top; gp.Margin = new Padding(0, 0, 0, 10);
            var gs = BuildSizeGroup(); gs.Dock = DockStyle.Top; gs.Margin = new Padding(0, 0, 0, 10);
            var gc = BuildContentGroup(); gc.Dock = DockStyle.Top; gc.Margin = new Padding(0, 0, 0, 10);
            var gprop = BuildPropGroup(); gprop.Dock = DockStyle.Top; gprop.Margin = new Padding(0, 0, 0, 4);
            rightCol.Controls.Add(gp, 0, 0);
            rightCol.Controls.Add(gs, 0, 1);
            rightCol.Controls.Add(gc, 0, 2);
            rightCol.Controls.Add(gprop, 0, 3);
            rightScroll.Controls.Add(rightCol);

            main.Controls.Add(gLeft, 0, 0);
            main.Controls.Add(gPrev, 1, 0);
            main.Controls.Add(rightScroll, 2, 0);

            Controls.Add(main);
            Controls.Add(gHist);
            ResumeLayout();
        }

        private GroupBox BuildScanGroup()
        {
            var g = new GroupBox { Text = "扫码录入", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10), Margin = Padding.Empty };
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 14, Padding = new Padding(0), Margin = Padding.Empty };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 14; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var modeRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
            modeRow.Controls.Add(new Label { Text = "录入方式：", AutoSize = true, Margin = new Padding(0, 4, 6, 0) });
            var cmbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Margin = new Padding(0, 2, 0, 6) };
            cmbMode.Items.AddRange(new object[] { "二维码模式", "固定型号模式" });
            cmbMode.SelectedIndexChanged += (s, e) => SetMode(cmbMode.SelectedIndex == 1);
            modeRow.Controls.Add(cmbMode);
            t.Controls.Add(modeRow, 0, 0); t.SetColumnSpan(modeRow, 2);

            lblStatus = new Label { Text = "请扫描设备二维码…", AutoSize = true, Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold), ForeColor = Color.DodgerBlue, Margin = new Padding(0, 2, 0, 8) };
            t.Controls.Add(lblStatus, 0, 1); t.SetColumnSpan(lblStatus, 2);

            var lblScanHint = new Label { Text = "扫码输入框（扫完自动识别，无需点击）", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
            t.Controls.Add(lblScanHint, 0, 2); t.SetColumnSpan(lblScanHint, 2);

            txtScan = new TextBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 12F), Margin = new Padding(0, 2, 0, 8) };
            txtScan.KeyDown += TxtScan_KeyDown;
            t.Controls.Add(txtScan, 0, 3); t.SetColumnSpan(txtScan, 2);

            lblStep = new Label { Text = "第 1 步：扫描二维码\n第 2 步：扫描 MAC 条码", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(0, 0, 0, 6) };
            t.Controls.Add(lblStep, 0, 4); t.SetColumnSpan(lblStep, 2);

            var lblHeader = new Label { Text = "当前设备数据（可手动修改）", AutoSize = true, Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold), Margin = new Padding(0, 4, 0, 6) };
            t.Controls.Add(lblHeader, 0, 5); t.SetColumnSpan(lblHeader, 2);

            AddFieldRow(t, "型号：", 6, out txtModel);
            AddFieldRow(t, "类型：", 7, out txtType);
            AddFieldRow(t, "SN：", 8, out txtSN);
            AddFieldRow(t, "MAC：", 9, out txtMAC);
            foreach (var tb in new[] { txtModel, txtType, txtSN, txtMAC })
                tb.TextChanged += (s, e) => UpdatePreview();

            chkAuto = new CheckBox { Text = "扫描完成后自动打印并保存", AutoSize = true, Checked = _settings.AutoPrint, Margin = new Padding(0, 2, 0, 8) };
            chkAuto.CheckedChanged += (s, e) => _settings.AutoPrint = chkAuto.Checked;
            t.Controls.Add(chkAuto, 0, 10); t.SetColumnSpan(chkAuto, 2);

            var btnPrint = new Button { Text = "打印当前设备", Dock = DockStyle.Fill, Height = 36, Font = new Font("Microsoft YaHei", 10F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) };
            btnPrint.Click += (s, e) => SaveAndPrint(false);
            t.Controls.Add(btnPrint, 0, 11); t.SetColumnSpan(btnPrint, 2);

            var btnSave = new Button { Text = "仅保存", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0) };
            btnSave.Click += (s, e) => SaveCurrent(false);
            t.Controls.Add(btnSave, 0, 12);
            var btnClear = new Button { Text = "清空输入", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 0) };
            btnClear.Click += (s, e) => ClearInputs();
            t.Controls.Add(btnClear, 1, 12);

            var lblHintBottom = new Label { Text = "提示：历史记录自动保存，随时可重印。", AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(0, 6, 0, 0) };
            t.Controls.Add(lblHintBottom, 0, 13); t.SetColumnSpan(lblHintBottom, 2);

            g.Controls.Add(t);
            cmbMode.SelectedIndex = 0;
            return g;
        }

        private void AddFieldRow(TableLayoutPanel t, string label, int row, out TextBox box)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) }, 0, row);
            box = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 8) };
            t.Controls.Add(box, 1, row);
        }

        private GroupBox BuildPreviewGroup()
        {
            var g = new GroupBox { Text = "标签预览（鼠标拖动文字/条码可调整位置）", Dock = DockStyle.Fill, Padding = new Padding(8), Margin = Padding.Empty };
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
            var g = new GroupBox { Text = "打印机（可添加网络/局域网打印机）", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10), Margin = Padding.Empty };
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 4, Padding = new Padding(0), Margin = Padding.Empty };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            for (int i = 0; i < 4; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var lsel = new Label { Text = "选择打印机：", AutoSize = true, Margin = new Padding(0, 2, 0, 6) };
            t.Controls.Add(lsel, 0, 0); t.SetColumnSpan(lsel, 2);

            cmbPrinter = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 8) };
            cmbPrinter.SelectedIndexChanged += (s, e) => { if (cmbPrinter.SelectedItem != null) _settings.Printer = cmbPrinter.SelectedItem.ToString(); };
            t.Controls.Add(cmbPrinter, 0, 1); t.SetColumnSpan(cmbPrinter, 2);

            var btnAddNet = new Button { Text = "添加网络打印机…", Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            btnAddNet.Click += (s, e) => AddNetworkPrinter();
            t.Controls.Add(btnAddNet, 0, 2); t.SetColumnSpan(btnAddNet, 2);

            txtNetPrinter = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };
            t.Controls.Add(txtNetPrinter, 0, 3);
            var btnCol = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty, Padding = Padding.Empty };
            var btnRefresh = new Button { Text = "刷新", Width = 78, Height = 28, Margin = new Padding(0, 0, 0, 4) };
            btnRefresh.Click += (s, e) => RefreshPrinters(false);
            var btnConnect = new Button { Text = "连接", Width = 78, Height = 28, Margin = Padding.Empty };
            btnConnect.Click += (s, e) => ConnectNetworkPrinter();
            btnCol.Controls.Add(btnRefresh);
            btnCol.Controls.Add(btnConnect);
            t.Controls.Add(btnCol, 1, 3);

            g.Controls.Add(t);
            return g;
        }

        private GroupBox BuildSizeGroup()
        {
            var g = new GroupBox { Text = "标签规格", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10), Margin = Padding.Empty };
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, RowCount = 3, Padding = new Padding(0), Margin = Padding.Empty };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            t.Controls.Add(new Label { Text = "宽", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 10) }, 0, 0);
            numW = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 5, Maximum = 200, DecimalPlaces = 1, Increment = 0.5m, Margin = new Padding(0, 6, 10, 10) };
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
            var g = new GroupBox { Text = "打印内容（可多选）", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10), Margin = Padding.Empty };
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
            var g = new GroupBox { Text = "排版属性（选中预览中的元素后编辑）", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(10), Margin = Padding.Empty };
            var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 8, Padding = new Padding(0), Margin = Padding.Empty };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            for (int i = 0; i < 8; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            lblSel = new Label { Text = "未选择元素", AutoSize = true, Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) };
            t.Controls.Add(lblSel, 0, 0); t.SetColumnSpan(lblSel, 2);

            t.Controls.Add(new Label { Text = "字体大小(pt)", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 8) }, 0, 1);
            numFont = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 4, Maximum = 40, DecimalPlaces = 1, Increment = 0.5m, Margin = new Padding(0, 4, 0, 8) };
            t.Controls.Add(numFont, 1, 1);

            t.Controls.Add(new Label { Text = "水平位置(mm)", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 8) }, 0, 2);
            numX = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = 200, DecimalPlaces = 1, Increment = 0.1m, Margin = new Padding(0, 4, 0, 8) };
            t.Controls.Add(numX, 1, 2);

            t.Controls.Add(new Label { Text = "垂直位置(mm)", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 8) }, 0, 3);
            numY = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 0, Maximum = 300, DecimalPlaces = 1, Increment = 0.1m, Margin = new Padding(0, 4, 0, 8) };
            t.Controls.Add(numY, 1, 3);

            t.Controls.Add(new Label { Text = "条码高度(mm)", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 8) }, 0, 4);
            numBarcodeH = new NumericUpDown { Dock = DockStyle.Fill, Minimum = 5, Maximum = 60, DecimalPlaces = 1, Increment = 0.5m, Margin = new Padding(0, 4, 0, 8) };
            t.Controls.Add(numBarcodeH, 1, 4);

            chkItemVisible = new CheckBox { Text = "在标签上显示此元素", AutoSize = true, Margin = new Padding(0, 4, 0, 8) };
            t.Controls.Add(chkItemVisible, 0, 5); t.SetColumnSpan(chkItemVisible, 2);

            var chkMoveAll = new CheckBox { Text = "整体移动（一起移动所有元素）", AutoSize = true, Margin = new Padding(0, 2, 0, 6) };
            chkMoveAll.CheckedChanged += (s, e) => { _moveAll = chkMoveAll.Checked; if (!_moveAll) _dragging = false; };
            t.Controls.Add(chkMoveAll, 0, 6); t.SetColumnSpan(chkMoveAll, 2);

            btnResetLayout = new Button { Text = "恢复默认排版", Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 2) };
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

        private GroupBox BuildHistoryGroup()
        {
            var g = new GroupBox { Text = "历史记录（自动保存，双击可重印）", Dock = DockStyle.Fill, Padding = new Padding(8), Margin = Padding.Empty };
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
            var btnReprint = new Button { Text = "重印选中", Width = 170, Height = 24, Margin = new Padding(0, 0, 0, 2) };
            btnReprint.Click += (s, e) => { var rec = SelectedRecord(); if (rec != null) Reprint(rec); };
            var btnDel = new Button { Text = "删除选中", Width = 170, Height = 24, Margin = new Padding(0, 0, 0, 2) };
            btnDel.Click += (s, e) => DeleteSelected();
            var btnClearAll = new Button { Text = "清空全部", Width = 170, Height = 24, Margin = new Padding(0, 0, 0, 2) };
            btnClearAll.Click += (s, e) => { if (MessageBox.Show("确定清空全部历史记录？此操作不可恢复。", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) { _records.Clear(); LoadHistoryGrid(); SaveAll(); } };
            var btnFolder = new Button { Text = "打开数据文件夹", Width = 170, Height = 24, Margin = new Padding(0, 0, 0, 2) };
            btnFolder.Click += (s, e) => { try { System.Diagnostics.Process.Start("explorer.exe", _dataDir); } catch { } };
            var btnUpdate = new Button { Text = "检查更新", Width = 170, Height = 24, Margin = Padding.Empty };
            btnUpdate.Click += (s, e) => CheckUpdate(true);
            btnCol.Controls.Add(btnReprint);
            btnCol.Controls.Add(btnDel);
            btnCol.Controls.Add(btnClearAll);
            btnCol.Controls.Add(btnFolder);
            btnCol.Controls.Add(btnUpdate);
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

        private void TxtScan_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            string s = txtScan.Text.Trim();
            if (string.IsNullOrEmpty(s)) return;
            txtScan.Clear();

            if (_fixedModel)
            {
                if (_scanState == ScanState.AwaitSN)
                {
                    if (QRParser.NormalizeMac(s) != null)
                    {
                        SetStatus("这是 MAC，请先扫 SN 条码", Color.Red);
                        return;
                    }
                    txtSN.Text = s;
                    _scanState = ScanState.AwaitMAC;
                    SetStatus("请扫描 MAC 条码…", Color.DodgerBlue);
                    txtScan.Focus();
                    return;
                }
                string macf = QRParser.NormalizeMac(s);
                if (macf == null)
                {
                    SetStatus("MAC 格式不正确（应为 12 位字母数字），请重扫", Color.Red);
                    return;
                }
                txtMAC.Text = macf;
                _scanState = ScanState.AwaitSN;
                if (chkAuto.Checked)
                {
                    SaveAndPrint(true);
                }
                else
                {
                    SetStatus("数据已录入，点击“打印当前设备”或“仅保存”", Color.DarkOrange);
                    txtScan.Focus();
                }
                return;
            }

            if (_scanState == ScanState.AwaitQR)
            {
                var p = QRParser.Parse(s);
                if (p == null || (string.IsNullOrEmpty(p.Type) && string.IsNullOrEmpty(p.Model) && string.IsNullOrEmpty(p.SN)))
                {
                    SetStatus("未识别出二维码内容，请重新扫描", Color.Red);
                    return;
                }
                _lastRawQR = s;
                txtModel.Text = p.Type;
                txtType.Text = p.Model;
                txtSN.Text = p.SN;
                _scanState = ScanState.AwaitMAC;
                SetStatus("已识别二维码，请扫描 MAC 条码…", Color.DodgerBlue);
                txtScan.Focus();
                return;
            }

            // AwaitMAC
            string mac = QRParser.NormalizeMac(s);
            if (mac == null)
            {
                SetStatus("MAC 格式不正确（应为 12 位字母数字），请重扫", Color.Red);
                return;
            }
            txtMAC.Text = mac;
            if (chkAuto.Checked)
            {
                SaveAndPrint(true);
            }
            else
            {
                _scanState = ScanState.AwaitQR;
                SetStatus("数据已录入，点击“打印当前设备”或“仅保存”", Color.DarkOrange);
                txtScan.Focus();
            }
        }

        private void SaveAndPrint(bool clearAfter)
        {
            var rec = CurrentRecord();
            if (string.IsNullOrWhiteSpace(rec.SN) || string.IsNullOrWhiteSpace(rec.MAC))
            {
                MessageBox.Show("SN 和 MAC 不能为空，无法打印。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (cmbPrinter.SelectedItem == null)
            {
                MessageBox.Show("没有可用打印机，请先在右侧选择打印机。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<string> warnings;
            var bmp = LabelRenderer.Render(rec, _settings.LabelWidthMm, _settings.LabelHeightMm, _settings.Dpi, _settings.Layout, out warnings, false);
            bool printed = PrintBitmap(rec, cmbPrinter.Text);
            bmp.Dispose();
            if (!printed) return;

            rec.PrintTime = DateTime.Now;
            bool dup = AddRecord(rec);
            _scanState = _fixedModel ? ScanState.AwaitSN : ScanState.AwaitQR;
            if (clearAfter)
            {
                ClearInputs();
                SetStatus(dup ? "已打印并保存（注意：该设备之前已录入过）" : "已打印并保存，等待下一台…", dup ? Color.Red : Color.SeaGreen);
                txtScan.Focus();
            }
            else
            {
                SetStatus(dup ? "已打印并保存（注意：该设备之前已录入过）" : "已打印并保存", dup ? Color.Red : Color.SeaGreen);
            }
            UpdateTodayCount();
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
            _scanState = ScanState.AwaitQR;
            if (clearAfter) ClearInputs();
            SetStatus(dup ? "已保存（注意：该设备之前已录入过）" : "已保存到历史记录", dup ? Color.Red : Color.SeaGreen);
        }

        private void Reprint(DeviceRecord rec)
        {
            if (cmbPrinter.SelectedItem == null)
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
            SetStatus("已重印：" + rec.SN, Color.SeaGreen);
        }

        private bool PrintBitmap(DeviceRecord rec, string printerName)
        {
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
                MessageBox.Show("打印失败：" + ex.Message + "\n请检查打印机是否开启、驱动是否正确、标签纸尺寸是否已设为 " + _settings.LabelWidthMm.ToString("0.#") + "×" + _settings.LabelHeightMm.ToString("0.#") + "mm。", "打印错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            EnsureCurrentDayFile();
            bool dup = !string.IsNullOrEmpty(rec.SN) && !string.IsNullOrEmpty(rec.MAC) &&
                       _records.Any(r => r.SN == rec.SN && r.MAC == rec.MAC);
            _records.Insert(0, rec);
            SaveAll();
            LoadHistoryGrid();
            return dup;
        }

        private void EnsureCurrentDayFile()
        {
            string todayPath = Path.Combine(_dataDir, "历史记录_" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv");
            if (string.Equals(todayPath, _historyPath, StringComparison.OrdinalIgnoreCase)) return;
            _historyPath = todayPath;
            _history = new HistoryStore(_historyPath);
            _records.Clear();
            _records.AddRange(_history.Load());
            LoadHistoryGrid();
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
            _records.Remove(rec);
            SaveAll();
            LoadHistoryGrid();
        }

        private void LoadHistoryGrid()
        {
            grid.SuspendLayout();
            grid.Rows.Clear();
            List<DeviceRecord> list = _records;
            if (!string.IsNullOrEmpty(_searchText))
                list = _records.Where(r =>
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
                lblCount.Text = string.IsNullOrEmpty(_searchText)
                    ? "共 " + _records.Count + " 条记录"
                    : "共 " + _records.Count + " 条记录（匹配 " + list.Count + " 条）";
        }

        private void UpdateTodayCount()
        {
            int n = _records.Count(r => r.PrintTime.HasValue && r.PrintTime.Value.Date == DateTime.Today);
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
            _lastRawQR = "";
            _scanState = _fixedModel ? ScanState.AwaitSN : ScanState.AwaitQR;
            SetStatus(_fixedModel ? "请扫描 SN 条码…" : "请扫描设备二维码…", Color.DodgerBlue);
            txtScan.Focus();
            UpdatePreview();
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
            UpdatePreview();
        }

        private void SetStatus(string text, Color c)
        {
            if (lblStatus == null) return;
            lblStatus.Text = text;
            lblStatus.ForeColor = c;
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
            _history.Save(_records);
        }
    }
}
