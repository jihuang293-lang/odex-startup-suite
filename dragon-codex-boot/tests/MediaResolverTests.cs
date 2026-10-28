using System;
using System.IO;
using System.Web.Script.Serialization;
using DragonCodexBoot;
internal static class MediaResolverTests {
 private static int checks;
 private static void Check(bool condition,string message) {if(!condition)throw new Exception(message);checks++;}
 public static int Main(string[] args) {
  try {
   string root=Path.GetFullPath(args[0]),active=Path.Combine(root,"active-theme"),cache=Path.Combine(root,"media-cache");
   Directory.CreateDirectory(active);Directory.CreateDirectory(cache);
   string theme=Path.Combine(active,"theme.json"),first=Path.Combine(active,"first.mp4"),second=Path.Combine(active,"second.mp4"),proxy=Path.Combine(cache,"smooth.mp4");
   File.WriteAllText(first,"fixture");File.WriteAllText(second,"fixture");File.WriteAllText(proxy,"fixture");
   var serializer=new JavaScriptSerializer();var config=new Config {FollowSkin=true,Video="old-stale.mp4",MinimumSeconds=7,Volume=.2};
   File.WriteAllText(theme,serializer.Serialize(new {id="one",name="First",image="first.mp4"}));
   Check(MediaResolver.Resolve(config,root,theme).MediaPath==first,"First live skin was not resolved.");
   File.WriteAllText(theme,serializer.Serialize(new {id="two",name="Second",image="second.mp4"}));
   var changed=MediaResolver.Resolve(config,root,theme);
   Check(changed.MediaPath==second&&changed.ThemeId=="two","Skin change retained stale cached media.");
   File.WriteAllText(theme,serializer.Serialize(new {id="cached",name="Cached",image="second.mp4",media=new {proxy="media-cache/smooth.mp4"}}));
   Check(MediaResolver.Resolve(config,root,theme).MediaPath==proxy,"Existing playback proxy was not preferred.");
   File.WriteAllText(theme,serializer.Serialize(new {id="fallback",name="Fallback",image="second.mp4",media=new {proxy="media-cache/missing.mp4"}}));
   Check(MediaResolver.Resolve(config,root,theme).MediaPath==second,"Missing proxy did not fall back to the live original.");
   config.FollowSkin=false;config.CustomVideo=first;
   Check(MediaResolver.Resolve(config,root,theme).MediaPath==first,"Custom media selection was ignored.");
   Check(config.MinimumSeconds==7&&config.Volume==.2&&config.Video=="old-stale.mp4","Resolving media mutated user preferences.");
   config.FollowSkin=true;File.WriteAllText(theme,"{\"image\":\"missing.mp4\"}");
   bool rejected=false;try {MediaResolver.Resolve(config,root,theme);}catch {rejected=true;}
   Check(rejected,"Missing live skin silently reused old wallpaper.");
   string sceneFixture=Path.Combine(root,"scene-fixture"),animation=Path.Combine(sceneFixture,"animation");
   string sceneCache=Path.Combine(sceneFixture,"codex-dynamic-skin-trial","scene-runtime","poster-cache");Directory.CreateDirectory(animation);Directory.CreateDirectory(sceneCache);
   string poster=Path.Combine(sceneCache,"12345.jpg");File.WriteAllText(poster,"fixture");
   File.WriteAllText(theme,serializer.Serialize(new {id="scene",image="second.mp4",media=new {type="scene",workshopId="12345"}}));
   Check(MediaResolver.Resolve(config,animation,theme).MediaPath==poster,"Scene poster was not preferred.");
   Check(MediaResolver.Resolve(config,animation+Path.DirectorySeparatorChar,theme).MediaPath==poster,"Trailing animation directory separator broke scene poster lookup.");
   File.WriteAllText(theme,serializer.Serialize(new {id="scene",image="second.mp4",media=new {type="scene",workshopId="../12345"}}));
   Check(MediaResolver.Resolve(config,animation,theme).MediaPath==second,"Invalid scene ID escaped the poster cache.");
   Console.WriteLine("PASS: "+checks+" live wallpaper, proxy-cache, stale-path and preference-preservation checks.");return 0;
  } catch(Exception e) {Console.Error.WriteLine(e);return 1;}
 }
}
