using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace DragonCodexBoot {
 public sealed class WallpaperSource {
  public string ThemeId { get; set; }
  public string ThemeName { get; set; }
  public string MediaPath { get; set; }
  public bool IsScene { get; set; }
 }
 public static class MediaResolver {
  private static string Text(IDictionary<string,object> map,string key) {
   object value;return map!=null&&map.TryGetValue(key,out value)&&value!=null?Convert.ToString(value):"";
  }
  public static string CurrentThemePath() {
   return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexDreamSkin","active-theme","theme.json");
  }
  public static WallpaperSource Resolve(Config config,string animationRoot,string themePath) {
   string candidate;string id="custom",name="自定义素材";bool scene=false;
   if(config.FollowSkin) {
    var info=new FileInfo(themePath);
    if(!info.Exists||info.Length<2||info.Length>262144)throw new Exception("当前皮肤的壁纸记录不可用，请重新应用皮肤后再预览。");
    var theme=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(themePath));
    id=Text(theme,"id");name=Text(theme,"name");
    object raw;IDictionary<string,object> media=theme.TryGetValue("media",out raw)?raw as IDictionary<string,object>:null;
    string themeDirectory=Path.GetDirectoryName(Path.GetFullPath(themePath));
    string stateRoot=Directory.GetParent(themeDirectory).FullName;
    string proxy=Text(media,"proxy");candidate="";
    string workshopId=Text(media,"workshopId");
    if(Text(media,"type")=="scene"&&System.Text.RegularExpressions.Regex.IsMatch(workshopId,"\\A[0-9]{1,20}\\z")) {
     scene=true;
     string scenePoster=Path.Combine(Directory.GetParent(Path.GetFullPath(animationRoot).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)).FullName,"codex-dynamic-skin-trial","scene-runtime","poster-cache",workshopId+".jpg");
     candidate=scenePoster;
     if(!File.Exists(candidate)){
      Directory.CreateDirectory(Path.GetDirectoryName(candidate));
      using(var pending=new System.Drawing.Bitmap(1280,720))using(var drawing=System.Drawing.Graphics.FromImage(pending)){
       drawing.Clear(System.Drawing.Color.FromArgb(8,12,20));
       string temporary=candidate+"."+Guid.NewGuid().ToString("N")+".tmp";
       try{pending.Save(temporary,System.Drawing.Imaging.ImageFormat.Jpeg);if(!File.Exists(candidate))File.Move(temporary,candidate);}finally{if(File.Exists(temporary))File.Delete(temporary);}
      }
     }
    }
    if(!scene&&!String.IsNullOrWhiteSpace(proxy)&&!Path.IsPathRooted(proxy)) {
     string cached=Path.GetFullPath(Path.Combine(stateRoot,proxy));
     if(cached.StartsWith(stateRoot.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase)&&File.Exists(cached))candidate=cached;
    }
    if(candidate.Length==0&&Text(media,"source")=="wallpaper-engine-local") {
     candidate=Path.Combine(Text(media,"workshopRoot"),Text(media,"workshopId"),Text(media,"relativePath"));
    }
    if(candidate.Length==0) {
     string image=Text(theme,"image");candidate=Path.IsPathRooted(image)?image:Path.Combine(themeDirectory,image);
    }
   } else {
    candidate=String.IsNullOrWhiteSpace(config.CustomVideo)?config.Video:config.CustomVideo;
    if(!Path.IsPathRooted(candidate))candidate=Path.Combine(animationRoot,candidate);
   }
   candidate=Path.GetFullPath(candidate);
   string extension=Path.GetExtension(candidate).ToLowerInvariant();
   if(extension!=".mp4"&&extension!=".png"&&extension!=".jpg"&&extension!=".jpeg"&&extension!=".bmp")
    throw new Exception("请选择 MP4 视频或 PNG、JPEG、BMP 图片。");
   if(!File.Exists(candidate))throw new Exception("当前皮肤的壁纸文件不存在，请重新应用皮肤或选择其他素材。");
   return new WallpaperSource {ThemeId=id,ThemeName=name,MediaPath=candidate,IsScene=scene};
  }
 }
}
