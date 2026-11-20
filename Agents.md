# chat-hub

把常用的 AI 聊天网页收进一个 Windows 桌面窗口：自定义工具栏列出所有已添加的网址，点一下就切换，网页用 WebView2 渲染，站点清单是 AppData 里的一个纯文本文件，随时可用记事本改。

## 功能

- **自定义工具栏**：所有已添加的站点都作为顶部条目展示，当前站点点亮；站点多时自动换行。
- **站点管理**：新增 / 修改 / 查询（按名称或网址即时过滤）/ 删除，改动立即写入清单文件。
- **每站一个 WebView2**：切换站点只做视觉挂载/卸载，页面不重新加载，因此聊天会话与登录态都保留。
- **应用内临时标签页**：站点里的 `target="_blank"` / `window.open()` 链接不再交给系统浏览器，而是在主窗口顶部新增临时标签页；原站点页面保持不动，`window.opener` 与 `window.close()` 的登录弹窗流程仍可用。标签页和站点页共享登录态，但使用独立 WebView2，关闭即释放，不进入站点池。
- **导航按钮**：后退 / 前进 / 刷新 / 回到首页；后退、前进、刷新跟随当前活动页面（站点页或临时标签页），可用状态自动变化。
- **默认站点**：首次运行自动写入「豆包」（`https://www.doubao.com/chat/`）与「DeepSeek」（`https://chat.deepseek.com/`）。
- **会话持久化**：所有站点共用一个 WebView2 用户数据目录，登录态重启后仍然有效。
- **提示信息**：未安装 WebView2 运行时会在内容区顶部给出下载入口；页面加载失败、清单写入失败、清单里有被跳过的无效行都会用弹窗或提示条说明，不会静默退出。

## 环境要求

| 项 | 要求 |
|---|---|
| 系统 | Windows 10 1809 及以上（WPF + WebView2） |
| 运行时 | .NET 10 桌面运行时（`Microsoft.WindowsDesktop.App`） |
| 浏览器内核 | Microsoft Edge WebView2 Runtime（Win11 与较新 Win10 通常已自带） |

若缺少 WebView2 运行时，应用会在内容区顶部给出下载入口：
<https://go.microsoft.com/fwlink/p/?LinkId=2124703>

## 构建与运行

```powershell
# 运行（开发）
dotnet run --project ChatHub.csproj

# 构建
dotnet build chat-hub.slnx -c Release

# 运行单元测试
dotnet test chat-hub.slnx

# 发布（依赖框架，需目标机器已装 .NET 10 桌面运行时）
dotnet publish ChatHub.csproj -c Release -r win-x64 --self-contained false
```

## 数据文件

| 内容 | 位置 | 说明 |
|---|---|---|
| 站点清单 | `%AppData%\ChatHub\sites.txt` | UTF-8 纯文本，Tab 分隔，可直接编辑 |
| WebView2 用户数据 | `%LocalAppData%\ChatHub\WebView2\` | Cookie / 登录态 / 缓存，站点页与临时标签页共用 |

### sites.txt 格式

```
# ChatHub 站点清单（Tab 分隔，可直接编辑；以 # 开头的行会被忽略）
# id	name	url
doubao01	豆包	https://www.doubao.com/chat/
deepseek	DeepSeek	https://chat.deepseek.com/
```

手工编辑时的规则：

- 以 `#` 开头的行、空行会被忽略。
- 支持三种写法：`id 名称 网址`、`名称 网址`（自动补 id）、整行只有一个网址（自动补 id 与名称）。
- 缺少网址 scheme 会自动补 `https://`；只接受 `http` / `https`。
- 网址无效的行会被跳过，跳过行数会提示出来。
- 名称留空时用域名兜底；清单顺序 = 工具栏显示顺序。

## 项目结构

```
src/ChatHub.csproj            应用工程（WPF, net10.0-windows）
src/App.xaml(.cs)             启动、全局异常兜底
src/MainWindow.xaml(.cs)      主窗口：工具栏 + 临时标签条 + 内容区
src/Models/Site.cs            站点模型
src/Services/AppPaths.cs      AppData / LocalAppData 路径定义
src/Services/UrlNormalizer.cs 网址规范化与校验（可单测）
src/Services/SiteStore.cs     sites.txt 读写（原子写入，可单测）
src/Services/WebViewRuntime.cs 共享 WebView2 环境、活动视图与通用事件接线
src/Services/WebViewPool.cs   每站点一个 WebView2 的创建 / 复用 / 释放
src/Services/InAppTabHost.cs  临时标签页的创建 / 释放与新窗口接管
src/Services/NewWindowPolicy.cs 新窗口路由策略（可单测）
src/Models/InAppTab.cs        临时标签页模型
src/ViewModels/               视图模型（手写轻量 MVVM，无第三方依赖）
src/Views/                    站点管理窗口、编辑对话框
src/tests/ChatHub.Tests/      MSTest 单元测试（72 个）
```

依赖仅一个 NuGet 包：`Microsoft.Web.WebView2`。

## 已知限制

- 只支持 Windows（WPF + WebView2）。
- 新窗口链接在应用内以临时标签页打开：标签页不持久化（关闭应用即消失），最多同时 12 个，`mailto:`、`file:`、自定义协议等非 http(s) 请求会被拦截。
- 临时标签页与站点页共用同一个 WebView2 用户数据目录和默认 Profile，因此 Cookie / 登录态共享；不是相互隔离的浏览器会话。
- 每个打开过的站点保留一个 WebView2 实例，每个临时标签页也各自占用一个 WebView2，内存占用会随打开过的页面数增长（这是保留会话与标签页的代价）。
- 暂不支持站点排序、导入导出、分组、标签页拖拽排序或会话恢复。
