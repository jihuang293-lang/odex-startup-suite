# Third-party notices

## Dragon Codex Boot

Animation launcher code is derived from [yushua0808-cpu/dragon-codex-boot](https://github.com/yushua0808-cpu/dragon-codex-boot), v0.2.0, commit `d3b7bb84b75134f8250491c86d72996a70d58165`, under MIT. Its copyright and license are preserved in `dragon-codex-boot/LICENSE` and the root license. Changes include integration gates, current-skin media resolution, cinematic transitions, distributed English captions, window alignment and portable paths. Original demonstration videos, music and extracted application icons are excluded.

The staged visual sequence was inspired by [yanami-sec/codex-startup-animation-windows](https://github.com/yanami-sec/codex-startup-animation-windows). No source code, demo footage, personal images or avatar from that project is included. The WPF cinematic implementation is local original code.

## Codex Dynamic Skin

[CCDawn/Codex-Dynamic-Skin](https://github.com/CCDawn/Codex-Dynamic-Skin), v1.7.3, is an external dependency, not redistributed here. Obtain it from its upstream release and follow its license and notices. This suite adds a version-checked hook to its extracted runtime scripts and a small reversible source transformation of its watcher retry policy. It does not change the manager executable or official Codex files. The full upstream watcher is not bundled. The scene bridge implements the existing SceneViewer interface and is not an upstream official SceneViewer binary.

## FFmpeg

FFmpeg is an external executable dependency, not included in the repository or release ZIP. The helper downloads Gyan's FFmpeg **9.0.2 essentials** build:

- Build and source information: https://www.gyan.dev/ffmpeg/builds/
- Fixed archive: https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.7z
- Archive SHA-256: `4705843ccaaf54257c16ad90f3e952ece33c17df964ecf7bfdbb0f49c7171077`
- `ffmpeg.exe` SHA-256: `3256173f3f8bffd7df12227c68adf68025edb1832273a9530688a7bb1ed8edec`
- FFmpeg licensing and source: https://ffmpeg.org/legal.html and https://ffmpeg.org/download.html

That build enables GPL components, including libx264. Its own license applies separately from this suite's MIT code. The download helper keeps the upstream license and README with the tool. Anyone redistributing a modified package with FFmpeg must satisfy that build's corresponding license and source obligations; this source-only distribution does not grant an exception.

## Wallpaper Engine and platform components

Wallpaper Engine is proprietary software installed and licensed by each user. Its program, Workshop content and scene assets are not distributed. The bridge uses its [documented CLI](https://help.wallpaperengine.io/en/functionality/cli.html) and captures only its own verified wallpaper window. The original bridge code is MIT; see `codex-scene-bridge/LICENSE`.

Windows, .NET Framework, media components and Codex are supplied by their respective vendors. OpenAI, ChatGPT, Codex and Wallpaper Engine names identify supported products and do not imply sponsorship or endorsement. Users retain responsibility for the rights to their own wallpapers and media.
