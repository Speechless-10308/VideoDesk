# 测试约定

## 运行

```powershell
./.tools/dotnet/dotnet.exe test -c Release
```

使用 xUnit v3 和 .NET 10 的 Microsoft.Testing.Platform。Core.Tests 引用业务项目，App.Tests 引用应用项目；依赖方向均从测试指向产品。依赖和测试运行器版本进入锁文件。

## 范围

| 测试 | 关心的行为 |
| --- | --- |
| CommentTimestampTests | 时间格式、分 P、无效值与正文位置 |
| CommentSessionTests | 服务器列表延迟时保留新评论、去重、楼中楼和删除 |
| WeightedLruCacheTests | 图片缓存的内存预算、最近使用顺序、替换和缩小预算 |
| DanmakuSegmentCacheTests | 定位后只读邻近分段、并发合并、失败重试和排序去重 |
| DanmakuProtocolTests | protobuf 毫秒／长 ID 解析与截断数据拒绝 |
| PlaybackClockTests | 暂停、定位、倍速与量化时间戳的连续性 |
| PlaybackLoadStateTests / PlaybackHealthTests | 文件事件隔离、首帧、连续跳转、卡顿期限、暂停及有限重试 |
| MpvMediaSourceTests | 分离音视频的 EDL 地址与 UTF-8 长度转义 |
| App.Tests / PlayerNetworkTests | 本地 HTTP 首包延迟、不响应、低带宽、中途断流与恢复、持续断流、连接关闭、跳转卡顿、替换加载与音视频同步加载 |
| App.Tests / PlaybackTimelineTests | 点击跨越播放刷新、鼠标捕获与丢失、加载取消手势及键盘跳转；原生播放测试另覆盖连续前后跳转和暂停状态 |
| App.Tests / ViewportRowsTests | 分批构建期间缩放窗口、替换列表、万条记录的可见控件回收；重复快照报告本次运行的分配量与耗时，不设机器相关阈值 |
| App.Tests / ImageCacheTests | 模拟 HTTP 图片、小图不放大、长图像素上限、原始字节完整性与解码后编码缓冲回收 |
| App.Tests / DanmakuPlaybackTests | 连续跳转时提前返回的弹幕等待目标画面；真实 libmpv 跳转遇到断流时不提前恢复弹幕 |
| VideoStoryboardTests | 官方截图索引的首项哨兵、时间边界、分页和损坏元数据 |
| VideoPlayerFeaturesTests | 章节边界、互动弹幕元数据、投票类型和选项标记、二进制预览索引 |
| BilibiliClientTests | 实际公共客户端方法的请求上下文、编码、回执及写请求不自动重试 |
| GitHubReleaseTests | 无发布版本、版本比较、公开更新请求及下载页面地址 |
| ResponseParsingTests | 稍后再看状态、评论图片／链接、验证失败与不完整回执 |
| LocalStoreTests | 隔离临时目录中的进度落盘、删除与设置恢复 |
| CapturePrivacyTests | 显式诊断的脱敏，不修改原数据 |

HTTP 测试使用严格的内存 HttpMessageHandler，未知请求立即失败，不回退真实网络。凭据为固定假值。存储及客户端测试只创建独立临时目录并在结束后清理，不接触 `.local`。`Fixtures/videoshot.json` 是公开截图元数据样本，不包含账号信息；保留它用于复现曾发生的索引偏移问题，禁止把整套抓包复制成测试夹具。

App.Tests 在 Windows 的 STA 线程中创建独立 WPF 播放窗口，调用随构建准备的真实 libmpv。测试服务器只监听本机随机端口，按场景延迟响应、限速或断开连接；音视频在内存中生成。测试等待实际播放状态、进度和截止期限，结束后关闭窗口、原生播放器和连接，不更改系统代理、网卡或账号数据。完整的网络测试需要约一到两分钟。

新增测试围绕可观察结果及明确回归场景。控件名称、子元素数量、私有字段、调用顺序和当前截图像素不是通用功能断言；不为修改样式同步维护反射探测器。不要逐项搬运旧检查计数，也不添加固定等待时间来让异步测试碰巧通过。

## 界面与性能验证边界

App.Tests 覆盖低分辨率测试流的原生解码和网络恢复，不代表所有显卡、编码与远端 CDN 的兼容性。界面排版、输入法及窗口行为仍在 Release 程序中检查：首页首次返回与滚动位置、动态评论开关及草稿、普通／最大化／视频全屏切换、弹幕输入、图片与稍后再看浮窗互不阻塞。真实发布或账号写操作需另有用户授权。

需要测量性能时，用 Visual Studio CPU／内存分析器或标准 `dotnet-counters`、`dotnet-gcdump` 等工具，固定相同数据、窗口尺寸、缩放、操作和预热条件，分别比较耗时、分配、堆存活及进程内存。记录测量环境和原始数值；不把某次机器上的耗时硬编码成单元测试门槛。分析工具仅在需要时安装，不编进应用；输出用完清理。

## 参考

- [Microsoft：单元测试最佳实践](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices)
- [Community Toolkit：src 与 tests 的独立项目组织](https://github.com/CommunityToolkit/dotnet/blob/main/dotnet.slnx)
- [xUnit v3 入门](https://xunit.net/docs/getting-started/v3/getting-started)
- [.NET 10 dotnet test 与 Microsoft.Testing.Platform](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test)
