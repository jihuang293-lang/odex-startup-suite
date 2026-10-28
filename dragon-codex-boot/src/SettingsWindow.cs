using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using F=System.Windows.Forms;

namespace DragonCodexBoot {
 public sealed class SettingsForm:F.Form {
  private readonly string root;
  private readonly Config config;
  private readonly F.CheckBox follow=new F.CheckBox();
  private readonly F.TextBox file=new F.TextBox();
  private readonly F.TextBox welcomeTitle=new F.TextBox(),welcomeSubtitle=new F.TextBox();
  private readonly F.Button browse=new F.Button();
  private readonly F.NumericUpDown minimum=new F.NumericUpDown(),fade=new F.NumericUpDown();
  private readonly F.TrackBar volume=new F.TrackBar();
  private readonly F.Label hint=new F.Label();
  private readonly F.ComboBox mode=new F.ComboBox();
  public SettingsForm(string directory) {
   root=directory;config=Entry.ReadConfig(root);
   Text="Codex 启动画面设置";ClientSize=new Size(700,580);MinimumSize=new Size(700,580);
   StartPosition=F.FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10);
   BackColor=Color.FromArgb(245,247,251);AutoScaleMode=F.AutoScaleMode.Dpi;
   var grid=new F.TableLayoutPanel { Dock=F.DockStyle.Fill,Padding=new F.Padding(22),ColumnCount=2,RowCount=11 };
   grid.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.Absolute,140));grid.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.Percent,100));
   foreach(int h in new int[]{45,38,44,44,44,44,44,58,38,50,44})grid.RowStyles.Add(new F.RowStyle(F.SizeType.Absolute,h));
   mode.DropDownStyle=F.ComboBoxStyle.DropDownList;mode.Items.AddRange(new object[]{"多段转场 · 碎片 / 线稿 / 对齐淡出","简洁壁纸 · 淡入淡出"});
   mode.SelectedIndex=config.TransitionMode=="Cinematic"?0:1;mode.Dock=F.DockStyle.Fill;mode.Margin=new F.Padding(0,8,0,0);
   grid.Controls.Add(Label("动画风格"),0,0);grid.Controls.Add(mode,1,0);
   follow.Text="自动跟随当前皮肤壁纸";follow.Checked=config.FollowSkin;follow.Dock=F.DockStyle.Fill;
   grid.Controls.Add(follow,0,1);grid.SetColumnSpan(follow,2);
   grid.Controls.Add(Label("自定义素材"),0,2);
   var fileRow=new F.TableLayoutPanel {Dock=F.DockStyle.Fill,ColumnCount=2,Margin=new F.Padding(0)};
   fileRow.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.Percent,100));fileRow.ColumnStyles.Add(new F.ColumnStyle(F.SizeType.Absolute,78));
   file.Text=config.CustomVideo??"";file.Dock=F.DockStyle.Fill;file.Margin=new F.Padding(0,5,8,0);
   browse.Text="选择…";browse.Dock=F.DockStyle.Fill;browse.Margin=new F.Padding(0,2,0,3);
   fileRow.Controls.Add(file,0,0);fileRow.Controls.Add(browse,1,0);grid.Controls.Add(fileRow,1,2);
   follow.CheckedChanged+=delegate { file.Enabled=browse.Enabled=!follow.Checked;RefreshWallpaperName(); };file.Enabled=browse.Enabled=!follow.Checked;
   browse.Click+=delegate {
    using(var picker=new F.OpenFileDialog {Title="选择启动壁纸或视频",Filter="视频或图片|*.mp4;*.png;*.jpg;*.jpeg;*.bmp",CheckFileExists=true}) {
     if(picker.ShowDialog(this)==F.DialogResult.OK)file.Text=picker.FileName;
    }
   };
   welcomeTitle.Text=config.WelcomeTitle??"";welcomeTitle.MaxLength=32;welcomeTitle.Dock=F.DockStyle.Fill;welcomeTitle.Margin=new F.Padding(0,5,0,0);
   welcomeSubtitle.Text=config.WelcomeSubtitle??"";welcomeSubtitle.MaxLength=80;welcomeSubtitle.Dock=F.DockStyle.Fill;welcomeSubtitle.Margin=new F.Padding(0,5,0,0);
   grid.Controls.Add(Label("欢迎标题"),0,3);grid.Controls.Add(welcomeTitle,1,3);
   grid.Controls.Add(Label("欢迎小字"),0,4);grid.Controls.Add(welcomeSubtitle,1,4);
   ConfigureNumber(minimum,1,30,(decimal)config.MinimumSeconds,1);grid.Controls.Add(Label("最短显示时间 / 秒"),0,5);grid.Controls.Add(minimum,1,5);
   ConfigureNumber(fade,.25m,3,(decimal)config.FadeSeconds,.1m);grid.Controls.Add(Label("结束转场 / 秒"),0,6);grid.Controls.Add(fade,1,6);
   volume.Minimum=0;volume.Maximum=100;volume.TickFrequency=10;volume.Value=(int)Math.Round(config.Volume*100);volume.Dock=F.DockStyle.Fill;
   grid.Controls.Add(Label("音量（0 为静音）"),0,7);grid.Controls.Add(volume,1,7);
   hint.Text="等待应用时视频会继续播放，按 Esc 可以跳过。";hint.Dock=F.DockStyle.Fill;hint.ForeColor=Color.FromArgb(90,101,120);
   grid.Controls.Add(hint,0,8);grid.SetColumnSpan(hint,2);
   var buttons=new F.FlowLayoutPanel {Dock=F.DockStyle.Fill,FlowDirection=F.FlowDirection.RightToLeft};
   var preview=new F.Button {Text="保存并预览",AutoSize=true,Padding=new F.Padding(9,3,9,3)};
   var save=new F.Button {Text="保存",AutoSize=true,Padding=new F.Padding(9,3,9,3)};
   var close=new F.Button {Text="关闭",AutoSize=true,Padding=new F.Padding(9,3,9,3)};
   preview.Click+=delegate { try {Save();Preview();} catch(Exception e) {Error(e);} };
   save.Click+=delegate {try {Save();hint.Text="已保存，下次启动会使用新设置。";} catch(Exception e) {Error(e);} };
   close.Click+=delegate {Close();};buttons.Controls.Add(preview);buttons.Controls.Add(save);buttons.Controls.Add(close);
   grid.Controls.Add(buttons,0,9);grid.SetColumnSpan(buttons,2);Controls.Add(grid);
   var timingNote=new F.Label {Text="主屏播放；界面和皮肤就绪后，对齐窗口并柔和淡出。",Dock=F.DockStyle.Fill,ForeColor=Color.FromArgb(90,101,120),Font=new Font("Microsoft YaHei UI",9)};
   grid.Controls.Add(timingNote,0,10);grid.SetColumnSpan(timingNote,2);
   Activated+=delegate {RefreshWallpaperName();};RefreshWallpaperName();
  }
  private static F.Label Label(string text) {return new F.Label {Text=text,Dock=F.DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};}
  private static void ConfigureNumber(F.NumericUpDown control,decimal min,decimal max,decimal value,decimal step) {
   control.Minimum=min;control.Maximum=max;control.DecimalPlaces=2;control.Increment=step;
   control.Value=Math.Max(min,Math.Min(max,value));control.Width=120;control.Margin=new F.Padding(0,5,0,0);
  }
  private void Save() {
   if(!follow.Checked&&!File.Exists(file.Text.Trim()))throw new Exception("请先选择一个存在的本地视频或图片。");
   config.FollowSkin=follow.Checked;config.CustomVideo=file.Text.Trim();config.MinimumSeconds=(double)minimum.Value;
   config.FadeSeconds=(double)fade.Value;config.Volume=volume.Value/100.0;config.TransitionMode=mode.SelectedIndex==0?"Cinematic":"SimpleFade";config.FitToClient=true;
   config.WelcomeTitle=welcomeTitle.Text.Trim();config.WelcomeSubtitle=welcomeSubtitle.Text.Trim();config.ShowSkipButton=false;
   config.ShowWelcome=config.WelcomeTitle.Length>0||config.WelcomeSubtitle.Length>0;
   config.Video=MediaResolver.Resolve(config,root,MediaResolver.CurrentThemePath()).MediaPath;
   Entry.ValidateConfig(config);
   string path=Path.Combine(root,"launcher.json"),temp=path+".tmp";
   File.WriteAllText(temp,new JavaScriptSerializer().Serialize(config),new UTF8Encoding(false));
   File.Replace(temp,path,path+".previous");
  }
  private void RefreshWallpaperName() {
   if(!follow.Checked) {hint.Text="使用你选择的素材；按 Esc 可以跳过。";return;}
   try {hint.Text="当前跟随："+MediaResolver.Resolve(new Config {FollowSkin=true},root,MediaResolver.CurrentThemePath()).ThemeName;}
   catch {hint.Text="当前壁纸不可用，请重新应用皮肤后再预览。";}
  }
  private void Preview() {
   string launcher=Path.Combine(Directory.GetParent(root.TrimEnd('\\')).FullName,"codex-proxy","start-codex-with-skin.ps1");
   string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
   Process.Start(new ProcessStartInfo(powershell,"-NoProfile -ExecutionPolicy RemoteSigned -WindowStyle Hidden -File \""+launcher+"\" -PreviewAnimation") {
    UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden
   });
  }
  private void Error(Exception error) {F.MessageBox.Show(this,error.Message,"无法保存",F.MessageBoxButtons.OK,F.MessageBoxIcon.Information);}
 }
}
