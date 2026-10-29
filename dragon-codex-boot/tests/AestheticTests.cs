using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DragonCodexBoot;

class AestheticTests {
 static string root;static int checks;
 static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;}
 static BitmapSource Bitmap(Visual visual,int w,int h){var image=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);image.Render(visual);image.Freeze();return image;}
 static void Save(BitmapSource image,string path){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(path))encoder.Save(file);}
 static BitmapSource Load(string path){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.CreateOptions=BitmapCreateOptions.IgnoreImageCache;image.UriSource=new Uri(Path.GetFullPath(path));image.EndInit();image.Freeze();return image;}
 static BitmapSource Fixture(string type){
  int w=1280,h=720;if(type=="portrait"){w=720;h=1280;}
  var visual=new DrawingVisual();using(var d=visual.RenderOpen()){
   d.DrawRectangle(new SolidColorBrush(Color.FromRgb(7,15,29)),null,new Rect(0,0,w,h));
   if(type=="bright")d.DrawRectangle(Brushes.White,null,new Rect(0,0,w,h));
   else if(type=="dark")d.DrawRectangle(Brushes.Black,null,new Rect(0,0,w,h));
   else if(type=="noisy"){
    var random=new Random(24);for(int y=0;y<h;y+=8)for(int x=0;x<w;x+=8){byte v=(byte)random.Next(256);d.DrawRectangle(new SolidColorBrush(Color.FromRgb(v,(byte)(255-v),v)),null,new Rect(x,y,8,8));}
   }else{
    if(type=="subject-left")d.PushTransform(new ScaleTransform(-1,1,w/2.0,h/2.0));
    double x=type=="portrait"?w*.35:w*.65;
    d.DrawEllipse(new SolidColorBrush(Color.FromRgb(244,102,97)),null,new Point(x+w*.12,h*.44),w*.17,h*.30);
    for(int i=0;i<22;i++)d.DrawLine(new Pen(Brushes.White,4),new Point(x,h*.19+i*14),new Point(Math.Min(w-15,x+w*.25),h*.24+i*14));
    if(type=="subject-left")d.Pop();
   }
  }return Bitmap(visual,w,h);
 }
 static BitmapSource Render(BitmapSource poster,string source,double time,bool automatic){
  var config=new Config{AutoAesthetics=automatic,Video=source,ShowWelcome=true,WelcomeTitle="Welcome back to Codex.",WelcomeSubtitle="Let\u2019s build what\u2019s next.",MinimumSeconds=7};
  var scene=new CinematicScene(config,poster);scene.SetLaunchState(time>11,false,false);scene.SetTime(time);
  var panel=new Grid{Width=1280,Height=720,Background=Brushes.Black};panel.Children.Add(scene);panel.Measure(new Size(1280,720));panel.Arrange(new Rect(0,0,1280,720));panel.UpdateLayout();return Bitmap(panel,1280,720);
 }
 static void Label(DrawingContext d,string text,double x,double y){d.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Consolas"),18,Brushes.White),new Point(x,y));}
 [STAThread] static int Main(string[] args){try{
  if(args.Length>2&&args[2]=="video-probe"){
   var image=PosterCache.Prepare(args[0],args[1]);double selected=PosterCache.VideoStartTime(args[0],args[1]);
   Check(selected>=1,"Black startup frame was selected for video");Check(AestheticAnalyzer.FrameQuality(image)>0,"Video preview did not contain a useful sampled frame");
   Console.WriteLine("PASS: video frame selection skipped its black opening; playback resumes at the selected preview time ("+selected+"s).");return 0;
  }
  if(args.Length>2&&args[2]=="cache-probe"){
   var profile=AestheticAnalyzer.Analyze(Load(args[0]),args[1]);Check(!profile.CacheHit,"Corrupt disk cache was accepted");Console.WriteLine("PASS: corrupted disk cache recomputed in a fresh process.");return 0;
  }
  root=args[0];Directory.CreateDirectory(root);string cache=Path.Combine(root,"cache");
  var watch=Stopwatch.StartNew();var records=new List<object>();
  foreach(string type in new string[]{"subject-right","subject-left","bright","dark","noisy","portrait"}){
   var image=Fixture(type);var profile=AestheticAnalyzer.Analyze(image,cache);var layout=AestheticAnalyzer.Plan(profile,16.0/9);
   Check(profile.AnalysisMilliseconds<250,"Analysis exceeded its interactive-time budget");
   Check(profile.ContourBudget<=4200,"Contour budget unbounded");
   foreach(var slot in layout.Captions)Check(slot.X>=0&&slot.Y>=0&&slot.X+slot.Width<=1&&slot.Y+slot.Height<=1,"Caption is outside viewport");
   if(type=="subject-right")Check(layout.Captions[3].X<.5,"Text did not avoid a salient subject on the right");
   if(type=="subject-left")Check(layout.Captions[3].X>.5,"Text did not avoid a salient subject on the left");
   if(type=="bright")Check(layout.Captions[3].Shade>=.8,"Bright wallpaper did not get stronger text contrast");
   if(type=="noisy"||type=="dark")Check(profile.Effect=="soft-fade","Difficult wallpaper did not get a gentle fallback");
   Check(AestheticAnalyzer.Analyze(image,cache).CacheHit,"Repeated image was not cached");
   Save(Render(image,"fixture.png",12,true),Path.Combine(root,type+".png"));
   records.Add(new{name=type,profile=profile,layout=layout});
  }
  var contact=new DrawingVisual();using(var d=contact.RenderOpen()){
   d.DrawRectangle(new SolidColorBrush(Color.FromRgb(8,12,20)),null,new Rect(0,0,1600,1000));
   string[] ids={"3068495643","2893211456","3021850803"};
   string[] labels={"GOJO / CLEAR SKY","CAK / NIGHT SCENE","GOJO / PORTRAIT"};
   for(int i=0;i<3;i++){
    string path=args.Length>1?Path.Combine(args[1],ids[i]+".jpg"):"";
    bool real=File.Exists(path);var image=real?Load(path):Fixture(new string[]{"subject-right","portrait","subject-left"}[i]);
    if(!real){path="generated-fixture-"+i+".png";labels[i]="GENERATED FIXTURE / "+(i+1);}
    var profile=AestheticAnalyzer.Analyze(image,cache);var layout=AestheticAnalyzer.Plan(profile,16.0/9);
    var frame=Render(image,path,12,true);Save(frame,Path.Combine(root,"scene-"+ids[i]+".png"));
    double x=32+(i%2)*784,y=85+(i/2)*455;
    Label(d,labels[i]+" / AUTOMATIC LAYOUT",x,y-30);
    d.DrawImage(frame,new Rect(x,y,752,423));records.Add(new{name=ids[i],profile=profile,layout=layout});
   }
   Label(d,"BRIGHT / AUTO CONTRAST",816,510);d.DrawImage(Load(Path.Combine(root,"bright.png")),new Rect(816,540,752,423));
   Label(d,"CODEX / AUTOMATIC COMPOSITION PREVIEW",32,15);
  }Save(Bitmap(contact,1600,1000),Path.Combine(root,"automatic-composition.png"));
  // A second process makes this a disk-cache test, independent of the memory cache.
  string badDirectory=Path.Combine(root,"bad-cache");Directory.CreateDirectory(badDirectory);
  var fresh=Fixture("subject-left");var known=AestheticAnalyzer.Analyze(fresh,cache);File.WriteAllText(Path.Combine(badDirectory,known.Fingerprint+".json"),"broken");
  string probe=Path.Combine(root,"cache-probe.png");Save(fresh,probe);
  using(var process=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"\""+probe+"\" \""+badDirectory+"\" cache-probe"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){
   Check(process.WaitForExit(5000)&&process.ExitCode==0,"Corrupt cache stopped the fresh analyzer process");
  }
  File.WriteAllText(Path.Combine(root,"analysis-report.json"),new JavaScriptSerializer{MaxJsonLength=2097152}.Serialize(new{checks=checks,milliseconds=watch.ElapsedMilliseconds,items=records}));
  Console.WriteLine("PASS: "+checks+" saliency placement, adaptive contrast, effect fallback, bounds, performance and cache checks; generated fixtures or explicitly supplied scene previews rendered.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
