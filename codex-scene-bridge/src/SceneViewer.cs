// Local Wallpaper Engine capture bridge. Original MIT-licensed implementation.
// Wallpaper Engine renders the scene; FFmpeg captures only the verified pop-out HWND.
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexSceneBridge {
 public static class Entry {
  public static string Quote(string value) {
   if(value==null)throw new ArgumentNullException("value");
   var result=new StringBuilder("\"");int slashes=0;
   foreach(char c in value) {
    if(c=='\\') {slashes++;continue;}
    if(c=='\"')result.Append('\\',slashes*2+1).Append(c);
    else {result.Append('\\',slashes).Append(c);}
    slashes=0;
   }
   return result.Append('\\',slashes*2).Append('"').ToString();
  }
  internal static string Arguments(IEnumerable<string> values) {var quoted=new List<string>();foreach(string value in values)quoted.Add(Quote(value));return String.Join(" ",quoted.ToArray());}
  public static int Main(string[] args) {
   try {
    if(args.Length==1&&args[0]=="--self-test")return SelfTest();
    using(var bridge=new Bridge(Options.Parse(args)))return bridge.Run();
   } catch(Exception error) {Console.Error.WriteLine("scene-bridge: "+error.Message);return 1;}
  }
  private static void Check(bool value,string label) {if(!value)throw new InvalidOperationException("Self-test failed: "+label);}
  private static byte[] Box(string type,byte[] payload) {
   int size=payload.Length+8;var result=new byte[size];result[0]=(byte)(size>>24);result[1]=(byte)(size>>16);result[2]=(byte)(size>>8);result[3]=(byte)size;
   Encoding.ASCII.GetBytes(type,0,4,result,4);Buffer.BlockCopy(payload,0,result,8,payload.Length);return result;
  }
  private static byte[] Join(params byte[][] data) {using(var buffer=new MemoryStream()){foreach(var item in data)buffer.Write(item,0,item.Length);return buffer.ToArray();}}
  private static int SelfTest() {
   var init=Join(Box("ftyp",new byte[]{1,2}),Box("moov",new byte[]{3,4}));
   var one=Join(Box("moof",new byte[]{5}),Box("mdat",new byte[]{6,7}));
   var two=Join(Box("moof",new byte[]{8}),Box("mdat",new byte[]{9,10}));int frames=0;
   using(var source=new MemoryStream(Join(init,one,two)))Mp4Reader.Parse(source,delegate(byte[] header,byte[] fragment){Check(Convert.ToBase64String(header)==Convert.ToBase64String(init),"init retained");Check(Convert.ToBase64String(fragment)==Convert.ToBase64String(frames++==0?one:two),"fragment retained");});
   Check(frames==2,"two complete fragments");
   var tfdt1=Box("tfdt",new byte[]{1,0,0,0,0,0,0,0,0,0,3,232});
   var tfdt2=Box("tfdt",new byte[]{1,0,0,0,0,0,0,0,0,0,7,208});
   ulong? origin=null;var rebasedOne=FragmentClock.Rebase(Join(Box("moof",Box("traf",tfdt1)),Box("mdat",new byte[]{0})),ref origin);
   var rebasedTwo=FragmentClock.Rebase(Join(Box("moof",Box("traf",tfdt2)),Box("mdat",new byte[]{0})),ref origin);
   Check(origin==1000&&rebasedOne[34]==0&&rebasedOne[35]==0,"first client decode time is zero");
   Check(rebasedTwo[34]==3&&rebasedTwo[35]==232,"later decode time retains elapsed interval");
   foreach(var invalid in new byte[][] {new byte[8],new byte[]{0,0,0,9,109,100,97,116},new byte[]{0x7f,0xff,0xff,0xff,109,100,97,116},Join(init,Box("mdat",new byte[]{1}))}) {
    bool rejected=false;try {using(var source=new MemoryStream(invalid))Mp4Reader.Parse(source,delegate{});}catch(InvalidDataException){rejected=true;}Check(rejected,"invalid MP4 rejected");
   }
   Check(Quote("a b")=="\"a b\"","space quoting");Check(Quote("a\"b")=="\"a\\\"b\"","quote escaping");Check(Quote("a\\")=="\"a\\\\\"","trailing backslash");
   Check(!Options.TokenValid("wrong")&&Options.TokenValid(new string('a',48)),"token validation");
   string path="/"+new string('a',48)+"/stream.mp4";
   Check(HttpProtocol.RequestKind("GET "+path+" HTTP/1.1",path)==1,"GET route");
   Check(HttpProtocol.RequestKind("OPTIONS "+path+" HTTP/1.1",path)==2,"private-network preflight route");
   Check(HttpProtocol.RequestKind("OPTIONS /wrong HTTP/1.1",path)==0&&HttpProtocol.RequestKind("POST "+path+" HTTP/1.1",path)==0,"token route and method restriction");
   string preflight=HttpProtocol.Response("204 No Content",false);
   Check(preflight.Contains("Access-Control-Allow-Private-Network: true\r\n")&&preflight.Contains("Access-Control-Allow-Methods: GET, OPTIONS\r\n")&&preflight.Contains("Content-Length: 0\r\n"),"PNA preflight response");
   bool badArguments=false;try{Options.Parse(new string[]{"--resolution","4x4","x","y"});}catch{badArguments=true;}Check(badArguments,"invalid resolution");
   Console.WriteLine("PASS: fragmented MP4 framing, bounded invalid input rejection, argument quoting, token and option validation.");return 0;
  }
 }

 internal sealed class Options {
  public string Assets,Scene,Project,Engine,Token,Ffmpeg,Node,Relay;public int Width=1280,Height=720,Fps=15,Port;
  public static bool TokenValid(string value) {return value!=null&&Regex.IsMatch(value,"\\A[0-9a-fA-F]{48}\\z");}
  public static void SafePath(string path) {
   string cursor=Path.GetFullPath(path);
   while(!String.IsNullOrEmpty(cursor)) {
    if((File.Exists(cursor)||Directory.Exists(cursor))&&(File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Linked runtime and scene paths are not supported.");
    var parent=Directory.GetParent(cursor);cursor=parent==null?null:parent.FullName;
   }
  }
  public static Options Parse(string[] args) {
   var options=new Options();var positional=new List<string>();
   for(int i=0;i<args.Length;i++) {
    string arg=args[i];
    if(arg=="--stdin-json")continue;
    if(arg=="--resolution") {if(++i>=args.Length)throw new ArgumentException("Missing resolution.");var parts=args[i].Split('x');if(parts.Length!=2||!Int32.TryParse(parts[0],out options.Width)||!Int32.TryParse(parts[1],out options.Height))throw new ArgumentException("Invalid resolution.");}
    else if(arg=="--fps") {if(++i>=args.Length||!Int32.TryParse(args[i],out options.Fps))throw new ArgumentException("Invalid frame rate.");}
    else if(arg.StartsWith("--",StringComparison.Ordinal))throw new ArgumentException("Unknown bridge option.");
    else positional.Add(arg);
   }
   if(options.Width<320||options.Width>1920||options.Height<180||options.Height>1080||options.Width%2!=0||options.Height%2!=0||options.Fps<5||options.Fps>30)throw new ArgumentException("Use an even resolution up to 1920x1080 and 5-30 fps.");
   if(positional.Count!=2)throw new ArgumentException("Expected Wallpaper Engine assets and scene.pkg paths.");
   options.Assets=Path.GetFullPath(positional[0]).TrimEnd(Path.DirectorySeparatorChar);options.Scene=Path.GetFullPath(positional[1]);
   if(!File.Exists(options.Scene)||!String.Equals(Path.GetFileName(options.Scene),"scene.pkg",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("A local scene.pkg file is required.");
   string projectDirectory=Path.GetDirectoryName(options.Scene),workshopRoot=Path.GetDirectoryName(projectDirectory);
   if(!Regex.IsMatch(Path.GetFileName(projectDirectory),"\\A[0-9]{1,20}\\z")||!workshopRoot.EndsWith(Path.Combine("steamapps","workshop","content","431960"),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Scene must belong to a local Wallpaper Engine Workshop item.");
   string steamApps=Directory.GetParent(Directory.GetParent(Directory.GetParent(workshopRoot).FullName).FullName).FullName;
   string expectedAssets=Path.GetFullPath(Path.Combine(steamApps,"common","wallpaper_engine","assets"));
   if(!Directory.Exists(options.Assets)||!String.Equals(options.Assets,expectedAssets,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Assets must match the same Steam library as the scene.");
   options.Project=Path.Combine(projectDirectory,"project.json");
   if(!File.Exists(options.Project)||new FileInfo(options.Project).Length>2*1024*1024)throw new ArgumentException("Missing or oversized Wallpaper Engine project metadata.");
   var project=new JavaScriptSerializer {MaxJsonLength=2*1024*1024}.DeserializeObject(File.ReadAllText(options.Project,Encoding.UTF8)) as Dictionary<string,object>;object kind;
   if(project==null||!project.TryGetValue("type",out kind)||!String.Equals(kind as string,"scene",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("The selected project must be a Scene wallpaper.");
   string engineRoot=Path.GetDirectoryName(options.Assets);options.Engine=Path.Combine(engineRoot,"wallpaper64.exe");
   // Target the already running official engine, never a similarly named process elsewhere.
   bool engineRunning=false;
   foreach(string name in new string[]{"wallpaper64","wallpaper32"})foreach(var process in Process.GetProcessesByName(name))using(process) {
    try {string expected=Path.Combine(engineRoot,name+".exe");if(String.Equals(process.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase)){options.Engine=expected;engineRunning=true;break;}}catch{}
   }
   if(!engineRunning)throw new InvalidOperationException("Start the installed Wallpaper Engine before using Scene wallpapers.");
   options.Ffmpeg=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tools","ffmpeg.exe");
   if(!File.Exists(options.Ffmpeg)||!File.Exists(options.Engine))throw new InvalidOperationException("Required local capture tools are missing.");
   string stateRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexDreamSkin");
   string statePath=Path.Combine(stateRoot,"state.json");
   if(!File.Exists(statePath)||new FileInfo(statePath).Length>1024*1024)throw new InvalidOperationException("The managed skin session is unavailable.");
   SafePath(statePath);
   var state=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(statePath,Encoding.UTF8)) as Dictionary<string,object>;object nodeValue;
   options.Node=state!=null&&state.TryGetValue("nodePath",out nodeValue)?nodeValue as string:null;
   string nodeRoot=Path.Combine(stateRoot,"manager-runtime")+Path.DirectorySeparatorChar;
   if(String.IsNullOrWhiteSpace(options.Node)||!Path.GetFullPath(options.Node).StartsWith(nodeRoot,StringComparison.OrdinalIgnoreCase)||!String.Equals(Path.GetFileName(options.Node),"node.exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(options.Node))throw new InvalidOperationException("A trusted bundled skin runtime is required for the scene relay.");
   options.Relay=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"scene-relay.mjs");
   if(!File.Exists(options.Relay))throw new InvalidOperationException("The local scene relay is missing.");
   options.Token=Environment.GetEnvironmentVariable("WP_STREAM_TOKEN");if(!TokenValid(options.Token))throw new ArgumentException("A 48-character stream token is required.");
   string port=Environment.GetEnvironmentVariable("WP_STREAM_PORT");if(!String.IsNullOrEmpty(port)&&(!Int32.TryParse(port,out options.Port)||options.Port<0||options.Port>65535))throw new ArgumentException("Invalid loopback port.");
   SafePath(options.Assets);SafePath(options.Scene);SafePath(options.Project);SafePath(options.Engine);SafePath(options.Ffmpeg);SafePath(options.Node);SafePath(options.Relay);return options;
  }
 }

 internal static class Native {
  public delegate bool EnumCallback(IntPtr hwnd,IntPtr data);
  [DllImport("user32.dll")]private static extern bool EnumWindows(EnumCallback callback,IntPtr data);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowText(IntPtr hwnd,StringBuilder value,int length);
  [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
  [DllImport("user32.dll")]private static extern bool IsWindow(IntPtr hwnd);
  [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
  [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
  [DllImport("user32.dll")]private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
  public static IntPtr FindOwned(string title,string engine) {
   IntPtr found=IntPtr.Zero;
   EnumWindows(delegate(IntPtr hwnd,IntPtr data) {
    var buffer=new StringBuilder(512);GetWindowText(hwnd,buffer,buffer.Capacity);if(!String.Equals(buffer.ToString(),title,StringComparison.Ordinal))return true;
    uint pid;GetWindowThreadProcessId(hwnd,out pid);
    try{using(var process=Process.GetProcessById((int)pid)){if(!String.Equals(process.MainModule.FileName,engine,StringComparison.OrdinalIgnoreCase))return true;}}catch{return true;}
    found=hwnd;return false;
   },IntPtr.Zero);return found;
  }
  public static void PlaceOwned(IntPtr hwnd,int x,int y,int width,int height) {
   long style=GetWindowLongPtr(hwnd,-20).ToInt64();style=(style|0x80L)&~0x40000L;SetWindowLongPtr(hwnd,-20,new IntPtr(style));
   if(!SetWindowPos(hwnd,IntPtr.Zero,x,y,width,height,0x0010|0x0004|0x0020))throw new InvalidOperationException("Could not position the dedicated scene window.");
  }
 }

 internal static class Mp4Reader {
  private const int BoxLimit=8*1024*1024;
  private static bool Exact(Stream source,byte[] data,int offset,int count,bool allowEnd) {
   int read=0;while(read<count){int n=source.Read(data,offset+read,count-read);if(n==0){if(allowEnd&&read==0)return false;throw new InvalidDataException("Truncated MP4 box.");}read+=n;}return true;
  }
  public static void Parse(Stream source,Action<byte[],byte[]> publish) {
   byte[] initialization=null,pending=null;bool ftyp=false;using(var initial=new MemoryStream()) {
    while(true) {
     var header=new byte[8];if(!Exact(source,header,0,8,true)){if(pending!=null)throw new InvalidDataException("MP4 fragment ended before its media payload.");return;}
     ulong length=((ulong)header[0]<<24)|((ulong)header[1]<<16)|((ulong)header[2]<<8)|header[3];int headerLength=8;
     if(length==1){var extended=new byte[8];Exact(source,extended,0,8,false);length=0;foreach(byte value in extended)length=(length<<8)|value;var joined=new byte[16];Buffer.BlockCopy(header,0,joined,0,8);Buffer.BlockCopy(extended,0,joined,8,8);header=joined;headerLength=16;}
     if(length<(ulong)headerLength||length>BoxLimit)throw new InvalidDataException("MP4 box length is outside the bridge limit.");
     var box=new byte[(int)length];Buffer.BlockCopy(header,0,box,0,headerLength);Exact(source,box,headerLength,box.Length-headerLength,false);
     string type=Encoding.ASCII.GetString(header,4,4);
     if(initialization==null) {
      if(type!="ftyp"&&type!="moov"&&type!="free")throw new InvalidDataException("MP4 initialization boxes are missing.");
      if(initial.Length+box.Length>1024*1024)throw new InvalidDataException("MP4 initialization is too large.");initial.Write(box,0,box.Length);
      if(type=="ftyp")ftyp=true;
      if(type=="moov"){if(!ftyp)throw new InvalidDataException("MP4 file type is missing.");initialization=initial.ToArray();}
     } else if(type=="moof") {if(pending!=null)throw new InvalidDataException("MP4 fragment has no media payload.");pending=box;}
     else if(type=="mdat") {
      if(pending==null||pending.Length+box.Length>BoxLimit)throw new InvalidDataException("Invalid MP4 media fragment.");var fragment=new byte[pending.Length+box.Length];Buffer.BlockCopy(pending,0,fragment,0,pending.Length);Buffer.BlockCopy(box,0,fragment,pending.Length,box.Length);pending=null;publish(initialization,fragment);
     }
    }
   }
  }
 }

 internal sealed class StreamPeer : IDisposable {
  private readonly TcpClient client;private readonly BlockingCollection<byte[]> queue=new BlockingCollection<byte[]>(4);private int closed;
  private ulong? origin;
  public StreamPeer(TcpClient value){client=value;client.NoDelay=true;client.SendTimeout=2000;}
  public bool Offer(byte[] data){try{if(closed!=0)return false;if(data.Length>=8&&Encoding.ASCII.GetString(data,4,4)=="moof")data=FragmentClock.Rebase(data,ref origin);if(queue.TryAdd(data))return true;}catch{}Dispose();return false;}
  public void Pump() {try{using(var stream=client.GetStream()){stream.WriteTimeout=2000;foreach(var data in queue.GetConsumingEnumerable())stream.Write(data,0,data.Length);}}catch{}finally{Dispose();}}
  public void Dispose(){if(Interlocked.Exchange(ref closed,1)!=0)return;queue.CompleteAdding();try{client.Close();}catch{}}
  public bool Closed {get{return closed!=0;}}
 }

 internal static class HttpProtocol {
  public static int RequestKind(string firstLine,string expectedPath) {
   var parts=firstLine.Split(' ');
   if(parts.Length!=3||parts[1]!=expectedPath||(parts[2]!="HTTP/1.1"&&parts[2]!="HTTP/1.0"))return 0;
   return parts[0]=="GET"?1:parts[0]=="OPTIONS"?2:0;
  }
  public static string Response(string status,bool streaming) {
   return "HTTP/1.1 "+status+"\r\n"+(streaming?"Content-Type: video/mp4\r\n":"Content-Length: 0\r\n")+
    "Access-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: GET, OPTIONS\r\n"+
    "Access-Control-Allow-Headers: Content-Type, Cache-Control, Pragma, Range\r\n"+
    "Access-Control-Allow-Private-Network: true\r\nAccess-Control-Max-Age: 600\r\n"+
    "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
  }
 }

 internal static class FragmentClock {
  private static uint U32(byte[] data,int offset){return ((uint)data[offset]<<24)|((uint)data[offset+1]<<16)|((uint)data[offset+2]<<8)|data[offset+3];}
  private static ulong U64(byte[] data,int offset){return ((ulong)U32(data,offset)<<32)|U32(data,offset+4);}
  private static void Put32(byte[] data,int offset,uint value){for(int i=3;i>=0;i--){data[offset+i]=(byte)value;value>>=8;}}
  public static byte[] Rebase(byte[] fragment,ref ulong? origin) {
   var result=(byte[])fragment.Clone();bool found=false;
   Walk(result,0,result.Length,ref origin,ref found);
   if(!found)throw new InvalidDataException("Fragment is missing its decode timestamp.");return result;
  }
  private static void Walk(byte[] data,int start,int end,ref ulong? origin,ref bool found) {
   int offset=start;
   while(offset<end) {
    if(end-offset<8)throw new InvalidDataException("Truncated fragment box.");
    ulong length=U32(data,offset);int header=8;if(length==1){if(end-offset<16)throw new InvalidDataException("Truncated extended fragment box.");length=U64(data,offset+8);header=16;}
    if(length<(ulong)header||length>(ulong)(end-offset))throw new InvalidDataException("Invalid fragment box extent.");int next=offset+(int)length;
    string type=Encoding.ASCII.GetString(data,offset+4,4);
    if(type=="moof"||type=="traf")Walk(data,offset+header,next,ref origin,ref found);
    else if(type=="tfdt") {
     if(next-offset<header+8)throw new InvalidDataException("Truncated fragment timestamp.");int field=offset+header+4;byte version=data[offset+header];ulong timestamp;
     if(version==1){if(next-field<8)throw new InvalidDataException("Truncated 64-bit fragment timestamp.");timestamp=U64(data,field);}
     else if(version==0)timestamp=U32(data,field);else throw new InvalidDataException("Unsupported fragment timestamp version.");
     if(!origin.HasValue)origin=timestamp;if(timestamp<origin.Value)throw new InvalidDataException("Fragment clock moved backwards.");timestamp-=origin.Value;
     if(version==1){Put32(data,field,(uint)(timestamp>>32));Put32(data,field+4,(uint)timestamp);}else {if(timestamp>UInt32.MaxValue)throw new InvalidDataException("Fragment timestamp overflow.");Put32(data,field,(uint)timestamp);}
     found=true;
    }
    offset=next;
   }
  }
 }

 internal static class ScenePosterCache {
  internal static double Quality(string path,int width,int height) {
   Validate(path,width,height);
   using(var image=new System.Drawing.Bitmap(path)){
    const int w=96,h=54;double[] luma=new double[w*h];int[] histogram=new int[256];double mean=0,sharp=0;int edges=0;
    for(int y=0;y<h;y++)for(int x=0;x<w;x++){
     var color=image.GetPixel((int)((x+.5)*image.Width/w),(int)((y+.5)*image.Height/h));double value=(.2126*color.R+.7152*color.G+.0722*color.B)/255;
     luma[y*w+x]=value;mean+=value;histogram[(int)(value*255)]++;
    }
    mean/=w*h;
    for(int y=1;y<h-1;y++)for(int x=1;x<w-1;x++){
     int i=y*w+x;double gx=luma[i+1]-luma[i-1],gy=luma[i+w]-luma[i-w],edge=Math.Sqrt(gx*gx+gy*gy);sharp+=Math.Min(.5,edge);edges++;
    }
    int low=0,high=255,sum=0;for(int i=0;i<256;i++){sum+=histogram[i];if(sum>=w*h*.05){low=i;break;}}
    sum=0;for(int i=0;i<256;i++){sum+=histogram[i];if(sum>=w*h*.95){high=i;break;}}
    return (high-low)/255.0*2+sharp/edges*3-Math.Abs(mean-.45)*.8;
   }
  }
  internal static string SelectBest(string[] paths,int width,int height,out double score) {
   string best=null;score=Double.NegativeInfinity;
   foreach(string path in paths)try{double candidate=Quality(path,width,height);if(candidate>score){score=candidate;best=path;}}catch(IOException){}catch(ArgumentException){}
   if(best==null)throw new IOException("No complete nonblack scene frame was available for selection.");return best;
  }
  internal static void Validate(string path,int expectedWidth,int expectedHeight) {
   Options.SafePath(path);
   var file=new FileInfo(path);
   if(!file.Exists||file.Length<128||file.Length>10*1024*1024)throw new IOException("Poster capture did not produce a bounded image.");
   using(var image=new System.Drawing.Bitmap(path)) {
    if(image.Width!=expectedWidth||image.Height!=expectedHeight||image.Width<320||image.Height<180||image.Width>1920||image.Height>1080)throw new IOException("Poster dimensions do not match the dedicated scene stream.");
    int lit=0;for(int y=0;y<18;y++)for(int x=0;x<32;x++){var pixel=image.GetPixel((int)((x+.5)*image.Width/32),(int)((y+.5)*image.Height/18));if(Math.Max(pixel.R,Math.Max(pixel.G,pixel.B))>24)lit++;}
    if(lit<6)throw new IOException("Scene is still showing a black loading frame.");
   }
  }
  private static void Replace(string temporary,string destination) {
   Options.SafePath(destination);
   if(File.Exists(destination))File.Replace(temporary,destination,null);else File.Move(temporary,destination);
  }
  internal static void Publish(string temporary,string destination,string id,int width,int height,int sampleCount=1,double frameQualityScore=0) {
   if(!Regex.IsMatch(id??"","\\A[0-9]{1,20}\\z")||!String.Equals(Path.GetFileName(destination),id+".jpg",StringComparison.Ordinal))throw new InvalidDataException("Invalid scene poster identity.");
   Validate(temporary,width,height);
   string hash;using(var sha=SHA256.Create())using(var input=File.OpenRead(temporary))hash=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();
   string metadata=Path.ChangeExtension(destination,".json"),pending=metadata+"."+Process.GetCurrentProcess().Id+"."+Guid.NewGuid().ToString("N")+".tmp";
   Options.SafePath(metadata);Options.SafePath(pending);
   try {
    var record=new {schemaVersion=1,source="rendered-scene",workshopId=id,width=width,height=height,capturedAt=DateTimeOffset.UtcNow.ToString("o"),imageSha256=hash,sampleCount=sampleCount,frameQualityScore=frameQualityScore};
    File.WriteAllText(pending,new JavaScriptSerializer().Serialize(record),new UTF8Encoding(false));
    // An old marker can never authenticate a newly replaced thumbnail: the digest
    // must match. Readers retry during the tiny interval between these replaces.
    Replace(temporary,destination);Replace(pending,metadata);
   }finally{if(File.Exists(pending))try{File.Delete(pending);}catch{}}
  }
 }

 internal sealed class Bridge : IDisposable {
  private readonly Options options;private readonly ManualResetEventSlim stop=new ManualResetEventSlim(),ready=new ManualResetEventSlim();private readonly object gate=new object();
  private readonly List<StreamPeer> peers=new List<StreamPeer>();private readonly Queue<string> errors=new Queue<string>();
  private readonly object snapshotGate=new object();
  private TcpListener listener;private Process encoder,relay,snapshotProcess;private Task posterWorker;private byte[] initialization,latest;private string windowName;private int disposed,connectionCount;private bool openedWindow;private volatile bool failed;
  public Bridge(Options value){options=value;}
  private void Error(string message){Console.Error.WriteLine("scene-bridge: "+message);}
  private void PosterEvent(string message) {
   try {
    string id=Path.GetFileName(Path.GetDirectoryName(options.Scene));
    if(!Regex.IsMatch(id??"","\\A[0-9]{1,20}\\z"))return;
    string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"poster-cache"),path=Path.Combine(directory,"poster-diagnostics.log");
    Options.SafePath(directory);Options.SafePath(path);Directory.CreateDirectory(directory);
    message=Regex.Replace(message??"","/[a-fA-F0-9]{48}/","/<token>/").Replace('\r',' ').Replace('\n',' ');
    if(message.Length>768)message=message.Substring(0,768);
    string line=DateTimeOffset.UtcNow.ToString("o")+" pid="+Process.GetCurrentProcess().Id+" workshopId="+id+" "+message+Environment.NewLine;
    if(File.Exists(path)&&new FileInfo(path).Length>65536)File.WriteAllText(path,line,new UTF8Encoding(false));else File.AppendAllText(path,line,new UTF8Encoding(false));
   }catch{}
  }
  private void EngineCommand(params string[] arguments) {
   using(var command=Process.Start(new ProcessStartInfo(options.Engine,Entry.Arguments(arguments)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(options.Engine)})) {
    if(!command.WaitForExit(5000))throw new InvalidOperationException("Wallpaper Engine control command timed out.");
   }
  }
  public int Run() {
   Task.Run((Action)ReadCommands);windowName="CodexSceneBridge-"+Process.GetCurrentProcess().Id+"-"+Guid.NewGuid().ToString("N");
   var bounds=SystemInformation.VirtualScreen;int x=bounds.Right+96,y=bounds.Top+96;
   EngineCommand("-control","openWallpaper","-file",options.Project,"-playInWindow",windowName,"-width",options.Width.ToString(),"-height",options.Height.ToString(),"-x",x.ToString(),"-y",y.ToString(),"-activate","0","-borderless","1");openedWindow=true;
   IntPtr hwnd=IntPtr.Zero;var wait=Stopwatch.StartNew();
   while(!stop.IsSet&&wait.Elapsed.TotalSeconds<12){hwnd=Native.FindOwned(windowName,options.Engine);if(hwnd!=IntPtr.Zero)break;Thread.Sleep(100);}
   if(stop.IsSet)return 0;if(hwnd==IntPtr.Zero)throw new InvalidOperationException("Wallpaper Engine did not create a verified dedicated scene window.");Native.PlaceOwned(hwnd,x,y,options.Width,options.Height);
   listener=new TcpListener(IPAddress.Loopback,options.Port);listener.Start(8);Task.Run((Action)AcceptClients);
   StartEncoder(hwnd);wait.Restart();while(!ready.IsSet&&!stop.IsSet&&wait.Elapsed.TotalSeconds<15)Thread.Sleep(50);
   if(!ready.IsSet){failed=true;throw new InvalidOperationException("The scene capture did not produce valid fragmented video. "+LastError());}
   int port=((IPEndPoint)listener.LocalEndpoint).Port;
   string streamUrl="http://127.0.0.1:"+port+"/"+options.Token+"/stream.mp4";
   StartRelay(streamUrl);
   Console.WriteLine("scene-viewer: stream ready "+streamUrl);Console.Out.Flush();
   PosterEvent("worker-scheduled");
   posterWorker=Task.Run(delegate {PreparePosters(streamUrl);});
   stop.Wait();return failed?1:0;
  }
  private void PreparePosters(string streamUrl) {
   // Poster work must never delay relay readiness or the application's launch.
   // Scene assets can take several seconds to appear after the first black frame.
   var watch=Stopwatch.StartNew();
   int workers,completion;ThreadPool.GetAvailableThreads(out workers,out completion);PosterEvent("worker-started; available-workers="+workers);
   foreach(int at in new int[]{1,4,12,30}) {
    int delay=(int)Math.Max(0,at*1000-watch.ElapsedMilliseconds);
    if(stop.Wait(delay))return;
    PosterEvent("attempt-start; schedule-second="+at+"; elapsed-ms="+watch.ElapsedMilliseconds);
    if(PreparePoster(streamUrl))return;
   }
   if(!stop.IsSet){PosterEvent("attempts-exhausted");Error("No rendered scene poster was captured after four attempts; the existing image was preserved.");}
  }
  private void CancelOwnedSnapshot() {
   lock(snapshotGate) {
    if(snapshotProcess!=null)try{if(!snapshotProcess.HasExited)snapshotProcess.Kill();}catch{}
   }
  }
  private bool PreparePoster(string streamUrl) {
   string temporary=null;var samples=new List<string>();
   try {
    string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"poster-cache");Options.SafePath(directory);Directory.CreateDirectory(directory);
    string id=Path.GetFileName(Path.GetDirectoryName(options.Scene)),destination=Path.Combine(directory,id+".jpg");
    string prefix=Path.Combine(directory,id+"."+Process.GetCurrentProcess().Id+"."+Guid.NewGuid().ToString("N"));
    temporary=prefix+".%02d.tmp.jpg";
    for(int i=0;i<3;i++)samples.Add(prefix+"."+i.ToString("00")+".tmp.jpg");
    var arguments=new string[]{"-nostdin","-hide_banner","-loglevel","error","-rw_timeout","3000000","-i",streamUrl,"-ss","1","-vf","fps=1","-frames:v","3","-start_number","0","-pix_fmt","yuvj420p","-q:v","3","-y",temporary};
    Process snapshot;
    lock(snapshotGate) {
     if(stop.IsSet)return false;
     // A background decoder must never inherit the JSON control pipe. FFmpeg's
     // console-input reader otherwise holds that pipe and stalls process exit
     // even after it has already written the requested JPEG.
     snapshot=Process.Start(new ProcessStartInfo(options.Ffmpeg,Entry.Arguments(arguments)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardError=true});
     snapshot.StandardInput.Close();
     snapshotProcess=snapshot;
    }
    try {
     var errorTask=Task.Run(delegate {string last="",line;while((line=ReadBoundedLine(snapshot.StandardError,512,false))!=null)last=line;return last;});
     var watch=Stopwatch.StartNew();
     while(!snapshot.WaitForExit(100)) {
      if(stop.IsSet||watch.ElapsedMilliseconds>=8000) {try{snapshot.Kill();}catch{};snapshot.WaitForExit(1000);if(stop.IsSet)return false;throw new IOException("Poster capture exceeded eight seconds.");}
     }
     if(stop.IsSet)return false;
     if(snapshot.ExitCode!=0)throw new IOException("Poster decoder failed"+(errorTask.Wait(500)&&!String.IsNullOrEmpty(errorTask.Result)?": "+errorTask.Result:"."));
    }finally {
     lock(snapshotGate){if(ReferenceEquals(snapshotProcess,snapshot))snapshotProcess=null;}
     snapshot.Dispose();
    }
    if(stop.IsSet)return false;
    double quality;string selected=ScenePosterCache.SelectBest(samples.ToArray(),options.Width,options.Height,out quality);
    ScenePosterCache.Publish(selected,destination,id,options.Width,options.Height,samples.Count,quality);temporary=null;
    PosterEvent("poster-published; width="+options.Width+"; height="+options.Height+"; ranked-frames="+samples.Count+"; quality="+quality.ToString("F3",CultureInfo.InvariantCulture));
    Error("Rendered scene poster cached at "+options.Width+"x"+options.Height+".");return true;
   }catch(Exception error){if(!stop.IsSet){PosterEvent("attempt-failed; "+error.GetType().Name+": "+error.Message);Error("Scene poster attempt skipped: "+error.Message);}return false;}
   finally{foreach(string file in samples)try{if(File.Exists(file))File.Delete(file);}catch{}}
  }
  private void StartRelay(string streamUrl) {
   relay=new Process {StartInfo=new ProcessStartInfo(options.Node,Entry.Arguments(new string[]{options.Relay,"--url",streamUrl})) {
    UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true
   }};
   if(!relay.Start())throw new InvalidOperationException("Could not start the local skin stream relay.");
   Task.Run(delegate {try{string line;while((line=ReadBoundedLine(relay.StandardError,1500,false))!=null)Error(line);}catch{}});
   var response=Task.Run(delegate {return ReadBoundedLine(relay.StandardOutput,1024,true);});
   if(!response.Wait(10000)||response.Result!="scene-relay: ready")throw new InvalidOperationException("The scene relay could not attach to the managed Codex session.");
   Task.Run(delegate {try{while(ReadBoundedLine(relay.StandardOutput,1024,false)!=null){}relay.WaitForExit();if(!stop.IsSet){failed=true;Error("The managed skin relay ended.");stop.Set();}}catch{if(!stop.IsSet){failed=true;stop.Set();}}});
  }
  private void StartEncoder(IntPtr hwnd) {
   string filter="gfxcapture=hwnd="+hwnd.ToInt64().ToString(CultureInfo.InvariantCulture)+":max_framerate="+options.Fps+":capture_cursor=0:display_border=0";
   var arguments=new string[]{"-hide_banner","-loglevel","warning","-f","lavfi","-i",filter,"-vf","hwdownload,format=bgra,scale="+options.Width+":"+options.Height+":flags=fast_bilinear,setsar=1,format=yuv420p","-an","-c:v","libx264","-preset","ultrafast","-tune","zerolatency","-profile:v","baseline","-level:v","3.1","-crf","23","-maxrate","4000k","-bufsize","4000k","-g",options.Fps.ToString(),"-keyint_min",options.Fps.ToString(),"-bf","0","-sc_threshold","0","-movflags","frag_keyframe+empty_moov+default_base_moof","-f","mp4","pipe:1"};
   encoder=new Process {StartInfo=new ProcessStartInfo(options.Ffmpeg,Entry.Arguments(arguments)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true}};
   if(!encoder.Start())throw new InvalidOperationException("Could not start the local capture encoder.");
   Task.Run(delegate {try{string line;while((line=ReadBoundedLine(encoder.StandardError,1024,false))!=null){lock(errors){errors.Enqueue(line);while(errors.Count>12)errors.Dequeue();}}}catch{}});
   Task.Run(delegate {try{Mp4Reader.Parse(encoder.StandardOutput.BaseStream,Publish);if(!stop.IsSet){failed=true;Error("Capture encoder ended. "+LastError());stop.Set();}}catch(Exception error){if(!stop.IsSet){failed=true;Error(error.Message);stop.Set();}}});
  }
  private string LastError(){lock(errors){return String.Join(" | ",errors.ToArray());}}
  private void Publish(byte[] header,byte[] fragment) {
   lock(gate){initialization=header;latest=fragment;for(int i=peers.Count-1;i>=0;i--)if(!peers[i].Offer(fragment)){peers[i].Dispose();peers.RemoveAt(i);}}
   ready.Set();
  }
  private static string ReadBoundedLine(TextReader reader,int limit,bool rejectLong) {
   var text=new StringBuilder();bool any=false,tooLong=false;while(true){int ch=reader.Read();if(ch<0)return any?text.ToString():null;any=true;if(ch=='\n')break;if(ch=='\r')continue;if(text.Length<limit)text.Append((char)ch);else tooLong=true;}if(tooLong&&rejectLong)throw new InvalidDataException("Command exceeds the bridge input limit.");return text.ToString();
  }
  private void ReadCommands() {
   try{string line;while((line=ReadBoundedLine(Console.In,4096,true))!=null){var data=new JavaScriptSerializer {MaxJsonLength=4096}.DeserializeObject(line) as Dictionary<string,object>;object command;if(data!=null&&data.TryGetValue("command",out command)&&String.Equals(command as string,"shutdown",StringComparison.Ordinal)){stop.Set();return;}}}catch(Exception error){Error(error.Message);}finally{stop.Set();}
  }
  private void AcceptClients() {
   while(!stop.IsSet){try{var client=listener.AcceptTcpClient();if(Interlocked.Increment(ref connectionCount)>8){Interlocked.Decrement(ref connectionCount);client.Close();continue;}Task.Run(delegate{try{HandleClient(client);}finally{Interlocked.Decrement(ref connectionCount);}});}catch{if(!stop.IsSet){failed=true;stop.Set();}return;}}
  }
  private void HandleClient(TcpClient client) {
   bool handedOff=false;
   try {
    client.ReceiveTimeout=2000;client.SendTimeout=2000;var stream=client.GetStream();stream.ReadTimeout=2000;stream.WriteTimeout=2000;
    var header=new List<byte>();int state=0;
    while(header.Count<8192){int value=stream.ReadByte();if(value<0)throw new IOException("Closed request.");header.Add((byte)value);state=(state==0&&value==13)?1:(state==1&&value==10)?2:(state==2&&value==13)?3:(state==3&&value==10)?4:0;if(state==4)break;}
    if(state!=4)throw new InvalidDataException("Oversized request headers.");
    string firstLine=Encoding.ASCII.GetString(header.ToArray()).Split(new string[]{"\r\n"},StringSplitOptions.None)[0];
    string expected="/"+options.Token+"/stream.mp4";int kind=HttpProtocol.RequestKind(firstLine,expected);
    if(kind==0){Error("HTTP REJECT 404");WriteStatus(stream,"404 Not Found");return;}
    var endpoint=client.Client.RemoteEndPoint as IPEndPoint;if(endpoint==null||!IPAddress.IsLoopback(endpoint.Address)){WriteStatus(stream,"403 Forbidden");return;}
    if(kind==2){Error("HTTP OPTIONS 204");WriteStatus(stream,"204 No Content");return;}
    StreamPeer peer;
    lock(gate) {
     peers.RemoveAll(delegate(StreamPeer item){return item.Closed;});
     if(peers.Count>=4||initialization==null||latest==null){WriteStatus(stream,"503 Service Unavailable");return;}
     byte[] response=Encoding.ASCII.GetBytes(HttpProtocol.Response("200 OK",true));stream.Write(response,0,response.Length);Error("HTTP GET 200");
     peer=new StreamPeer(client);peer.Offer(initialization);peer.Offer(latest);peers.Add(peer);handedOff=true;
    }
    peer.Pump();
   } catch{}finally{if(!handedOff)client.Close();}
  }
  private static void WriteStatus(Stream stream,string status){byte[] bytes=Encoding.ASCII.GetBytes(HttpProtocol.Response(status,false));stream.Write(bytes,0,bytes.Length);}
  public void Dispose() {
   if(Interlocked.Exchange(ref disposed,1)!=0)return;stop.Set();try{if(listener!=null)listener.Stop();}catch{}
   CancelOwnedSnapshot();if(posterWorker!=null)try{posterWorker.Wait(1500);}catch{}
   lock(gate){foreach(var peer in peers)peer.Dispose();peers.Clear();}
   if(relay!=null){try{if(!relay.HasExited){relay.StandardInput.WriteLine("{\"command\":\"shutdown\"}");relay.StandardInput.Flush();if(!relay.WaitForExit(2500))relay.Kill();}}catch{try{if(!relay.HasExited)relay.Kill();}catch{}}finally{relay.Dispose();}}
   if(encoder!=null){try{if(!encoder.HasExited){encoder.StandardInput.WriteLine("q");encoder.StandardInput.Flush();if(!encoder.WaitForExit(2000))encoder.Kill();}}catch{try{if(!encoder.HasExited)encoder.Kill();}catch{}}finally{encoder.Dispose();}}
   if(openedWindow&&windowName!=null){try{EngineCommand("-control","closeWallpaper","-location",windowName);}catch(Exception error){Error("Could not close the dedicated scene window: "+error.Message);}}
  }
 }
}
