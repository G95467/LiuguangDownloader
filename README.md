# 流光下载器源码

这是“流光下载器 1.0.0”的完整源码，采用 **.NET 10 + WPF + Microsoft Edge WebView2** 编写。

## 目录

```
src/LumenDownloader.App        WPF 宿主、安装桥、设置与下载队列管理
src/LumenDownloader.Engine     多连接 HTTP/HTTPS 下载引擎
src/LumenDownloader.App/ui     HTML / CSS / JavaScript 界面
assets                         应用图标
tests/LumenDownloader.Smoke    下载引擎冒烟测试
InstallerSource                安装程序和卸载程序源码
scripts                        图标生成、测试服务器等辅助脚本
```

## 构建应用

1. 安装 .NET 10 SDK：
   https://dotnet.microsoft.com/download/dotnet/10.0
2. Windows 10/11 需要 Microsoft Edge WebView2 Runtime：
   https://developer.microsoft.com/microsoft-edge/webview2/
3. 在源码根目录执行：

```powershell
dotnet restore src\LumenDownloader.App\LumenDownloader.App.csproj
dotnet build src\LumenDownloader.App\LumenDownloader.App.csproj -c Release
dotnet publish src\LumenDownloader.App\LumenDownloader.App.csproj -c Release -r win-x64 --self-contained false -o publish
```

生成结果是框架依赖版本，需要目标电脑安装 .NET 10 Desktop Runtime。

## 运行测试

```powershell
dotnet run --project tests\LumenDownloader.Smoke\LumenDownloader.Smoke.csproj -c Release
```

测试会在本机随机端口启动一个支持 Range 和重定向的 HTTP 服务，然后验证：

- 多连接分段下载与文件完整性
- 不支持 Range 时单连接回退
- 重定向、Cookie、UTF-8 文件名
- 暂停 / 检查点 / 断点续传
- HTTP 错误提示

## 安装程序

`InstallerSource` 内包含安装器与卸载器源码，使用 .NET Framework 4.8 和系统自带的 `csc.exe` 编译。安装器会把编译好的应用和界面文件打包为内嵌 ZIP，并以当前用户身份安装到 `%LOCALAPPDATA%\Programs\流光下载器`，无需管理员权限。

如需重新构建安装包，先发布应用，然后执行：

```powershell
$env:LUMEN_APP_DIR = "发布目录的完整路径"
powershell -ExecutionPolicy Bypass -File .\InstallerSource\build.ps1
```

安装器会：

- 复制程序文件、可选源码和说明文档
- 创建桌面、开始菜单快捷方式
- 写入当前用户卸载注册表项
- 附带独立的卸载程序

安装器与卸载器都没有商业代码签名，Windows SmartScreen 可能会显示提示。

## 许可

应用源码采用 MIT 许可证，详见 `LICENSE.txt`。
第三方组件：
- .NET（MIT）
- Microsoft Edge WebView2（Microsoft 软件许可条款）
