# SAM

Windows WPF 桌面端的 Steam 账号任务管理基础架构。默认使用 `FakeSteamClient`；本项目不会接收、存储或提交 Steam 密码、Cookie、令牌或 Steam Guard Secret。

## 构建
PowerShell:
`powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1`

## 运行桌面端
`dotnet run --project .\src\SAM.Desktop\SAM.Desktop.csproj`

默认界面为模拟模式，可生成模拟账号并以最高 10 并发运行任务。这个路径不需要也不应输入真实 Steam 凭据。

## 发布产物

运行构建脚本后，生成的 Windows 自包含文件位于：

- 桌面端：`artifacts\SAM\SAM.Desktop.exe`
- 本地认证 Broker：`artifacts\SAM.SteamBroker\SAM.SteamBroker.exe`
- 完整性清单：`artifacts\SHA256SUMS.txt`

可以先用 `Get-FileHash` 对照 SHA-256 清单，再启动程序。

也可以直接执行：

`pwsh -NoProfile -File .\scripts\verify-artifacts.ps1 -ArtifactDirectory .\artifacts`

## 可选：单账号本地 Broker 测试

这是一个需要用户本人在本机完成的手动冒烟测试，不会被自动化执行。

1. 启动 `SAM.SteamBroker.exe`，保持控制台窗口打开；默认管道名为 `sam-steam-auth`。
2. 启动桌面端，选择“外部认证代理”，确认默认管道名一致后点击“应用认证设置”。
3. 先点击“测试代理连接”。这一步不会提交账号名，也不会请求任何凭据。
4. 在“单账号真实测试”中填写账号名并确认保存。它会替换本地模拟账号列表，且并发固定为 1。
5. 点击“单账号登录测试”。只有这时 Broker 控制台才会请求输入；凭据仅停留在 Broker 进程内存中，SAM 桌面端不会读取或保存它们。

不要将任何凭据粘贴到聊天、源代码、配置文件、日志或 Git 提交中。测试结束后关闭 Broker 控制台，并在桌面端切回“模拟客户端”。

## 已完成的基础能力

- SQLite 账号与 Task Center 持久化
- Serilog 结构化文件日志
- Retry、Timeout、Cancellation 和任务历史
- 受信任哈希清单保护的插件加载与受限 PluginHost 元数据协议
- GitHub Actions Windows 发布、校验和与测试结果产物

详见 [DEVELOPMENT_STATUS.md](DEVELOPMENT_STATUS.md) 和 [PLUGIN_SECURITY.md](PLUGIN_SECURITY.md)。
