using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DragonCodexBoot {
 public sealed class AestheticProfile {
  public int Version {get;set;}
  public string Fingerprint {get;set;}
  public int Width {get;set;}
  public int Height {get;set;}
  public double Aspect {get;set;}
  public string Risk {get;set;}
  public string Luma {get;set;}
  public double MeanLuma {get;set;}
  public double EdgeDensity {get;set;}
  public double TonalRange {get;set;}
  public double FocusX {get;set;}
  public double FocusY {get;set;}
  public double FocusWidth {get;set;}
  public double FocusHeight {get;set;}
  public double Hue {get;set;}
  public string Effect {get;set;}
  public int ContourThreshold {get;set;}
  public int ContourBudget {get;set;}
  public bool CacheHit {get;set;}
  public double AnalysisMilliseconds {get;set;}
 }
 public sealed class AestheticPlacement {
  public double X,Y,Width,Height,Score,Shade;
  public bool DarkText;
 }
 public sealed class AestheticLayout {
  public AestheticPlacement[] Captions;
  public AestheticPlacement Progress;
  public Color[] Accents;
  public Color Background;
 }
 // Bounded local image analysis. "Focus" is visual saliency, not semantic face recognition.
 public static class AestheticAnalyzer {
  const int Version=2;
  static readonly Dictionary<string,AestheticProfile> memory=new Dictionary<string,AestheticProfile>();
  static readonly object sync=new object();
  static double Clamp(double value){return Math.Max(0,Math.Min(1,value));}
  static byte[] Pixels(BitmapSource source,out int w,out int h){
   double scale=Math.Min(160.0/source.PixelWidth,120.0/source.PixelHeight);
   var scaled=new TransformedBitmap(source,new ScaleTransform(scale,scale));
   var bitmap=new FormatConvertedBitmap(scaled,PixelFormats.Bgra32,null,0);
   w=bitmap.PixelWidth;h=bitmap.PixelHeight;
   byte[] pixels=new byte[w*h*4];bitmap.CopyPixels(pixels,w*4,0);return pixels;
  }
  static double Hue(byte r,byte g,byte b){
   double max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),d=max-min;if(d<1)return 210;
   double hue=max==r?((g-b)/d)%6:max==g?(b-r)/d+2:(r-g)/d+4;return (hue*60+360)%360;
  }
  static Color Hsv(double hue,double saturation,double value){
   double c=value*saturation,x=c*(1-Math.Abs((hue/60)%2-1)),m=value-c;double r=0,g=0,b=0;
   if(hue<60){r=c;g=x;}else if(hue<120){r=x;g=c;}else if(hue<180){g=c;b=x;}else if(hue<240){g=x;b=c;}else if(hue<300){r=x;b=c;}else{r=c;b=x;}
   return Color.FromRgb((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));
  }
  static string Fingerprint(byte[] pixels,int w,int h){
   using(var hash=SHA256.Create()){
    byte[] prefix=Encoding.ASCII.GetBytes(Version+"|"+w+"|"+h+"|");byte[] all=new byte[prefix.Length+pixels.Length];Buffer.BlockCopy(prefix,0,all,0,prefix.Length);Buffer.BlockCopy(pixels,0,all,prefix.Length,pixels.Length);
    return BitConverter.ToString(hash.ComputeHash(all)).Replace("-","").ToLowerInvariant();
   }
  }
  static bool Valid(AestheticProfile p,string key){
   try{return p!=null&&p.Version==Version&&p.Fingerprint==key&&p.Width>1&&p.Width<=160&&p.Height>1&&p.Height<=120&&p.Aspect>.01&&p.Aspect<100&&
    !Double.IsNaN(p.Hue)&&p.Hue>=0&&p.Hue<360&&(p.Effect=="contour"||p.Effect=="soft-fade")&&
    Convert.FromBase64String(p.Risk).Length==p.Width*p.Height&&Convert.FromBase64String(p.Luma).Length==p.Width*p.Height&&
    p.ContourBudget>=800&&p.ContourBudget<=4200&&p.ContourThreshold>=60&&p.ContourThreshold<=190&&
    p.FocusX>=0&&p.FocusY>=0&&p.FocusWidth>0&&p.FocusHeight>0&&p.FocusX+p.FocusWidth<=1.001&&p.FocusY+p.FocusHeight<=1.001;}catch{return false;}
  }
  static void Safe(string path){
   string cursor=Path.GetFullPath(path);while(!String.IsNullOrEmpty(cursor)){
    if((File.Exists(cursor)||Directory.Exists(cursor))&&(File.GetAttributes(cursor)&FileAttributes.ReparsePoint)!=0)throw new IOException("Redirected aesthetic cache.");
    var parent=Directory.GetParent(cursor);cursor=parent==null?null:parent.FullName;
   }
  }
  public static AestheticProfile Analyze(BitmapSource source,string cacheRoot){
   var clock=Stopwatch.StartNew();int w,h;byte[] pixels=Pixels(source,out w,out h);string key=Fingerprint(pixels,w,h);
   lock(sync){AestheticProfile known;if(memory.TryGetValue(key,out known)){known.CacheHit=true;return known;}}
   string path=null;
   if(!String.IsNullOrEmpty(cacheRoot))try{
    Safe(cacheRoot);path=Path.Combine(cacheRoot,key+".json");Safe(path);
    if(File.Exists(path)&&new FileInfo(path).Length<=131072){
     var cached=new JavaScriptSerializer().Deserialize<AestheticProfile>(File.ReadAllText(path));
     if(Valid(cached,key)){cached.CacheHit=true;cached.AnalysisMilliseconds=clock.Elapsed.TotalMilliseconds;Remember(key,cached);return cached;}
    }
   }catch{}
   var profile=Compute(pixels,w,h,source.PixelWidth/(double)source.PixelHeight);profile.Fingerprint=key;profile.AnalysisMilliseconds=clock.Elapsed.TotalMilliseconds;
   if(path!=null)try{
    Directory.CreateDirectory(cacheRoot);
    // Cache only this component's generated records; no deletion of user data.
    if(Directory.GetFiles(cacheRoot,"*.json").Length<64||File.Exists(path)){
     string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
     try{File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(profile),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
   }catch{}
   Remember(key,profile);return profile;
  }
  static void Remember(string key,AestheticProfile profile){lock(sync){if(memory.Count>=32)memory.Clear();memory[key]=profile;}}
  static AestheticProfile Compute(byte[] pixels,int w,int h,double aspect){
   int n=w*h;double[] lum=new double[n],edge=new double[n],risk=new double[n];double mean=0,r=0,g=0,b=0;double[] hues=new double[24];int[] histogram=new int[256];
   for(int i=0;i<n;i++){
    byte blue=pixels[i*4],green=pixels[i*4+1],red=pixels[i*4+2];double y=(.2126*red+.7152*green+.0722*blue)/255;
    lum[i]=y;mean+=y;histogram[(int)(y*255)]++;r+=red;g+=green;b+=blue;
    double max=Math.Max(red,Math.Max(green,blue)),min=Math.Min(red,Math.Min(green,blue));double sat=max<1?0:(max-min)/max;
    if(sat>.18&&max>35)hues[Math.Min(23,(int)(Hue(red,green,blue)/15))]+=sat*(.3+y);
   }
   mean/=n;r/=n;g/=n;b/=n;int strong=0;double total=0;
   for(int y=1;y<h-1;y++)for(int x=1;x<w-1;x++){
    int i=y*w+x;double gx=lum[i+1]-lum[i-1],gy=lum[i+w]-lum[i-w];double gradient=Math.Sqrt(gx*gx+gy*gy);edge[i]=gradient;
    if(gradient>.16)strong++;
    double color=Math.Sqrt(Math.Pow((pixels[i*4+2]-r)/255,2)+Math.Pow((pixels[i*4+1]-g)/255,2)+Math.Pow((pixels[i*4]-b)/255,2))/1.73;
    risk[i]=Clamp(gradient*1.65+Math.Abs(lum[i]-mean)*.46+color*.5);total+=risk[i];
   }
   // Smooth the saliency map so text does not fit into tiny gaps between facial features.
   byte[] map=new byte[n],brightness=new byte[n];
   for(int y=0;y<h;y++)for(int x=0;x<w;x++){
    double sum=0;int count=0;for(int yy=Math.Max(0,y-2);yy<=Math.Min(h-1,y+2);yy++)for(int xx=Math.Max(0,x-2);xx<=Math.Min(w-1,x+2);xx++){sum+=risk[yy*w+xx];count++;}
    map[y*w+x]=(byte)Math.Round(Clamp(sum/count)*255);brightness[y*w+x]=(byte)Math.Round(lum[y*w+x]*255);
   }
   int low=Percentile(histogram,n,.05),high=Percentile(histogram,n,.95),winner=0;
   for(int i=1;i<hues.Length;i++)if(hues[i]>hues[winner])winner=i;
   double dominant=hues[winner]>.01?winner*15+7.5:210;
   double density=strong/(double)n;
   var p=new AestheticProfile{Version=Version,Width=w,Height=h,Aspect=aspect,Risk=Convert.ToBase64String(map),Luma=Convert.ToBase64String(brightness),MeanLuma=mean,EdgeDensity=density,TonalRange=(high-low)/255.0,Hue=dominant,
    Effect=density>.29||(high-low)<20||mean<.035?"soft-fade":"contour",ContourThreshold=(int)Math.Round(72+Math.Min(1,density/.30)*92),ContourBudget=(int)Math.Round(4200-2800*Math.Min(1,density/.30))};
   // Localized high-information area. No claim that this is a detected face.
   double best=-1;int bw=Math.Max(5,(int)(w*.42)),bh=Math.Max(5,(int)(h*.52));
   for(int y=0;y<=h-bh;y+=Math.Max(1,h/16))for(int x=0;x<=w-bw;x+=Math.Max(1,w/20)){
    double sum=0;for(int yy=y;yy<y+bh;yy+=2)for(int xx=x;xx<x+bw;xx+=2)sum+=map[yy*w+xx];
    double cx=(x+bw*.5)/w,cy=(y+bh*.5)/h;
    sum*=.25+1.5*Math.Exp(-Math.Pow(cx-.5,2)/.11-Math.Pow(cy-.43,2)/.16);
    if(sum>best){best=sum;p.FocusX=x/(double)w;p.FocusY=y/(double)h;p.FocusWidth=bw/(double)w;p.FocusHeight=bh/(double)h;}
   }
   p.FocusX=Math.Max(0,p.FocusX-.06);p.FocusY=Math.Max(0,p.FocusY-.045);
   p.FocusWidth=Math.Min(1-p.FocusX,p.FocusWidth+.12);p.FocusHeight=Math.Min(1-p.FocusY,p.FocusHeight+.09);
   return p;
  }
  static int Percentile(int[] histogram,int n,double fraction){int sum=0;for(int i=0;i<256;i++){sum+=histogram[i];if(sum>=n*fraction)return i;}return 255;}
  static AestheticPlacement Evaluate(AestheticProfile p,byte[] risk,byte[] luma,Rect slot,Rect image){
   double sum=0,mean=0,mean2=0;int count=0;int[] histogram=new int[256];
   for(int y=0;y<12;y++)for(int x=0;x<20;x++){
    double vx=slot.X+(x+.5)*slot.Width/20,vy=slot.Y+(y+.5)*slot.Height/12;double value=0,light=.02;
    if(image.Contains(new Point(vx,vy))){int sx=Math.Max(0,Math.Min(p.Width-1,(int)((vx-image.X)/image.Width*p.Width))),sy=Math.Max(0,Math.Min(p.Height-1,(int)((vy-image.Y)/image.Height*p.Height)));int i=sy*p.Width+sx;value=risk[i]/255.0;light=luma[i]/255.0;}
    sum+=value;mean+=light;mean2+=light*light;histogram[(int)(light*255)]++;count++;
   }
   var focus=new Rect(image.X+p.FocusX*image.Width,image.Y+p.FocusY*image.Height,p.FocusWidth*image.Width,p.FocusHeight*image.Height);
   Rect overlap=Rect.Intersect(slot,focus);double cover=overlap.IsEmpty?0:overlap.Width*overlap.Height/(slot.Width*slot.Height);
   // A conservative central exclusion helps avoid eyes/heads when the saliency
   // estimate is uncertain; this is a composition prior, not a semantic detector.
   Rect center=Rect.Intersect(slot,new Rect(image.X+image.Width*.27,image.Y+image.Height*.19,image.Width*.46,image.Height*.41));
   double centralCover=center.IsEmpty?0:center.Width*center.Height/(slot.Width*slot.Height);
   double brightness95=Percentile(histogram,count,.95)/255.0,variance=Math.Max(0,mean2/count-Math.Pow(mean/count,2));
   return new AestheticPlacement{X=slot.X,Y=slot.Y,Width=slot.Width,Height=slot.Height,Score=sum/count*3+cover*1.2+centralCover*.95+Math.Sqrt(variance)*.65,
    DarkText=mean/count>.72&&variance<.025,
    Shade=Math.Min(.92,Math.Max(.48,1-.14/Math.Max(.15,brightness95)))};
  }
  public static AestheticLayout Plan(AestheticProfile p,double viewportAspect){
   byte[] risk=Convert.FromBase64String(p.Risk),luma=Convert.FromBase64String(p.Luma);
   double iw=1,ih=viewportAspect/p.Aspect;if(ih>1){iw=1/ih;ih=1;}var image=new Rect((1-iw)/2,(1-ih)/2,iw,ih);
   var candidates=new List<AestheticPlacement>();
   foreach(double y in new double[]{.21,.40,.63})foreach(double x in new double[]{.07,.55})candidates.Add(Evaluate(p,risk,luma,new Rect(x,y,.38,.20),image));
   var chosen=new AestheticPlacement[4];
   for(int phase=0;phase<4;phase++){
    double best=Double.MaxValue;AestheticPlacement pick=null;
    foreach(var slot in candidates){
     double score=slot.Score;
     for(int previous=0;previous<phase;previous++){
      double distance=Math.Sqrt(Math.Pow(slot.X-chosen[previous].X,2)+Math.Pow(slot.Y-chosen[previous].Y,2));
      score+=Math.Max(0,.48-distance)*(previous==phase-1?.72:.14);
     }
     // The last line should settle in the clearest region, rather than chase variety.
     if(phase==3)score=slot.Score;
     if(score<best){best=score;pick=slot;}
    }
    chosen[phase]=pick;
   }
   var left=Evaluate(p,risk,luma,new Rect(.04,.055,.34,.115),image);var right=Evaluate(p,risk,luma,new Rect(.62,.055,.34,.115),image);
   var colors=new Color[4];double[] offset={0,35,0,180};
   for(int i=0;i<4;i++)colors[i]=Hsv((p.Hue+offset[i])%360,i==2?.16:.34,1);
   return new AestheticLayout{Captions=chosen,Progress=left.Score<=right.Score?left:right,Accents=colors,Background=Hsv(p.Hue,.35,.055)};
  }
  public static double FrameQuality(BitmapSource source){
   int w,h;byte[] pixels=Pixels(source,out w,out h);var profile=Compute(pixels,w,h,source.PixelWidth/(double)source.PixelHeight);
   return profile.TonalRange*2+Math.Min(.30,profile.EdgeDensity)*2-Math.Abs(profile.MeanLuma-.45)*.8-(profile.MeanLuma<.035?4:0);
  }
 }
}
