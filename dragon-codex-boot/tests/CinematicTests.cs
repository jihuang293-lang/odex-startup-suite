using System;
using System.IO;
using System.Web.Script.Serialization;
using DragonCodexBoot;
class CinematicTests {
 static void Check(bool ok,string label) {if(!ok)throw new Exception(label);}
 public static int Main(string[] args) {
  try {
   var c=new JavaScriptSerializer().Deserialize<Config>(File.ReadAllText(args[0]));
   c.TransitionMode="Cinematic";Entry.ValidateConfig(c);
   Check(CinematicTiming.ExitAt(c)>=10.2,"Sequence must finish before the shutter");
   c.MinimumSeconds=20;Check(CinematicTiming.ExitAt(c)==20,"Longer user duration must be retained");
   string[] expected={"pulse","identity","dispersing","drawing","colorize","portrait"};
   double[] time={0,1.5,4,5.5,6.8,8};
   for(int i=0;i<time.Length;i++)Check(CinematicTiming.Stage(time[i])==expected[i],"Sequence order");
   double previous=1;
   for(int i=0;i<=1000;i++) {
    double p=i/1000.0,h=CinematicTiming.ShutterHeight(p),w=CinematicTiming.ShutterWidth(p);
    Check(h>=0&&h<=1&&w>=0&&w<=1,"Invalid shutter bounds");
    Check(h*w<=previous+1e-9,"Shutter must not reopen or flash");previous=h*w;
   }
   Check(CinematicTiming.ShutterHeight(0)==1&&CinematicTiming.ShutterWidth(0)==1,"Initial window must remain complete");
   Check(CinematicTiming.ShutterHeight(1)==0&&CinematicTiming.ShutterWidth(1)==0,"Exit must fully reveal the real app");
   var gate=new CinematicDock();var old=new Bounds(1589,0,973,1440);var actual=new Bounds(720,330,1140,760);
   Check(!gate.Observe(0,false,old),"Loading window must not authorize reveal");
   Check(!gate.Observe(30,false,old),"Elapsed time alone must never authorize reveal");
   Check(!gate.Observe(31,true,old),"First verified measurement must wait for stability");
   Check(!gate.Observe(31.5,true,actual),"Restored window position must reset stability");
   Check(!gate.Observe(32,true,actual),"Repositioned window must stabilize before docking");
   Check(gate.Observe(32.4,true,actual),"Stable verified target must become ready");
   Check(!gate.Observe(33,false,actual),"Losing verification must close the readiness gate");
   Check(!gate.Observe(34,true,actual),"Verification recovery needs a fresh stability interval");
   Check(!gate.Observe(35,true,new Bounds()),"Missing window must not authorize reveal");
   var fullscreen=new Bounds(0,0,2560,1440);
   for(int i=0;i<=100;i++) {
    var fit=Geometry.Lerp(fullscreen,actual,Geometry.Ease(i/100.0));
    Check(fit.Left<=actual.Left&&fit.Top<=actual.Top&&fit.Right>=actual.Right&&fit.Bottom>=actual.Bottom,"Docking must keep the real loading window covered throughout");
   }
   c.FadeSeconds=Double.NaN;bool rejected=false;try{Entry.ValidateConfig(c);}catch{rejected=true;}Check(rejected,"Reject invalid cinematic timing");
   double lastProgress=0;
   for(int i=0;i<=300;i++) {
    double value=CinematicTiming.Progress(i,false,false);
    Check(value>=lastProgress&&value<1,"Loading progress must not claim completion while waiting");lastProgress=value;
   }
   Check(CinematicTiming.Progress(20,true,false)<1,"Verification alone must not complete the transition before alignment");
   Check(CinematicTiming.Progress(20,true,true)==1,"Only aligned verified content may complete the progress bar");
   Check(!CinematicTiming.NeedsWindowFallback(80,-1,10,false),"Loading app must not be prematurely revealed");
   Check(!CinematicTiming.NeedsWindowFallback(8,0,10,false),"Visual sequence must finish first");
   Check(!CinematicTiming.NeedsWindowFallback(30,28,10,false),"New ready signal must allow window detection to settle");
   Check(CinematicTiming.NeedsWindowFallback(32,28,10,false),"Verified content must not remain blocked by failed window detection");
   Check(!CinematicTiming.NeedsWindowFallback(32,28,10,true),"Stable app must use normal aligned transition");
   Console.WriteLine("PASS: cinematic stages, readiness loss/recovery, moving target stability, 101 covered docking positions, and timing validation.");return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }
}
