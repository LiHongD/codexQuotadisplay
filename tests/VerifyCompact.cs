using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class VerifyCompact
{
    private static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Check(bool passed, string text) { if (!passed) throw new Exception(text); Console.WriteLine("PASS " + text); }
    private static object Call(object target, string method, params object[] args) { return target.GetType().GetMethod(method, Flags).Invoke(target, args); }
    private static FieldInfo Field(string name) { return typeof(CodexUsageWidgetV2).GetField(name, Flags); }
    private static void Snapshot(CodexUsageWidgetV2 widget, string path)
    {
        using (Bitmap image = (Bitmap)Call(widget, "CreateFrame")) image.Save(path);
    }
    [STAThread] private static int Main(string[] args)
    {
        try {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string root = args[0], data = Path.Combine(root, ".build", "qa", "compact-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(data);
            var limits = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>("{\"primary\":{\"usedPercent\":63},\"secondary\":{\"usedPercent\":18}}");
            using (CodexUsageWidgetV2 widget = new CodexUsageWidgetV2(root, data)) {
                Call(widget, "ApplySnapshot", new QuotaSnapshot { State = QuotaState.Connected, Plan = "plus", Limits = limits });
                Check((int)Field("mode").GetValue(widget) == 5 && widget.Width == 176 && widget.Height == 176, "new install defaults to 176px single orb");
                Snapshot(widget, Path.Combine(root, "previews", "energy-orb.png"));
                using (Bitmap image = (Bitmap)Call(widget, "CreateFrame")) {
                    Check(image.GetPixel(0, 0).A == 0 && image.GetPixel(88, 88).A == 255, "orb has transparent corners and opaque readable center");
                    bool coverage = false;
                    for (int x = 0; x < image.Width; x++) { int alpha = image.GetPixel(x, 4).A; if (alpha > 0 && alpha < 255) coverage = true; }
                    Check(coverage, "orb silhouette has antialiased pixel coverage");
                }
                Call(widget, "ResizeToWidth", 128); Check(widget.Size == new Size(128,128), "orb can resize to 128px");
                Snapshot(widget, Path.Combine(root, "previews", "energy-orb-128.png"));
                Call(widget, "ResizeToWidth", 176);
                Field("palette").SetValue(widget, 3); Call(widget, "ChangePalette");
                using (Bitmap changed = (Bitmap)Call(widget, "CreateFrame"))
                using (CodexUsageWidgetV2 fresh = new CodexUsageWidgetV2(root, data)) {
                    Call(fresh, "ApplySnapshot", new QuotaSnapshot { State = QuotaState.Connected, Plan = "plus", Limits = limits });
                    using (Bitmap expectedFrame = (Bitmap)Call(fresh, "CreateFrame")) {
                        bool same = changed.Size == expectedFrame.Size;
                        for (int y = 0; same && y < changed.Height; y++) for (int x = 0; x < changed.Width; x++)
                            if (changed.GetPixel(x,y).ToArgb() != expectedFrame.GetPixel(x,y).ToArgb()) { same = false; break; }
                        Check(same, "theme changes replace cached glass shading and match a fresh render");
                    }
                }
                Field("palette").SetValue(widget, 1); Call(widget, "ChangePalette");
                Type sideType = Field("dockSide").FieldType;
                MethodInfo near = typeof(CodexUsageWidgetV2).GetMethod("NearEdge", BindingFlags.NonPublic | BindingFlags.Static);
                Rectangle virtualScreen = new Rectangle(-1920, 0, 1920, 1040);
                Rectangle[] drops = { new Rectangle(-1912,300,176,176), new Rectangle(-184,300,176,176), new Rectangle(-1000,8,176,176), new Rectangle(-1000,856,176,176), new Rectangle(-1000,400,176,176), new Rectangle(-2000,300,176,176) };
                string[] expected = { "Left", "Right", "Top", "Bottom", "None", "Left" };
                for (int i = 0; i < drops.Length; i++) Check(near.Invoke(null,new object[] { drops[i],virtualScreen,22 }).ToString() == expected[i], "negative-coordinate screen edge " + expected[i]);
                widget.TopMost = false;
                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                Field("dockMonitor").SetValue(widget, Screen.PrimaryScreen.DeviceName);
                foreach (string side in new[] { "Left", "Right", "Top", "Bottom" }) {
                    Field("dockSide").SetValue(widget, Enum.Parse(sideType, side));
                    foreach (double offset in new[] { .01, .5, .99 }) {
                        Field("dockFraction").SetValue(widget, offset);
                        Call(widget, "SetDockCollapsed", true, false);
                        Check(widget.Size == new Size(68,68) && work.Contains(widget.Bounds), side + " miniature remains inside work area at " + offset);
                        Call(widget, "SetDockCollapsed", false, false);
                        Check(widget.Size == new Size(176,176) && work.Contains(widget.Bounds), side + " expanded remains inside work area at " + offset);
                    }
                }
                Check(!widget.TopMost, "docking and expansion respect non-topmost preference");
                Field("dockSide").SetValue(widget, Enum.Parse(sideType, "Right"));
                Call(widget, "SetDockCollapsed", true, false);
                Snapshot(widget, Path.Combine(root, "previews", "energy-orb-docked.png"));
                Call(widget, "SaveSettings");
                using (CodexUsageWidgetV2 restored = new CodexUsageWidgetV2(root, data)) {
                    Check(restored.Width == 176 && (int)Field("orbWidth").GetValue(restored) == 176, "saved miniature does not overwrite expanded size");
                    Check(!restored.TopMost && Field("dockSide").GetValue(restored).ToString() == "Right", "restores docking side and topmost preference");
                }
                Field("hoverReady").SetValue(widget, true);
                Field("hoverAfter").SetValue(widget, DateTime.UtcNow.AddSeconds(-1));
                widget.Location = new Point(Cursor.Position.X - 34, Cursor.Position.Y - 34);
                Call(widget, "DockTick");
                Check(!(bool)Field("dockCollapsed").GetValue(widget) && (bool)Field("geometryActive").GetValue(widget), "hover begins smooth expansion without moving the real mouse");
                Call(widget, "SetDockCollapsed", false, false);
                widget.Location = new Point(Cursor.Position.X + 400, Cursor.Position.Y + 400);
                Field("collapseAfter").SetValue(widget, DateTime.UtcNow.AddSeconds(-1));
                Call(widget, "DockTick");
                Check((bool)Field("dockCollapsed").GetValue(widget), "leaving the expanded orb begins collapse after the delay");
                System.Threading.Thread.Sleep(220); Call(widget, "DockTick");
                Check(widget.Width == 68 && !(bool)Field("geometryActive").GetValue(widget), "collapse animation settles exactly at miniature size");
                Call(widget, "DetachDock"); Check(widget.Width == 176 && Field("dockSide").GetValue(widget).ToString() == "None", "dragging away can restore normal size");
                for (int layout = 0; layout < 5; layout++) {
                    Call(widget, "ChangeCompactMode", layout);
                    Snapshot(widget, Path.Combine(root, "previews", "compact-layout-" + layout + ".png"));
                    Check(widget.Width < 380 && widget.Height < 176, "legacy quota-only layout " + layout + " is compact");
                }
            }
            Console.WriteLine("ALL COMPACT AND DOCKING CHECKS PASSED"); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
