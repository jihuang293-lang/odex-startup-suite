// Original sample artwork and actual CinematicScene rendering. No user media.
using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DragonCodexBoot;

internal static class PreviewRenderer {
 const int W=960,H=600;
 static Brush ColorBrush(string color) {return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));}
 static void Text(DrawingContext d,string text,double x,double y,double size,string color) {
  var f=new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Consolas"),size,ColorBrush(color));
  d.DrawText(f,new Point(x,y));
 }
 static BitmapSource Bitmap(Visual visual,int width,int height) {
  var result=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);result.Render(visual);result.Freeze();return result;
 }
 static void Save(BitmapSource bitmap,string path) {
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))encoder.Save(file);
 }
 static BitmapSource Artwork() {
  var visual=new DrawingVisual();using(var d=visual.RenderOpen()) {
   d.DrawRectangle(new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#050C1B"),(Color)ColorConverter.ConvertFromString("#14294B"),35),null,new Rect(0,0,W,H));
   var glow=new RadialGradientBrush();glow.GradientStops.Add(new GradientStop(Color.FromArgb(135,40,110,240),0));glow.GradientStops.Add(new GradientStop(Color.FromArgb(0,50,60,130),1));
   d.DrawEllipse(glow,null,new Point(680,265),440,345);
   var stars=new Random(713);for(int i=0;i<140;i++){double r=stars.NextDouble()*1.1+.25;d.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)stars.Next(25,140),180,219,255)),null,new Point(stars.Next(W),stars.Next(H)),r,r);}
   var grid=new Pen(new SolidColorBrush(Color.FromArgb(25,103,184,244)),1);
   for(int i=0;i<24;i++)d.DrawLine(grid,new Point(480+(i-12)*14,365),new Point(480+(i-12)*115,H));
   for(int i=0;i<9;i++){double y=370+Math.Pow(i/8.0,2)*230;d.DrawLine(grid,new Point(0,y),new Point(W,y));}
   var orb=new RadialGradientBrush();orb.GradientOrigin=new Point(.26,.18);orb.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#5D9FC5"),0));orb.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#153D69"),.45));orb.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#070F26"),1));
   d.DrawEllipse(orb,new Pen(ColorBrush("#69BAD9"),1.5),new Point(657,277),174,174);
   d.PushClip(new EllipseGeometry(new Point(657,277),173,173));
   var bands=new Pen(new SolidColorBrush(Color.FromArgb(70,146,218,255)),1);
   for(int i=-6;i<7;i++)d.DrawEllipse(null,bands,new Point(657,277+i*30),179,37);
   for(int i=0;i<6;i++)d.DrawEllipse(null,bands,new Point(657,277),25+i*26,174);
   d.Pop();
   d.PushTransform(new RotateTransform(-24,657,277));
   d.DrawEllipse(null,new Pen(ColorBrush("#81CADA"),2),new Point(657,277),282,61);
   d.DrawEllipse(null,new Pen(new SolidColorBrush(Color.FromArgb(75,123,175,240)),1),new Point(657,277),293,70);d.Pop();
   d.DrawLine(new Pen(ColorBrush("#82BDD2"),1),new Point(68,470),new Point(275,470));
   Text(d,"A WORKSPACE FOR WHAT COMES NEXT",68,485,12,"#91AABD");
   Text(d,"COMMUNITY PREVIEW / SAMPLE ARTWORK",68,512,10,"#597A96");
  }return Bitmap(visual,W,H);
 }
 static BitmapSource Frame(Config config,BitmapSource poster,double time) {
  var scene=new CinematicScene(config,poster);scene.SetLaunchState(time>=10.2,time>=10.2,time>=11.2);scene.SetTime(time);
  var panel=new Grid {Width=W,Height=H,Background=Brushes.Black};
  panel.Children.Add(new Image {Source=poster,Stretch=Stretch.UniformToFill});panel.Children.Add(scene);
  panel.Measure(new Size(W,H));panel.Arrange(new Rect(0,0,W,H));panel.UpdateLayout();return Bitmap(panel,W,H);
 }
 [STAThread] public static int Main(string[] args) {
  try {
   string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);string frames=Path.Combine(root,"frames");Directory.CreateDirectory(frames);
   var config=Entry.ReadConfigFile(args[1]);var poster=Artwork();
   double[] moments={1.6,3.95,5.8,9.8};string[] labels={"01 / INITIALIZE","02 / DISPERSE","03 / RECONSTRUCT","04 / WELCOME"};
   var contact=new DrawingVisual();using(var d=contact.RenderOpen()) {
    d.DrawRectangle(ColorBrush("#070C15"),null,new Rect(0,0,1280,964));
    Text(d,"CODEX STARTUP SUITE",28,20,27,"#D8F2FF");Text(d,"ACTUAL ANIMATION RENDER / ORIGINAL SAMPLE BACKGROUND",28,58,13,"#7EA8BE");
    for(int i=0;i<4;i++) {
     double x=24+(i%2)*632,y=103+(i/2)*416;
     d.DrawImage(Frame(config,poster,moments[i]),new Rect(x,y,608,380));
     Text(d,labels[i],x+8,y+387,14,"#A3C9D9");
    }
    Text(d,"Wallpaper follows your active skin. Preview timing demonstrates the animation stages.",28,946,11,"#7393AA");
   }Save(Bitmap(contact,1280,964),Path.Combine(root,"animation-overview.png"));
   for(int i=0;i<144;i++)Save(Frame(config,poster,i/12.0),Path.Combine(frames,"frame-"+i.ToString("000")+".png"));
   Console.WriteLine("Rendered 144 animation frames and a four-stage overview with original sample artwork.");return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
 }
}
