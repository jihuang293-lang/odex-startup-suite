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
  public static BitmapSource Prepare(string root,string media) {
   string ext=Path.GetExtension(media).ToLowerInvariant(),image=media;
   if(ext==".mp4") {
    FileInfo f=new FileInfo(media);string stamp=f.FullName+"|"+f.Length+"|"+f.LastWriteTimeUtc.Ticks;
    string key;
    using(var sha=SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(stamp))).Replace("-","").Substring(0,24);
    string directory=Path.Combine(root,"media","poster-cache");Directory.CreateDirectory(directory);
    image=Path.Combine(directory,key+".png");
    if(!File.Exists(image)) {
     string ffmpeg=Path.Combine(root,"tools","ffmpeg.exe");
     // Both paths are local filenames; ProcessStartInfo never invokes a shell.
     string temporary=Path.Combine(directory,key+"."+Process.GetCurrentProcess().Id+".png");
     using(var p=Process.Start(new ProcessStartInfo(ffmpeg,"-v error -ss 0 -i \""+media+"\" -frames:v 1 -vf scale=960:-1 -an -y \""+temporary+"\"") {
      UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden
     })) {
      if(!p.WaitForExit(12000)) {try {p.Kill();}catch{}throw new IOException("Wallpaper preview preparation timed out.");}
      if(p.ExitCode!=0||!File.Exists(temporary))throw new IOException("Could not prepare this wallpaper for the cinematic intro.");
     }
     if(!File.Exists(image))File.Move(temporary,image);else File.Delete(temporary);
    }
   }
   var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
   bitmap.UriSource=new Uri(Path.GetFullPath(image));bitmap.DecodePixelWidth=960;bitmap.EndInit();bitmap.Freeze();return bitmap;
  }
 }
 public sealed class CinematicScene:FrameworkElement {
  private sealed class Stroke {public double X,Y,DX,DY,SX,SY,Seed;}
  private readonly Config config;
  private readonly BitmapSource poster;
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
   config=c;poster=image;IsHitTestVisible=false;
   BuildContours();
  }
  public void SetLaunchState(bool verified,bool aligning,bool aligned) {appVerified=verified;appAligning=aligning;appAligned=aligned&&verified;}
  public void SetTime(double t) {
   seconds=t;progress=Math.Max(progress,CinematicTiming.Progress(t,appVerified,appAligned));
   if(!appAligned)progress=Math.Min(.96,progress);InvalidateVisual();
  }
  private void BuildContours() {
   // Downsample once. Each selected edge becomes a short independently moving stroke.
   var scaled=new TransformedBitmap(poster,new ScaleTransform(640.0/poster.PixelWidth,640.0/poster.PixelWidth));
   var gray=new FormatConvertedBitmap(scaled,PixelFormats.Gray8,null,0);int w=gray.PixelWidth,h=gray.PixelHeight;
   byte[] pixels=new byte[w*h];gray.CopyPixels(pixels,w,0);var random=new Random(70419);
   for(int y=2;y<h-2;y+=3)for(int x=2;x<w-2;x+=3) {
    double gx=-pixels[(y-1)*w+x-1]+pixels[(y-1)*w+x+1]-2*pixels[y*w+x-1]+2*pixels[y*w+x+1]-pixels[(y+1)*w+x-1]+pixels[(y+1)*w+x+1];
    double gy=-pixels[(y-1)*w+x-1]-2*pixels[(y-1)*w+x]-pixels[(y-1)*w+x+1]+pixels[(y+1)*w+x-1]+2*pixels[(y+1)*w+x]+pixels[(y+1)*w+x+1];
    double strength=Math.Sqrt(gx*gx+gy*gy);if(strength<85)continue;
    double angle=random.NextDouble()*Math.PI*2,r=.08+random.NextDouble()*.56;
    strokes.Add(new Stroke {X=x/(double)w,Y=y/(double)h,DX=-gy/strength*3.4/w,DY=gx/strength*3.4/h,
     SX=.5+Math.Cos(angle)*r,SY=.44+Math.Sin(angle)*r,Seed=random.NextDouble()*6.28});
   }
   // Uniform budget keeps detailed wallpaper from making the opening CPU bound.
   if(strokes.Count>6500) {var keep=new List<Stroke>();double stride=strokes.Count/6500.0;for(double i=0;i<strokes.Count;i+=stride)keep.Add(strokes[(int)i]);strokes.Clear();strokes.AddRange(keep);}
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
   var brush=new LinearGradientBrush(Color.FromRgb(247,251,255),color,90);
   var layout=TechLayout(text,size);
   if(layout.Width>available) {size*=available/layout.Width;layout=TechLayout(text,size);}
   double margin=Math.Max(24,w*.04);
   double left=Math.Max(margin,Math.Min(w-margin-layout.Width,x-layout.Width*align));
   double top=Math.Max(54,Math.Min(h-38-layout.Height,y));
   // Each phrase settles gently into its own composition, with no visible panel.
   double drift=(1-entrance)*Math.Min(20,w*.016);
   left+=align>.5?drift:-drift;top+=drift*.35;
   var shade=new RadialGradientBrush();
   shade.GradientStops.Add(new GradientStop(Color.FromArgb(175,0,0,0),0));
   shade.GradientStops.Add(new GradientStop(Color.FromArgb(135,0,0,0),.45));
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
   d.DrawGeometry(new SolidColorBrush(Color.FromArgb(180,0,0,0)),new Pen(Brushes.Black,2),layout.Shape);d.Pop();
   d.DrawGeometry(null,new Pen(new SolidColorBrush(Color.FromArgb(42,color.R,color.G,color.B)),4),layout.Shape);
   d.DrawGeometry(brush,null,layout.Shape);d.Pop();
  }
  private void DrawWelcome(DrawingContext d,double w,double h,double t) {
   if(!config.ShowWelcome)return;
   string title,tag;double alpha,start,x,y,available,align;Color color;
   if(t<3.9) {
    title="It starts\nwith an idea.";tag="01 / CODEX.INIT";start=.25;
    x=w*.08;y=h*.25;available=w*.4;align=0;color=Color.FromRgb(126,219,255);
    alpha=Geometry.Ease((t-.25)/.65)*(1-Geometry.Ease((t-3.25)/.45));
   } else if(t<7.1) {
    title="Watch it\ntake shape.";tag="02 / IDEA.COMPILE";start=4.05;
    x=w*.91;y=h*.32;available=w*.38;align=1;color=Color.FromRgb(200,169,255);
    alpha=Geometry.Ease((t-4.05)/.5)*(1-Geometry.Ease((t-6.55)/.45));
   } else if(t<8.95) {
    title=config.WelcomeTitle;tag="03 / SESSION.ONLINE";start=7.1;
    x=w*.5;y=h*.43;available=w*.84;align=.5;color=Color.FromRgb(222,241,255);
    alpha=Geometry.Ease((t-7.1)/.5)*(1-Geometry.Ease((t-8.5)/.4));
   } else {
    title=config.WelcomeSubtitle;tag="04 / READY.TO.BUILD";start=8.95;
    if(title=="Let\u2019s build what\u2019s next.")title="Let\u2019s build\nwhat\u2019s next.";
    x=w*.09;y=h*.65;available=w*.7;align=0;color=Color.FromRgb(255,218,155);
    alpha=Geometry.Ease((t-8.95)/.55);
   }
   if(alpha<=0)return;
   double size=Math.Min(64,Math.Max(26,w*.034));
   d.PushOpacity(alpha);
   Caption(d,title,tag,w,h,x,y,available,align,size,color,Geometry.Ease((t-start)/.7));
   d.Pop();
  }
  private Rect Cover(double width,double height) {
   double scale=Math.Max(width/poster.PixelWidth,height/poster.PixelHeight);
   return new Rect((width-poster.PixelWidth*scale)/2,(height-poster.PixelHeight*scale)/2,poster.PixelWidth*scale,poster.PixelHeight*scale);
  }
  private Point Map(double x,double y,Rect image) {return new Point(image.Left+x*image.Width,image.Top+y*image.Height);}
  protected override void OnRender(DrawingContext d) {
   base.OnRender(d);double w=ActualWidth,h=ActualHeight;if(w<2||h<2)return;
   double s=seconds,color=Geometry.Ease((s-6.6)/.5);Rect area=new Rect(0,0,w,h),image=Cover(w,h);
   d.PushClip(new RectangleGeometry(area));
   // At full color this layer disappears and exposes the already playing wallpaper video.
   d.PushOpacity(1-color);d.DrawRectangle(dark,null,area);
   double ui=Geometry.Ease(s/.35)*(1-Geometry.Ease((s-3.5)/.5));
   if(s<4.1) {
    d.PushOpacity(ui);DrawIdentity(d,w,h,s);d.Pop();
   }
   if(s>=3.5&&s<7.1) {
    double scatter=Geometry.Ease((s-3.5)/.9),assemble=Geometry.Clamp((s-4.4)/2.2);
    DrawFragments(d,w,h,image,scatter,assemble,s);
   }
   d.Pop();
   DrawWelcome(d,w,h,s);
   DrawProgress(d,w,h,s);
   d.Pop();
  }
  private void DrawProgress(DrawingContext d,double w,double h,double t) {
   double x=Math.Max(28,w*.04),y=Math.Max(28,h*.065),width=Math.Min(w-2*x,Math.Max(320,Math.Min(450,w*.32)));
   double labelSize=Math.Min(18,Math.Max(13,w*.009)),phaseSize=Math.Min(16,Math.Max(12,w*.008));
   string phase=appAligned?"READY TO CREATE":appAligning?"ALIGNING WORKSPACE":t<.9?"INITIALIZING CODEX":t<3.5?"PREPARING WORKSPACE":t<6.6?"BUILDING THE SCENE":t<7.1?"BRINGING IT TO LIFE":appVerified?"WORKSPACE READY":"WAITING FOR CODEX";
   d.PushOpacity(Geometry.Ease((t-.15)/.35));
   Text(d,"CODEX / BOOT SEQUENCE",x,y,labelSize,ink,false,true);
   Text(d,((int)Math.Round(progress*100)).ToString("00",CultureInfo.InvariantCulture)+"%",x+width-labelSize*3,y,labelSize,ink,false,true);
   double barY=y+labelSize+14;
   d.DrawRectangle(new SolidColorBrush(Color.FromArgb(65,190,222,245)),null,new Rect(x,barY,width,3));
   d.DrawRectangle(ink,null,new Rect(x,barY,Math.Max(1,width*progress),3));
   if(!appAligned) {
    double tip=x+width*progress;
    d.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(100+100*(.5+.5*Math.Sin(t*4))),210,240,255)),null,new Point(tip,barY+1.5),2.5,2.5);
   }
   Text(d,phase,x,barY+14,phaseSize,ink,false,true);d.Pop();
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
   d.DrawRectangle(brush,null,avatar);
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
     var brush=new ImageBrush(poster) {ViewboxUnits=BrushMappingMode.RelativeToBoundingBox,Viewbox=new Rect(.33+x*.34/9,.25+y*.5/9,.34/9,.5/9),Stretch=Stretch.Fill};
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
   geometry.Freeze();d.PushOpacity(show*.9);d.DrawGeometry(null,contour,geometry);d.Pop();
  }
 }
 public static class CinematicPreview {
  public static void Render(string root,Config c) {
   var poster=PosterCache.Prepare(root,c.Video);var scene=new CinematicScene(c,poster);
   var panel=new System.Windows.Controls.Grid {Width=1120,Height=720,Background=Brushes.Black};
   panel.Children.Add(new System.Windows.Controls.Image {Source=poster,Stretch=Stretch.UniformToFill});panel.Children.Add(scene);
   panel.Measure(new Size(1120,720));panel.Arrange(new Rect(0,0,1120,720));panel.UpdateLayout();
   string directory=Path.Combine(root,"qa","cinematic");Directory.CreateDirectory(directory);
   double[] times={.4,1.5,2.8,3.9,4.6,5.3,6.2,6.8,7.6,12,20,21,22};
   var watch=Stopwatch.StartNew();int n=0;
   foreach(double t in times) {
    scene.SetLaunchState(t>=21,t>=21,t>=22);scene.SetTime(t);panel.UpdateLayout();var bitmap=new RenderTargetBitmap(1120,720,96,96,PixelFormats.Pbgra32);bitmap.Render(panel);
    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using(var file=File.Create(Path.Combine(directory,"stage-"+(n++).ToString("00")+".png")))encoder.Save(file);
   }
   File.WriteAllText(Path.Combine(directory,"render.json"),"{\"frames\":"+n+",\"milliseconds\":"+watch.ElapsedMilliseconds+"}");
  }
 }
}
