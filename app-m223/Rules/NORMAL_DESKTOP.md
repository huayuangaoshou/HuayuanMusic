# M2-23 桌面路径

运行 `src/Preview.csproj` 构建的桌面入口，R2UI 本地与酷狗概念版共用一个 Core、BASS 实例和全局队列。正常播放从当前登录会话和平台授权响应解析音频。无需项目审批、单次计数、试播租约或离线 Profile。歌曲权限和访问范围由平台响应和来源校验决定。平台 API 保持 HTTPS。桌面媒体使用平台授权的 HTTP/HTTPS 地址，独立传输层不发送登录凭据。

`Check-ProviderBoundary.ps1` 校验 Contracts 和 Mock 的编译期边界。历史试播和夹具的实现保留供审计，普通桌面入口没有调用这些启动器、受控 Gateway 或夹具。
