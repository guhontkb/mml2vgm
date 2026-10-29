using Corex64;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Collections;
using System.Runtime.CompilerServices;

namespace mml2vgmIDEx64
{
    public static class Common
    {
        private const string DarkModeExplorerTheme = "DarkMode_Explorer";
        private const string LightModeExplorerTheme = "Explorer";
        private const long ObjectIdVScroll = unchecked((long)0xFFFFFFFB);
        private const long ObjectIdHScroll = unchecked((long)0xFFFFFFFA);
        private const uint StateSystemInvisible = 0x00008000;
        private static readonly ConditionalWeakTable<Control, ScrollBarThemeState> ScrollBarThemeStates = new();

        public enum ScrollBarThemeMode
        {
            Dark = 0,
            Standard = 1,
            Custom = 2
        }

        private sealed class ScrollBarThemeState
        {
            public string ThemeName { get; set; } = DarkModeExplorerTheme;
            public bool UseCustomColors { get; set; } = false;
            public Color CustomThumbColor { get; set; } = Color.FromArgb(100, 140, 220);
            public Color CustomTrackColor { get; set; } = Color.FromArgb(45, 60, 95);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SCROLLBARINFO
        {
            public int cbSize;
            public RECT rcScrollBar;
            public int dxyLineButton;
            public int xyThumbTop;
            public int xyThumbBottom;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public uint[] rgstate;
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        [DllImport("user32.dll")]
        private static extern bool GetScrollBarInfo(IntPtr hwnd, long idObject, ref SCROLLBARINFO psbi);

        public const int DEV_WaveOut = 0;
        public const int DEV_DirectSound = 1;
        public const int DEV_WasapiOut = 2;
        public const int DEV_AsioOut = 3;
        public const int DEV_SPPCM = 4;
        public const int DEV_Null = 5;

        public static Int32 SampleRate = 44100;//44100;
        public static Int32 DataSequenceSampleRate = 44100;//44100;

        public static string settingFilePath = "";
        public static string playingFilePath = "";

        public static void SetDoubleBuffered(Control control)
        {
            control.GetType().InvokeMember(
               "DoubleBuffered",
               BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
               null,
               control,
               new object[] { true });
        }

        public static void ApplyDarkScrollBarTheme(Control root)
        {
            ApplyScrollBarTheme(root, true);
        }

        public static void ApplyScrollBarTheme(Control root, bool useDarkTheme)
        {
            ScrollBarThemeMode mode = useDarkTheme ? ScrollBarThemeMode.Dark : ScrollBarThemeMode.Standard;
            ApplyScrollBarTheme(root, mode, Color.Empty, Color.Empty);
        }

        public static void ApplyScrollBarTheme(Control root, ScrollBarThemeMode mode, Color customThumbColor, Color customTrackColor)
        {
            if (!OperatingSystem.IsWindows() || root == null) return;
            bool useCustomColors = mode == ScrollBarThemeMode.Custom;
            string themeName = mode == ScrollBarThemeMode.Dark ? DarkModeExplorerTheme : LightModeExplorerTheme;
            Color thumbColor = useCustomColors && customThumbColor != Color.Empty
                ? customThumbColor
                : Color.FromArgb(100, 140, 220);
            Color trackColor = useCustomColors && customTrackColor != Color.Empty
                ? customTrackColor
                : Color.FromArgb(45, 60, 95);

            ApplyScrollBarThemeCore(root, themeName, useCustomColors, thumbColor, trackColor);
        }

        private static void ApplyScrollBarThemeCore(Control control, string themeName, bool useCustomColors, Color customThumbColor, Color customTrackColor)
        {
            ScrollBarThemeState state = EnsureScrollBarThemeState(control);
            state.ThemeName = themeName;
            state.UseCustomColors = useCustomColors;
            state.CustomThumbColor = customThumbColor;
            state.CustomTrackColor = customTrackColor;

            if (RequiresDarkScrollBarTheme(control))
            {
                control.HandleCreated -= DarkScrollBarTarget_HandleCreated;
                control.HandleCreated += DarkScrollBarTarget_HandleCreated;
                control.Layout -= DarkScrollBarTarget_Layout;
                control.Layout += DarkScrollBarTarget_Layout;
                control.Paint -= DarkScrollBarTarget_Paint;
                control.Paint += DarkScrollBarTarget_Paint;
                control.Resize -= DarkScrollBarTarget_Resize;
                control.Resize += DarkScrollBarTarget_Resize;
                control.MouseWheel -= DarkScrollBarTarget_MouseWheel;
                control.MouseWheel += DarkScrollBarTarget_MouseWheel;
                ApplyScrollBarThemeToHandle(control);
            }

            control.ControlAdded -= DarkScrollBarTarget_ControlAdded;
            control.ControlAdded += DarkScrollBarTarget_ControlAdded;

            foreach (Control child in control.Controls)
            {
                ApplyScrollBarThemeCore(child, themeName, useCustomColors, customThumbColor, customTrackColor);
            }
        }

        private static bool RequiresDarkScrollBarTheme(Control control)
        {
            bool isScrollableTextBox = control is TextBox textBox
                && textBox.Multiline
                && textBox.ScrollBars != ScrollBars.None;

            bool isScrollableRichTextBox = control is RichTextBox richTextBox
                && richTextBox.ScrollBars != RichTextBoxScrollBars.None;

            return control is TreeView
                || control is ListView
                || isScrollableTextBox
                || isScrollableRichTextBox
                || control is DataGridView
                || control is PropertyGrid
                || control is ScrollableControl scrollableControl && control is not Form && scrollableControl.AutoScroll
                || control.GetType().FullName == "Sgry.Azuki.WinForms.AzukiControl";
        }

        private static void DarkScrollBarTarget_ControlAdded(object sender, ControlEventArgs e)
        {
            if (e.Control == null) return;
            ScrollBarThemeState state = sender is Control control
                ? EnsureScrollBarThemeState(control)
                : new ScrollBarThemeState();
            ApplyScrollBarThemeCore(e.Control, state.ThemeName, state.UseCustomColors, state.CustomThumbColor, state.CustomTrackColor);
        }

        private static void DarkScrollBarTarget_HandleCreated(object sender, EventArgs e)
        {
            if (sender is not Control control) return;
            ApplyScrollBarThemeToHandle(control);
        }

        private static void DarkScrollBarTarget_Layout(object sender, LayoutEventArgs e)
        {
            if (sender is not Control control) return;
            ApplyScrollBarThemeToHandle(control);
        }

        private static void DarkScrollBarTarget_Paint(object sender, PaintEventArgs e)
        {
            if (sender is not Control control) return;
            ApplyScrollBarThemeToHandle(control);
        }

        private static void DarkScrollBarTarget_Resize(object sender, EventArgs e)
        {
            if (sender is not Control control) return;
            ApplyScrollBarThemeToHandle(control);
        }

        private static void DarkScrollBarTarget_MouseWheel(object sender, MouseEventArgs e)
        {
            if (sender is not Control control) return;
            ApplyScrollBarThemeToHandle(control);
        }

        private static void ApplyScrollBarThemeToHandle(Control control)
        {
            if (!control.IsHandleCreated) return;
            ScrollBarThemeState state = EnsureScrollBarThemeState(control);
            string themeName = state.ThemeName;
            SetWindowTheme(control.Handle, themeName, null);
            if (state.UseCustomColors)
            {
                DrawCustomScrollBars(control, state.CustomThumbColor, state.CustomTrackColor);
            }
        }

        private static ScrollBarThemeState EnsureScrollBarThemeState(Control control)
        {
            return ScrollBarThemeStates.GetValue(control, _ => new ScrollBarThemeState());
        }

        private static void DrawCustomScrollBars(Control control, Color thumbColor, Color trackColor)
        {
            if (!control.Visible || control.Width <= 0 || control.Height <= 0) return;

            Color borderColor = BlendColor(thumbColor, Color.Black, 0.70f);

            using Graphics g = Graphics.FromHwnd(control.Handle);
            DrawCustomScrollBar(control, g, ObjectIdVScroll, true, trackColor, thumbColor, borderColor);
            DrawCustomScrollBar(control, g, ObjectIdHScroll, false, trackColor, thumbColor, borderColor);
        }

        private static void DrawCustomScrollBar(Control control, Graphics g, long objectId, bool isVertical, Color trackColor, Color thumbColor, Color borderColor)
        {
            SCROLLBARINFO info = new SCROLLBARINFO
            {
                cbSize = Marshal.SizeOf<SCROLLBARINFO>(),
                rgstate = new uint[6]
            };

            if (!GetScrollBarInfo(control.Handle, objectId, ref info)) return;
            if ((info.rgstate[0] & StateSystemInvisible) != 0) return;

            Rectangle trackRect = ToClientRectangle(control, info.rcScrollBar);
            if (trackRect.Width <= 0 || trackRect.Height <= 0) return;

            using SolidBrush trackBrush = new SolidBrush(trackColor);
            using SolidBrush thumbBrush = new SolidBrush(thumbColor);
            using Pen borderPen = new Pen(borderColor);
            g.FillRectangle(trackBrush, trackRect);
            g.DrawRectangle(borderPen, trackRect.Left, trackRect.Top, trackRect.Width - 1, trackRect.Height - 1);

            Rectangle thumbRect = GetThumbRectangle(control, trackRect, info.xyThumbTop, info.xyThumbBottom, isVertical);
            if (thumbRect.Width <= 0 || thumbRect.Height <= 0) return;

            g.FillRectangle(thumbBrush, thumbRect);
            g.DrawRectangle(borderPen, thumbRect.Left, thumbRect.Top, thumbRect.Width - 1, thumbRect.Height - 1);
        }

        private static Rectangle ToClientRectangle(Control control, RECT rect)
        {
            Point lt = control.PointToClient(new Point(rect.Left, rect.Top));
            Point rb = control.PointToClient(new Point(rect.Right, rect.Bottom));
            return Rectangle.FromLTRB(lt.X, lt.Y, rb.X, rb.Y);
        }

        private static Rectangle GetThumbRectangle(Control control, Rectangle trackRect, int thumbTop, int thumbBottom, bool isVertical)
        {
            Point thumbStart;
            Point thumbEnd;
            if (isVertical)
            {
                thumbStart = control.PointToClient(new Point(trackRect.Left, thumbTop));
                thumbEnd = control.PointToClient(new Point(trackRect.Left, thumbBottom));
                int top = Math.Max(trackRect.Top + 1, thumbStart.Y);
                int bottom = Math.Min(trackRect.Bottom - 1, thumbEnd.Y + 1);
                return Rectangle.FromLTRB(trackRect.Left + 1, top, trackRect.Right - 1, bottom);
            }

            thumbStart = control.PointToClient(new Point(thumbTop, trackRect.Top));
            thumbEnd = control.PointToClient(new Point(thumbBottom, trackRect.Top));
            int left = Math.Max(trackRect.Left + 1, thumbStart.X);
            int right = Math.Min(trackRect.Right - 1, thumbEnd.X + 1);
            return Rectangle.FromLTRB(left, trackRect.Top + 1, right, trackRect.Bottom - 1);
        }

        private static Color BlendColor(Color baseColor, Color overlayColor, float ratio)
        {
            ratio = Math.Clamp(ratio, 0f, 1f);
            int r = (int)(baseColor.R * (1f - ratio) + overlayColor.R * ratio);
            int g = (int)(baseColor.G * (1f - ratio) + overlayColor.G * ratio);
            int b = (int)(baseColor.B * (1f - ratio) + overlayColor.B * ratio);
            return Color.FromArgb(r, g, b);
        }

        public static string AssemblyTitle
        {
            get
            {
                object[] attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyTitleAttribute), false);
                if (attributes.Length > 0)
                {
                    AssemblyTitleAttribute titleAttribute = (AssemblyTitleAttribute)attributes[0];
                    if (titleAttribute.Title != "")
                    {
                        return titleAttribute.Title;
                    }
                }
                return System.IO.Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);
            }
        }

