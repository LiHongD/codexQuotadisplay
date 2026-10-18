using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public sealed partial class CodexUsageWidgetV2 : Form
{
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] public struct Size { public int Width, Height; public Size(int w, int h) { Width = w; Height = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct Blend { public byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool UpdateLayeredWindow(IntPtr window,
            IntPtr screen, ref Point destination, ref Size size, IntPtr source, ref Point origin, uint key, ref Blend blend, uint flags);
    }
    private struct EdgePixel { public int Offset; public byte Alpha; public EdgePixel(int offset, byte alpha) { Offset = offset; Alpha = alpha; } }
    private readonly List<EdgePixel> edgePixels = new List<EdgePixel>();
    private Size maskSize;
    private bool presenting;
    private int presentedFrames;
    private double panelOpacity = 1;
    private sealed class Palette
    {
        public string Name;
        public Color Bg, Surface, Track, Text, Muted, A, B;
        public Palette(string name, string bg, string surface, string track, string text, string muted, string a, string b)
        {
            Name = name; Bg = Hex(bg); Surface = Hex(surface); Track = Hex(track);
            Text = Hex(text); Muted = Hex(muted); A = Hex(a); B = Hex(b);
        }
    }
    private static Color Hex(string value) { return ColorTranslator.FromHtml(value); }
    private static Color Fade(Color color, int alpha) { return Color.FromArgb(alpha, color); }
    private static readonly Palette[] Palettes = {
        new Palette("曜石黄", "#222320", "#292A26", "#373831", "#F5F3E8", "#A9A99B", "#F6E64B", "#D5B950"),
        new Palette("极光青", "#15242B", "#1D3038", "#2B4249", "#EAF8F6", "#91AFB4", "#63E3CD", "#44BEE0"),
        new Palette("星云紫", "#201C2B", "#2A2538", "#3D3650", "#F6F0FF", "#AEA3C2", "#CFA0FF", "#8F96FF"),
        new Palette("日落橙", "#29201D", "#342722", "#4B3730", "#FFF4E8", "#C3A89C", "#FFB76A", "#FF785F"),
        new Palette("冰川蓝", "#17222E", "#203143", "#30455B", "#EDF6FF", "#9BB3C7", "#82C9FF", "#4F8DFF"),
        new Palette("月光白", "#F2F0EA", "#FFFFFF", "#E2DED4", "#272B30", "#747B81", "#507ACB", "#50B9B2"),
        new Palette("自定义", "#1D2228", "#282F37", "#3A454F", "#F4F7FA", "#A5B3BF", "#64D9C5", "#5D9FE8")
    };
    private static readonly string[] Modes = { "双环仪表", "流光进度", "能量刻度", "极简数字", "液态水位", "双额度能量球" };
    private static readonly string[] Materials = { "磨砂亚克力", "烟熏玻璃", "拉丝金属", "陶瓷釉面", "碳纤维", "经典纯色" };
    private static readonly string[] Animations = { "静态", "柔光呼吸", "丝绸流光", "轨道光点", "涟漪扩散", "彗星拖尾", "流水折射" };
    private const int BaseWidth = 360, BaseHeight = 266;
    private readonly string settingsPath, dataDirectory;
    private readonly CodexAccountReader accountReader;
    private readonly JavaScriptSerializer json = new JavaScriptSerializer();
    private readonly Timer pollTimer = new Timer(), clockTimer = new Timer(), motionTimer = new Timer();
    private double? primary, secondary;
    private DateTime? primaryReset, secondaryReset;
    private DateTime lastUpdated;
    private string plan = "CODEX", status = "正在连接 Codex…";
    private bool busy, failed, dragging, resizing, showReset, animate = true;
    private QuotaState quotaState = QuotaState.Unavailable;
    private string stateTitle = "正在检测本机 Codex", stateDetail = "请稍候…";
    private int mode = 5, palette = 1, material, animationStyle = 6;
    private float animationSpeed = 1;
    private float phase;
    private Color customAccent = Hex("#64D9C5");
    private Point dragAt, originalAt;
    private Size originalSize;
    private Palette cachedColors;
    private int cachedPalette = -1, cachedMaterial = -1;
    private float UiScale { get { return Width / (OrbSurface ? 180F : 360F); } }
    private float UiScaleY { get { return Height / (OrbSurface ? 180F : 176F); } }
    private Color A { get { return palette == 6 ? customAccent : Palettes[palette].A; } }
    private Color B { get { return palette == 6 ? Color.FromArgb(Math.Min(255, customAccent.R + 32), Math.Min(255, customAccent.G + 32), Math.Min(255, customAccent.B + 32)) : Palettes[palette].B; } }
    private Palette Colors {
        get {
            if (cachedColors != null && cachedPalette == palette && cachedMaterial == material) return cachedColors;
            Palette chosen = Palettes[palette];
            string a = ColorTranslator.ToHtml(A), b = ColorTranslator.ToHtml(B);
            cachedColors = material == 1 ? new Palette("烟熏玻璃", "#151B24", "#263441", "#34424F", "#F4F7FC", "#A5B5C4", a, b)
                : material == 2 ? new Palette("拉丝金属", "#232B33", "#36424C", "#40505A", "#F1F6FA", "#A9B9C3", a, b)
                : material == 3 ? new Palette("陶瓷釉面", "#F0EDE7", "#FFFFFF", "#DCDAD5", "#27313A", "#73808A", a, b)
                : material == 4 ? new Palette("碳纤维", "#14191D", "#222A30", "#303A42", "#F3F7F8", "#9AAAB0", a, b)
                : chosen;
            cachedPalette = palette; cachedMaterial = material;
            return cachedColors;
        }
    }

    public CodexUsageWidgetV2(string contextPath) : this(Path.GetDirectoryName(Path.GetFullPath(contextPath)), Path.GetDirectoryName(Path.GetFullPath(contextPath))) { }

    public CodexUsageWidgetV2(string appDirectory, string userDataDirectory)
    {
        dataDirectory = userDataDirectory;
        Directory.CreateDirectory(dataDirectory);
        accountReader = new CodexAccountReader(appDirectory);
        settingsPath = Path.Combine(dataDirectory, "settings.json");
        Text = "Codex 用量挂件";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        // UpdateLayeredWindow owns the backing bitmap; WinForms must not allocate a second paint buffer.
        DoubleBuffered = false;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(176, 176);
        StartPosition = FormStartPosition.Manual;
        Rectangle area = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(area.Right - Width - 24, area.Bottom - Height - 24);
        Font = new Font("Microsoft YaHei UI", 9);
        TopMost = true;
        LoadSettings();
        BackColor = Colors.Bg;
        Resize += (s, e) => Redraw();
        InitializeCompactInteraction();
        pollTimer.Interval = 60000; pollTimer.Tick += (s, e) => RefreshUsage(); pollTimer.Start();
        clockTimer.Interval = 1000; clockTimer.Tick += (s, e) => { if (!MotionActive) Redraw(); }; clockTimer.Start();
        motionTimer.Interval = 40; motionTimer.Tick += (s, e) => { if (MotionActive && Visible) { phase = (phase + .042F * animationSpeed) % (2F * (float)Math.PI); Redraw(); } };
        motionTimer.Start();
        Shown += (s, e) => { Redraw(); RefreshUsage(); };
    }

    private void ResizeToWidth(int width)
    {
        int clamped = mode == 5 ? Math.Max(128, Math.Min(280, width)) : Math.Max(220, Math.Min(480, width));
        if (mode == 5) orbWidth = clamped; else panelWidth = clamped;
        ClientSize = ExpandedSize();
    }
    private bool MotionActive { get { return animate && animationStyle > 0; } }
    private bool CanRetry(float x, float y) { return !busy && quotaState != QuotaState.Connected && x >= 100 && x <= 260 && y >= 181 && y <= 216; }
    private GraphicsPath CardOutline() {
        if (!OrbSurface) return Round(new RectangleF(0, 0, 360, 176), 16);
        GraphicsPath shape = new GraphicsPath(); shape.AddEllipse(2, 2, 176, 176); return shape;
    }

    private ContextMenuStrip MakeMenu()
    {
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Font = new Font("Microsoft YaHei UI", 9);
        ToolStripMenuItem styles = new ToolStripMenuItem("显示样式");
        for (int i = 0; i < Modes.Length; i++) { int chosen = i;
            styles.DropDownItems.Add(Modes[i], null, (s, e) => { mode = chosen; SaveSettings(); Redraw(); }); }
        menu.Items.Add(styles);
        ToolStripMenuItem themes = new ToolStripMenuItem("主题颜色");
        for (int i = 0; i < Palettes.Length - 1; i++) { int chosen = i;
            themes.DropDownItems.Add(Palettes[i].Name, null, (s, e) => { palette = chosen; ChangePalette(); }); }
        themes.DropDownItems.Add("自定义强调色…", null, (s, e) => {
            using (ColorDialog dialog = new ColorDialog()) { dialog.Color = customAccent; dialog.FullOpen = true;
                if (dialog.ShowDialog(this) == DialogResult.OK) { customAccent = dialog.Color; palette = 6; ChangePalette(); } }
        });
        menu.Items.Add(themes);
        ToolStripMenuItem surfaces = new ToolStripMenuItem("背景材质");
        for (int i = 0; i < Materials.Length; i++) { int chosen = i;
            surfaces.DropDownItems.Add(Materials[i], null, (s, e) => { material = chosen; ChangePalette(); }); }
        menu.Items.Add(surfaces);
        ToolStripMenuItem sizes = new ToolStripMenuItem("面板大小");
        foreach (int width in new[] { 380, 460, 540, 620 }) { int chosen = width;
            sizes.DropDownItems.Add(width + " 像素", null, (s, e) => { ResizeToWidth(chosen); SaveSettings(); }); }
        menu.Items.Add(sizes);
        ToolStripMenuItem opacity = new ToolStripMenuItem("透明度");
        foreach (int value in new[] { 100, 90, 80, 70 }) { int chosen = value;
            opacity.DropDownItems.Add(value + "%", null, (s, e) => { panelOpacity = chosen / 100.0; SaveSettings(); Redraw(); }); }
        menu.Items.Add(opacity);
        ToolStripMenuItem effects = new ToolStripMenuItem("动态光效");
        for (int i = 0; i < Animations.Length; i++) { int chosen = i;
            effects.DropDownItems.Add(Animations[i], null, (s, e) => { animationStyle = chosen; animate = chosen > 0; phase = 0; SaveSettings(); Redraw(); }); }
        effects.DropDownItems.Add(new ToolStripSeparator());
        ToolStripMenuItem speed = new ToolStripMenuItem("动效速度");
        float[] speeds = { .55F, 1F, 1.65F }; string[] speedNames = { "舒缓", "标准", "轻快" };
        for (int i = 0; i < speeds.Length; i++) { float chosen = speeds[i];
            speed.DropDownItems.Add(speedNames[i], null, (s, e) => { animationSpeed = chosen; SaveSettings(); }); }
        effects.DropDownItems.Add(speed);
        menu.Items.Add(effects);
        menu.Items.Add("显示重置时间", null, (s, e) => { showReset = !showReset; SaveSettings(); Redraw(); });
        menu.Items.Add("始终置顶", null, (s, e) => { TopMost = !TopMost; SaveSettings(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("立即刷新", null, (s, e) => RefreshUsage());
        menu.Items.Add("退出挂件", null, (s, e) => Close());
        menu.Opening += (s, e) => {
            for (int i = 0; i < Modes.Length; i++) ((ToolStripMenuItem)styles.DropDownItems[i]).Checked = mode == i;
            for (int i = 0; i < Palettes.Length - 1; i++) ((ToolStripMenuItem)themes.DropDownItems[i]).Checked = palette == i;
            ((ToolStripMenuItem)themes.DropDownItems[6]).Checked = palette == 6;
            for (int i = 0; i < Materials.Length; i++) ((ToolStripMenuItem)surfaces.DropDownItems[i]).Checked = material == i;
            for (int i = 0; i < 4; i++) ((ToolStripMenuItem)opacity.DropDownItems[i]).Checked = Math.Round(panelOpacity * 100) == new[] { 100, 90, 80, 70 }[i];
            for (int i = 0; i < Animations.Length; i++) ((ToolStripMenuItem)effects.DropDownItems[i]).Checked = i == (animate ? animationStyle : 0);
            for (int i = 0; i < speeds.Length; i++) ((ToolStripMenuItem)speed.DropDownItems[i]).Checked = Math.Abs(animationSpeed - speeds[i]) < .01;
            ((ToolStripMenuItem)menu.Items[6]).Checked = showReset;
            ((ToolStripMenuItem)menu.Items[7]).Checked = TopMost;
        };
        return menu;
    }
    private void ChangePalette() { cachedColors = null; BackColor = Colors.Bg; SaveSettings(); Redraw(); }
    private void LoadSettings()
    {
        try {
            if (!File.Exists(settingsPath)) return;
            var data = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(settingsPath, Encoding.UTF8));
            mode = Math.Max(0, Math.Min(Modes.Length - 1, Convert.ToInt32(data["mode"])));
            palette = Math.Max(0, Math.Min(6, Convert.ToInt32(data["palette"])));
            if (data.ContainsKey("accent")) customAccent = Hex(data["accent"].ToString());
            if (data.ContainsKey("opacity")) panelOpacity = Math.Max(.7, Math.Min(1, Convert.ToDouble(data["opacity"])));
            if (!data.ContainsKey("clarityVersion")) panelOpacity = 1;
            if (data.ContainsKey("showReset")) showReset = Convert.ToBoolean(data["showReset"]);
            if (data.ContainsKey("material")) material = Math.Max(0, Math.Min(Materials.Length - 1, Convert.ToInt32(data["material"])));
            else if (data.ContainsKey("acrylic") && !Convert.ToBoolean(data["acrylic"])) material = 1;
            if (data.ContainsKey("animate")) animate = Convert.ToBoolean(data["animate"]);
            if (data.ContainsKey("animationStyle")) animationStyle = Math.Max(0, Math.Min(Animations.Length - 1, Convert.ToInt32(data["animationStyle"])));
            if (data.ContainsKey("animationSpeed")) animationSpeed = Math.Max(.3F, Math.Min(2F, Convert.ToSingle(data["animationSpeed"])));
            if (data.ContainsKey("topMost")) TopMost = Convert.ToBoolean(data["topMost"]);
            LoadCompactSettings(data);
            if (data.ContainsKey("x") && data.ContainsKey("y")) {
                Point location = new Point(Convert.ToInt32(data["x"]), Convert.ToInt32(data["y"]));
                Rectangle desktop = Screen.FromRectangle(new Rectangle(location, Size)).WorkingArea;
                Location = new Point(Math.Max(desktop.Left, Math.Min(desktop.Right - Width, location.X)),
                    Math.Max(desktop.Top, Math.Min(desktop.Bottom - Height, location.Y)));
            }
        } catch { /* Broken settings do not block startup. */ }
    }
    private void SaveSettings()
    {
        try {
            var data = new Dictionary<string, object> { {"mode", mode}, {"palette", palette}, {"accent", ColorTranslator.ToHtml(customAccent)},
                {"opacity", panelOpacity}, {"showReset", showReset}, {"material", material}, {"animate", animate},
                {"animationStyle", animationStyle}, {"animationSpeed", animationSpeed},
                {"topMost", TopMost}, {"width", Width}, {"clarityVersion", 2}, {"x", Left}, {"y", Top} };
            SaveCompactSettings(data);
            File.WriteAllText(settingsPath, json.Serialize(data), new UTF8Encoding(false));
        } catch { /* Temporary changes still work if the folder is read-only. */ }
    }

    private static GraphicsPath Round(RectangleF rect, float r)
    {
        GraphicsPath path = new GraphicsPath(); float d = r * 2;
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
    private static void Label(Graphics g, string text, float x, float y, float size, Color color,
        bool bold = false, float width = 316, StringAlignment align = StringAlignment.Near, bool numeric = false)
    {
        using (Font font = new Font(numeric ? "Bahnschrift SemiBold" : "Microsoft YaHei UI", size,
            bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point))
        using (SolidBrush brush = new SolidBrush(color))
        using (StringFormat format = new StringFormat()) {
            format.Alignment = align; format.LineAlignment = StringAlignment.Center;
            format.FormatFlags = StringFormatFlags.NoWrap; format.Trimming = StringTrimming.EllipsisCharacter;
            g.DrawString(text, font, brush, new RectangleF(x, y, width, size * 2.2F), format);
        }
    }
    protected override CreateParams CreateParams {
        get { CreateParams parameters = base.CreateParams; parameters.ExStyle |= 0x00080000 | 0x00000080; return parameters; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { pollTimer.Dispose(); clockTimer.Dispose(); motionTimer.Dispose(); dockTimer.Dispose(); hint.Dispose(); DisposeOrbLayers(); }
        base.Dispose(disposing);
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { /* The compositor retains the layered bitmap. */ }
    private void Redraw()
    {
        if (!IsHandleCreated || IsDisposed || presenting || Width <= 0 || Height <= 0) return;
        presenting = true;
        try {
            using (Bitmap image = CreateFrame()) {
                IntPtr screen = Native.GetDC(IntPtr.Zero), memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
                try {
                    memory = Native.CreateCompatibleDC(screen);
                    bitmap = image.GetHbitmap(Color.FromArgb(0)); previous = Native.SelectObject(memory, bitmap);
                    Native.Point destination = new Native.Point(Left, Top), origin = new Native.Point(0, 0);
                    Native.Size size = new Native.Size(Width, Height);
                    Native.Blend blend = new Native.Blend { Alpha = (byte)Math.Round(panelOpacity * 255), Format = 1 };
                    if (!Native.UpdateLayeredWindow(Handle, screen, ref destination, ref size, memory, ref origin, 0, ref blend, 2))
                        throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    presentedFrames++;
                } finally {
                    if (previous != IntPtr.Zero) Native.SelectObject(memory, previous);
                    if (bitmap != IntPtr.Zero) Native.DeleteObject(bitmap);
                    if (memory != IntPtr.Zero) Native.DeleteDC(memory);
                    if (screen != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, screen);
                }
            }
        } finally { presenting = false; }
    }
    private Bitmap CreateFrame()
    {
        if (OrbSurface) return CreateSmoothOrbFrame();
        Bitmap image = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(image)) Render(g);
        if (maskSize != ClientSize || maskIsOrb != OrbSurface) {
            edgePixels.Clear(); maskSize = ClientSize; maskIsOrb = OrbSurface;
            // Supersample only the reusable coverage mask; keep text at native resolution.
            using (Bitmap large = new Bitmap(Width * 4, Height * 4, PixelFormat.Format32bppPArgb))
            using (Bitmap mask = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb)) {
                using (Graphics g = Graphics.FromImage(large)) {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.ScaleTransform(UiScale * 4, UiScaleY * 4);
                    using (GraphicsPath shape = CardOutline()) g.FillPath(Brushes.White, shape);
                }
                using (Graphics g = Graphics.FromImage(mask)) {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (ImageAttributes attributes = new ImageAttributes()) {
                        attributes.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(large, new Rectangle(0, 0, Width, Height), 0, 0, large.Width, large.Height, GraphicsUnit.Pixel, attributes);
                    }
                }
                int radius = (int)Math.Ceiling(19 * Math.Max(UiScale, UiScaleY));
                for (int y = 0; y < Height; y++) {
                    if (!OrbSurface && y >= radius && y < Height - radius) continue;
                    for (int x = 0; x < Width; x++) {
                        if (!OrbSurface && x >= radius && x < Width - radius) continue;
                        byte alpha = mask.GetPixel(x, y).A;
                        if (alpha < 255) edgePixels.Add(new EdgePixel((y * Width + x) * 4, alpha));
                    }
                }
            }
        }
        BitmapData data = image.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try {
            foreach (EdgePixel pixel in edgePixels) {
                int color = Marshal.ReadInt32(data.Scan0, pixel.Offset);
                int alpha = pixel.Alpha;
                int blue = (color & 255) * alpha / 255, green = ((color >> 8) & 255) * alpha / 255;
                int red = ((color >> 16) & 255) * alpha / 255, opacity = (int)((uint)color >> 24) * alpha / 255;
                Marshal.WriteInt32(data.Scan0, pixel.Offset, blue | green << 8 | red << 16 | opacity << 24);
            }
        } finally { image.UnlockBits(data); }
        return image;
    }
    private void Render(Graphics g)
    {
        g.Clear(Color.Transparent);
        GraphicsState state = g.Save();
        g.ScaleTransform(UiScale, UiScaleY);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        if (OrbSurface) PaintEnergyOrb(g); else PaintCompactPanel(g);
        g.Restore(state);
    }
    private void PaintCard(Graphics g)
    {
        Palette c = Colors;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        DrawMaterial(g, c);
        using (GraphicsPath edge = Round(new RectangleF(.85F, .85F, BaseWidth - 1.7F, BaseHeight - 1.7F), 17.15F))
        using (LinearGradientBrush rim = new LinearGradientBrush(new Rectangle(0, 0, BaseWidth, BaseHeight),
            Fade(material == 3 ? Hex("#7E898E") : Color.White, material == 3 ? 62 : 49),
            Fade(material == 3 ? Hex("#7E898E") : Color.White, 12), 85F))
        using (Pen border = new Pen(rim, .8F / UiScale)) g.DrawPath(border, edge);
        using (GraphicsPath inner = Round(new RectangleF(1.8F, 1.8F, BaseWidth - 3.6F, BaseHeight - 3.6F), 16.2F))
        using (Pen depth = new Pen(Fade(Color.Black, material == 3 ? 8 : 28), .55F / UiScale)) g.DrawPath(depth, inner);
        using (SolidBrush dot = new SolidBrush(A)) g.FillEllipse(dot, 22, 22, 7, 7);
        Label(g, "CODEX  /  USAGE", 37, 12, 11.5F, c.Text, true);
        Label(g, plan.ToUpperInvariant(), 22, 37, 10, c.Muted);
        Label(g, "↻", 286, 9, 19, c.Text, false, 32, StringAlignment.Center);
        Label(g, "×", 324, 9, 19, c.Text, false, 29, StringAlignment.Center);
        using (Pen line = new Pen(Fade(c.Text, 24))) { g.DrawLine(line, 22, 62, 338, 62); g.DrawLine(line, 22, 235, 338, 235); }
        if (quotaState != QuotaState.Connected) PaintAccountState(g);
        else if (mode == 0) { Ring(g, 101, primary, A, "5 小时", primaryReset); Ring(g, 259, secondary, B, "7 天", secondaryReset); }
        else if (mode == 1) { Bar(g, 79, primary, A, B, "5 小时额度", primaryReset); Bar(g, 157, secondary, B, A, "7 天额度", secondaryReset); }
        else if (mode == 2) { Segments(g, 83, primary, A, "5 小时额度", primaryReset); Segments(g, 158, secondary, B, "7 天额度", secondaryReset); }
        else if (mode == 4) { Liquid(g, 101, primary, A, "5 小时", primaryReset); Liquid(g, 259, secondary, B, "7 天", secondaryReset); }
        else { using (Pen line = new Pen(Fade(c.Text, 28))) g.DrawLine(line, 180, 82, 180, 216);
            Number(g, 20, primary, A, "5 小时", primaryReset); Number(g, 190, secondary, B, "7 天", secondaryReset); }
        string footer = failed ? status : busy ? "正在刷新…" : lastUpdated == DateTime.MinValue ? status :
            lastUpdated.ToString("HH:mm") + " 已更新  ·  60 秒刷新";
        Label(g, footer, 22, 240, 9.5F, failed && quotaState != QuotaState.NotSignedIn && quotaState != QuotaState.NoQuota ? Hex("#FF8C83") : c.Muted, false, 285);
        using (Pen grip = new Pen(Fade(c.Muted, 140), 1.5F)) {
            g.DrawLine(grip, 331, 256, 348, 239);
            g.DrawLine(grip, 338, 256, 348, 246);
            g.DrawLine(grip, 345, 256, 348, 253);
        }
    }
    private void PaintAccountState(Graphics g)
    {
        Color ink = Colors.Text;
        using (Pen icon = new Pen(Fade(A, 190), 1.5F)) {
            g.DrawEllipse(icon, 175, 78, 10, 10);
            g.DrawArc(icon, 168, 92, 24, 16, 180, 180);
        }
        Label(g, busy ? "正在检测本机 Codex" : stateTitle, 20, 105, 15, ink, true, 320, StringAlignment.Center);
        string[] lines = (busy ? "正在读取本机登录账号…" : stateDetail).Split('\n');
        for (int i = 0; i < Math.Min(2, lines.Length); i++)
            Label(g, lines[i], 24, 142 + i * 18, 10, Colors.Muted, false, 312, StringAlignment.Center);
        using (GraphicsPath button = Round(new RectangleF(100, 185, 160, 32), 10)) {
            using (SolidBrush fill = new SolidBrush(Fade(A, 23))) g.FillPath(fill, button);
            using (Pen rim = new Pen(Fade(A, 82), .8F)) g.DrawPath(rim, button);
        }
        Label(g, busy ? "检测中…" : "重新检测", 100, 190, 10, ink, true, 160, StringAlignment.Center);
    }
    private void DrawMaterial(Graphics g, Palette c)
    {
        Rectangle canvas = new Rectangle(0, 0, BaseWidth, BaseHeight);
        using (LinearGradientBrush baseBrush = new LinearGradientBrush(canvas,
            material == 3 ? Color.White : c.Surface, c.Bg, material == 2 ? 0F : 65F))
            g.FillRectangle(baseBrush, canvas);
        if (material == 0 || material == 1) {
            using (LinearGradientBrush reflection = new LinearGradientBrush(new Rectangle(0, 0, BaseWidth, 125),
                Fade(Color.White, material == 0 ? 40 : 57), Fade(Color.White, 0), 90F))
                g.FillRectangle(reflection, 0, 0, BaseWidth, 125);
            using (GraphicsPath ellipse = new GraphicsPath()) {
                ellipse.AddEllipse(180, -150, 320, 250);
                using (PathGradientBrush bloom = new PathGradientBrush(ellipse)) {
                    bloom.CenterColor = Fade(material == 1 ? A : Color.White, material == 1 ? 30 : 15);
                    bloom.SurroundColors = new[] { Fade(c.Bg, 0) };
                    g.FillPath(bloom, ellipse);
                }
            }
            Random grain = new Random(19);
            using (SolidBrush speck = new SolidBrush(Fade(Color.White, material == 0 ? 8 : 7)))
                for (int i = 0; i < 300; i++) g.FillRectangle(speck, grain.Next(BaseWidth), grain.Next(BaseHeight), 1, 1);
        } else if (material == 2) {
            using (Pen dark = new Pen(Fade(Color.Black, 17)))
            using (Pen light = new Pen(Fade(Color.White, 13)))
                for (int y = 3; y < BaseHeight; y += 3) { g.DrawLine(y % 2 == 0 ? light : dark, 0, y, BaseWidth, y); }
            using (LinearGradientBrush reflection = new LinearGradientBrush(new Rectangle(0, 0, BaseWidth, 85),
                Fade(Color.White, 39), Fade(Color.White, 0), 90F)) g.FillRectangle(reflection, 0, 0, BaseWidth, 85);
        } else if (material == 3) {
            using (GraphicsPath ellipse = new GraphicsPath()) {
                ellipse.AddEllipse(-75, -130, 540, 370);
                using (PathGradientBrush glaze = new PathGradientBrush(ellipse)) {
                    glaze.CenterColor = Fade(Color.White, 116);
                    glaze.SurroundColors = new[] { Fade(Hex("#C9C9C4"), 55) };
                    g.FillPath(glaze, ellipse);
                }
            }
            using (Pen edge = new Pen(Fade(Hex("#9199A0"), 22), 4)) g.DrawLine(edge, 8, BaseHeight - 5, BaseWidth - 8, BaseHeight - 5);
        } else if (material == 4) {
            using (Pen weaveA = new Pen(Fade(Color.White, 7), 2))
            using (Pen weaveB = new Pen(Fade(Color.Black, 20), 2))
                for (int x = -BaseHeight; x < BaseWidth + BaseHeight; x += 8) {
                    g.DrawLine(weaveA, x, 0, x + BaseHeight, BaseHeight);
                    g.DrawLine(weaveB, x + BaseHeight, 0, x, BaseHeight);
                }
            using (SolidBrush veil = new SolidBrush(Fade(c.Bg, 105))) g.FillRectangle(veil, canvas);
        }
    }
    private static Color Mix(Color first, Color second, float amount)
    {
        return Color.FromArgb((int)(first.R + (second.R - first.R) * amount),
            (int)(first.G + (second.G - first.G) * amount), (int)(first.B + (second.B - first.B) * amount));
    }
    private static float WaterLine(double fraction)
    {
        // Invert circular-segment area so 50% fills half the vessel and other values remain accurate.
        double low = -1, high = 1;
        for (int i = 0; i < 25; i++) {
            double position = (low + high) / 2;
            double area = (Math.Acos(position) - position * Math.Sqrt(Math.Max(0, 1 - position * position))) / Math.PI;
            if (area > fraction) low = position; else high = position;
        }
        return (float)((low + high) / 2);
    }
    private GraphicsPath WaterWave(RectangleF bowl, float level, float amplitude, float offset)
    {
        GraphicsPath wave = new GraphicsPath();
        List<PointF> points = new List<PointF>();
        float time = MotionActive ? phase : 0;
        for (float x = bowl.Left - 2; x <= bowl.Right + 4; x += 2) {
            float unit = (x - bowl.Left) / bowl.Width;
            float y = level + amplitude * (float)(.72 * Math.Sin(unit * Math.PI * 2 - time + offset)
                + .28 * Math.Sin(unit * Math.PI * 4 + time * 2 + offset));
            points.Add(new PointF(x, y));
        }
        wave.AddLines(points.ToArray());
        wave.AddLine(points[points.Count - 1], new PointF(bowl.Right + 4, bowl.Bottom + 4));
        wave.AddLine(bowl.Right + 4, bowl.Bottom + 4, bowl.Left - 2, bowl.Bottom + 4);
        wave.CloseFigure(); return wave;
    }
    private void Liquid(Graphics g, int cx, double? amount, Color color, string name, DateTime? reset)
    {
        RectangleF bowl = new RectangleF(cx - 58, 79, 116, 116);
        using (SolidBrush shadow = new SolidBrush(Fade(Color.Black, material == 3 ? 18 : 48)))
            g.FillEllipse(shadow, cx - 46, 186, 92, 12);
        using (GraphicsPath vessel = new GraphicsPath()) {
            vessel.AddEllipse(bowl);
            using (LinearGradientBrush glass = new LinearGradientBrush(bowl, Mix(Colors.Surface, Color.White, .05F), Colors.Bg, 90F)) g.FillPath(glass, vessel);
            double fraction = amount.HasValue ? amount.Value / 100 : 0;
            float level = bowl.Y + bowl.Height / 2 + WaterLine(fraction) * bowl.Height / 2;
            float amplitude = (float)(4.4 * Math.Min(1, Math.Min(fraction, 1 - fraction) * 15));
            float offset = cx == 101 ? 0 : 1.8F;
            using (GraphicsPath front = WaterWave(bowl, level, amplitude, offset))
            using (GraphicsPath back = WaterWave(bowl, level - 2, amplitude * .8F, offset + 2.1F)) {
                GraphicsState state = g.Save(); g.SetClip(vessel, CombineMode.Intersect);
                if (amount.HasValue && amount.Value > 0) {
                    using (SolidBrush distant = new SolidBrush(Fade(Mix(WaterTint, Color.White, .3F), 95))) g.FillPath(distant, back);
                    using (LinearGradientBrush water = new LinearGradientBrush(bowl, Mix(WaterTint, Color.White, .42F), Mix(WaterTint, Hex("#16465A"), .62F), 90F)) g.FillPath(water, front);
                    GraphicsState waterState = g.Save(); g.SetClip(front, CombineMode.Intersect);
                    // Slow curved refraction bands and bubbles remain inside the actual water volume.
                    float time = MotionActive ? phase : 0;
                    for (int i = 0; i < 4; i++) {
                        float y = bowl.Top + 27 + i * 25;
                        float shift = 7 * (float)Math.Sin(time + i * 1.3 + offset);
                        using (Pen refraction = new Pen(Fade(Color.White, 20 + i * 3), 1.4F))
                            g.DrawBezier(refraction, bowl.Left - 8, y, cx - 20, y + shift + 12, cx + 20, y - shift - 12, bowl.Right + 8, y - 4);
                    }
                    if (MotionActive) for (int i = 0; i < 7; i++) {
                        float progress = MotionPosition(i * .147F + offset * .1F);
                        float x = cx - 41 + (i * 23 % 81) + 2.5F * (float)Math.Sin(phase * 2 + i);
                        float y = bowl.Bottom - progress * (bowl.Height + 12);
                        float radius = 1.1F + (i % 3) * .6F;
                        using (Pen bubble = new Pen(Fade(Color.White, (int)(90 * Math.Sin(progress * Math.PI))), .7F))
                            g.DrawEllipse(bubble, x - radius, y - radius, radius * 2, radius * 2);
                    }
                    PaintSwimmingKoi(g, bowl, level + amplitude, offset);
                    g.Restore(waterState);
                    // Follow the wave surface without outlining the bottom of the fill path.
                    PointF[] surface = new PointF[61];
                    for (int i = 0; i < surface.Length; i++) {
                        float unit = i / 60F;
                        surface[i] = new PointF(bowl.Left + unit * bowl.Width,
                            level + amplitude * (float)(.72 * Math.Sin(unit * Math.PI * 2 - time + offset) + .28 * Math.Sin(unit * Math.PI * 4 + time * 2 + offset)));
                    }
                    using (Pen meniscus = new Pen(Fade(Color.White, 125), .9F)) g.DrawLines(meniscus, surface);
                }
                Label(g, Percent(amount), cx - 52, 109, 28, Colors.Text, true, 104, StringAlignment.Center, true);
                Label(g, name, cx - 50, 151, 10, Colors.Muted, false, 100, StringAlignment.Center);
                if (amount.HasValue && amount.Value > 0) {
                    g.SetClip(front, CombineMode.Intersect);
                    Label(g, Percent(amount), cx - 52, 109, 28, Hex("#F6FEFF"), true, 104, StringAlignment.Center, true);
                    Label(g, name, cx - 50, 151, 10, Hex("#DEF6FF"), false, 100, StringAlignment.Center);
                }
                g.Restore(state);
            }
            using (Pen rim = new Pen(Fade(Colors.Text, 58), .8F)) g.DrawEllipse(rim, bowl);
            using (Pen glassHighlight = new Pen(Fade(Color.White, material == 3 ? 205 : 95), 1.7F))
                g.DrawArc(glassHighlight, cx - 54, 83, 108, 108, 205, 68);
            using (Pen lowerRim = new Pen(Fade(WaterTint, 105), 1.1F)) g.DrawArc(lowerRim, cx - 54, 83, 108, 108, 30, 100);
        }
        if (showReset) Label(g, Reset(reset), cx - 75, 202, 9.3F, Colors.Muted, false, 150, StringAlignment.Center);
    }
    private static string Percent(double? value) { return value.HasValue ? value.Value.ToString("0") + "%" : "--%"; }
    private float MotionPosition(float offset)
    {
        float value = phase / (2F * (float)Math.PI) + offset;
        return value - (float)Math.Floor(value);
    }
    private void DrawRingMotion(Graphics g, Rectangle rect, float sweep, Color color, float offset)
    {
        if (!MotionActive || animationStyle == 1) return;
        float t = MotionPosition(offset), cx = rect.X + rect.Width / 2F, cy = rect.Y + rect.Height / 2F;
        float angle = -90 + sweep * t;
        double radians = angle * Math.PI / 180;
        float x = cx + rect.Width / 2F * (float)Math.Cos(radians), y = cy + rect.Height / 2F * (float)Math.Sin(radians);
        if (animationStyle == 2) {
            float start = t * (sweep + 42) - 42, end = Math.Min(sweep, start + 42);
            start = Math.Max(0, start);
            if (end > start) using (Pen silk = new Pen(Fade(Color.White, 115), 5)) {
                silk.StartCap = silk.EndCap = LineCap.Round; g.DrawArc(silk, rect, -90 + start, end - start);
            }
        } else if (animationStyle == 6) {
            for (int layer = 0; layer < 3; layer++) {
                int count = Math.Max(2, (int)Math.Ceiling(sweep / 3));
                PointF[] flow = new PointF[count + 1];
                for (int i = 0; i <= count; i++) {
                    double theta = (-90 + sweep * i / count) * Math.PI / 180;
                    float radius = rect.Width / 2F + (layer - 1) * 3 + 1.3F * (float)Math.Sin(theta * 5 - phase * 2 + offset + layer);
                    flow[i] = new PointF(cx + radius * (float)Math.Cos(theta), cy + radius * (float)Math.Sin(theta));
                }
                using (Pen stream = new Pen(Fade(Color.White, layer == 1 ? 105 : 45), layer == 1 ? 1.5F : .8F)) g.DrawLines(stream, flow);
            }
        } else if (animationStyle == 4) {
            radians = (-90 + sweep) * Math.PI / 180;
            x = cx + rect.Width / 2F * (float)Math.Cos(radians); y = cy + rect.Height / 2F * (float)Math.Sin(radians);
            for (int i = 0; i < 2; i++) {
                float pulse = (t + i * .5F) % 1, radius = 5 + pulse * 15;
                using (Pen ripple = new Pen(Fade(color, (int)(110 * (1 - pulse))), 1.2F))
                    g.DrawEllipse(ripple, x - radius, y - radius, radius * 2, radius * 2);
            }
        } else {
            if (animationStyle == 5) for (int i = 10; i > 0; i--) {
                float tail = sweep * t - i * 2.3F;
                if (tail < 0) continue;
                using (Pen comet = new Pen(Fade(Color.White, 150 - i * 13), 4.5F - i * .2F))
                    g.DrawArc(comet, rect, -90 + tail, Math.Min(2.5F, sweep - tail));
            }
            using (SolidBrush halo = new SolidBrush(Fade(color, 55))) g.FillEllipse(halo, x - 7, y - 7, 14, 14);
            using (SolidBrush point = new SolidBrush(Fade(Color.White, 225))) g.FillEllipse(point, x - 2.4F, y - 2.4F, 4.8F, 4.8F);
        }
    }
    private void DrawLinearMotion(Graphics g, RectangleF area, Color color, float offset)
    {
        if (!MotionActive || area.Width <= 0) return;
        float t = MotionPosition(offset), cy = area.Y + area.Height / 2;
        GraphicsState state = g.Save();
        using (GraphicsPath clip = Round(area, Math.Min(8, area.Height / 2))) g.SetClip(clip, CombineMode.Intersect);
        if (animationStyle == 1) {
            using (SolidBrush light = new SolidBrush(Fade(Color.White, (int)(15 + 30 * (.5 + .5 * Math.Sin(phase)))))) g.FillRectangle(light, area);
        } else if (animationStyle == 2) {
            RectangleF band = new RectangleF(area.X - 45 + t * (area.Width + 90), area.Y, 45, area.Height);
            using (LinearGradientBrush silk = new LinearGradientBrush(band, Color.Transparent, Color.White, 0F)) {
                silk.InterpolationColors = new ColorBlend { Positions = new[] { 0F, .5F, 1F }, Colors = new[] { Fade(Color.White, 0), Fade(Color.White, 130), Fade(Color.White, 0) } };
                g.FillRectangle(silk, band);
            }
        } else if (animationStyle == 6) {
            for (int layer = 0; layer < 3; layer++) {
                int count = Math.Max(2, (int)Math.Ceiling(area.Width / 3));
                PointF[] flow = new PointF[count + 1];
                for (int i = 0; i <= count; i++) {
                    float unit = i / (float)count;
                    flow[i] = new PointF(area.Left + unit * area.Width, area.Y + area.Height * (.25F + layer * .25F)
                        + area.Height * .12F * (float)Math.Sin(unit * Math.PI * 4 - phase * 2 + layer + offset));
                }
                using (Pen stream = new Pen(Fade(Color.White, layer == 1 ? 135 : 65), layer == 1 ? 1.1F : .7F)) g.DrawLines(stream, flow);
            }
        } else if (animationStyle == 4) {
            for (int i = 0; i < 2; i++) {
                float pulse = (t + i * .5F) % 1, radius = pulse * Math.Max(30, area.Width);
                using (Pen ripple = new Pen(Fade(Color.White, (int)(150 * (1 - pulse))), 3))
                    g.DrawEllipse(ripple, area.Right - radius, cy - radius, radius * 2, radius * 2);
            }
        } else {
            float x = area.X + t * area.Width;
            if (animationStyle == 5) for (int i = 12; i > 0; i--)
                using (SolidBrush tail = new SolidBrush(Fade(Color.White, 150 - i * 11)))
                    g.FillEllipse(tail, x - i * 2.5F - 2, cy - 2, 5, 4);
            using (SolidBrush halo = new SolidBrush(Fade(Color.White, 45))) g.FillEllipse(halo, x - 8, cy - 7, 16, 14);
            using (SolidBrush point = new SolidBrush(Fade(Color.White, 210))) g.FillEllipse(point, x - 2.5F, cy - 2.5F, 5, 5);
        }
        g.Restore(state);
    }
    private static string Reset(DateTime? time)
    {
        if (!time.HasValue) return "重置时间未知";
        TimeSpan left = time.Value - DateTime.Now;
        if (left.TotalSeconds <= 0) return "即将重置";
        return (left.TotalDays >= 1 ? (int)left.TotalDays + "天 " + left.Hours + "时" : left.Hours + "时 " + left.Minutes + "分") + "后重置";
    }
    private void Ring(Graphics g, int cx, double? amount, Color color, string name, DateTime? reset)
    {
        int cy = 139; Rectangle rect = new Rectangle(cx - 52, cy - 52, 104, 104);
        Palette c = Colors;
        using (Pen shadow = new Pen(Fade(Color.Black, material == 3 ? 31 : 65), 23))
            g.DrawArc(shadow, new Rectangle(rect.X, rect.Y + 3, rect.Width, rect.Height), -90, 360);
        using (Pen track = new Pen(c.Track, 18)) g.DrawArc(track, rect, -90, 360);
        using (Pen bevel = new Pen(Fade(Color.White, material == 3 ? 96 : 32), 2))
            g.DrawArc(bevel, new Rectangle(rect.X - 8, rect.Y - 8, rect.Width + 16, rect.Height + 16), -90, 360);
        using (GraphicsPath inside = new GraphicsPath()) {
            inside.AddEllipse(cx - 40, cy - 40, 80, 80);
            using (PathGradientBrush shade = new PathGradientBrush(inside)) {
                shade.CenterColor = c.Surface;
                shade.SurroundColors = new[] { c.Bg };
                g.FillPath(shade, inside);
            }
        }
        if (amount.HasValue && amount.Value > 0) {
            float sweep = (float)Math.Min(360, amount.Value * 3.6);
            using (Pen lowShadow = new Pen(Fade(Color.Black, 85), 19))
                g.DrawArc(lowShadow, new Rectangle(rect.X, rect.Y + 2, rect.Width, rect.Height), -90, sweep);
            using (Pen glow = new Pen(Fade(color, MotionActive && animationStyle == 1 ? 30 + (int)(28 * (.5 + .5 * Math.Sin(phase))) : 30), 23)) g.DrawArc(glow, rect, -90, sweep);
            using (LinearGradientBrush gradient = new LinearGradientBrush(rect, color, B, 35F))
            using (Pen arc = new Pen(gradient, 15)) g.DrawArc(arc, rect, -90, sweep);
            using (Pen crest = new Pen(Fade(Color.White, material == 3 ? 82 : 55), 2.2F))
                g.DrawArc(crest, new Rectangle(rect.X - 6, rect.Y - 6, rect.Width + 12, rect.Height + 12), -90, sweep);
            double angle = (-90 + sweep) * Math.PI / 180;
            float endX = cx + 52F * (float)Math.Cos(angle), endY = cy + 52F * (float)Math.Sin(angle);
            using (SolidBrush halo = new SolidBrush(Fade(color, 55))) g.FillEllipse(halo, endX - 10, endY - 10, 20, 20);
            using (SolidBrush bead = new SolidBrush(color)) g.FillEllipse(bead, endX - 5, endY - 5, 10, 10);
            using (SolidBrush glint = new SolidBrush(Fade(Color.White, 170))) g.FillEllipse(glint, endX - 3, endY - 3, 3, 3);
            DrawRingMotion(g, rect, sweep, color, cx == 101 ? 0 : .18F);
        }
        Label(g, Percent(amount), cx - 45, cy - 24, 26, Colors.Text, true, 90, StringAlignment.Center, true);
        Label(g, name, cx - 45, cy + 12, 10, Colors.Muted, false, 90, StringAlignment.Center);
        if (showReset) Label(g, Reset(reset), cx - 75, 202, 9.3F, Colors.Muted, false, 150, StringAlignment.Center);
    }
    private void Bar(Graphics g, int y, double? amount, Color first, Color second, string name, DateTime? reset)
    {
        Label(g, name, 22, y - 4, 11, Colors.Text, true);
        Label(g, Percent(amount), 253, y - 15, 23, Colors.Text, true, 84, StringAlignment.Far, true);
        RectangleF track = new RectangleF(22, y + 30, 315, 16);
        using (SolidBrush shadow = new SolidBrush(Fade(Color.Black, 55)))
        using (GraphicsPath under = Round(new RectangleF(22, y + 33, 315, 17), 8)) g.FillPath(shadow, under);
        using (GraphicsPath path = Round(track, 8)) using (SolidBrush brush = new SolidBrush(Colors.Track)) g.FillPath(brush, path);
        if (amount.HasValue && amount.Value > 0) {
            float width = Math.Max(16, 315F * (float)Math.Min(100, amount.Value) / 100F);
            RectangleF fill = new RectangleF(22, y + 30, width, 16);
            using (GraphicsPath path = Round(fill, 8)) using (LinearGradientBrush brush = new LinearGradientBrush(fill, first, second, 0F)) g.FillPath(brush, path);
            using (Pen rim = new Pen(Fade(Color.White, 90), 1)) g.DrawLine(rim, 29, y + 32, 16 + width, y + 32);
            using (SolidBrush halo = new SolidBrush(Fade(second, 47))) g.FillEllipse(halo, 22 + width - 13, y + 28, 22, 20);
            using (SolidBrush shine = new SolidBrush(Fade(Color.White, 170))) g.FillEllipse(shine, 22 + width - 8, y + 34, 5, 5);
            DrawLinearMotion(g, fill, first, y == 79 ? 0 : .18F);
        }
        for (int i = 1; i < 4; i++) using (Pen tick = new Pen(Fade(Colors.Bg, 105))) g.DrawLine(tick, 22 + i * 315 / 4, y + 33, 22 + i * 315 / 4, y + 43);
        if (showReset) Label(g, Reset(reset), 22, y + 50, 9.3F, Colors.Muted);
    }
    private void Segments(Graphics g, int y, double? amount, Color color, string name, DateTime? reset)
    {
        Label(g, name, 22, y - 4, 11, Colors.Text, true);
        Label(g, Percent(amount), 253, y - 15, 23, Colors.Text, true, 84, StringAlignment.Far, true);
        int count = 28, filled = amount.HasValue ? (int)Math.Round(amount.Value * count / 100) : 0;
        for (int i = 0; i < count; i++) {
            RectangleF cell = new RectangleF(22 + i * 11.3F, y + 31, 7.4F, 19);
            using (GraphicsPath path = Round(new RectangleF(cell.X, cell.Y + 2, cell.Width, cell.Height), 3))
            using (SolidBrush shadow = new SolidBrush(Fade(Color.Black, 54))) g.FillPath(shadow, path);
            using (GraphicsPath path = Round(cell, 3)) {
                using (SolidBrush brush = new SolidBrush(i < filled ? color : Colors.Track)) g.FillPath(brush, path);
                if (i < filled && MotionActive) {
                    float position = MotionPosition(y == 83 ? 0 : .18F), distance = Math.Abs(i - position * Math.Max(1, filled - 1));
                    double light = animationStyle == 1 ? .5 + .5 * Math.Sin(phase)
                        : animationStyle == 6 ? .5 + .5 * Math.Sin(i * .7 - phase * 2)
                        : animationStyle == 4 ? Math.Max(0, Math.Cos(i * .65 - phase * 2))
                        : Math.Max(0, 1 - distance / (animationStyle == 5 ? 5 : 2));
                    using (SolidBrush highlight = new SolidBrush(Fade(Color.White, (int)(light * 120)))) g.FillPath(highlight, path);
                }
            }
            if (i < filled) using (Pen highlight = new Pen(Fade(Color.White, 75))) g.DrawLine(highlight, cell.X + 2, cell.Y + 2, cell.X + 2, cell.Y + 14);
        }
        if (showReset) Label(g, Reset(reset), 22, y + 53, 9.3F, Colors.Muted);
    }
    private void Number(Graphics g, int x, double? amount, Color color, string name, DateTime? reset)
    {
        Label(g, name, x + 3, 82, 10.5F, Colors.Muted);
        Label(g, amount.HasValue ? amount.Value.ToString("0") : "--", x, 103, 48, Colors.Text, true, 130, StringAlignment.Near, true);
        Label(g, "%", x + 113, 138, 17, color, false, 30, StringAlignment.Near, true);
        using (SolidBrush brush = new SolidBrush(Colors.Track)) g.FillRectangle(brush, x + 3, 180, 145, 4);
        if (amount.HasValue && amount.Value > 0) {
            RectangleF fill = new RectangleF(x + 3, 180, 145 * (float)amount.Value / 100F, 4);
            using (SolidBrush brush = new SolidBrush(color)) g.FillRectangle(brush, fill);
            DrawLinearMotion(g, fill, color, x == 20 ? 0 : .18F);
        }
        if (showReset) Label(g, Reset(reset), x + 3, 196, 9.3F, Colors.Muted, false, 154);
    }

    private async void RefreshUsage()
    {
        if (busy) return;
        busy = true; failed = false; Redraw();
        try {
            QuotaSnapshot snapshot = await Task.Run(() => accountReader.Read());
            if (!IsDisposed) ApplySnapshot(snapshot);
        } catch (Exception) {
            ApplySnapshot(QuotaSnapshot.Empty(QuotaState.Unavailable, "找不到额度信息", "暂时无法读取本机 Codex 信息\n请稍后重新检测"));
        } finally { busy = false; Redraw(); }
    }
    private void ApplySnapshot(QuotaSnapshot snapshot)
    {
        quotaState = snapshot.State;
        stateTitle = snapshot.Title; stateDetail = snapshot.Detail ?? "";
        primary = secondary = null; primaryReset = secondaryReset = null; lastUpdated = DateTime.MinValue;
        failed = quotaState != QuotaState.Connected;
        if (!failed) {
            plan = String.IsNullOrEmpty(snapshot.Plan) ? "CODEX" : snapshot.Plan.ToUpperInvariant() + "  /  CODEX";
            Window(snapshot.Limits, "primary", out primary, out primaryReset);
            Window(snapshot.Limits, "secondary", out secondary, out secondaryReset);
            lastUpdated = DateTime.Now; status = "用量已更新";
        } else {
            plan = quotaState == QuotaState.NotSignedIn ? "本机 Codex · 未登录" : "本机 Codex · 信息不可用";
            status = quotaState == QuotaState.NotSignedIn ? "未登录 · 每 60 秒自动检测" : "信息不可用 · 可重新检测";
        }
        UpdateHint();
    }
    private static void Window(Dictionary<string, object> limits, string key, out double? amount, out DateTime? reset)
    {
        amount = null; reset = null; object raw;
        if (!limits.TryGetValue(key, out raw) || raw == null) return;
        var window = (Dictionary<string, object>)raw;
        amount = Math.Max(0, Math.Min(100, 100 - Convert.ToDouble(window["usedPercent"])));
        object epoch;
        if (window.TryGetValue("resetsAt", out epoch) && epoch != null)
            reset = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(Convert.ToDouble(epoch)).ToLocalTime();
    }
    private void Preview(string path, int selectedMode, int selectedPalette, int selectedMaterial, int selectedWidth, int selectedAnimation, float selectedPhase)
    {
        mode = selectedMode; palette = selectedPalette; material = selectedMaterial; cachedColors = null;
        if (selectedWidth > 0) ResizeToWidth(selectedWidth);
        animationStyle = selectedAnimation; animate = selectedAnimation > 0; phase = selectedPhase;
        primary = 37; secondary = 82;
        quotaState = QuotaState.Connected;
        primaryReset = DateTime.Now.AddHours(3).AddMinutes(17); secondaryReset = DateTime.Now.AddDays(4).AddHours(7);
        plan = "PLUS  /  CODEX"; lastUpdated = DateTime.Now;
        using (Bitmap image = CreateFrame()) image.Save(path, ImageFormat.Png);
    }
    [STAThread]
    public static void Main(string[] args)
    {
        bool standalone = args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal);
        string appDirectory = standalone ? AppDomain.CurrentDomain.BaseDirectory : Path.GetDirectoryName(Path.GetFullPath(args[0]));
        string userDirectory = standalone ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexUsageWidget") : appDirectory;
        if (standalone) {
            string[] normalized = new string[args.Length + 1];
            normalized[0] = Path.Combine(userDirectory, "settings-context");
            args.CopyTo(normalized, 1); args = normalized;
        }
        if (args.Length > 1 && args[1] == "--check") {
            QuotaSnapshot snapshot = new CodexAccountReader(appDirectory).Read();
            Console.WriteLine(new JavaScriptSerializer().Serialize(new { state = snapshot.State.ToString(), title = snapshot.Title, detail = snapshot.Detail, plan = snapshot.Plan, limits = snapshot.Limits }));
            Environment.ExitCode = snapshot.State == QuotaState.Connected ? 0 : (int)snapshot.State + 1;
            return;
        }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        using (CodexUsageWidgetV2 widget = new CodexUsageWidgetV2(appDirectory, userDirectory)) {
            if (args.Length == 4 && args[1] == "--preview") {
                string[] choices = args[3].Split(',');
                widget.Preview(args[2], int.Parse(choices[0]), int.Parse(choices[1]),
                    choices.Length > 2 ? int.Parse(choices[2]) : 0, choices.Length > 3 ? int.Parse(choices[3]) : 0,
                    choices.Length > 4 ? int.Parse(choices[4]) : 2, choices.Length > 5 ? float.Parse(choices[5], System.Globalization.CultureInfo.InvariantCulture) : 1.7F);
            } else {
                Timer smoke = null;
                if (args.Length > 1 && args[1] == "--smoke") {
                    if (args.Length > 2) widget.mode = Math.Max(0, Math.Min(Modes.Length - 1, int.Parse(args[2])));
                    if (args.Length > 3) { widget.animationStyle = Math.Max(0, Math.Min(Animations.Length - 1, int.Parse(args[3]))); widget.animate = widget.animationStyle > 0; }
                    smoke = new Timer { Interval = 5000 };
                    smoke.Tick += (s, e) => { smoke.Stop(); widget.Close(); };
                    smoke.Start();
                }
                try {
                    Application.Run(widget);
                    if (smoke != null) Console.WriteLine("Native frames presented: " + widget.presentedFrames);
                }
                catch (Exception ex) {
                    File.WriteAllText(Path.Combine(widget.dataDirectory, "runtime-error.log"), ex.ToString(), Encoding.UTF8);
                    throw;
                }
                finally { if (smoke != null) smoke.Dispose(); }
            }
        }
    }
}
