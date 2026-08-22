# SAM Milestone 0

第一阶段完全使用 FakeSteamClient，不连接真实 Steam。

## 构建
PowerShell:
`powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1`

## 运行
`dotnet run --project .\src\SAM.Desktop\SAM.Desktop.csproj`

## M0 当前能力
- WPF GUI
- 100/500 模拟账号
- 可调并发 Worker Pool
- FakeSteamClient
- 成功/Steam Guard/限流/失败状态模拟
- xUnit 基础测试

## M0 第二阶段基础架构
- SQLite 账号基础表及 Task Center 持久化存储
- Serilog 文件日志（含应用属性与结构化属性输出）
- 任务状态、指数退避 Retry、每次执行 Timeout 与协作式 Cancellation
- `SAM.PluginHost` 插件发现报告与唯一 ID 注册表

仍仅使用 `FakeSteamClient`；不处理或存储真实 Steam 凭据、Cookie 或 Steam Guard Secret。
