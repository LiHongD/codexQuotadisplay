using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class VerifyWater
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
    private static object Call(object widget, string method, params object[] args) { return widget.GetType().GetMethod(method, Private).Invoke(widget, args); }
    private static bool Same(Bitmap a, Bitmap b) {
        for (int y=0;y<a.Height;y++) for (int x=0;x<a.Width;x++) if(a.GetPixel(x,y)!=b.GetPixel(x,y))return false;
        return true;
    }
    [STAThread] private static int Main(string[] args) {
        try {
            Application.EnableVisualStyles();
            var type=typeof(CodexUsageWidgetV2);
            MethodInfo reset=type.GetMethod("HoverReset",BindingFlags.Static|BindingFlags.NonPublic);
            DateTime now=new DateTime(2026,10,18,12,0,0,DateTimeKind.Local);
            string future=(string)reset.Invoke(null,new object[]{now.AddHours(3).AddMinutes(17),now});
            Check(future.Contains("10月18日 15:17") && future.Contains("3小时 17分钟"),"hover shows exact local reset time and countdown");
            string utc=(string)reset.Invoke(null,new object[]{now.AddDays(4).AddHours(7).ToUniversalTime(),now});
            Check(utc.Contains("10月22日 19:00") && utc.Contains("4天 7小时"),"UTC weekly reset converts to local time");
            Check(((string)reset.Invoke(null,new object[]{null,now})).Contains("暂未提供"),"missing reset is explicit");
            Check(((string)reset.Invoke(null,new object[]{now.AddSeconds(-1),now})).Contains("等待额度更新"),"past reset never shows a negative countdown");
            string data=Path.Combine(args[0],".build","qa","water-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(data);
            using(var widget=new CodexUsageWidgetV2(args[0],data)) {
                using(var a=new Bitmap(220,240)) using(var b=new Bitmap(220,240)) {
                    using(var g=Graphics.FromImage(a))Call(widget,"Liquid",g,101,(double?)37,Color.Red,"5h",null);
                    using(var g=Graphics.FromImage(b))Call(widget,"Liquid",g,101,(double?)37,Color.Purple,"5h",null);
                    Check(Same(a,b),"water appearance is independent of the requested accent color");
                }
                type.GetField("animate",Private).SetValue(widget,true);
                type.GetField("animationStyle",Private).SetValue(widget,6);
                using(var a=new Bitmap(180,180)) using(var b=new Bitmap(180,180)) {
                    type.GetField("phase",Private).SetValue(widget,.3F);
                    using(var g=Graphics.FromImage(a))Call(widget,"PaintSwimmingKoi",g,new RectangleF(24,24,132,132),100F,0F);
                    type.GetField("phase",Private).SetValue(widget,1F);
                    using(var g=Graphics.FromImage(b))Call(widget,"PaintSwimmingKoi",g,new RectangleF(24,24,132,132),100F,0F);
                    Check(!Same(a,b),"koi position, tail and turning change over time");
                }
                foreach(float level in new[]{30F,100F,140F,150F,154F,156F}) {
                    for(int step=0;step<12;step++)using(var frame=new Bitmap(180,180)) {
                        type.GetField("phase",Private).SetValue(widget,(float)(step*Math.PI/6));
                        using(var g=Graphics.FromImage(frame))Call(widget,"PaintSwimmingKoi",g,new RectangleF(24,24,132,132),level,0F);
                        bool inside=true;
                        for(int y=0;y<180;y++)for(int x=0;x<180;x++)if(frame.GetPixel(x,y).A>0 && y<Math.Floor(level))inside=false;
                        Check(inside,"koi stays submerged at surface "+level+", phase "+step);
                    }
                }
            }
            return 0;
        } catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
    }
}
