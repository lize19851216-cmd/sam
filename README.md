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

下一轮：SQLite 持久化、结构化日志、重试策略、任务中心、插件 Host。
