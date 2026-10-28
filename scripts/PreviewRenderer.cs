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
 static BitmapSource Load(string path) {
  var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(Path.GetFullPath(path));image.EndInit();image.Freeze();return image;
 }
 static void Chinese(DrawingContext d,string text,double x,double y,double size,string color) {
  d.DrawText(new FormattedText(text,CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,ColorBrush(color)),new Point(x,y));
 }
 static void Feature(string root,Config config,BitmapSource poster) {
  var source=Load(Path.Combine(root,"wallpaper-engine-source.jpg"));var actual=SkinLayout(poster);
  var visual=new DrawingVisual();using(var d=visual.RenderOpen()) {
   d.DrawRectangle(ColorBrush("#080F1B"),null,new Rect(0,0,1500,840));
   Text(d,"CODEX STARTUP SUITE / WALLPAPER ENGINE",40,27,17,"#7DB8D6");
   Chinese(d,"同一张壁纸，贯穿 Codex 开场与应用背景",40,66,35,"#EDF6FF");
   Chinese(d,"视频动态壁纸 + 场景壁纸，都能扫描、导入和应用",40,119,21,"#8CD6F5");
   string[] en={"WALLPAPER ENGINE","STARTUP ANIMATION","CODEX BACKGROUND"};
   string[] zh={"五条悟 · 场景画面实采","同源壁纸 · 实际开场渲染","同源壁纸 · 背景界面示意"};
   BitmapSource[] images={source,Frame(config,poster,5.8),actual};
   for(int i=0;i<3;i++) {
    double x=40+490*i;
    Text(d,en[i],x,184,23,"#E5F4FF");Chinese(d,zh[i],x,220,17,"#8EABC1");
    d.PushClip(new RectangleGeometry(new Rect(x,264,440,275),8,8));
    var brush=new ImageBrush(images[i]){Stretch=Stretch.UniformToFill};d.DrawRectangle(brush,null,new Rect(x,264,440,275));d.Pop();
    d.DrawRoundedRectangle(null,new Pen(ColorBrush("#304B65"),1),new Rect(x,264,440,275),8,8);
    if(i<2){double a=x+453,y=398;d.DrawLine(new Pen(ColorBrush("#71C6E4"),2),new Point(a,y),new Point(a+21,y));d.DrawLine(new Pen(ColorBrush("#71C6E4"),2),new Point(a+13,y-7),new Point(a+21,y));d.DrawLine(new Pen(ColorBrush("#71C6E4"),2),new Point(a+13,y+7),new Point(a+21,y));}
   }
   d.DrawRoundedRectangle(ColorBrush("#102034"),null,new Rect(40,580,1420,90),10,10);
   Chinese(d,"视频动态壁纸 / VIDEO",61,595,22,"#BDE7FF");
   Chinese(d,"扫描并导入视频  →  开场跟随当前视频  →  Codex 动态背景",61,631,19,"#D1DFEC");
   d.DrawRoundedRectangle(ColorBrush("#102034"),null,new Rect(40,687,1420,90),10,10);
   Chinese(d,"场景壁纸 / SCENE",61,702,22,"#BDE7FF");
   Chinese(d,"场景桥接组件  →  开场使用自动生成的预览图  →  Codex 内实时渲染播放",61,738,19,"#D1DFEC");
   Chinese(d,"Wallpaper Engine · 五条悟 / 3021850803 · 右侧为界面示意，不含真实聊天内容",40,805,15,"#8CA4B9");
  }Save(Bitmap(visual,1500,840),Path.Combine(root,"wallpaper-link.png"));
 }
 static BitmapSource SkinLayout(BitmapSource poster) {
  var v=new DrawingVisual();using(var d=v.RenderOpen()) {
   d.DrawRectangle(new ImageBrush(poster){Stretch=Stretch.UniformToFill},null,new Rect(0,0,W,H));
   d.DrawRectangle(new SolidColorBrush(Color.FromArgb(75,4,10,20)),null,new Rect(0,0,W,H));
   d.DrawRectangle(new SolidColorBrush(Color.FromArgb(170,7,13,23)),null,new Rect(0,0,W,44));
   d.DrawRectangle(new SolidColorBrush(Color.FromArgb(175,7,13,23)),null,new Rect(0,44,210,H-44));
   Text(d,"CODEX",20,12,16,"#E2F2FF");Text(d,"WORKSPACE",232,12,14,"#BED0DE");
   Text(d,"+ New chat",22,75,16,"#E2F2FF");Text(d,"Projects",22,130,14,"#A4BDCF");
   Text(d,"  Workspace",22,165,14,"#D3E3F0");Text(d,"  Ideas",22,205,14,"#D3E3F0");Text(d,"  Build",22,245,14,"#D3E3F0");
   Text(d,"What will you build?",290,260,30,"#FFFFFF");
   d.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(210,12,20,32)),new Pen(ColorBrush("#829AB2"),1),new Rect(250,466,665,90),14,14);
   Text(d,"Describe your next idea...",274,489,17,"#D2E3EE");
   Text(d,"UI LAYOUT DEMO",736,573,12,"#D2E3EE");
  }return Bitmap(v,W,H);
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
   var config=Entry.ReadConfigFile(args[1]);bool custom=args.Length>2;var poster=custom?Load(args[2]):Artwork();
   if(File.Exists(Path.Combine(root,"wallpaper-engine-source.jpg")))Feature(root,config,poster);
   double[] moments={1.6,3.95,5.8,9.8};string[] labels={"01 / INITIALIZE","02 / DISPERSE","03 / RECONSTRUCT","04 / WELCOME"};
   var contact=new DrawingVisual();using(var d=contact.RenderOpen()) {
    d.DrawRectangle(ColorBrush("#070C15"),null,new Rect(0,0,1280,964));
    Text(d,"CODEX STARTUP SUITE",28,20,27,"#D8F2FF");Text(d,custom?"ACTUAL ANIMATION RENDER / WALLPAPER ENGINE BACKGROUND":"ACTUAL ANIMATION RENDER / ORIGINAL SAMPLE BACKGROUND",28,58,13,"#7EA8BE");
    for(int i=0;i<4;i++) {
     double x=24+(i%2)*632,y=103+(i/2)*416;
     d.DrawImage(Frame(config,poster,moments[i]),new Rect(x,y,608,380));
     Text(d,labels[i],x+8,y+387,14,"#A3C9D9");
    }
    Text(d,"Wallpaper follows your active skin. Preview timing demonstrates the animation stages.",28,946,11,"#7393AA");
   }Save(Bitmap(contact,1280,964),Path.Combine(root,"animation-overview.png"));
   for(int i=0;i<144;i++)Save(Frame(config,poster,i/12.0),Path.Combine(frames,"frame-"+i.ToString("000")+".png"));
   Console.WriteLine("Rendered 144 animation frames and a four-stage overview using the selected background.");return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
 }
}
