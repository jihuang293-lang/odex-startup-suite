using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using CodexSceneBridge;
internal static class NativePosterTests {
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);}
 static void Image(string path,int width,int height,Color color){using(var bitmap=new Bitmap(width,height))using(var draw=Graphics.FromImage(bitmap)){draw.Clear(color);bitmap.Save(path,ImageFormat.Jpeg);}}
 static string Hash(string path){using(var sha=SHA256.Create())using(var input=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
 static Dictionary<string,object> Read(string path){return new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)) as Dictionary<string,object>;}
 static void Rejected(Action action,string label){bool rejected=false;try{action();}catch(IOException){rejected=true;}catch(InvalidDataException){rejected=true;}Check(rejected,label);}
 public static int Main(string[] args){try{
  string directory=Path.Combine(Path.GetFullPath(args[0]),"poster-cache");Directory.CreateDirectory(directory);
  string destination=Path.Combine(directory,"12345.jpg"),metadata=Path.Combine(directory,"12345.json"),capture=Path.Combine(directory,"capture.tmp.jpg");
  Image(capture,640,360,Color.CornflowerBlue);ScenePosterCache.Publish(capture,destination,"12345",640,360);
  var record=Read(metadata);Check((int)record["schemaVersion"]==1&&(string)record["source"]=="rendered-scene"&&(string)record["workshopId"]=="12345","Rendered source identity metadata missing");
  Check((int)record["width"]==640&&(int)record["height"]==360&&(string)record["imageSha256"]==Hash(destination),"Metadata dimensions or digest do not match image");
  DateTimeOffset parsed;Check(DateTimeOffset.TryParse((string)record["capturedAt"],out parsed),"Capture timestamp missing");
  string oldHash=Hash(destination),oldRecord=File.ReadAllText(metadata);
  Image(capture,640,360,Color.Black);Rejected(delegate{ScenePosterCache.Publish(capture,destination,"12345",640,360);},"Black frame must be rejected");
  Check(Hash(destination)==oldHash&&File.ReadAllText(metadata)==oldRecord,"Black retry overwrote previous usable capture");
  Image(capture,720,720,Color.Orange);Rejected(delegate{ScenePosterCache.Publish(capture,destination,"12345",640,360);},"A Workshop-shaped thumbnail must not masquerade as a full scene frame");
  Check(Hash(destination)==oldHash&&File.ReadAllText(metadata)==oldRecord,"Wrong dimensions overwrote previous usable capture");
  Image(capture,640,360,Color.SeaGreen);ScenePosterCache.Publish(capture,destination,"12345",640,360);
  Check(Hash(destination)!=oldHash&&(string)Read(metadata)["imageSha256"]==Hash(destination),"Retry publication did not replace the image and digest together");
  oldHash=Hash(destination);Image(capture,640,360,Color.Orange);Rejected(delegate{ScenePosterCache.Publish(capture,destination,"../12345",640,360);},"Invalid Workshop ID accepted");
  Check(Hash(destination)==oldHash,"Invalid identity changed the cache");
  string flat=Path.Combine(directory,"flat.jpg"),detail=Path.Combine(directory,"detail.jpg"),black=Path.Combine(directory,"black.jpg");
  Image(flat,640,360,Color.CornflowerBlue);Image(black,640,360,Color.Black);
  using(var bitmap=new Bitmap(640,360))using(var draw=Graphics.FromImage(bitmap)){
   draw.Clear(Color.FromArgb(8,14,25));for(int i=0;i<25;i++)draw.FillRectangle(Brushes.White,180+i*7,70,3,220);bitmap.Save(detail,ImageFormat.Jpeg);
  }
  double quality;Check(ScenePosterCache.SelectBest(new string[]{black,flat,detail},640,360,out quality)==detail,"Multi-frame selection did not skip black/flat startup frames");
  Rejected(delegate{double score;ScenePosterCache.SelectBest(new string[]{black},640,360,out score);},"An all-black capture set was accepted");
  if(args.Length>1){
   File.Copy(args[1],capture,true);using(var bitmap=new Bitmap(capture))ScenePosterCache.Validate(capture,bitmap.Width,bitmap.Height);
   int width,height;using(var bitmap=new Bitmap(capture)){width=bitmap.Width;height=bitmap.Height;}ScenePosterCache.Publish(capture,destination,"12345",width,height);
   Check((string)Read(metadata)["imageSha256"]==Hash(destination),"Real scene frame did not publish a matching digest");
  }
  string diagnostics=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"poster-cache","poster-diagnostics.log"),token=new string('a',48);
  var log=typeof(Bridge).GetMethod("PosterEvent",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
  using(var bridge=new Bridge(new Options{Scene=Path.Combine(args[0],"12345","scene.pkg")})){
   log.Invoke(bridge,new object[]{"failed: http://127.0.0.1:54321/"+token+"/stream.mp4"});
   Check(!File.ReadAllText(diagnostics).Contains(token)&&File.ReadAllText(diagnostics).Contains("/<token>/"),"Diagnostic leaked a stream token");
   for(int i=0;i<100;i++)log.Invoke(bridge,new object[]{new string('x',1000)});
  }
  Check(new FileInfo(diagnostics).Length<66560,"Poster diagnostic was not bounded");
  Console.WriteLine("PASS: trusted rendered-scene sidecar and digest, black/wrong-sized frame preservation, atomic retry replacement, path identity rejection, bounded redacted diagnostics, and optional real scene frame.");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
