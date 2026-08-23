using System.Collections.ObjectModel;
using System.Windows;
using System.IO;
using SAM.Core;
using SAM.Core.Steam;
using SAM.Core.Tasks;
using SAM.Infrastructure.Data;
using SAM.Infrastructure.Logging;
using SAM.Infrastructure.Steam;
using SAM.PluginHost;
using Serilog;

namespace SAM.Desktop;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Account> _accounts = [];
    private readonly ObservableCollection<SamTaskRecord> _tasks = [];
    private readonly ObservableCollection<PluginDisplay> _plugins = [];
    private readonly PluginLoader _pluginLoader = new();
    private readonly ExclusiveOperationGate _loginGate = new();
    private readonly SteamClientOptions _steamClientOptions = new();
    private readonly WorkerPool _pool;
    private readonly SamDatabase _database;
    private readonly SqliteTaskStore _taskStore;
    private readonly SamTaskCenter _taskCenter;
    private readonly ILogger _log;
    private const int TaskHistoryPageSize = 200;
    private int _taskHistoryOffset;
    private CancellationTokenSource? _loginCancellation;

    public MainWindow()
    {
        InitializeComponent();
        _pool = new WorkerPool(new SteamClientFactory().Create(_steamClientOptions));
        var appDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SAM");
        _database = new SamDatabase(Path.Combine(appDirectory, "sam.db"));
        _taskStore = new SqliteTaskStore(Path.Combine(appDirectory, "sam.db"));
        _taskCenter = new SamTaskCenter(_taskStore);
        _taskCenter.TaskChanged += TaskCenter_TaskChanged;
        _log = SamLog.Create(Path.Combine(appDirectory, "logs"));
        AccountsGrid.ItemsSource = _accounts;
        TasksGrid.ItemsSource = _tasks;
        PluginsGrid.ItemsSource = _plugins;
        Loaded += MainWindow_Loaded;
        Closed += (_, _) =>
        {
            _loginCancellation?.Cancel();
            var unloadReport = _pluginLoader.Unload();
            if (unloadReport.Failures.Count > 0)
                _log.Warning("Failed to unload {FailureCount} plugins during shutdown", unloadReport.Failures.Count);
            (_log as IDisposable)?.Dispose();
        };
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _database.InitializeAsync();
            await _taskStore.InitializeAsync();
            foreach (var account in await _database.GetAccountsAsync()) _accounts.Add(account);
            await RefreshTasksAsync();
            var clientMode = _steamClientOptions.EffectiveMode == SteamClientMode.Fake ? "模拟客户端" : "SteamKit 客户端";
            StatusText.Text = $"就绪：{clientMode}，已加载 {_accounts.Count} 个模拟账号";
            _log.Information("SAM desktop initialized with {AccountCount} accounts using {SteamClientMode}", _accounts.Count, _steamClientOptions.EffectiveMode);
        }
        catch (Exception exception)
        {
            _log.Error(exception, "Desktop initialization failed");
            StatusText.Text = $"初始化错误：{exception.Message}";
        }
    }

    private async Task GenerateAsync(int count)
    {
        _accounts.Clear();
        for (var i = 1; i <= count; i++)
            _accounts.Add(new Account {
                AccountName = $"mock_{i:0000}",
                SteamId = $"7656119{Random.Shared.NextInt64(10000000000, 99999999999)}"
            });
        await _database.ReplaceAccountsAsync(_accounts);
        _log.Information("Generated and persisted {AccountCount} fake accounts", count);
        StatusText.Text = $"已生成 {count} 个模拟账号";
    }

    private async void Generate100_Click(object sender, RoutedEventArgs e) => await GenerateAsync(100);
    private async void Generate500_Click(object sender, RoutedEventArgs e) => await GenerateAsync(500);

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        var loginLease = _loginGate.TryEnter();
        if (loginLease is null)
        {
            StatusText.Text = "已有登录批次正在运行";
            return;
        }

        using (loginLease)
        {
            if (!int.TryParse(ConcurrencyBox.Text, out var concurrency)) concurrency = WorkerPool.MaximumConcurrency;
            concurrency = WorkerPool.NormalizeConcurrency(concurrency);
            ConcurrencyBox.Text = concurrency.ToString();
            if (_accounts.Count == 0) { StatusText.Text = "请先生成模拟账号"; return; }
            _loginCancellation = new CancellationTokenSource();
            LoginButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            StatusText.Text = $"运行中：{_accounts.Count} 个账号，并发 {concurrency}";
            try
            {
                await _pool.RunLoginBatchAsync(_accounts, concurrency, _ =>
                    Dispatcher.BeginInvoke(AccountsGrid.Items.Refresh), _loginCancellation.Token, new RetryPolicy(), _taskCenter);
                await Task.WhenAll(_accounts.Select(_database.SaveAccountAsync));
                await RefreshTasksAsync();
                StatusText.Text = $"完成：在线 {_accounts.Count(a => a.Status == AccountStatus.Online)} / {_accounts.Count}";
                _log.Information("Login batch completed with {OnlineCount} online accounts", _accounts.Count(a => a.Status == AccountStatus.Online));
            }
            catch (Exception ex) { _log.Error(ex, "Login batch failed"); StatusText.Text = $"错误：{ex.Message}"; }
            finally
            {
                CancelButton.IsEnabled = false;
                LoginButton.IsEnabled = true;
                _loginCancellation?.Dispose();
                _loginCancellation = null;
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _loginCancellation?.Cancel();
        StatusText.Text = "正在取消任务…";
    }

    private async Task RefreshTasksAsync()
    {
        _tasks.Clear();
        _taskHistoryOffset = 0;
        await LoadTaskHistoryPageAsync(reset: true);
    }

    private async void LoadMoreTasks_Click(object sender, RoutedEventArgs e) => await LoadTaskHistoryPageAsync();

    private async Task LoadTaskHistoryPageAsync(bool reset = false)
    {
        LoadMoreTasksButton.IsEnabled = false;
        var page = await _taskStore.GetPageAsync(_taskHistoryOffset, TaskHistoryPageSize);
        foreach (var task in page.Where(task => _tasks.All(existing => existing.Id != task.Id))) _tasks.Add(task);
        _taskHistoryOffset += page.Count;
        LoadMoreTasksButton.IsEnabled = page.Count == TaskHistoryPageSize;
        if (!reset) _log.Information("Loaded {TaskCount} additional task history records at offset {TaskOffset}", page.Count, _taskHistoryOffset);
    }

    private void TaskCenter_TaskChanged(object? sender, SamTaskUpdate update)
    {
        _log.ForContext("TaskId", update.Id)
            .ForContext("AccountId", update.AccountId)
            .ForContext("TaskType", update.TaskType)
            .ForContext("TaskStatus", update.Status)
            .ForContext("RetryCount", update.RetryCount)
            .ForContext("TaskMessage", update.Message)
            .Information("Task state changed");
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(() => UpsertTask(update));
    }

    private void UpsertTask(SamTaskUpdate update)
    {
        var existing = _tasks.FirstOrDefault(task => task.Id == update.Id);
        if (existing is null)
        {
            _tasks.Insert(0, update.ToRecord());
            return;
        }

        existing.Status = update.Status;
        existing.RetryCount = update.RetryCount;
        existing.Message = update.Message;
        existing.StartedAt = update.StartedAt;
        existing.CompletedAt = update.CompletedAt;
        existing.UpdatedAt = update.UpdatedAt;
        TasksGrid.Items.Refresh();
    }

    private void LoadPlugins_Click(object sender, RoutedEventArgs e)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SAM", "plugins");
        var unloadReport = _pluginLoader.Unload();
        if (unloadReport.Failures.Count > 0)
            _log.Warning("Failed to unload {FailureCount} plugins before reloading", unloadReport.Failures.Count);
        var report = _pluginLoader.LoadWithReport(directory);
        _plugins.Clear();
        foreach (var plugin in report.Plugins) _plugins.Add(new(plugin.Id, plugin.Name, plugin.Version.ToString(), "Loaded", ""));
        foreach (var failure in report.Failures)
        {
            var hash = File.Exists(failure.AssemblyPath) ? PluginTrustPolicy.CalculateHash(failure.AssemblyPath) : "";
            _plugins.Add(new(Path.GetFileName(failure.AssemblyPath), "", "", failure.Message, hash));
        }
        StatusText.Text = $"插件：已加载 {report.Plugins.Count}，失败 {report.Failures.Count}";
        _log.Information("Loaded {PluginCount} plugins with {FailureCount} failures", report.Plugins.Count, report.Failures.Count);
    }

    private void CopySelectedPluginHash_Click(object sender, RoutedEventArgs e)
    {
        if (PluginsGrid.SelectedItem is not PluginDisplay { Hash.Length: > 0 } plugin) return;
        Clipboard.SetText(plugin.Hash);
        StatusText.Text = $"已复制 {plugin.Id} 的 SHA-256；审查后写入 trusted-plugins.sha256";
    }
}

public sealed record PluginDisplay(string Id, string Name, string Version, string Status, string Hash);
