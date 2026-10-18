using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

public sealed partial class CodexUsageWidgetV2
{
    private static readonly Color WaterTint = Color.FromArgb(126, 219, 240);
    private Bitmap orbUnderlay, orbGlaze, orbScaledUnderlay, orbScaledGlaze;
    private Size orbLayerSize;
    private int layerPalette = -1, layerMaterial = -1, layerA, layerB;
    private enum DockSide { None, Left, Right, Top, Bottom }
    private DockSide dockSide;
    private bool autoDock = true, dockCollapsed, maskIsOrb, pointerDown, pointerMoved, geometryActive, hoverReady;
    private int orbWidth = 176, panelWidth = 280, dockSize = 68;
    private double dockFraction = .5;
    private string dockMonitor = "";
    private readonly Timer dockTimer = new Timer();
    private readonly ToolTip hint = new ToolTip();
    private readonly Stopwatch geometryClock = new Stopwatch();
    private Rectangle geometryFrom, geometryTo;
    private DateTime collapseAfter, hoverAfter;
    private bool OrbSurface { get { return mode == 5 || dockCollapsed; } }
    protected override bool ShowWithoutActivation { get { return true; } }

    private Size ExpandedSize()
    {
        return mode == 5 ? new Size(orbWidth, orbWidth) : new Size(panelWidth, (int)Math.Round(panelWidth * 176 / 360.0));
    }
    private void LoadCompactSettings(Dictionary<string, object> data)
    {
        if (!data.ContainsKey("compactVersion")) {
            mode = 5; showReset = false;
        } else {
            if (data.ContainsKey("orbWidth")) orbWidth = Math.Max(128, Math.Min(280, Convert.ToInt32(data["orbWidth"])));
            if (data.ContainsKey("panelWidth")) panelWidth = Math.Max(220, Math.Min(480, Convert.ToInt32(data["panelWidth"])));
            if (data.ContainsKey("autoDock")) autoDock = Convert.ToBoolean(data["autoDock"]);
            if (data.ContainsKey("dockSide")) dockSide = (DockSide)Math.Max(0, Math.Min(4, Convert.ToInt32(data["dockSide"])));
            if (data.ContainsKey("dockFraction")) dockFraction = Math.Max(0, Math.Min(1, Convert.ToDouble(data["dockFraction"])));
            if (data.ContainsKey("dockMonitor")) dockMonitor = Convert.ToString(data["dockMonitor"]);
        }
        ClientSize = ExpandedSize();
    }
    private void SaveCompactSettings(Dictionary<string, object> data)
    {
        data["compactVersion"] = 2; data["orbWidth"] = orbWidth; data["panelWidth"] = panelWidth;
        data["autoDock"] = autoDock; data["dockSide"] = (int)dockSide; data["dockFraction"] = dockFraction;
        data["dockMonitor"] = dockMonitor;
        data["width"] = mode == 5 ? orbWidth : panelWidth;
    }
    private void InitializeCompactInteraction()
    {
        ContextMenuStrip = MakeCompactMenu();
        MouseDown += PointerDown; MouseMove += PointerMove; MouseUp += PointerUp;
        MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left && !resizing) RefreshUsage(); };
        MouseWheel += (s, e) => {
            if ((ModifierKeys & Keys.Control) == 0) return;
            DetachDock(); ResizeToWidth((mode == 5 ? orbWidth : panelWidth) + (e.Delta > 0 ? 12 : -12));
            ClampToScreen(); SaveSettings(); Redraw();
        };
        MouseCaptureChanged += (s, e) => {
            if (Capture || !pointerDown) return;
            pointerDown = dragging = resizing = false; pointerMoved = false;
        };
        hint.ShowAlways = true;
        hint.AutoPopDelay = 20000; hint.InitialDelay = 450; hint.ReshowDelay = 250;
        MouseEnter += (s, e) => UpdateHint();
        dockTimer.Interval = 30; dockTimer.Tick += (s, e) => DockTick(); dockTimer.Start();
        ContextMenuStrip.Closed += (s, e) => collapseAfter = DateTime.UtcNow.AddMilliseconds(850);
        Shown += (s, e) => {
            if (autoDock && dockSide != DockSide.None) {
                dockCollapsed = true; hoverReady = false; hoverAfter = DateTime.UtcNow.AddMilliseconds(600);
                Bounds = DockBounds(new Size(dockSize, dockSize));
            }
            UpdateHint(); Redraw();
        };
    }
    private bool ResizeHit(Point point)
    {
        if (dockCollapsed) return false;
        if (mode != 5) return point.X >= Width - 18 && point.Y >= Height - 18;
        float x = point.X - Width / 2F, y = point.Y - Height / 2F;
        float distance = (float)Math.Sqrt(x * x + y * y);
        return x > Width * .23F && y > Height * .23F && distance > Width * .40F;
    }
    private void PointerDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (geometryActive) { geometryActive = false; Bounds = geometryTo; }
        pointerDown = true; pointerMoved = false;
        dragAt = Cursor.Position; originalAt = Location; originalSize = ExpandedSize();
        resizing = ResizeHit(e.Location); dragging = !resizing;
        Capture = true; hint.Active = false;
    }
    private void PointerMove(object sender, MouseEventArgs e)
    {
        Cursor = ResizeHit(e.Location) ? Cursors.SizeNWSE : Cursors.SizeAll;
        if (!pointerDown) return;
        Point current = Cursor.Position;
        int dx = current.X - dragAt.X, dy = current.Y - dragAt.Y;
        if (!pointerMoved && Math.Abs(dx) + Math.Abs(dy) < 4) return;
        if (!pointerMoved) {
            pointerMoved = true;
            if (dockSide != DockSide.None) {
                Size expanded = ExpandedSize();
                float ratioX = Math.Max(0, Math.Min(1, (dragAt.X - Left) / (float)Math.Max(1, Width)));
                float ratioY = Math.Max(0, Math.Min(1, (dragAt.Y - Top) / (float)Math.Max(1, Height)));
                DetachDock();
                originalAt = new Point(dragAt.X - (int)(expanded.Width * ratioX), dragAt.Y - (int)(expanded.Height * ratioY));
                Location = originalAt;
            }
        }
        if (resizing) { ResizeToWidth(originalSize.Width + dx); Redraw(); }
        else if (dragging) Location = new Point(originalAt.X + dx, originalAt.Y + dy);
    }
    private void PointerUp(object sender, MouseEventArgs e)
    {
        if (!pointerDown || e.Button != MouseButtons.Left) return;
        bool moved = pointerMoved;
        pointerDown = dragging = resizing = false; pointerMoved = false; Capture = false; hint.Active = true;
        if (!moved) return;
        if (!autoDock || !TryDock()) ClampToScreen();
        SaveSettings(); Redraw();
    }
    private void ClampToScreen()
    {
        Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
        Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, Left)), Math.Max(area.Top, Math.Min(area.Bottom - Height, Top)));
    }
    private static DockSide NearEdge(Rectangle bounds, Rectangle area, int threshold)
    {
        int[] distances = { Math.Max(0, bounds.Left - area.Left), Math.Max(0, area.Right - bounds.Right), Math.Max(0, bounds.Top - area.Top), Math.Max(0, area.Bottom - bounds.Bottom) };
        int nearest = 0;
        for (int i = 1; i < distances.Length; i++) if (distances[i] < distances[nearest]) nearest = i;
        return distances[nearest] <= threshold ? (DockSide)(nearest + 1) : DockSide.None;
    }
    private bool TryDock()
    {
        Screen screen = Screen.FromRectangle(Bounds); Rectangle area = screen.WorkingArea;
        DockSide side = NearEdge(Bounds, area, 22);
        if (side == DockSide.None) return false;
        dockSide = side; dockMonitor = screen.DeviceName;
        bool vertical = side == DockSide.Left || side == DockSide.Right;
        dockFraction = vertical ? (Top + Height / 2.0 - area.Top) / area.Height : (Left + Width / 2.0 - area.Left) / area.Width;
        dockFraction = Math.Max(0, Math.Min(1, dockFraction));
        hoverReady = false; hoverAfter = DateTime.UtcNow.AddMilliseconds(800);
        SetDockCollapsed(true, true); return true;
    }
    private Screen DockScreen()
    {
        foreach (Screen screen in Screen.AllScreens) if (screen.DeviceName == dockMonitor) return screen;
        return Screen.FromRectangle(Bounds);
    }
    private Rectangle DockBounds(Size size)
    {
        Rectangle area = DockScreen().WorkingArea;
        int centerX = area.Left + (int)Math.Round(area.Width * dockFraction), centerY = area.Top + (int)Math.Round(area.Height * dockFraction);
        int x = centerX - size.Width / 2, y = centerY - size.Height / 2;
        if (dockSide == DockSide.Left) x = area.Left;
        else if (dockSide == DockSide.Right) x = area.Right - size.Width;
        else if (dockSide == DockSide.Top) y = area.Top;
        else if (dockSide == DockSide.Bottom) y = area.Bottom - size.Height;
        return new Rectangle(Math.Max(area.Left, Math.Min(area.Right - size.Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - size.Height, y)), size.Width, size.Height);
    }
    private void SetDockCollapsed(bool collapsed, bool smooth)
    {
        dockCollapsed = collapsed;
        Rectangle target = DockBounds(collapsed ? new Size(dockSize, dockSize) : ExpandedSize());
        if (!smooth) { geometryActive = false; Bounds = target; Redraw(); return; }
        geometryFrom = Bounds; geometryTo = target; geometryActive = true; geometryClock.Restart();
    }
    private void DetachDock()
    {
        geometryActive = false; dockSide = DockSide.None; dockCollapsed = false;
        ClientSize = ExpandedSize(); maskSize = Size.Empty;
    }
    private void DockTick()
    {
        if (IsDisposed) return;
        if (geometryActive) {
            double fraction = Math.Min(1, geometryClock.Elapsed.TotalMilliseconds / 190);
            double ease = 1 - Math.Pow(1 - fraction, 3);
            Rectangle next = new Rectangle(
                (int)Math.Round(geometryFrom.X + (geometryTo.X - geometryFrom.X) * ease),
                (int)Math.Round(geometryFrom.Y + (geometryTo.Y - geometryFrom.Y) * ease),
                (int)Math.Round(geometryFrom.Width + (geometryTo.Width - geometryFrom.Width) * ease),
                (int)Math.Round(geometryFrom.Height + (geometryTo.Height - geometryFrom.Height) * ease));
            Bounds = next;
            if (fraction >= 1) { geometryActive = false; geometryClock.Stop(); }
            Redraw(); return;
        }
        if (!autoDock || dockSide == DockSide.None || pointerDown || ContextMenuStrip.Visible) return;
        bool inside = Bounds.Contains(Cursor.Position);
        if (dockCollapsed) {
            if (!inside) hoverReady = true;
            if (inside && hoverReady && DateTime.UtcNow >= hoverAfter) {
                collapseAfter = DateTime.UtcNow.AddMilliseconds(650); SetDockCollapsed(false, true);
            }
        } else if (inside) collapseAfter = DateTime.UtcNow.AddMilliseconds(650);
        else if (DateTime.UtcNow >= collapseAfter) { hoverReady = true; SetDockCollapsed(true, true); }
    }
    private void ChangeCompactMode(int selected)
    {
        mode = selected; maskSize = Size.Empty;
        if (dockSide != DockSide.None) SetDockCollapsed(false, false); else { ClientSize = ExpandedSize(); ClampToScreen(); }
        SaveSettings(); Redraw();
    }
    private ContextMenuStrip MakeCompactMenu()
    {
        ContextMenuStrip menu = new ContextMenuStrip { Font = new Font("Microsoft YaHei UI", 9) };
        ToolStripMenuItem styles = new ToolStripMenuItem("额度显示");
        foreach (int value in new[] { 5, 4, 0, 1, 2, 3 }) {
            int selected = value; ToolStripMenuItem item = new ToolStripMenuItem(Modes[value]) { Tag = value };
            item.Click += (s, e) => ChangeCompactMode(selected); styles.DropDownItems.Add(item);
        }
        menu.Items.Add(styles);
        ToolStripMenuItem themes = new ToolStripMenuItem("外观 / 外环颜色（水色固定）");
        for (int i = 0; i < 6; i++) { int selected = i; themes.DropDownItems.Add(Palettes[i].Name, null, (s, e) => { palette = selected; ChangePalette(); }); }
        themes.DropDownItems.Add("自定义强调色…", null, (s, e) => {
            using (ColorDialog dialog = new ColorDialog { Color = customAccent, FullOpen = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) { palette = 6; customAccent = dialog.Color; ChangePalette(); }
        });
        menu.Items.Add(themes);
        ToolStripMenuItem materials = new ToolStripMenuItem("球体 / 面板材质");
        for (int i = 0; i < Materials.Length; i++) { int selected = i; materials.DropDownItems.Add(Materials[i], null, (s, e) => { material = selected; ChangePalette(); }); }
        menu.Items.Add(materials);
        ToolStripMenuItem sizes = new ToolStripMenuItem("大小"); menu.Items.Add(sizes);
        ToolStripMenuItem effects = new ToolStripMenuItem("动态光效");
        for (int i = 0; i < Animations.Length; i++) { int selected = i;
            effects.DropDownItems.Add(Animations[i], null, (s, e) => { animationStyle = selected; animate = selected > 0; SaveSettings(); Redraw(); });
        }
        ToolStripMenuItem speed = new ToolStripMenuItem("动效速度");
        float[] speeds = { .55F, 1F, 1.65F }; string[] names = { "舒缓", "标准", "轻快" };
        for (int i = 0; i < 3; i++) { float selected = speeds[i]; speed.DropDownItems.Add(names[i], null, (s, e) => { animationSpeed = selected; SaveSettings(); }); }
        effects.DropDownItems.Add(new ToolStripSeparator()); effects.DropDownItems.Add(speed); menu.Items.Add(effects);
        ToolStripMenuItem top = new ToolStripMenuItem("始终置顶"); top.Click += (s, e) => { TopMost = !TopMost; SaveSettings(); }; menu.Items.Add(top);
        ToolStripMenuItem snap = new ToolStripMenuItem("靠近屏幕边缘自动吸附");
        snap.Click += (s, e) => { autoDock = !autoDock; if (!autoDock) { DetachDock(); ClampToScreen(); } SaveSettings(); Redraw(); }; menu.Items.Add(snap);
        ToolStripMenuItem detach = new ToolStripMenuItem("取消吸附，恢复大小");
        detach.Click += (s, e) => { DetachDock(); ClampToScreen(); SaveSettings(); Redraw(); }; menu.Items.Add(detach);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("立即刷新 / 重新检测", null, (s, e) => RefreshUsage());
        menu.Items.Add("账号与额度状态…", null, (s, e) => {
            string text = HoverQuotaText();
            MessageBox.Show(this, text, "Codex 额度状态", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
        menu.Items.Add("退出挂件", null, (s, e) => { SaveSettings(); Close(); });
        menu.Opening += (s, e) => {
            if (dockCollapsed) SetDockCollapsed(false, false);
            foreach (ToolStripMenuItem item in styles.DropDownItems) item.Checked = (int)item.Tag == mode;
            for (int i = 0; i < 7; i++) ((ToolStripMenuItem)themes.DropDownItems[i]).Checked = palette == i;
            for (int i = 0; i < Materials.Length; i++) ((ToolStripMenuItem)materials.DropDownItems[i]).Checked = material == i;
            for (int i = 0; i < Animations.Length; i++) ((ToolStripMenuItem)effects.DropDownItems[i]).Checked = i == (animate ? animationStyle : 0);
            for (int i = 0; i < 3; i++) ((ToolStripMenuItem)speed.DropDownItems[i]).Checked = Math.Abs(animationSpeed - speeds[i]) < .01;
            top.Checked = TopMost; snap.Checked = autoDock; detach.Enabled = dockSide != DockSide.None;
            sizes.DropDownItems.Clear();
            foreach (int value in mode == 5 ? new[] { 128, 152, 176, 208, 248 } : new[] { 220, 260, 320, 400 }) {
                int selected = value; ToolStripMenuItem item = new ToolStripMenuItem(value + " 像素") { Checked = value == (mode == 5 ? orbWidth : panelWidth) };
                item.Click += (a, b) => { ResizeToWidth(selected); if (dockSide != DockSide.None) SetDockCollapsed(false, false); else ClampToScreen(); SaveSettings(); Redraw(); }; sizes.DropDownItems.Add(item);
            }
        };
        return menu;
    }
    private void UpdateHint()
    {
        hint.SetToolTip(this, HoverQuotaText() + "\n\n拖动靠边吸附 · 右键设置 · 双击刷新");
    }
    private string HoverQuotaText()
    {
        if (quotaState != QuotaState.Connected) return stateTitle + "\n" + stateDetail;
        DateTime now = DateTime.Now;
        return "球内水位 · 5 小时剩余：" + Percent(primary) + "\n" + HoverReset(primaryReset, now)
            + "\n\n外侧细环 · 7 天剩余：" + Percent(secondary) + "\n" + HoverReset(secondaryReset, now);
    }
    private static string HoverReset(DateTime? reset, DateTime now)
    {
        if (!reset.HasValue) return "重置时间：暂未提供";
        DateTime local = reset.Value.ToLocalTime();
        TimeSpan remaining = local - now.ToLocalTime();
        string date = local.ToString(local.Year == now.Year ? "M月d日 HH:mm" : "yyyy年M月d日 HH:mm");
        if (remaining.TotalSeconds <= 0) return "重置时间：" + date + "（等待额度更新）";
        int minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        string countdown = minutes >= 1440 ? (minutes / 1440) + "天 " + (minutes % 1440 / 60) + "小时"
            : minutes >= 60 ? (minutes / 60) + "小时 " + (minutes % 60) + "分钟" : minutes + "分钟";
        return "重置时间：" + date + "（约 " + countdown + "后，本地时间）";
    }
    private void PaintCompactPanel(Graphics g)
    {
        DrawMaterial(g, Colors);
        using (GraphicsPath edge = CardOutline()) using (Pen rim = new Pen(Fade(Colors.Text, 38), .7F / UiScale)) g.DrawPath(rim, edge);
        if (quotaState != QuotaState.Connected) {
            Label(g, busy ? "正在检测…" : stateTitle, 20, 42, 17, Colors.Text, true, 320, StringAlignment.Center);
            Label(g, "双击重新检测 · 右键设置", 20, 97, 10, Colors.Muted, false, 320, StringAlignment.Center); return;
        }
        GraphicsState state = g.Save(); g.TranslateTransform(0, -64);
        bool previous = showReset; showReset = false;
        if (mode == 0) { Ring(g, 101, primary, A, "5h", primaryReset); Ring(g, 259, secondary, B, "7d", secondaryReset); }
        else if (mode == 1) { Bar(g, 79, primary, A, B, "5h", primaryReset); Bar(g, 157, secondary, B, A, "7d", secondaryReset); }
        else if (mode == 2) { Segments(g, 83, primary, A, "5h", primaryReset); Segments(g, 158, secondary, B, "7d", secondaryReset); }
        else if (mode == 4) { Liquid(g, 101, primary, A, "5h", primaryReset); Liquid(g, 259, secondary, B, "7d", secondaryReset); }
        else { Number(g, 20, primary, A, "5h", primaryReset); Number(g, 190, secondary, B, "7d", secondaryReset); }
        showReset = previous; g.Restore(state);
    }
    private Bitmap CreateSmoothOrbFrame()
    {
        // Supersample all curved artwork, including clipped water and arc caps.
        // Text is composited at native resolution afterwards so small numerals stay crisp.
        int samples = Width <= 100 ? 4 : 3;
        Bitmap image = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
        using (Bitmap artwork = new Bitmap(Width * samples, Height * samples, PixelFormat.Format32bppPArgb)) {
            using (Graphics g = Graphics.FromImage(artwork)) {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.ScaleTransform(UiScale * samples, UiScaleY * samples);
                using (GraphicsPath outline = CardOutline()) g.SetClip(outline);
                PaintEnergyOrbArtwork(g);
            }
            using (Graphics g = Graphics.FromImage(image)) {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (ImageAttributes attributes = new ImageAttributes()) {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(artwork, new Rectangle(0, 0, Width, Height), 0, 0, artwork.Width, artwork.Height, GraphicsUnit.Pixel, attributes);
                }
                g.CompositingMode = CompositingMode.SourceOver;
                g.ScaleTransform(UiScale, UiScaleY);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                PaintOrbLabels(g);
            }
        }
        return image;
    }
    private void PaintEnergyOrb(Graphics g)
    {
        using (Bitmap frame = CreateSmoothOrbFrame()) g.DrawImage(frame, new RectangleF(0, 0, 180, 180));
    }
    private void DisposeOrbLayers()
    {
        if (orbUnderlay != null) { orbUnderlay.Dispose(); orbUnderlay = null; }
        if (orbGlaze != null) { orbGlaze.Dispose(); orbGlaze = null; }
        if (orbScaledUnderlay != null) { orbScaledUnderlay.Dispose(); orbScaledUnderlay = null; }
        if (orbScaledGlaze != null) { orbScaledGlaze.Dispose(); orbScaledGlaze = null; }
        orbLayerSize = Size.Empty;
    }
    private void EnsureOrbLayers()
    {
        if (orbUnderlay != null && layerPalette == palette && layerMaterial == material && layerA == A.ToArgb() && layerB == B.ToArgb()) { EnsureScaledOrbLayers(); return; }
        DisposeOrbLayers();
        // Cache at a fixed high resolution so hover expansion never rebuilds the expensive glass shading.
        orbUnderlay = new Bitmap(720, 720, PixelFormat.Format32bppPArgb);
        orbGlaze = new Bitmap(720, 720, PixelFormat.Format32bppPArgb);
        foreach (Bitmap layer in new[] { orbUnderlay, orbGlaze }) using (Graphics g = Graphics.FromImage(layer)) {
            g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.ScaleTransform(4, 4);
            if (layer == orbUnderlay) PaintOrbUnderlay(g); else PaintOrbGlaze(g);
        }
        layerPalette = palette; layerMaterial = material; layerA = A.ToArgb(); layerB = B.ToArgb();
        EnsureScaledOrbLayers();
    }
    private void EnsureScaledOrbLayers()
    {
        int samples = Width <= 100 ? 4 : 3;
        Size target = new Size(Width * samples, Height * samples);
        if (target == orbLayerSize && orbScaledUnderlay != null) return;
        if (orbScaledUnderlay != null) orbScaledUnderlay.Dispose();
        if (orbScaledGlaze != null) orbScaledGlaze.Dispose();
        orbScaledUnderlay = ScaleOrbLayer(orbUnderlay, target);
        orbScaledGlaze = ScaleOrbLayer(orbGlaze, target);
        orbLayerSize = target;
    }
    private static Bitmap ScaleOrbLayer(Bitmap source, Size target)
    {
        Bitmap result = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(result)) {
            g.CompositingMode = CompositingMode.SourceCopy; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using (ImageAttributes attributes = new ImageAttributes()) {
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(source, new Rectangle(Point.Empty, target), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
        }
        return result;
    }
    private static void DrawOrbLayer(Graphics g, Bitmap layer)
    {
        GraphicsState state = g.Save(); g.ResetTransform();
        g.DrawImageUnscaled(layer, 0, 0); g.Restore(state);
    }
    private void SoftReflection(Graphics g, RectangleF rectangle, float angle, Color color, int alpha)
    {
        GraphicsState state = g.Save();
        float x = rectangle.X + rectangle.Width / 2, y = rectangle.Y + rectangle.Height / 2;
        g.TranslateTransform(x, y); g.RotateTransform(angle); g.TranslateTransform(-x, -y);
        using (GraphicsPath spot = new GraphicsPath()) {
            spot.AddEllipse(rectangle);
            using (PathGradientBrush glow = new PathGradientBrush(spot)) {
                glow.CenterColor = Fade(color, alpha); glow.SurroundColors = new[] { Fade(color, 0) };
                glow.FocusScales = new PointF(.18F, .18F); g.FillPath(glow, spot);
            }
        }
        g.Restore(state);
    }
    private void PaintOrbUnderlay(Graphics g)
    {
        RectangleF shell = new RectangleF(4, 4, 172, 172);
        Color light = Colors.Text;
        using (GraphicsPath sphere = new GraphicsPath()) {
            sphere.AddEllipse(shell);
            using (PathGradientBrush glass = new PathGradientBrush(sphere)) {
                glass.CenterPoint = new PointF(58, 43);
                glass.CenterColor = Mix(Mix(Colors.Surface, A, .06F), Color.White, material == 3 ? .25F : .19F);
                glass.SurroundColors = new[] { Mix(Colors.Bg, Color.Black, material == 3 ? .18F : .68F) };
                glass.FocusScales = new PointF(.08F, .12F); g.FillPath(glass, sphere);
            }
        }
        using (GraphicsPath shadePath = new GraphicsPath()) {
            shadePath.AddEllipse(shell);
            using (PathGradientBrush depth = new PathGradientBrush(shadePath)) {
                depth.CenterPoint = new PointF(65, 57); depth.CenterColor = Fade(Color.Black, 0);
                depth.SurroundColors = new[] { Fade(Color.Black, material == 3 ? 20 : 78) };
                depth.FocusScales = new PointF(.45F,.4F); g.FillPath(depth, shadePath);
            }
        }
        using (LinearGradientBrush rim = new LinearGradientBrush(shell, Fade(Color.White, 75), Fade(Color.Black, 70), 48F))
        using (Pen edge = new Pen(rim, .8F)) g.DrawEllipse(edge, shell);
        using (Pen bottomRim = new Pen(Fade(B, 46), .8F)) g.DrawArc(bottomRim, 5.5F, 5.5F, 169, 169, 25, 92);
        using (Pen trackShadow = new Pen(Fade(Color.Black, material == 3 ? 25 : 80), 6)) g.DrawEllipse(trackShadow, 12, 13.2F, 156, 156);
        using (Pen track = new Pen(Fade(light, 23), 4.2F)) g.DrawEllipse(track, 12, 12, 156, 156);
    }
    private void PaintEnergyOrbArtwork(Graphics g)
    {
        EnsureOrbLayers(); DrawOrbLayer(g, orbScaledUnderlay);
        RectangleF shell = new RectangleF(4, 4, 172, 172), core = new RectangleF(24, 24, 132, 132);
        Color light = Colors.Text;
        float mini = dockSide == DockSide.None ? 0 : Math.Max(0, Math.Min(1, (orbWidth - Width) / (float)Math.Max(1, orbWidth - dockSize)));
        if (quotaState == QuotaState.Connected && secondary.HasValue && secondary.Value > 0) {
            float sweep = (float)(Math.Min(100, secondary.Value) * 3.6);
            using (LinearGradientBrush gradient = new LinearGradientBrush(shell, Mix(B, Color.White, .32F), Mix(B, Color.Black, .16F), 55F))
            using (Pen ring = new Pen(gradient, 4.2F)) { ring.StartCap = ring.EndCap = LineCap.Round; g.DrawArc(ring, 12, 12, 156, 156, -90, sweep); }
            using (LinearGradientBrush crestLight = new LinearGradientBrush(shell, Fade(Color.White, 95), Fade(Color.White, 12), 45F))
            using (Pen crest = new Pen(crestLight, .65F)) {
                crest.StartCap = crest.EndCap = LineCap.Round; g.DrawArc(crest, 11.3F, 11.3F, 157.4F, 157.4F, -90, sweep);
            }
            DrawRingMotion(g, new Rectangle(12, 12, 156, 156), sweep, B, .22F);
        }
        using (GraphicsPath vessel = new GraphicsPath()) {
            vessel.AddEllipse(core);
            GraphicsState state = g.Save(); g.SetClip(vessel, CombineMode.Intersect);
            if (quotaState == QuotaState.Connected && primary.HasValue && primary.Value > 0) {
                double fraction = primary.Value / 100;
                float level = 90 + WaterLine(fraction) * 66;
                float amplitude = (float)(3.5 * Math.Min(1, Math.Min(fraction, 1 - fraction) * 16));
                using (GraphicsPath back = WaterWave(core, level - 1.8F, amplitude * .7F, 2.1F))
                using (GraphicsPath wave = WaterWave(core, level, amplitude, 0)) {
                    using (SolidBrush distant = new SolidBrush(Fade(Mix(WaterTint, Color.White, .2F), 100))) g.FillPath(distant, back);
                    using (PathGradientBrush water = new PathGradientBrush(vessel)) {
                        water.CenterPoint = new PointF(61, 88); water.CenterColor = Mix(WaterTint, Color.White, .28F);
                        water.SurroundColors = new[] { Mix(WaterTint, Hex("#16465A"), .65F) };
                        water.FocusScales = new PointF(.12F,.08F); g.FillPath(water, wave);
                    }
                    g.SetClip(wave, CombineMode.Intersect);
                    float time = MotionActive ? phase : 0;
                    for (int i = 0; i < 3; i++) using (Pen caustic = new Pen(Fade(Color.White, 22), 1.1F))
                        g.DrawBezier(caustic, 15, 82 + i * 24, 60, 90 + i * 24 + 5 * (float)Math.Sin(time + i), 115, 70 + i * 24, 166, 82 + i * 24);
                    if (MotionActive && mini < .8F) for (int i = 0; i < 5; i++) {
                        float t = MotionPosition(i * .191F), x = 45 + (i * 29 % 88) + 2 * (float)Math.Sin(time + i), y = 158 - 140 * t;
                        using (Pen bubble = new Pen(Fade(Color.White, (int)(60 * Math.Sin(t * Math.PI))), .7F)) g.DrawEllipse(bubble, x, y, 2.7F, 2.7F);
                    }
                    SoftReflection(g, new RectangleF(47, 133, 88, 17), -5, Mix(WaterTint, Color.White, .42F), 83);
                    PaintSwimmingKoi(g, core, level + amplitude, 0);
                }
            }
            g.Restore(state);
            using (Pen glass = new Pen(Fade(light, 12), .65F)) g.DrawEllipse(glass, core);
            using (Pen glassLip = new Pen(Fade(WaterTint, 50), 1.2F)) g.DrawArc(glassLip, 24.7F, 24.7F, 130.6F, 130.6F, 35, 105);
        }
        DrawOrbLayer(g, orbScaledGlaze);
        if (MotionActive && animationStyle == 1)
            SoftReflection(g, new RectangleF(35, 20, 102, 61), -25, A, 12 + (int)(22 * (.5 + .5 * Math.Sin(phase))));
    }
    private void PaintSwimmingKoi(Graphics g, RectangleF bowl, float surface, float offset)
    {
        // Fit the whole fish beneath the lowest point of the moving water surface.
        float depth = bowl.Bottom - surface;
        if (depth < 3) return;
        float size = Math.Min(1, (depth - 2) / 28F) * bowl.Width / 132F;
        float y = bowl.Bottom - Math.Min(22 * bowl.Width / 132F, depth * .48F);
        float radius = bowl.Width / 2, dy = y - (bowl.Top + radius);
        float halfChord = (float)Math.Sqrt(Math.Max(0, radius * radius - dy * dy));
        float travel = Math.Max(0, halfChord - 24 * size - 5);
        float t = MotionActive ? phase + offset : .55F + offset;
        float tail = (float)Math.Sin(t * 6) * 3;
        GraphicsState state = g.Save();
        g.TranslateTransform(bowl.Left + radius + travel * (float)Math.Sin(t), y);
        g.RotateTransform(5 * (float)Math.Sin(t * 2));
        // Foreshortening through each turn avoids an instantaneous left/right flip.
        g.ScaleTransform(size * (float)Math.Cos(t), size);
        using (GraphicsPath fin = new GraphicsPath()) {
            fin.AddBezier(-8, 1, -12, 5, -12, 10 + tail * .3F, -3, 5);
            fin.AddBezier(-3, 5, 2, 3, 0, 2, -8, 1);
            using (SolidBrush silk = new SolidBrush(Color.FromArgb(153, 224, 245, 240))) g.FillPath(silk, fin);
            using (Pen ray = new Pen(Color.FromArgb(110, 255, 255, 245), .5F)) g.DrawBezier(ray, -7, 2, -10, 5, -8, 7, -4, 5);
        }
        using (GraphicsPath tailFin = new GraphicsPath()) {
            tailFin.AddBezier(-10, 0, -16, -1, -18, -7 + tail, -23, -6 + tail);
            tailFin.AddBezier(-23, -6 + tail, -20, -1 + tail, -22, 4 + tail, -23, 7 + tail);
            tailFin.AddBezier(-23, 7 + tail, -16, 7 + tail, -15, 2, -10, 0);
            using (LinearGradientBrush silk = new LinearGradientBrush(new RectangleF(-24,-10,16,22), Color.FromArgb(110, 224, 250, 247), Color.FromArgb(235, 247, 161, 102), 0F)) g.FillPath(silk, tailFin);
            using (Pen ray = new Pen(Color.FromArgb(130, 255, 239, 215), .55F)) {
                g.DrawBezier(ray, -11, 0, -15, 0, -18, -3 + tail, -21, -5 + tail);
                g.DrawBezier(ray, -11, 0, -15, 1, -18, 3 + tail, -21, 5 + tail);
            }
        }
        using (GraphicsPath body = new GraphicsPath()) {
            body.AddBezier(-13, 0, -6, -3, -2, -7, 6, -5);
            body.AddBezier(6, -5, 16, -4, 18, 0, 12, 4);
            body.AddBezier(12, 4, 4, 9, -4, 3, -13, 0);
            using (LinearGradientBrush pearl = new LinearGradientBrush(new RectangleF(-14,-7,32,16), Hex("#FFF9E7"), Hex("#70B9C5"), 90F)) g.FillPath(pearl, body);
            GraphicsState pattern = g.Save(); g.SetClip(body, CombineMode.Intersect);
            using (SolidBrush vermilion = new SolidBrush(Hex("#F4763D"))) {
                g.FillEllipse(vermilion, 5,-7,12,10); g.FillEllipse(vermilion,-7,-5,8,8); g.FillEllipse(vermilion,-11,1,8,5);
            }
            using (Pen scales = new Pen(Color.FromArgb(48, 255, 255, 240), .55F))
                for (int i = -4; i < 8; i += 3) g.DrawArc(scales, i,-2,4,5,-70,140);
            g.Restore(pattern);
            using (Pen back = new Pen(Color.FromArgb(160, 255, 255, 242), .65F)) g.DrawBezier(back, -9,-1,-1,-5,5,-5,11,-2);
        }
        using (SolidBrush eye = new SolidBrush(Hex("#183F48"))) g.FillEllipse(eye, 11,-1.5F,1.5F,1.5F);
        using (Pen whisker = new Pen(Color.FromArgb(160, 244, 248, 228), .5F)) g.DrawBezier(whisker, 15,1,18,1,17,3,19,2);
        g.Restore(state);
    }
    private void PaintOrbGlaze(Graphics g)
    {
        // Broad, feathered reflections follow the curved surface rather than drawing more outline rings.
        SoftReflection(g, new RectangleF(31, 24, 83, 33), -29, Color.White, material == 3 ? 125 : 100);
        SoftReflection(g, new RectangleF(41, 26, 39, 12), -29, Color.White, 105);
        using (LinearGradientBrush edgeReflection = new LinearGradientBrush(new RectangleF(18, 46, 16, 81), Fade(Color.White, 55), Fade(Color.White, 0), 90F))
        using (Pen reflection = new Pen(edgeReflection, 2.4F)) {
            reflection.StartCap = reflection.EndCap = LineCap.Round;
            g.DrawBezier(reflection, 28, 49, 18, 72, 18, 104, 31, 125);
        }
        // Percentage text always keeps a quiet, opaque background; water still moves around it.
        using (GraphicsPath readability = new GraphicsPath()) {
            readability.AddEllipse(35, 40, 112, 106);
            using (PathGradientBrush veil = new PathGradientBrush(readability)) {
                veil.CenterColor = Fade(Colors.Bg, 165); veil.SurroundColors = new[] { Fade(Colors.Bg, 0) };
                g.FillPath(veil, readability);
            }
        }
    }
    private void PaintOrbLabels(Graphics g)
    {
        Color light = Colors.Text;
        float mini = dockSide == DockSide.None ? 0 : Math.Max(0, Math.Min(1, (orbWidth - Width) / (float)Math.Max(1, orbWidth - dockSize)));
        if (quotaState != QuotaState.Connected) {
            string message = busy ? "检测中" : quotaState == QuotaState.NotSignedIn ? "未登录" : quotaState == QuotaState.SessionExpired ? "登录失效" : "暂无信息";
            Label(g, message, 25, 64, mini > .7F ? 23 : 17, light, true, 130, StringAlignment.Center);
            if (mini < .5F) Label(g, "双击重新检测", 25, 108, 8.5F, Colors.Muted, false, 130, StringAlignment.Center);
            return;
        }
        int labelAlpha = (int)(255 * (1 - mini));
        if (labelAlpha > 5) {
            using (SolidBrush dot = new SolidBrush(Fade(WaterTint, labelAlpha))) g.FillEllipse(dot, 38, 75, 4, 4);
            using (SolidBrush dot = new SolidBrush(Fade(B, labelAlpha))) g.FillEllipse(dot, 38, 111, 4, 4);
            Label(g, "5h", 45, 66, 9, Fade(light, labelAlpha), false, 25);
            Label(g, "7d", 45, 102, 9, Fade(light, labelAlpha), false, 25);
        }
        float xText = 65 - mini * 27, textWidth = 83 + mini * 25;
        Label(g, Percent(primary), xText, 55 - mini * 15, 23 + mini * 11, light, true, textWidth, StringAlignment.Center, true);
        Label(g, Percent(secondary), xText, 92 + mini * 10, 23 + mini, mini > .5F ? B : light, true, textWidth, StringAlignment.Center, true);
    }
}
