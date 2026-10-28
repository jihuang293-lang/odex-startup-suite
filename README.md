# Codex Startup Suite

Windows 版 Codex 的社区启动工具：统一代理与皮肤启动、跟随壁纸的开场动画，以及 Wallpaper Engine 场景壁纸桥接。

这不是 OpenAI 官方项目。当前是 **v0.1.0 预览版**，适用于 Windows 11 x64、Microsoft Store 版 Codex 和 Codex Dynamic Skin **1.7.3**。代理入口要求已开启本机 HTTP 系统代理，不需要 TUN；它不保证服务端或代理节点永不重连。

## 效果预览

下面由程序实际渲染，使用项目原创示例背景。安装后背景会跟随当前皮肤；动图演示动画阶段，实际启动等待由应用就绪状态决定。

![Codex 开场动画动态预览](docs/preview/animation-preview.gif)

![初始化、粒子散开、轮廓重组和欢迎文字四阶段](docs/preview/animation-overview.png)

可使用 `scripts/Render-Preview.ps1 -FfmpegPath "C:\Tools\ffmpeg.exe"` 重新生成。预览不包含真实聊天、个人壁纸或桌面画面。

## 功能

- 保留本机代理参数和皮肤调试参数，统一启动应用。
- 英文科技感字幕、多阶段开场、进度条和贴合应用窗口的淡出交接；不显示 Esc 按钮，仍可按 Esc 跳过。
- 每次读取当前皮肤的图片或视频；场景壁纸的开场使用本机生成的预览图。
- 使用正版 Wallpaper Engine 渲染场景，在 Codex 背景播放；默认 1280×720、15 FPS，无需逐张录制。
- 每次打开管理器的 Wallpaper Engine 列表重新扫描。新场景是**自动识别、手动导入**，不在后台自动切换皮肤。

## 安装

1. 下载 [Windows x64 预览包](downloads/codex-startup-suite-v0.1.0-windows-x64.zip)（在文件页面点击 Download raw），完整解压到可写的固定目录。仓库的源码 ZIP 需要先运行 `Build.ps1`。
2. 安装 Microsoft Store 版 Codex。准备来自 [Codex Dynamic Skin v1.7.3](https://github.com/CCDawn/Codex-Dynamic-Skin/releases/tag/v1.7.3) 的 `CodexDreamSkinManager.exe`；安装程序只接受已核对的文件摘要，其他版本会停止。
3. 安装场景功能时需自行购买、安装并启动 [Wallpaper Engine](https://store.steampowered.com/app/431960/Wallpaper_Engine/)。项目不分发壁纸或引擎。
4. 在 Windows PowerShell 5.1 中运行：

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\scripts\Get-VideoTool.ps1
   powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\Install.ps1 -ManagerPath "C:\YourDownloads\CodexDreamSkinManager.exe"
   ```

   视频工具从 Gyan 的固定版本地址下载，安装前核对 SHA-256；源码与许可证链接见 [第三方说明](THIRD_PARTY_NOTICES.md)。也可将同一版本的 `ffmpeg.exe` 通过 `-FfmpegPath` 传给安装程序。
5. 开启本机 HTTP 系统代理；从桌面 **Codex Startup Suite** 打开。首次在皮肤管理器中选择并应用壁纸；之后从同一入口启动即可显示动画。未选择壁纸时会跳过动画，继续启动流程。

系统若阻止未签名程序或下载脚本，请先检查系统给出的原因和文件来源；本工具不会修改系统安全策略。解压位置需要允许脚本与程序运行。

## 使用

- 选择场景：管理器 **Wallpaper Engine → 选择场景 → 导入到主页 → 应用到 Codex**。保持管理器和 Wallpaper Engine 运行。
- 动画设置：安装后使用桌面 **Codex Startup Settings** 快捷方式。
- 只读检查：`powershell.exe -NoProfile -File .\codex-proxy\start-codex-with-skin.ps1 -CheckOnly`。
- 不播放动画：为启动脚本添加 `-WithoutAnimation`。
- 卸载集成：先正常退出本工具的管理器和场景播放，再运行 `Uninstall.ps1`。它只移除本工具创建的快捷方式和可确认的启动挂接，不删除聊天、壁纸或皮肤库。恢复官方界面和关闭调试端口，请在皮肤管理器中恢复官方外观并正常重启 Codex。

## 已知范围

进度条表示动画、界面准备与窗口对齐阶段，不是下载进度，也不是 Codex 内部真实完成百分比。应用没有准备好时不会仅凭计时宣布完成；等待超过上限会退出覆盖层。

场景交互不转发鼠标，输出流不含音轨；Wallpaper Engine 自身声音仍由其设置管理。首次生成场景预览之前，开场可能使用皮肤现有预览。新版 Codex、皮肤管理器或显卡驱动可能需要重新验证。

皮肤依赖 `127.0.0.1:9335` 本机调试连接。同机其他程序也可能访问这个端口；仅绑定回环地址不会提供身份认证。不要将它转发到外网。

原安装环境验证过 25 张本机场景渲染和解码，以及一张场景在实际 Codex 中的视频推进、切回视频和退出清理。这不等于所有场景、所有电脑或新版应用均已验证。公开包的离线验证与安装测试见 [验证说明](docs/VALIDATION.md)。

## 开发

使用 Windows 自带的 .NET Framework 4.x C# 编译器，无 NuGet 依赖。Node.js 22+ 用于传输模块测试；运行时复用皮肤管理器已校验的 Node。

```powershell
powershell.exe -NoProfile -File .\Build.ps1
powershell.exe -NoProfile -File .\Test.ps1
powershell.exe -NoProfile -File .\scripts\Pack.ps1
```

源码模块：`codex-proxy`（统一入口）、`dragon-codex-boot`（动画）、`codex-scene-bridge`（场景桥接）。不包含官方 Codex 安装包、第三方管理器、FFmpeg 二进制或任何个人壁纸。许可证与上游来源见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
