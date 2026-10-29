using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DragonCodexBoot {
 public sealed class CinematicDock {
  private Bounds previous;
  private double stableAt=-1;
  public static bool Same(Bounds a,Bounds b) {return Math.Abs(a.Left-b.Left)<=2&&Math.Abs(a.Top-b.Top)<=2&&Math.Abs(a.Width-b.Width)<=2&&Math.Abs(a.Height-b.Height)<=2;}
  public bool Observe(double time,bool verified,Bounds actual) {
   if(!verified||actual.Width<450||actual.Height<280) {stableAt=-1;previous=new Bounds();return false;}
   if(stableAt<0||!Same(previous,actual)) {previous=actual;stableAt=time;return false;}
   return time-stableAt>=.8;
  }
 }
 // Original WPF implementation, inspired by the staged timing of yanami-sec's demo.
 // No upstream JavaScript, images, or personal avatar are copied.
 public static class CinematicTiming {
  public const double PortraitAt=7.1;
  public static double ExitAt(Config c) { return Math.Max(10.2,c.MinimumSeconds); }
  public static string Stage(double t) {
   return t<.9?"pulse":t<3.5?"identity":t<4.4?"dispersing":t<6.6?"drawing":t<7.1?"colorize":"portrait";
  }
  public static double ShutterHeight(double p) { return Math.Max(0,1-Geometry.Ease(p/.65)); }
  public static double ShutterWidth(double p) { return Math.Max(0,1-Geometry.Ease((p-.60)/.40)); }
  public static double Progress(double seconds,bool verified,bool aligned) {
   if(aligned&&verified)return 1;
   if(verified)return .08+.88*Geometry.Clamp(seconds/10.2);
   return .08+.62*Geometry.Clamp(seconds/7.1)+.23*(1-Math.Exp(-Math.Max(0,seconds-7.1)/10));
  }
  public static bool NeedsWindowFallback(double now,double contentReadyAt,double earliestExit,bool stableWindow) {
   return !stableWindow&&contentReadyAt>=0&&now>=earliestExit&&now-contentReadyAt>=4;
  }
 }
 public static class PosterCache {
  static string VideoKey(string media){
   FileInfo f=new FileInfo(media);string stamp="ranked-v1|"+f.FullName+"|"+f.Length+"|"+f.LastWriteTimeUtc.Ticks;
   using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(stamp))).Replace("-","").Substring(0,24);
  }
  public static double VideoStartTime(string root,string media){
   try{
    string path=Path.Combine(root,"media","poster-cache",VideoKey(media)+".json");if(!File.Exists(path)||new FileInfo(path).Length>4096)return 0;
    var record=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
    double seconds=Convert.ToDouble(record["seekSeconds"]);return Double.IsNaN(seconds)||seconds<0||seconds>3?0:seconds;
   }catch{return 0;}
  }
  public static bool TryRenderedScene(string path,out BitmapSource poster) {
   poster=null;
   try {
    string metadata=Path.ChangeExtension(path,".json");var info=new FileInfo(metadata);var imageFile=new FileInfo(path);
    if(!info.Exists||info.Length>16384||!imageFile.Exists||imageFile.Length<128||imageFile.Length>10*1024*1024)return false;
    var record=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(metadata));
    if(Convert.ToInt32(record["schemaVersion"])!=1||Convert.ToString(record["source"])!="rendered-scene"||Convert.ToString(record["workshopId"])!=Path.GetFileNameWithoutExtension(path))return false;
    byte[] bytes=File.ReadAllBytes(path);string hash;
    using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
    if(hash!=Convert.ToString(record["imageSha256"]))return false;
    using(var stream=new MemoryStream(bytes)) {
     var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();
     if(image.PixelWidth!=Convert.ToInt32(record["width"])||image.PixelHeight!=Convert.ToInt32(record["height"])||image.PixelWidth>4096||image.PixelHeight>4096)return false;
     image.Freeze();poster=image;return true;
    }
   }catch{return false;}
  }
  public static BitmapSource Prepare(string root,string media) {
   string ext=Path.GetExtension(media).ToLowerInvariant(),image=media;
   if(ext==".mp4") {
    string key=VideoKey(media);
    string directory=Path.Combine(root,"media","poster-cache");Directory.CreateDirectory(directory);
    image=Path.Combine(directory,key+".png");
    if(!File.Exists(image)) {
     string ffmpeg=Path.Combine(root,"tools","ffmpeg.exe");
     // Both paths are local filenames; ProcessStartInfo never invokes a shell.
     string prefix=Path.Combine(directory,key+"."+Guid.NewGuid().ToString("N"));
     string[] samples={prefix+".00.png",prefix+".01.png",prefix+".02.png"};
     try{
      using(var p=Process.Start(new ProcessStartInfo(ffmpeg,"-nostdin -v error -i \""+media+"\" -t 3.2 -frames:v 3 -start_number 0 -vf \"fps=1:start_time=0,scale=1280:-2\" -an -y \""+prefix+".%02d.png\"") {
       UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true
      })) {
       p.StandardInput.Close();
       if(!p.WaitForExit(12000)) {try {p.Kill();}catch{}throw new IOException("Wallpaper preview preparation timed out.");}
       if(p.ExitCode!=0)throw new IOException("Could not sample this wallpaper for the cinematic intro.");
      }
      double best=Double.NegativeInfinity;int selected=-1,sampleCount=0;
      for(int i=0;i<samples.Length;i++)if(File.Exists(samples[i])){
       sampleCount++;
       var candidate=new BitmapImage();using(var input=File.OpenRead(samples[i])){candidate.BeginInit();candidate.CacheOption=BitmapCacheOption.OnLoad;candidate.StreamSource=input;candidate.EndInit();candidate.Freeze();}
       double quality=AestheticAnalyzer.FrameQuality(candidate);if(quality>best){best=quality;selected=i;}
      }
      if(selected<0)throw new IOException("Video did not produce a usable preview frame.");
      if(!File.Exists(image))File.Move(samples[selected],image);
      File.WriteAllText(Path.Combine(directory,key+".json"),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new {version=1,seekSeconds=selected,sampleCount=sampleCount,quality=best}),new UTF8Encoding(false));
     }finally{foreach(string sample in samples)if(File.Exists(sample))File.Delete(sample);}
    }
   }
   var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
   bitmap.CreateOptions=BitmapCreateOptions.IgnoreImageCache;
   bitmap.UriSource=new Uri(Path.GetFullPath(image));bitmap.EndInit();bitmap.Freeze();return bitmap;
  }
 }
 public sealed class CinematicScene:FrameworkElement {
  private sealed class Stroke {public double X,Y,DX,DY,SX,SY,Seed;}
  private readonly Config config;
  private BitmapSource poster,previousPoster;
  private readonly string posterPath;
  private long posterStamp;
  private double lastPosterCheck=-2,posterChangedAt=-2;
  private bool posterReady;
  private double sceneStartedAt;
  private AestheticProfile aesthetics;
  private AestheticLayout composition;
  private double compositionAspect;
  private double captionShade=.69;
  private bool captionDark;
  private double imageReveal;
  public AestheticProfile Analysis {get{return aesthetics;}}
  public string EffectStyle {get{return aesthetics==null?"contour":aesthetics.Effect;}}
  public double VisualSeconds {get{return config.ScenePoster?(posterReady?Math.Max(0,seconds-sceneStartedAt):Math.Min(seconds,2.8)):seconds;}}
  public bool ReadyToHandoff {get{return (posterReady?VisualSeconds:seconds)>=CinematicTiming.ExitAt(config);}}
  private readonly List<Stroke> strokes=new List<Stroke>();
  private readonly Brush dark=new SolidColorBrush(Color.FromRgb(8,12,20));
  private readonly Brush ink=new SolidColorBrush(Color.FromRgb(185,224,244));
  private readonly Brush muted=new SolidColorBrush(Color.FromRgb(114,146,169));
  private readonly Pen thin=new Pen(new SolidColorBrush(Color.FromArgb(100,129,180,212)),1);
  private readonly Pen contour=new Pen(new SolidColorBrush(Color.FromRgb(183,222,244)),1.1);
  private double seconds;
  private sealed class CaptionLayout {public GeometryGroup Shape;public double Width,Height;}
  private readonly Dictionary<string,CaptionLayout> captionLayouts=new Dictionary<string,CaptionLayout>();
  private bool appVerified,appAligning,appAligned;
  private double progress;
  public CinematicScene(Config c,BitmapSource image) {
   config=c;poster=image;posterReady=!c.ScenePoster;IsHitTestVisible=false;
   RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
   if(Path.IsPathRooted(c.Video??"")&&Path.GetExtension(c.Video).ToLowerInvariant()!=".mp4") {
    posterPath=c.Video;try{posterStamp=File.GetLastWriteTimeUtc(posterPath).Ticks;}catch{}
    if(c.ScenePoster){BitmapSource rendered;if(PosterCache.TryRenderedScene(posterPath,out rendered)){poster=rendered;posterReady=true;}}
   }
   if(posterReady)BuildContours();
  }
  public void SetLaunchState(bool verified,bool aligning,bool aligned) {appVerified=verified;appAligning=aligning;appAligned=aligned&&verified;}
  public void SetTime(double t) {
   seconds=t;if(!appAligning)RefreshPoster(t);progress=Math.Max(progress,CinematicTiming.Progress(t,appVerified,appAligned));
   if(!appAligned)progress=Math.Min(.96,progress);InvalidateVisual();
  }
  private void RefreshPoster(double t) {
   if(posterPath==null||t-lastPosterCheck<1)return;lastPosterCheck=t;
   try {
    var file=new FileInfo(posterPath);if(!file.Exists||file.Length<128||file.Length>10*1024*1024||(posterReady&&file.LastWriteTimeUtc.Ticks==posterStamp))return;
    BitmapSource next;
    if(config.ScenePoster){if(!PosterCache.TryRenderedScene(posterPath,out next))return;}
    else using(var input=new FileStream(posterPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
     var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=input;image.EndInit();
     if(image.PixelWidth<16||image.PixelHeight<16||image.PixelWidth>4096||image.PixelHeight>4096)return;
     image.Freeze();next=image;
    }
    previousPoster=posterReady?poster:null;poster=next;posterStamp=file.LastWriteTimeUtc.Ticks;posterChangedAt=t;
    if(!posterReady){sceneStartedAt=t-Math.Min(t,3.5);posterReady=true;}
    strokes.Clear();BuildContours();
   }catch{ /* Atomic replacement can race this read; keep the usable frame and retry. */ }
  }
  private void BuildContours() {
   aesthetics=null;composition=null;
   if(config.AutoAesthetics)try{
    aesthetics=AestheticAnalyzer.Analyze(poster,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"media","aesthetic-cache"));
   }catch{ /* Keep the existing bounded drawing path if local analysis is unavailable. */ }
   if(EffectStyle=="soft-fade")return;
   // Downsample once. Each selected edge becomes a short independently moving stroke.
   var scaled=new TransformedBitmap(poster,new ScaleTransform(640.0/poster.PixelWidth,640.0/poster.PixelWidth));
   var gray=new FormatConvertedBitmap(scaled,PixelFormats.Gray8,null,0);int w=gray.PixelWidth,h=gray.PixelHeight;
   byte[] pixels=new byte[w*h];gray.CopyPixels(pixels,w,0);var random=new Random(70419);
   for(int y=2;y<h-2;y+=3)for(int x=2;x<w-2;x+=3) {
    double gx=-pixels[(y-1)*w+x-1]+pixels[(y-1)*w+x+1]-2*pixels[y*w+x-1]+2*pixels[y*w+x+1]-pixels[(y+1)*w+x-1]+pixels[(y+1)*w+x+1];
    double gy=-pixels[(y-1)*w+x-1]-2*pixels[(y-1)*w+x]-pixels[(y-1)*w+x+1]+pixels[(y+1)*w+x-1]+2*pixels[(y+1)*w+x]+pixels[(y+1)*w+x+1];
    double strength=Math.Sqrt(gx*gx+gy*gy);if(strength<(aesthetics==null?85:aesthetics.ContourThreshold))continue;
    double angle=random.NextDouble()*Math.PI*2,r=.08+random.NextDouble()*.56;
    strokes.Add(new Stroke {X=x/(double)w,Y=y/(double)h,DX=-gy/strength*3.4/w,DY=gx/strength*3.4/h,
     SX=.5+Math.Cos(angle)*r,SY=.44+Math.Sin(angle)*r,Seed=random.NextDouble()*6.28});
   }
   // Uniform budget keeps detailed wallpaper from making the opening CPU bound.
   int budget=aesthetics==null?6500:aesthetics.ContourBudget;
   if(strokes.Count>budget) {var keep=new List<Stroke>();double stride=strokes.Count/(double)budget;for(double i=0;i<strokes.Count;i+=stride)keep.Add(strokes[(int)i]);strokes.Clear();strokes.AddRange(keep);}
  }
  private void Text(DrawingContext d,string text,double x,double y,double size,Brush brush,bool center,bool bold) {
   var f=new FormattedText(text??"",CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
    new Typeface(new FontFamily("Consolas"),FontStyles.Normal,bold?FontWeights.Bold:FontWeights.Normal,FontStretches.Normal),size,brush);
   d.DrawText(f,new Point(center?x-f.Width/2:x,y));
  }
  private CaptionLayout TechLayout(string text,double size) {
   text=(text??"").ToUpperInvariant().TrimEnd('.');
   string key=text+"|"+size.ToString("F3",CultureInfo.InvariantCulture);CaptionLayout cached;
   if(captionLayouts.TryGetValue(key,out cached))return cached;
   var face=new Typeface(new FontFamily("Consolas"),FontStyles.Normal,FontWeights.Bold,FontStretches.Normal);
   var layout=new CaptionLayout {Shape=new GeometryGroup()};double row=0,tracking=size*.075,lineHeight=size*1.4;
   foreach(string line in text.Split('\n')) {
    double advance=0;
    foreach(char ch in line.TrimEnd('\r')) {
     var glyph=new FormattedText(ch.ToString(),CultureInfo.InvariantCulture,FlowDirection.LeftToRight,face,size,Brushes.White);
     layout.Shape.Children.Add(glyph.BuildGeometry(new Point(advance,row)));
     advance+=glyph.WidthIncludingTrailingWhitespace+tracking;
    }
    layout.Width=Math.Max(layout.Width,Math.Max(0,advance-tracking));row+=lineHeight;
   }
   layout.Height=Math.Max(size,row-size*.15);layout.Shape.Freeze();
   if(captionLayouts.Count>=96)captionLayouts.Clear();captionLayouts[key]=layout;return layout;
  }
  private void Caption(DrawingContext d,string text,string tag,double w,double h,double x,double y,double available,double align,double size,Color color,double entrance) {
   var brush=new LinearGradientBrush(captionDark?Color.FromRgb(12,21,35):Color.FromRgb(247,251,255),color,90);
   var layout=TechLayout(text,size);
   if(layout.Width>available) {size*=available/layout.Width;layout=TechLayout(text,size);}
   double margin=Math.Max(24,w*.04);
   double left=Math.Max(margin,Math.Min(w-margin-layout.Width,x-layout.Width*align));
   double top=Math.Max(54,Math.Min(h-38-layout.Height,y));
   // Each phrase settles gently into its own composition, with no visible panel.
   double drift=(1-entrance)*Math.Min(20,w*.016);
   left+=align>.5?drift:-drift;top+=drift*.35;
   var shade=new RadialGradientBrush();
   shade.GradientStops.Add(new GradientStop(captionDark?Color.FromArgb(32,255,255,255):Color.FromArgb((byte)(255*captionShade),0,0,0),0));
   shade.GradientStops.Add(new GradientStop(captionDark?Color.FromArgb(16,255,255,255):Color.FromArgb((byte)(225*captionShade),0,0,0),.45));
   shade.GradientStops.Add(new GradientStop(Color.FromArgb(0,0,0,0),1));
   d.DrawRectangle(shade,null,new Rect(left-65,top-52,layout.Width+130,layout.Height+108));
   var accent=new SolidColorBrush(color);double labelSize=Math.Min(16,Math.Max(11,size*.27));
   Text(d,tag,left,top-labelSize-12,labelSize,accent,false,true);
   var fine=new Pen(new SolidColorBrush(Color.FromArgb(185,color.R,color.G,color.B)),1.2);
   d.DrawLine(fine,new Point(left-13,top+5),new Point(left-13,top+23));
   d.DrawLine(fine,new Point(left-13,top+5),new Point(left-3,top+5));
   double ruleY=top+layout.Height+8,ruleLength=Math.Min(180,layout.Width)*entrance;
   d.DrawLine(fine,new Point(left,ruleY),new Point(left+ruleLength,ruleY));
   d.DrawRectangle(accent,null,new Rect(left+ruleLength+6,ruleY-1.5,4,3));
   d.PushTransform(new TranslateTransform(left,top));
   d.PushTransform(new TranslateTransform(0,2));
   d.DrawGeometry(new SolidColorBrush(captionDark?Color.FromArgb(180,255,255,255):Color.FromArgb(180,0,0,0)),new Pen(captionDark?Brushes.White:Brushes.Black,2),layout.Shape);d.Pop();
   d.DrawGeometry(null,new Pen(new SolidColorBrush(Color.FromArgb(42,color.R,color.G,color.B)),4),layout.Shape);
   d.DrawGeometry(brush,null,layout.Shape);d.Pop();
  }
  private void DrawWelcome(DrawingContext d,double w,double h,double t) {
   if(!config.ShowWelcome)return;
   string title,tag;double alpha,start,x,y,available,align;Color color;int phase;
   if(t<3.9) {
    title="It starts\nwith an idea.";tag="01 / CODEX.INIT";start=.25;phase=0;
    x=w*.08;y=h*.25;available=w*.4;align=0;color=Color.FromRgb(126,219,255);
    alpha=Geometry.Ease((t-.25)/.65)*(1-Geometry.Ease((t-3.25)/.45));
   } else if(t<7.1) {
    title="Watch it\ntake shape.";tag="02 / IDEA.COMPILE";start=4.05;phase=1;
    x=w*.91;y=h*.32;available=w*.38;align=1;color=Color.FromRgb(200,169,255);
    alpha=Geometry.Ease((t-4.05)/.5)*(1-Geometry.Ease((t-6.55)/.45));
   } else if(t<8.95) {
    title=config.WelcomeTitle;tag="03 / SESSION.ONLINE";start=7.1;phase=2;
    if(composition!=null&&title=="Welcome back to Codex.")title="Welcome back\nto Codex.";
    x=w*.5;y=h*.43;available=w*.84;align=.5;color=Color.FromRgb(222,241,255);
    alpha=Geometry.Ease((t-7.1)/.5)*(1-Geometry.Ease((t-8.5)/.4));
   } else {
    title=config.WelcomeSubtitle;tag="04 / READY.TO.BUILD";start=8.95;phase=3;
    if(title=="Let\u2019s build what\u2019s next.")title="Let\u2019s build\nwhat\u2019s next.";
    x=w*.09;y=h*.65;available=w*.7;align=0;color=Color.FromRgb(255,218,155);
    alpha=Geometry.Ease((t-8.95)/.55);
   }
   if(alpha<=0)return;
   captionShade=.69;captionDark=false;
   if(composition!=null){
    var placement=composition.Captions[phase];x=w*placement.X;y=h*placement.Y;available=w*placement.Width;align=0;
    color=composition.Accents[phase];captionShade=placement.Shade;captionDark=placement.DarkText&&imageReveal>.95;
    if(captionDark)color=Color.FromRgb((byte)(color.R*.26),(byte)(color.G*.26),(byte)(color.B*.26));
   }
   double size=Math.Min(64,Math.Max(26,w*.034));
   d.PushOpacity(alpha);
   Caption(d,title,tag,w,h,x,y,available,align,size,color,Geometry.Ease((t-start)/.7));
   d.Pop();
  }
  public static Rect FitPoster(double width,double height,double imageWidth,double imageHeight) {
   double scale=Math.Min(width/imageWidth,height/imageHeight);
   return new Rect((width-imageWidth*scale)/2,(height-imageHeight*scale)/2,imageWidth*scale,imageHeight*scale);
  }
  private Rect Cover(double width,double height) {
   return FitPoster(width,height,poster.PixelWidth,poster.PixelHeight);
  }
  private Point Map(double x,double y,Rect image) {return new Point(image.Left+x*image.Width,image.Top+y*image.Height);}
  protected override void OnRender(DrawingContext d) {
   base.OnRender(d);double w=ActualWidth,h=ActualHeight;if(w<2||h<2)return;
   if(aesthetics!=null&&(composition==null||(!appAligning&&Math.Abs(w/h-compositionAspect)>.015))){
    compositionAspect=w/h;composition=AestheticAnalyzer.Plan(aesthetics,compositionAspect);
   }
   bool soft=EffectStyle=="soft-fade";
   double s=VisualSeconds,color=posterReady?Geometry.Ease(soft?(s-.6)/2.2:(s-6.6)/.5):0;Rect area=new Rect(0,0,w,h),image=Cover(w,h);
   imageReveal=color;
   d.PushClip(new RectangleGeometry(area));
   // Draw within the viewport, with no Image measure overflow or portrait cropping.
   // Video remains on the underlying player after the contour reveal completes.
   bool video=Path.GetExtension(config.Video??"").ToLowerInvariant()==".mp4";
   if(posterReady&&(!video||color<1)) {
    d.PushOpacity(video?1-color:1);d.DrawRectangle(composition==null?Brushes.Black:new SolidColorBrush(composition.Background),null,area);
    double blend=Geometry.Ease((seconds-posterChangedAt)/.65);
    if(previousPoster!=null&&blend<1)d.DrawImage(previousPoster,FitPoster(w,h,previousPoster.PixelWidth,previousPoster.PixelHeight));
    d.PushOpacity(previousPoster==null?1:blend);d.DrawImage(poster,image);d.Pop();d.Pop();
    if(blend>=1)previousPoster=null;
   }
   // At full color this layer disappears and exposes the already playing wallpaper video.
   d.PushOpacity(1-color);d.DrawRectangle(dark,null,area);
   double ui=Geometry.Ease(s/.35)*(1-Geometry.Ease((s-3.5)/.5));
   if(s<4.1&&(!soft||!posterReady)) {
    d.PushOpacity(ui);DrawIdentity(d,w,h,s);d.Pop();
   }
   if(!soft&&s>=3.5&&s<7.1) {
    double scatter=Geometry.Ease((s-3.5)/.9),assemble=Geometry.Clamp((s-4.4)/2.2);
    DrawFragments(d,w,h,image,scatter,assemble,s);
   }
   d.Pop();
   DrawWelcome(d,w,h,s);
   DrawProgress(d,w,h,seconds);
   d.Pop();
  }
  private void DrawProgress(DrawingContext d,double w,double h,double t) {
   double x=Math.Max(28,w*.04),y=Math.Max(28,h*.065),width=Math.Min(w-2*x,Math.Max(320,Math.Min(450,w*.32)));
   if(composition!=null){x=Math.Max(28,w*composition.Progress.X);y=Math.Max(28,h*composition.Progress.Y);width=Math.Min(450,w*composition.Progress.Width);}
   double labelSize=Math.Min(18,Math.Max(13,w*.009)),phaseSize=Math.Min(16,Math.Max(12,w*.008));
   string phase=appAligned?"READY TO CREATE":appAligning?"ALIGNING WORKSPACE":t<.9?"INITIALIZING CODEX":t<3.5?"PREPARING WORKSPACE":t<6.6?"BUILDING THE SCENE":t<7.1?"BRINGING IT TO LIFE":appVerified?"WORKSPACE READY":"WAITING FOR CODEX";
   if(config.ScenePoster&&!posterReady&&!appAligning&&!appAligned)phase="PREPARING YOUR SCENE";
   else if(EffectStyle=="soft-fade"&&!appAligning&&!appAligned)phase=t<3.5?"BRINGING SCENE INTO VIEW":appVerified?"WORKSPACE READY":"WAITING FOR CODEX";
   d.PushOpacity(Geometry.Ease((t-.15)/.35));
   bool darkHeader=composition!=null&&composition.Progress.DarkText&&imageReveal>.95;
   byte support=(byte)(255*(composition==null?.69:composition.Progress.Shade));
   var contrast=new LinearGradientBrush(new GradientStopCollection {
    new GradientStop(darkHeader?Color.FromArgb(24,255,255,255):Color.FromArgb(support,0,0,0),0),new GradientStop(darkHeader?Color.FromArgb(8,255,255,255):Color.FromArgb((byte)(support*.86),0,0,0),.76),new GradientStop(Color.FromArgb(0,0,0,0),1)
   },new Point(0,0),new Point(1,0));
   d.DrawRectangle(contrast,null,new Rect(x-16,y-12,width+88,labelSize+phaseSize+48));
   Brush progressInk=composition==null?ink:new SolidColorBrush(composition.Accents[0]);
   if(darkHeader){var hue=composition.Accents[0];progressInk=new SolidColorBrush(Color.FromRgb((byte)(hue.R*.26),(byte)(hue.G*.26),(byte)(hue.B*.26)));}
   Text(d,"CODEX / BOOT SEQUENCE",x,y,labelSize,progressInk,false,true);
   Text(d,((int)Math.Round(progress*100)).ToString("00",CultureInfo.InvariantCulture)+"%",x+width-labelSize*3,y,labelSize,progressInk,false,true);
   double barY=y+labelSize+14;
   d.DrawRectangle(new SolidColorBrush(Color.FromArgb(65,190,222,245)),null,new Rect(x,barY,width,3));
   d.DrawRectangle(progressInk,null,new Rect(x,barY,Math.Max(1,width*progress),3));
   if(!appAligned) {
    double tip=x+width*progress;
    d.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(100+100*(.5+.5*Math.Sin(t*4))),210,240,255)),null,new Point(tip,barY+1.5),2.5,2.5);
   }
   Text(d,phase,x,barY+14,phaseSize,progressInk,false,true);d.Pop();
  }
  private void DrawIdentity(DrawingContext d,double w,double h,double s) {
   double cx=w*.5,cy=h*.43,r=Math.Min(w,h)*.205,appear=Geometry.Ease((s-.65)/.65);
   // Subtle grid and frame corners stay behind the identity card.
   for(double x=cx-r*1.5;x<cx+r*1.5;x+=32)d.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(14,135,185,220)),1),new Point(x,cy-r*1.5),new Point(x,cy+r*1.5));
   for(double y=cy-r*1.5;y<cy+r*1.5;y+=32)d.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(14,135,185,220)),1),new Point(cx-r*1.5,y),new Point(cx+r*1.5,y));
   if(s<1.2)for(int j=0;j<3;j++) {
    double p=(s*.8+j*.33)%1;d.PushOpacity((1-p)*.65*(1-appear));
    d.DrawEllipse(null,new Pen(ink,1.4),new Point(cx,cy),r*(.15+p*.65),r*(.15+p*.65));d.Pop();
   }
   d.PushOpacity(appear);d.DrawEllipse(null,thin,new Point(cx,cy),r,r);
   for(int j=0;j<60;j++) {
    double a=j*Math.PI/30+s*.06,inner=j%5==0?.91:.96;
    d.DrawLine(thin,new Point(cx+Math.Cos(a)*r*inner,cy+Math.Sin(a)*r*inner),new Point(cx+Math.Cos(a)*r,cy+Math.Sin(a)*r));
   }
   double side=r*1.18,bob=Math.Sin(s*3)*2;Rect avatar=new Rect(cx-side/2,cy-side/2+bob,side,side);
   d.DrawRectangle(null,new Pen(ink,1),new Rect(avatar.Left-7,avatar.Top-7,side+14,side+14));
   var brush=new ImageBrush(poster) {Stretch=Stretch.UniformToFill,AlignmentX=AlignmentX.Center,AlignmentY=AlignmentY.Center};
   if(aesthetics!=null)brush.Viewbox=new Rect(aesthetics.FocusX,aesthetics.FocusY,aesthetics.FocusWidth,aesthetics.FocusHeight);
   if(posterReady)d.DrawRectangle(brush,null,avatar);
   else {d.DrawRectangle(dark,null,avatar);Text(d,"</>",cx,cy-side*.18,side*.24,ink,true,true);}
   Text(d,"C O D E X",cx,cy+r+22,13,ink,true,true);
   d.Pop();
   double m=Math.Max(20,w*.035),length=26;
   foreach(int x in new int[]{-1,1})foreach(int y in new int[]{-1,1}) {
    Point p=new Point(x<0?m:w-m,y<0?m:h-m);
    d.DrawLine(thin,p,new Point(p.X-x*length,p.Y));d.DrawLine(thin,p,new Point(p.X,p.Y-y*length));
   }
   Text(d,"CODEX  /  WORKSPACE INITIALIZE",m,h-m-43,10,muted,false,false);
   Text(d,"Ideas into motion.",m,h-m-24,11,muted,false,false);
   Text(d,"LOCAL SESSION  /  STARTING",w-m-194,h-m-24,10,muted,false,false);
  }
  private void DrawFragments(DrawingContext d,double w,double h,Rect image,double spread,double assemble,double s) {
   // The avatar breaks into actual wallpaper tiles before the strokes resolve left to right.
   double tileOpacity=1-Geometry.Ease((s-4.0)/.6);
   if(tileOpacity>.001) {
    double side=Math.Min(w,h)*.242;
    for(int y=0;y<9;y++)for(int x=0;x<9;x++) {
     double seed=(x*73+y*137)*.731,a=seed,r=.08+((x*37+y*53)%100)/210.0;
     double sourceX=w*.5+(x/9.0-.5)*side,sourceY=h*.43+(y/9.0-.5)*side;
     double destX=w*(.5+Math.Cos(a)*r),destY=h*(.44+Math.Sin(a)*r);
     double px=sourceX+(destX-sourceX)*spread,py=sourceY+(destY-sourceY)*spread,size=side/9*(1-.75*spread);
     double fx=aesthetics==null?.33:aesthetics.FocusX,fy=aesthetics==null?.25:aesthetics.FocusY,fw=aesthetics==null?.34:aesthetics.FocusWidth,fh=aesthetics==null?.5:aesthetics.FocusHeight;
     var brush=new ImageBrush(poster) {ViewboxUnits=BrushMappingMode.RelativeToBoundingBox,Viewbox=new Rect(fx+x*fw/9,fy+y*fh/9,fw/9,fh/9),Stretch=Stretch.Fill};
     d.PushOpacity(tileOpacity*(.5+.5*(1-spread)));d.PushTransform(new RotateTransform(Math.Sin(seed)*spread*160,px,py));
     d.DrawRectangle(brush,null,new Rect(px,py,size,size));d.Pop();d.Pop();
    }
   }
   double show=Geometry.Ease((s-3.65)/.6);
   var geometry=new StreamGeometry();
   using(var ctx=geometry.Open())foreach(var f in strokes) {
    double arrival=Geometry.Ease((assemble*1.5-f.X+.12)/.34),loose=1-arrival;
    double sx=.5+(f.SX-.5)*spread,sy=.44+(f.SY-.44)*spread;
    double x=sx*loose+f.X*arrival+Math.Sin(s+f.Seed)*.008*loose*spread;
    double y=sy*loose+f.Y*arrival+Math.Cos(s+f.Seed)*.008*loose*spread;
    Point p=Map(x,y,image);double scale=.15+.85*arrival;
    ctx.BeginFigure(p,false,false);ctx.LineTo(new Point(p.X+f.DX*image.Width*scale,p.Y+f.DY*image.Height*scale),true,false);
   }
   Pen line=composition==null?contour:new Pen(new SolidColorBrush(composition.Accents[0]),1.1);
   geometry.Freeze();d.PushOpacity(show*.9);d.DrawGeometry(null,line,geometry);d.Pop();
  }
 }
 public static class CinematicPreview {
  public static void Render(string root,Config c) {
   var poster=PosterCache.Prepare(root,c.Video);var scene=new CinematicScene(c,poster);
   const int width=1280,height=720;
   var panel=new System.Windows.Controls.Grid {Width=width,Height=height,Background=Brushes.Black};
   panel.Children.Add(scene);
   panel.Measure(new Size(width,height));panel.Arrange(new Rect(0,0,width,height));panel.UpdateLayout();
   string directory=Path.Combine(root,"qa","cinematic");Directory.CreateDirectory(directory);
   double[] times={.4,1.5,2.8,3.9,4.6,5.3,6.2,6.8,7.6,12,20,21,22};
   var watch=Stopwatch.StartNew();int n=0;
   foreach(double t in times) {
    scene.SetLaunchState(t>=21,t>=21,t>=22);scene.SetTime(t);panel.UpdateLayout();var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(panel);
    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using(var file=File.Create(Path.Combine(directory,"stage-"+(n++).ToString("00")+".png")))encoder.Save(file);
   }
   File.WriteAllText(Path.Combine(directory,"render.json"),"{\"frames\":"+n+",\"milliseconds\":"+watch.ElapsedMilliseconds+"}");
  }
 }
}
