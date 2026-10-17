# 第三方说明

VideoDesk 原创代码采用 [MIT](LICENSE)。第三方组件与素材保留各自的版权和许可。

## 使用的组件

| 组件 | 版本 | 许可与版权 |
| --- | --- | --- |
| [MaterialDesignThemes / Colors](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) | 5.3.2 | MIT；James Willock、Mulholland Software 及贡献者 |
| [Microsoft.Xaml.Behaviors.Wpf](https://github.com/microsoft/XamlBehaviorsWpf) | 1.1.77 | MIT；Microsoft 及贡献者 |
| [QRCoder](https://github.com/codebude/QRCoder) | 1.8.0 | MIT；Raffael Herrmann 及贡献者 |
| [ProtectedData](https://github.com/dotnet/runtime) | 10.0.0 | MIT；.NET Foundation 及贡献者 |
| [.NET / Windows Desktop 运行时](https://github.com/dotnet/runtime) | 10.0.12 | MIT 及其第三方声明；.NET Foundation、Microsoft 及贡献者 |
| [libmpv / FFmpeg](https://theiaplayer.com/third-party-notices) | mpv `13a4bfbc1a` / FFmpeg `7523428c26` | 组合 DLL 为 LGPL-3.0-or-later；各项目贡献者 |

依赖版本由 `packages.lock.json` 和 [构建依赖清单](build/dependencies.json) 记录。测试使用 xUnit v3（Apache-2.0）和 Microsoft.Testing.Platform（MIT）。

播放器为 zhongfly/mpv-winbuild 的 LGPL 构建 `2026-09-13-13a4bfbc1a`，从 Theia 0.4.0 镜像提取同一 DLL，不使用 Theia 的应用代码。该构建未启用 GPL / nonfree 组件；其静态依赖还保留各自的许可。详细版本、源码及构建配方见[上游说明](https://downloads.theiaplayer.com/source/README.txt)。

[源码清单](build/mpv-sources.json)记录对应源码归档的地址与校验值，由准备脚本下载并随程序提供。部分较小依赖的提交由上游按构建取源时间还原，未证明逐字节可复现。DLL 作为独立文件动态链接，用户可替换为兼容的 libmpv 2.x 构建，并可为调试库修改而进行必要的逆向工程。

## 素材

| 素材 | 来源与许可 |
| --- | --- |
| Material Design Icons | [Pictogrammers](https://github.com/Templarian/MaterialDesign)；Apache-2.0 为主，部分图标按其原声明 |
| Roboto / Noto 字体 | MaterialDesignThemes 内嵌资源；Apache-2.0 / SIL OFL。应用正文使用系统字体 |
| Twemoji 图形 | [Twemoji 17.0.3](https://github.com/jdecked/twemoji)；CC BY 4.0，Twitter, Inc.、Twemoji 及 jdecked 贡献者；按需加载，未修改 |
| 哔哩哔哩标识、表情及平台内容 | 权利属于相应权利人；标识来源见 [Official/README.md](src/VideoDesk.App/Assets/Official/README.md) |
| 应用图标与 README 封面 | AI 辅助生成；见 [assets/README.md](assets/README.md)。其中第三方画面与商标保留各自权利 |

## 参考资料

- [.NET WPF Slider 输入事件](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/Slider.cs)：`main`，2026-10-17 对照，MIT，.NET Foundation 及贡献者；用于确认移动到点击位置时的已处理事件与鼠标捕获行为，未复制其实现代码。
- [PiliPlus](https://github.com/bggRGjQaUbCoE/PiliPlus)、[PiliPala](https://github.com/guozhigq/pilipala)：GPLv3，作为交互及接口行为参考；未纳入其 Dart / Flutter 源码。
- [mpv EDL 协议](https://github.com/mpv-player/mpv/blob/v0.41.0/DOCS/edl-mpv.rst)：分离音视频使用官方 `new_stream` 与 UTF-8 字节长度转义协议，相关 C# 代码独立实现。
- [bilibili-API-collect](https://github.com/rinnein/bilibili-API-collect)：CC BY-NC 4.0，SocialSisterYi 及贡献者；参考接口地址、参数与协议字段，未纳入文档正文或示例程序。

分发二进制时，保留各依赖随附的版权声明、许可文本和播放器对应源码。第三方许可不因本项目采用 MIT 而改变。
