using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using DragonCodexBoot;
class PosterCompositionTests {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Write(string path,int width,int height,Color color){
  var v=new DrawingVisual();using(var d=v.RenderOpen())d.DrawRectangle(new SolidColorBrush(color),null,new Rect(0,0,width,height));
  var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(v);
  var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(path))png.Save(file);
 }
 static byte[] Pixel(CinematicScene scene,int x,int y){
  scene.Measure(new Size(640,360));scene.Arrange(new Rect(0,0,640,360));scene.UpdateLayout();
  var bitmap=new RenderTargetBitmap(640,360,96,96,PixelFormats.Pbgra32);bitmap.Render(scene);
  var bytes=new byte[4];bitmap.CopyPixels(new Int32Rect(x,y,1,1),bytes,4,0);return bytes;
 }
 [STAThread]static int Main(string[] args){try{
  Directory.CreateDirectory(args[0]);string path=Path.Combine(args[0],"refresh.png");
  foreach(var source in new Size[]{new Size(720,720),new Size(1280,720),new Size(720,1280)})foreach(var target in new Size[]{new Size(2560,1440),new Size(1058,877)}){
   Rect fit=CinematicScene.FitPoster(target.Width,target.Height,source.Width,source.Height);
   Check(fit.X>=-.01&&fit.Y>=-.01&&fit.Right<=target.Width+.01&&fit.Bottom<=target.Height+.01,"Poster was cropped outside the viewport");
   Check(Math.Abs(fit.Width/fit.Height-source.Width/source.Height)<.0001,"Poster proportions changed");
  }
  Write(path,320,320,Colors.Red);var c=new Config{Video=path,ShowWelcome=false,AutoAesthetics=false};
  var scene=new CinematicScene(c,PosterCache.Prepare(args[0],path));scene.SetTime(12);
  byte[] before=Pixel(scene,40,180);Check(before[0]==0&&before[1]==0&&before[2]==0,"Square thumbnail must fit, without a full-screen crop");
  Write(path,640,360,Colors.Lime);File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddSeconds(2));
  scene.SetTime(14);scene.SetTime(15);byte[] after=Pixel(scene,40,180);
  Check(after[1]>240&&after[0]<10&&after[2]<10,"Replacement scene frame did not refresh and fade in");
  scene.SetLaunchState(true,true,false);Write(path,640,360,Colors.Blue);File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddSeconds(4));scene.SetTime(17);
  Check(Pixel(scene,40,180)[1]>240,"Poster changed during the window handoff");
  scene.SetLaunchState(true,false,false);scene.SetTime(18);scene.SetTime(19);Check(Pixel(scene,40,180)[0]>240,"Poster refresh did not resume after handoff cancellation");
  string trustedDirectory=Path.Combine(args[0],Guid.NewGuid().ToString("N"));Directory.CreateDirectory(trustedDirectory);
  string trusted=Path.Combine(trustedDirectory,"12345.png");Write(trusted,640,360,Colors.Lime);
  var gated=new CinematicScene(new Config{Video=trusted,ShowWelcome=false,ScenePoster=true,AutoAesthetics=false},PosterCache.Prepare(args[0],trusted));gated.SetTime(12);
  Check(Pixel(gated,40,180)[1]<40,"Unmarked Workshop thumbnail appeared as a full scene");
  string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(trusted))).Replace("-","").ToLowerInvariant();
  var record=new {schemaVersion=1,source="rendered-scene",workshopId="12345",width=640,height=360,imageSha256=hash};
  File.WriteAllText(Path.ChangeExtension(trusted,".json"),new JavaScriptSerializer().Serialize(record));
  gated.SetTime(14);gated.SetTime(22);Check(Pixel(gated,40,180)[1]>240,"Trusted native scene did not replace the waiting cover");
  Write(trusted,640,360,Colors.Blue);BitmapSource invalid;
  Check(!PosterCache.TryRenderedScene(trusted,out invalid),"Old trusted marker authenticated a replacement thumbnail");
  Console.WriteLine("PASS: six uncropped aspect-ratio layouts; live square-preview replacement by a full scene frame; actual WPF pixel evidence.");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
