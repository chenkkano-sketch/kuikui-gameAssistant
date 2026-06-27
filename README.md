# 盔盔游戏助手

> 一个给 Windows 玩家准备的轻量游戏监控、截图、录屏和悬浮窗工具。

[![Release](https://img.shields.io/github/v/release/chenkkano-sketch/kuikui-gameAssistant?label=release)](https://github.com/chenkkano-sketch/kuikui-gameAssistant/releases)
[![Build](https://github.com/chenkkano-sketch/kuikui-gameAssistant/actions/workflows/release.yml/badge.svg)](https://github.com/chenkkano-sketch/kuikui-gameAssistant/actions/workflows/release.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4)](https://www.microsoft.com/windows)

盔盔游戏助手把硬件状态、FPS、截图录屏和游戏内悬浮监控放在一个安静、轻巧、能长期挂着用的桌面应用里。它不是庞大的控制中心，也不是花里胡哨的跑分面板，而是一个打开就能看、按下快捷键就能完成操作的小工具。

## 下载

当前正式版本：`v1.0.5`

前往 [GitHub Releases](https://github.com/chenkkano-sketch/kuikui-gameAssistant/releases/latest) 下载：

- 安装版：`KuikuiGameAssistant-1.0.5-setup.exe`
- 便携版：`KuikuiGameAssistant-1.0.5-win-x64-portable.zip`

固定直链：

- 最新安装版：https://github.com/chenkkano-sketch/kuikui-gameAssistant/releases/latest/download/KuikuiGameAssistant-setup.exe
- 最新便携版：https://github.com/chenkkano-sketch/kuikui-gameAssistant/releases/latest/download/KuikuiGameAssistant-win-x64-portable.zip

安装版适合长期使用，会安装到系统程序目录。便携版适合解压即用，可以放进移动硬盘或工具箱目录。

## 亮点

- 实时监控：CPU / GPU 负载、温度、内存占用和历史曲线。
- FPS 采集：自带 KuikuiTelemetryService 后台引擎，安装后自动以服务方式采集游戏帧率。
- 游戏悬浮窗：透明置顶、九宫格定位、可调颜色、布局、字号和尺寸。
- 区域截图：拖拽选区、标注、取色、放大镜、快捷复制到剪切板。
- MP4 录屏：基于 ScreenRecorderLib，支持 30 / 60 / 120 FPS、码率、系统声音、麦克风和鼠标指针。
- 游戏滤镜：支持亮度、对比度、灰度、饱和度、色调、屏幕暗角和预设存档。
- 全局快捷键：截图、录屏、悬浮窗都可以自定义快捷键。
- 自动更新：依托 GitHub Releases，支持安装版和便携版各自更新。

## 截图工具

截图工具支持一套偏效率流的操作：

- 左键拖拽选择区域。
- 左键单击空选区自动圈选全屏。
- 左键双击选区完成截图并复制到剪切板。
- 右键取消当前选中或编辑状态；未选择区域时退出截图。
- `Enter` 完成并复制到剪切板。
- `Esc` 取消。
- `Ctrl+Z` 撤销上一个标注。
- `C` 复制鼠标位置的色值。

工具栏左侧是标注工具，右侧是撤销、保存图片、取消和完成。保存图片会落盘，完成会复制到剪切板。

## 自动更新

应用默认使用这个仓库检查更新：

```text
chenkkano-sketch/kuikui-gameAssistant
```

更新逻辑：

- 启动时每天最多自动检查一次 GitHub Release。
- 安装版优先下载 `setup.exe`。
- 便携版优先下载 `portable.zip`。
- 设置页可以关闭自动检查，也可以手动检查更新。
- GitHub API 被限流时，会切换到 Releases 网页跳转线路继续判断最新版本。
- 设置页提供最新 Release 和固定下载直链，API 受限时可以直接复制给用户手动下载。

## 从源码运行

需要 Windows 10 / Windows 11 和 .NET 8 SDK。

```powershell
dotnet restore
dotnet build
dotnet run --project .\src\KuikuiGameAssistant\KuikuiGameAssistant.csproj
```

本仓库也带有本地 `.dotnet` 运行时目录时，可以使用：

```powershell
.\.dotnet\dotnet.exe run --project .\src\KuikuiGameAssistant\KuikuiGameAssistant.csproj
```

## 打包

生成安装版和便携版：

```powershell
.\scripts\package.ps1 -Version 1.0.5
```

只生成便携 zip：

```powershell
.\scripts\package.ps1 -Version 1.0.5 -SkipInstaller
```

输出文件：

- `artifacts\KuikuiGameAssistant-1.0.5-setup.exe`
- `artifacts\KuikuiGameAssistant-1.0.5-win-x64-portable.zip`
- `artifacts\KuikuiGameAssistant-setup.exe`
- `artifacts\KuikuiGameAssistant-win-x64-portable.zip`

安装版依赖 Inno Setup 6。GitHub Actions 会自动安装 Inno Setup 并完成发布构建。

## 发布流程

推送 tag 即可触发正式发布：

```powershell
git tag v1.0.5
git push origin main
git push origin v1.0.5
```

GitHub Actions 会构建并上传版本号文件和固定直链文件。Release 内容会注明下载版本、安装版文件名、便携版文件名和 latest/download 固定链接。

## 技术栈

- WPF + Windows Forms overlay
- .NET 8
- LibreHardwareMonitorLib
- KuikuiTelemetryService
- PresentMon
- ScreenRecorderLib
- Inno Setup
- GitHub Actions + GitHub Releases

## 许可证

本项目基于 [Apache License 2.0](LICENSE) 发布。