        public static string GetApplicationDataFolder(bool make = false)
        {
            string fullPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            fullPath = System.IO.Path.Combine(fullPath, "KumaApp", AssemblyTitle + "custom");
            if (!System.IO.Directory.Exists(fullPath)) System.IO.Directory.CreateDirectory(fullPath);

            return fullPath;
        }

        public static int Range(int n, int min, int max)
        {
            return (n > max) ? max : (n < min ? min : n);
        }

        public static string GetApplicationFolder()
        {
            string path = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(path))
            {
                path += path[path.Length - 1] == '\\' ? "" : "\\";
            }
            return path;
        }

        public static int GetYM2151Hosei(float YM2151ClockValue, float baseClock)
        {
            int ret = 0;

            float delta = (float)YM2151ClockValue / baseClock;
            float d;
            float oldD = float.MaxValue;
            for (int i = 0; i < Tables.pcmMulTbl.Length; i++)
            {
                d = Math.Abs(delta - Tables.pcmMulTbl[i]);
                ret = i;
                if (d > oldD) break;
                oldD = d;
            }
            ret -= 12;

            return ret;
        }
        public static GD3 getGD3Info(outDatum[] buf, uint adr)
        {
            GD3 GD3 = new GD3();

            GD3.TrackName = "";
            GD3.TrackNameJ = "";
            GD3.GameName = "";
            GD3.GameNameJ = "";
            GD3.SystemName = "";
            GD3.SystemNameJ = "";
            GD3.Composer = "";
            GD3.ComposerJ = "";
            GD3.Converted = "";
            GD3.Notes = "";
            GD3.VGMBy = "";
            GD3.Version = "";
            GD3.UsedChips = "";

            try
            {
                //trackName
                GD3.TrackName = Encoding.Unicode.GetString(Common.getByteArray(buf, ref adr));
                //trackNameJ
                GD3.TrackNameJ = Encoding.Unicode.GetString(Common.getByteArray(buf, ref adr));
                //gameName
                GD3.GameName = Encoding.Unicode.GetString(Common.getByteArray(buf, ref adr));
                //gameNameJ
                GD3.GameNameJ = Encoding.Unicode.GetString(Common.getByteArray(buf, ref adr));
                //systemName
                GD3.SystemName = Encoding.Unicode.GetString(Common.getByteArray(buf, ref adr));
                //systemNameJ
                GD3.SystemNameJ = Encoding.Unicode.GetString(Common.getByteArray(buf, ref adr));
                //Composer
                GD3.Composer = Encoding.Unicode.GetString(Common.getByteArray(buf, ref adr));
                //ComposerJ
                GD3.ComposerJ = Encoding.Unicode.GetString(Common.getByteArray(buf, ref adr));
                //Converted
                GD3.Converted = Encoding.Unicode.GetString(Common.getByteArray(buf, ref adr));
                //VGMBy
                GD3.VGMBy = Encoding.Unicode.GetString(Common.getByteArray(buf, ref adr));
                //Notes
                GD3.Notes = Encoding.Unicode.GetString(Common.getByteArray(buf, ref adr));
                //Lyric(独自拡張)
                byte[] bLyric = Common.getByteArray(buf, ref adr);
                if (bLyric != null)
                {
                    GD3.Lyrics = new List<Tuple<int, int, string>>();
                    int i = 0;
                    int st = 0;
                    while (i < bLyric.Length)
                    {
                        byte h = bLyric[i];
                        byte l = bLyric[i + 1];
                        if ((h == 0x5b && l == 0x00 && i != 0) || i >= bLyric.Length - 2)
                        {
                            if ((i >= bLyric.Length - 2) || (bLyric[i + 2] != 0x5b || bLyric[i + 3] != 0x00))
                            {
                                string m = Encoding.Unicode.GetString(bLyric, st, i - st + ((i >= bLyric.Length - 2) ? 2 : 0));
                                st = i;

                                int cnt = int.Parse(m.Substring(1, m.IndexOf("]") - 1));
                                m = m.Substring(m.IndexOf("]") + 1);
                                GD3.Lyrics.Add(new Tuple<int, int, string>(cnt, cnt, m));
                            }
                        }
                        i += 2;
                    }
                }
                else
                {
                    GD3.Lyrics = null;
                }

            }
            catch (Exception ex)
            {
                Log.ForcedWrite(ex);
            }

            return GD3;
        }

        public static UInt32 getBE16(byte[] buf, UInt32 adr)
        {
            if (buf == null || buf.Length - 1 < adr + 1)
            {
                throw new IndexOutOfRangeException();
            }

            UInt32 dat;
            dat = (UInt32)buf[adr] * 0x100 + (UInt32)buf[adr + 1];

            return dat;
        }

        public static UInt32 getBE16(outDatum[] buf, UInt32 adr)
        {
            if (buf == null || buf.Length - 1 < adr + 1)
            {
                throw new IndexOutOfRangeException();
            }

            UInt32 dat;
            dat = (UInt32)buf[adr].val * 0x100 + (UInt32)buf[adr + 1].val;

            return dat;
        }

        public static UInt32 getLE16(byte[] buf, UInt32 adr)
        {
            if (buf == null || buf.Length - 1 < adr + 1)
            {
                throw new IndexOutOfRangeException();
            }

            UInt32 dat;
            dat = (UInt32)buf[adr] + (UInt32)buf[adr + 1] * 0x100;

            return dat;
        }

        public static UInt32 getLE16(outDatum[] buf, UInt32 adr)
        {
            if (buf == null || buf.Length - 1 < adr + 1)
            {
                throw new IndexOutOfRangeException();
            }

            UInt32 dat;
            dat = (UInt32)buf[adr].val + (UInt32)buf[adr + 1].val * 0x100;

            return dat;
        }

        public static UInt32 getLE24(byte[] buf, UInt32 adr)
        {
            if (buf == null || buf.Length - 1 < adr + 2)
            {
                throw new IndexOutOfRangeException();
            }

            UInt32 dat;
            dat = (UInt32)buf[adr] + (UInt32)buf[adr + 1] * 0x100 + (UInt32)buf[adr + 2] * 0x10000;

            return dat;
        }
        public static UInt32 getLE24(outDatum[] buf, UInt32 adr)
        {
            if (buf == null || buf.Length - 1 < adr + 2)
            {
                throw new IndexOutOfRangeException();
            }

            UInt32 dat;
            dat = (UInt32)buf[adr].val + (UInt32)buf[adr + 1].val * 0x100 + (UInt32)buf[adr + 2].val * 0x10000;

            return dat;
        }

        public static UInt32 getLE32(byte[] buf, UInt32 adr)
        {
            if (buf == null || buf.Length - 1 < adr + 3)
            {
                throw new IndexOutOfRangeException();
            }

            UInt32 dat;
            dat = (UInt32)buf[adr] + (UInt32)buf[adr + 1] * 0x100 + (UInt32)buf[adr + 2] * 0x10000 + (UInt32)buf[adr + 3] * 0x1000000;

            return dat;
        }
        public static UInt32 getLE32(outDatum[] buf, UInt32 adr)
        {
            if (buf == null || buf.Length - 1 < adr + 3)
            {
                throw new IndexOutOfRangeException();
            }

            UInt32 dat;
            dat = (UInt32)buf[adr].val + (UInt32)buf[adr + 1].val * 0x100 + (UInt32)buf[adr + 2].val * 0x10000 + (UInt32)buf[adr + 3].val * 0x1000000;

            return dat;
        }

        public static void SetLE24(outDatum[] desBuf, uint adr, uint val)
        {
            desBuf[adr].val = (byte)val;
            desBuf[adr + 1].val = (byte)(val >> 8);
            desBuf[adr + 2].val = (byte)(val >> 16);
        }

        public static void SetLE32(outDatum[] desBuf, uint adr, uint val)
        {
            desBuf[adr].val = (byte)val;
            desBuf[adr + 1].val = (byte)(val >> 8);
            desBuf[adr + 2].val = (byte)(val >> 16);
            desBuf[adr + 3].val = (byte)(val >> 24);
        }

        public static byte[] getByteArray(byte[] buf, ref uint adr)
        {
            if (adr >= buf.Length) return null;

            List<byte> ary = new List<byte>();
            while (buf[adr] != 0 || buf[adr + 1] != 0)
            {
                ary.Add(buf[adr]);
                adr++;
                ary.Add(buf[adr]);
                adr++;
            }
            adr += 2;

            return ary.ToArray();
        }

        public static byte[] getByteArray(outDatum[] buf, ref uint adr)
        {
            if (adr >= buf.Length) return null;

            List<byte> ary = new List<byte>();
            while (buf[adr].val != 0 || buf[adr + 1].val != 0)
            {
                ary.Add(buf[adr].val);
                adr++;
                ary.Add(buf[adr].val);
                adr++;
            }
            adr += 2;

            return ary.ToArray();
        }

        public static void SetBFColor(Control parent, Setting setting)
        {
            if (parent is Form)
            {
                parent.BackColor = Color.FromArgb(setting.ColorScheme.Log_BackColor);
                parent.ForeColor = Color.FromArgb(setting.ColorScheme.Log_ForeColor);
            }

            foreach (Control child in parent.Controls)
            {
                if (child.Controls != null && child.Controls.Count > 0)
                    SetBFColor(child, setting);

                if (child is Label || child is TextBox || child is Panel)
                {
                    child.BackColor = Color.FromArgb(setting.ColorScheme.Log_BackColor);
                    child.ForeColor = Color.FromArgb(setting.ColorScheme.Log_ForeColor);
                }

                if (child is Button)
                {
                    child.BackColor = Color.FromArgb(setting.ColorScheme.Log_BackColor);
                    child.ForeColor = Color.FromArgb(setting.ColorScheme.Log_ForeColor);
                }
            }
        }

        public static EnmMmlFileFormat GetEnmMmlFileFormat(string extension)
        {
            if (extension.ToUpper() == ".GWI") return EnmMmlFileFormat.GWI;
            if (extension.ToUpper() == ".MUC") return EnmMmlFileFormat.MUC;
            if (extension.ToUpper() == ".MML") return EnmMmlFileFormat.MML;
            if (extension.ToUpper() == ".MDL") return EnmMmlFileFormat.MDL;
            if (extension.ToUpper() == ".MUS") return EnmMmlFileFormat.MUS;

            return EnmMmlFileFormat.unknown;
        }

        public static Stream GetOPNARyhthmStream(string fn)
        {
            string ffn = fn;

            string chk;

            chk = Path.Combine(playingFilePath, fn);
            if (File.Exists(chk))
                ffn = chk;
            else
            {
                chk = Path.Combine(GetApplicationFolder(), fn);
                if (File.Exists(chk)) ffn = chk;
            }

            try
            {
                if (!File.Exists(ffn)) return null;
                FileStream fs = new FileStream(ffn, FileMode.Open, FileAccess.Read, FileShare.Read);
                return fs;
            }
            catch
            {
                return null;
            }
        }

        public static string ConvertVoiceDataGWIToMucom(string v)
        {
            if (string.IsNullOrEmpty(v)) return v;


            //データ配列に変換する
            List<int> prm = GetVoiceParamFromVoiceStr(v,out string vnum);
            if (prm == null) return v;
            if (prm.Count != 46) return v;

            string msg = "";
            for(int op = 0; op < 4; op++)
            {
                msg += string.Format("  {0:D03} {1:D03} {2:D03} {3:D03} {4:D03} {5:D03} {6:D03} {7:D03} {8:D03}\r\n"
                    , prm[op * 11 + 0], prm[op * 11 + 1], prm[op * 11 + 2], prm[op * 11 + 3], prm[op * 11 + 4]
                    , prm[op * 11 + 5], prm[op * 11 + 6], prm[op * 11 + 7], prm[op * 11 + 8]);
            }

            msg = string.Format("  @{0}\r\n  {1:D03} {2:D03}\r\n", vnum, prm[45], prm[44])
                + msg
                + "\r\n";

            return msg;
        }

        private static List<int> GetVoiceParamFromVoiceStr(string v,out string vnum)
        {
            List<int> prm = new List<int>();
            vnum = "";

            string[] vs = v.Split(new string[] { "\r\n" }, StringSplitOptions.None);

            //1行目の解析(ボイスの種類とナンバーを取得する
            if (vs.Length < 3) return null;
            if (string.IsNullOrEmpty(vs[0])) return null;
            if (vs[0].IndexOf("'@") != 0) return null;
            vs[0] = vs[0].Substring(2).Trim();
            if (vs[0][0] == 'N' && vs[0].Length>1)
            {
                vnum = vs[0].Substring(1).Trim();
            }
            if (string.IsNullOrEmpty(vnum)) return null;

            //2行目以降の解析
            for (int r = 1; r < vs.Length; r++)
            {
                string df = vs[r].Trim();
                if (string.IsNullOrEmpty(df)) continue;
                if (df.IndexOf("'@") != 0) continue;
                df = df.Substring(2).Trim();
                string[] ps = df.Split(new char[] { ',', ' ', '\t' });
                foreach(string p in ps)
                {
                    int d;
                    if (!int.TryParse(p, out d)) return null;
                    prm.Add(d);
                }
            }

            return prm;
        }





        /// 参考URL：
        /// dobon.net ウィンドウのタイトルからプロセスを探す
        /// https://dobon.net/vb/dotnet/process/getprocessesbywindowtitle.html
        ///


        /// <summary>
        /// 指定された文字列をウィンドウのタイトルとクラス名に含んでいるプロセスを
        /// すべて取得する。
        /// </summary>
        /// <param name="windowText">ウィンドウのタイトルに含むべき文字列。
        /// nullを指定すると、classNameだけで検索する。</param>
        /// <param name="className">ウィンドウが属するクラス名に含むべき文字列。
        /// nullを指定すると、windowTextだけで検索する。</param>
        /// <returns>見つかったプロセスの配列。</returns>
        public static Tuple<Int64, Process>[] GetProcessesByWindow(
            string windowText, string className)
        {
            //検索の準備をする
            foundProcesses = new ArrayList();
            foundProcessIds = new ArrayList();
            searchWindowText = windowText;
            searchClassName = className;

            //ウィンドウを列挙して、対象のプロセスを探す
            EnumWindows(new EnumWindowsDelegate(EnumWindowCallBack), IntPtr.Zero);

            //結果を返す
            return (Tuple<Int64, Process>[])foundProcesses.ToArray(typeof(Tuple<Int64, Process>));
        }

        public static void setForegroundWindow(Int64 hWnd)
        {
            IntPtr h = new IntPtr(hWnd);
            SetForegroundWindow(h);
        }

        public static void sendmessage(Int64 hWnd, uint Msg, uint wParam, uint lParam)
        {
            IntPtr h = new IntPtr(hWnd);
            SendMessage(h, Msg, wParam, lParam);
        }


        private static string searchWindowText = null;
        private static string searchClassName = null;
        private static ArrayList foundProcessIds = null;
        private static ArrayList foundProcesses = null;

        private delegate bool EnumWindowsDelegate(IntPtr hWnd, IntPtr lparam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private extern static bool EnumWindows(EnumWindowsDelegate lpEnumFunc,
            IntPtr lparam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd,
            StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd,
            StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowThreadProcessId(
            IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern long SendMessage(IntPtr hWnd, uint Msg, uint wParam, uint lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);


        private static bool EnumWindowCallBack(IntPtr hWnd, IntPtr lparam)
        {
            if (searchWindowText != null)
            {
                //ウィンドウのタイトルの長さを取得する
                int textLen = GetWindowTextLength(hWnd);
                if (textLen == 0)
                {
                    //次のウィンドウを検索
                    return true;
                }
                //ウィンドウのタイトルを取得する
                StringBuilder tsb = new StringBuilder(textLen + 1);
                GetWindowText(hWnd, tsb, tsb.Capacity);
                //タイトルに指定された文字列を含むか
                if (tsb.ToString().IndexOf(searchWindowText) < 0)
                {
                    //含んでいない時は、次のウィンドウを検索
                    return true;
                }
            }

            if (searchClassName != null)
            {
                //ウィンドウのクラス名を取得する
                StringBuilder csb = new StringBuilder(256);
                GetClassName(hWnd, csb, csb.Capacity);
                //クラス名に指定された文字列を含むか
                if (csb.ToString().IndexOf(searchClassName) < 0)
                {
                    //含んでいない時は、次のウィンドウを検索
                    return true;
                }
            }

            //プロセスのIDを取得する
            int processId;

            GetWindowThreadProcessId(hWnd, out processId);
            //今まで見つかったプロセスでは無いことを確認する
            if (!foundProcessIds.Contains(processId))
            {
                foundProcessIds.Add(processId);
                //プロセスIDをからProcessオブジェクトを作成する
                foundProcesses.Add(new Tuple<Int64, Process>(hWnd.ToInt64(), Process.GetProcessById(processId)));
            }

            //次のウィンドウを検索
            return true;
        }


    }
}
