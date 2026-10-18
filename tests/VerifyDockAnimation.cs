using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class VerifyDockAnimation
{
    private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    private static FieldInfo Field(string name) { return typeof(CodexUsageWidgetV2).GetField(name, Flags); }
    private static object Call(object target, string method, params object[] args) { return target.GetType().GetMethod(method, Flags).Invoke(target, args); }
    private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS " + text); }
    [STAThread] private static int Main(string[] args) {
        try {
            Application.EnableVisualStyles();
            MethodInfo bounds=typeof(CodexUsageWidgetV2).GetMethod("DockAnimationBounds",BindingFlags.NonPublic|BindingFlags.Static);
            Rectangle from=new Rectangle(-176,200,176,176), to=new Rectangle(-68,254,68,68);
            Rectangle previous=from;
            for(int frame=0;frame<=120;frame++) {
                Rectangle value=(Rectangle)bounds.Invoke(null,new object[]{from,to,frame/120.0});
                if(value.Right!=0 || value.Width>previous.Width || value.Width<68 || value.Height!=value.Width)throw new Exception("anchoring / monotonic size / overshoot");
                previous=value;
            }
            Check(previous==to,"negative-coordinate edge stays anchored, with no overshoot, and reaches the exact target");
            Rectangle first=(Rectangle)bounds.Invoke(null,new object[]{from,to,.05});
            Rectangle last=(Rectangle)bounds.Invoke(null,new object[]{from,to,.95});
            Check(Math.Abs(first.Width-from.Width)<=1 && Math.Abs(last.Width-to.Width)<=1,"first and last 5 percent ease gently without an abrupt step");
            string data=Path.Combine(args[0],".build","qa","dock-animation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
            using(var widget=new CodexUsageWidgetV2(args[0],data)) {
                Field("dockSide").SetValue(widget,Enum.Parse(Field("dockSide").FieldType,"Right"));
                Field("dockMonitor").SetValue(widget,Screen.PrimaryScreen.DeviceName);
                Call(widget,"SetDockCollapsed",false,false);
                IntPtr handle=widget.Handle;
                Call(widget,"SetDockCollapsed",true,true);
                double duration=(double)Field("geometryDuration").GetValue(widget);
                Check(duration>=300 && ((Timer)Field("dockTimer").GetValue(widget)).Interval==15,"full transition has a gentle duration and a 15ms timer target");
                Call(widget,"ApplyDockFrame",duration*.25);
                using(var image=(Bitmap)Call(widget,"CreateFrame")) { }
                object cached=Field("orbScaledUnderlay").GetValue(widget);
                int before=(int)Field("presentedFrames").GetValue(widget);
                Call(widget,"ApplyDockFrame",duration*.5);
                Check((int)Field("presentedFrames").GetValue(widget)-before==1,"one animation step presents exactly one layered frame");
                Check(Object.ReferenceEquals(cached,Field("orbScaledUnderlay").GetValue(widget)),"intermediate sizes reuse the same supersampled glass layer");
                Rectangle middle=widget.Bounds;
                Call(widget,"PointerDown",widget,new MouseEventArgs(MouseButtons.Left,1,widget.Width/2,widget.Height/2,0));
                Check(widget.Bounds==middle && !(bool)Field("geometryActive").GetValue(widget),"catching the moving orb keeps its current position and size");
                Call(widget,"PointerUp",widget,new MouseEventArgs(MouseButtons.Left,1,widget.Width/2,widget.Height/2,0));
                Check((Rectangle)Field("geometryFrom").GetValue(widget)==middle,"release resumes from the caught position");
                Call(widget,"ApplyDockFrame",0.0);
                Check(widget.Bounds==middle,"resumed animation starts without a jump");
                Call(widget,"ApplyDockFrame",1000.0);
                Check(widget.Width==68 && !(bool)Field("geometryActive").GetValue(widget),"collapse completes exactly at 68px");
                Check(((Timer)Field("dockTimer").GetValue(widget)).Interval==30,"idle restores the lower-frequency docking timer");
                Call(widget,"SetDockCollapsed",false,true);
                Call(widget,"ApplyDockFrame",120.0);
                middle=widget.Bounds;
                Call(widget,"SetDockCollapsed",true,true);
                Call(widget,"ApplyDockFrame",0.0);
                Check(widget.Bounds==middle,"reversing direction continues from the current frame");
            }
            return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
}
