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
    private readonly ExclusiveOperationGate _accountOperationGate = new();
    private readonly ExclusiveOperationGate _taskHistoryOperationGate = new();
    private SteamClientOptions _steamClientOptions = new();
    private WorkerPool _pool;
    private readonly SamDatabase _database;
    private readonly SqliteTaskStore _taskStore;
    private readonly SamTaskCenter _taskCenter;
    private readonly ILogger _log;
    private const int TaskHistoryPageSize = 200;
    private const int TaskHistoryRetentionDays = 90;
    private SamTaskHistoryCursor? _taskHistoryCursor;
    private bool _taskHistoryRefreshPending;
    private CancellationTokenSource? _loginCancellation;
    private bool _externalBrokerEnabled;

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
            if (RealAccountTestPolicy.TryGetSingleExternalTestAccountName(_accounts, out var accountName))
                RealAccountNameBox.Text = accountName;
            await RefreshTasksAsync();
            var clientMode = _steamClientOptions.EffectiveMode == SteamClientMode.Fake ? "模拟客户端" : "SteamKit 客户端";
            EnvironmentText.Text = "   模拟环境";
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
        var operationLease = _accountOperationGate.TryEnter();
        if (operationLease is null)
        {
            StatusText.Text = "已有账号操作正在运行";
            return;
        }

        using (operationLease)
        {
            var generatedAccounts = Enumerable.Range(1, count).Select(i => new Account
            {
                AccountName = $"mock_{i:0000}",
                SteamId = $"7656119{Random.Shared.NextInt64(10000000000, 99999999999)}"
            }).ToArray();
            SetAccountOperationControls(isEnabled: false);
            try
            {
                await _database.ReplaceAccountsAsync(generatedAccounts);
                _accounts.Clear();
                foreach (var account in generatedAccounts) _accounts.Add(account);
                _log.Information("Generated and persisted {AccountCount} fake accounts", count);
                StatusText.Text = $"已生成 {count} 个模拟账号";
            }
            catch (Exception exception)
            {
                _log.Error(exception, "Failed to generate simulated accounts");
                StatusText.Text = $"生成错误：{exception.Message}";
            }
            finally { SetAccountOperationControls(isEnabled: true); }
        }
    }

    private async void Generate100_Click(object sender, RoutedEventArgs e) => await GenerateAsync(100);
    private async void Generate500_Click(object sender, RoutedEventArgs e) => await GenerateAsync(500);

    private async void DeleteSelectedAccount_Click(object sender, RoutedEventArgs e)
    {
        if (AccountsGrid.SelectedItem is not Account selectedAccount)
        {
            StatusText.Text = "请先在账号列表中选中要删除的账号";
            return;
        }

        var confirmation = MessageBox.Show(
            $"将从本机账号列表删除“{selectedAccount.AccountName}”。Task Center 历史不会被删除。是否继续？",
            "确认删除账号",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        var operationLease = _accountOperationGate.TryEnter();
        if (operationLease is null)
        {
            StatusText.Text = "已有账号操作正在运行";
            return;
        }

        using (operationLease)
        {
            SetAccountOperationControls(isEnabled: false);
            try
            {
                if (!await _database.DeleteAccountAsync(selectedAccount.Id))
                {
                    StatusText.Text = "账号已不在本机列表中";
                    return;
                }

                _accounts.Remove(selectedAccount);
                AccountsGrid.SelectedItem = null;
                if (!RealAccountTestPolicy.TryGetSingleExternalTestAccountName(_accounts, out var accountName))
                    RealAccountNameBox.Clear();
                else
                    RealAccountNameBox.Text = accountName;
                StatusText.Text = $"已删除账号：{selectedAccount.AccountName}";
                _log.Information("Deleted one account from the local account snapshot ({AccountCount} accounts remain)", _accounts.Count);
            }
            catch (Exception exception)
            {
                _log.Error(exception, "Failed to delete one account from the local account snapshot");
                StatusText.Text = $"删除账号错误：{exception.Message}";
            }
            finally { SetAccountOperationControls(isEnabled: true); }
        }
    }

    private async void ClearAccounts_Click(object sender, RoutedEventArgs e)
    {
        if (_accounts.Count == 0)
        {
            StatusText.Text = "账号列表已经为空";
            return;
        }

        var confirmation = MessageBox.Show(
            $"将清空本机保存的 {_accounts.Count} 个账号。Task Center 历史不会被删除。是否继续？",
            "确认清空账号列表",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        var operationLease = _accountOperationGate.TryEnter();
        if (operationLease is null)
        {
            StatusText.Text = "已有账号操作正在运行";
            return;
        }

        using (operationLease)
        {
            SetAccountOperationControls(isEnabled: false);
            try
            {
                var clearedCount = await _database.ClearAccountsAsync();
                _accounts.Clear();
                AccountsGrid.SelectedItem = null;
                RealAccountNameBox.Clear();
                StatusText.Text = $"已清空本机账号列表：{clearedCount} 个账号";
                _log.Information("Cleared {AccountCount} accounts from the local account snapshot", clearedCount);
            }
            catch (Exception exception)
            {
                _log.Error(exception, "Failed to clear the local account snapshot");
                StatusText.Text = $"清空账号列表错误：{exception.Message}";
            }
            finally { SetAccountOperationControls(isEnabled: true); }
        }
    }

    private async Task ReplaceAccountSnapshotAsync(
        IReadOnlyCollection<Account> accounts,
        string successMessage,
        string logMessage)
    {
        var operationLease = _accountOperationGate.TryEnter();
        if (operationLease is null)
        {
            StatusText.Text = "已有账号操作正在运行";
            return;
        }

        using (operationLease)
        {
            SetAccountOperationControls(isEnabled: false);
            try
            {
                await _database.ReplaceAccountsAsync(accounts);
                _accounts.Clear();
                foreach (var account in accounts) _accounts.Add(account);
                AccountsGrid.SelectedItem = null;
                if (accounts.Count == 0) RealAccountNameBox.Clear();
                else if (RealAccountTestPolicy.TryGetSingleExternalTestAccountName(accounts, out var accountName))
                    RealAccountNameBox.Text = accountName;
                StatusText.Text = successMessage;
                _log.Information("{AccountSnapshotOperation} ({AccountCount} accounts remain)", logMessage, accounts.Count);
            }
            catch (Exception exception)
            {
                _log.Error(exception, "Failed to replace the local account snapshot");
                StatusText.Text = $"账号列表更新错误：{exception.Message}";
            }
            finally { SetAccountOperationControls(isEnabled: true); }
        }
    }

    private void ClientModeBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (BrokerPipeNameBox is null || TestBrokerButton is null || RealAccountNameBox is null) return;
        BrokerPipeNameBox.IsEnabled = ClientModeBox.SelectedIndex == 1 && _loginCancellation is null;
        TestBrokerButton.IsEnabled = ClientModeBox.SelectedIndex == 1 && _loginCancellation is null;
        SetSingleAccountTestControls(ClientModeBox.SelectedIndex == 1 && _externalBrokerEnabled && _loginCancellation is null);
    }

    private void ApplyClientMode_Click(object sender, RoutedEventArgs e)
    {
        if (_loginCancellation is not null)
        {
            StatusText.Text = "任务运行中，无法更改认证客户端";
            return;
        }

        var factory = new SteamClientFactory();
        if (ClientModeBox.SelectedIndex != 1)
        {
            _steamClientOptions = new SteamClientOptions();
            _pool = new WorkerPool(factory.Create(_steamClientOptions));
            _externalBrokerEnabled = false;
            EnvironmentText.Text = "   模拟环境";
            LoginButton.Content = "批量模拟登录";
            BrokerPipeNameBox.IsEnabled = false;
            TestBrokerButton.IsEnabled = false;
            SetSingleAccountTestControls(isEnabled: false);
            StatusText.Text = "已应用模拟客户端";
            _log.Information("Desktop switched to the safe fake Steam client");
            return;
        }

        try
        {
            var confirmation = MessageBox.Show(
                "外部认证代理只会连接当前用户的本地管道。请先手动启动 SAM.SteamBroker；SAM 不会接收或保存密码、Cookie、令牌或 Steam Guard Secret。是否启用？",
                "启用外部认证代理",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes)
            {
                ClientModeBox.SelectedIndex = _externalBrokerEnabled ? 1 : 0;
                return;
            }

            var pipeName = SteamAuthenticationBrokerEndpoint.ValidatePipeName(BrokerPipeNameBox.Text);
            _steamClientOptions = new SteamClientOptions(SteamClientMode.SteamKit, EnableSteamKit: true);
            _pool = new WorkerPool(factory.CreateWithExternalBroker(_steamClientOptions, pipeName));
            _externalBrokerEnabled = true;
            EnvironmentText.Text = "   外部认证代理";
            LoginButton.Content = "单账号登录测试";
            BrokerPipeNameBox.IsEnabled = true;
            TestBrokerButton.IsEnabled = true;
            SetSingleAccountTestControls(isEnabled: true);
            StatusText.Text = $"已应用外部认证代理：{pipeName}";
            _log.Information("Desktop enabled the explicit external Steam authentication broker with pipe {BrokerPipeName}", pipeName);
        }
        catch (Exception exception)
        {
            ClientModeBox.SelectedIndex = _externalBrokerEnabled ? 1 : 0;
            BrokerPipeNameBox.IsEnabled = _externalBrokerEnabled;
            StatusText.Text = "外部认证代理设置无效，现有认证客户端未改变";
            _log.Warning(exception, "External Steam authentication broker configuration was rejected");
        }
    }

    private async void SaveSingleAccount_Click(object sender, RoutedEventArgs e)
    {
        if (!_externalBrokerEnabled || _loginCancellation is not null)
        {
            StatusText.Text = "请先应用外部认证代理设置";
            return;
        }

        string accountName;
        try { accountName = RealAccountTestPolicy.ValidateAccountName(RealAccountNameBox.Text); }
        catch (ArgumentException exception)
        {
            StatusText.Text = $"账号名无效：{exception.Message}";
            return;
        }

        var confirmation = MessageBox.Show(
            "这会替换本机保存的账号列表，仅保留一个用于外部认证代理测试的账号名。SAM 不会接收或保存密码、Cookie、令牌或 Steam Guard Secret。是否继续？",
            "确认单账号真实测试",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        var operationLease = _accountOperationGate.TryEnter();
        if (operationLease is null)
        {
            StatusText.Text = "已有账号操作正在运行";
            return;
        }

        using (operationLease)
        {
            SetAccountOperationControls(isEnabled: false);
            try
            {
                var account = new Account { AccountName = accountName };
                await _database.ReplaceAccountsAsync([account]);
                _accounts.Clear();
                _accounts.Add(account);
                ConcurrencyBox.Text = "1";
                StatusText.Text = "已保存单账号测试；先测试认证代理连接，再点击“单账号登录测试”";
                _log.Information("Replaced local account snapshot with one external-broker test account");
            }
            catch (Exception exception)
            {
                _log.Error(exception, "Failed to save the external-broker test account");
                StatusText.Text = $"保存错误：{exception.Message}";
            }
            finally { SetAccountOperationControls(isEnabled: true); }
        }
    }

    private async void TestBroker_Click(object sender, RoutedEventArgs e)
    {
        if (_loginCancellation is not null)
        {
            StatusText.Text = "任务运行中，无法测试认证代理";
            return;
        }

        TestBrokerButton.IsEnabled = false;
        try
        {
            var pipeName = SteamAuthenticationBrokerEndpoint.ValidatePipeName(BrokerPipeNameBox.Text);
            var connected = await new NamedPipeSteamAuthenticationBroker(pipeName, TimeSpan.FromSeconds(2)).ProbeAsync();
            StatusText.Text = connected
                ? "认证代理连接正常；未发起登录，也未请求凭据"
                : "无法连接认证代理；请确认 SAM.SteamBroker 正在运行且管道名称一致";
            _log.Information("External Steam authentication broker connectivity probe returned {Connected} for pipe {BrokerPipeName}", connected, pipeName);
        }
        catch (Exception exception)
        {
            StatusText.Text = "认证代理设置无效";
            _log.Warning(exception, "External Steam authentication broker connectivity probe was rejected");
        }
        finally
        {
            TestBrokerButton.IsEnabled = ClientModeBox.SelectedIndex == 1;
        }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if ((ClientModeBox.SelectedIndex == 1) != _externalBrokerEnabled)
        {
            StatusText.Text = "认证客户端设置已变更，请先点击“应用认证设置”";
            return;
        }

        if (_externalBrokerEnabled)
        {
            try { RealAccountTestPolicy.EnsureSingleExternalTestAccount(_accounts); }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                StatusText.Text = "外部认证代理仅允许一个已明确保存的非 mock 账号；请使用“保存单账号测试”";
                _log.Warning(exception, "Blocked external authentication broker login because the account selection was unsafe");
                return;
            }
        }

        var loginLease = _accountOperationGate.TryEnter();
        if (loginLease is null)
        {
            StatusText.Text = "已有账号操作正在运行";
            return;
        }

        using (loginLease)
        {
            if (!int.TryParse(ConcurrencyBox.Text, out var concurrency)) concurrency = WorkerPool.MaximumConcurrency;
            concurrency = _externalBrokerEnabled ? 1 : WorkerPool.NormalizeConcurrency(concurrency);
            ConcurrencyBox.Text = concurrency.ToString();
            if (_accounts.Count == 0) { StatusText.Text = "请先生成模拟账号或保存单账号测试"; return; }
            _loginCancellation = new CancellationTokenSource();
            SetAccountOperationControls(isEnabled: false);
            CancelButton.IsEnabled = true;
            StatusText.Text = $"运行中：{_accounts.Count} 个账号，并发 {concurrency}";
            try
            {
                var retryPolicy = _externalBrokerEnabled
                    ? RealAccountTestPolicy.CreateInteractiveLoginRetryPolicy()
                    : new RetryPolicy();
                await _pool.RunLoginBatchAsync(_accounts, concurrency, _ =>
                    Dispatcher.BeginInvoke(AccountsGrid.Items.Refresh), _loginCancellation.Token, retryPolicy, _taskCenter);
                await Task.WhenAll(_accounts.Select(_database.SaveAccountAsync));
                await RefreshTasksAsync();
                StatusText.Text = $"完成：在线 {_accounts.Count(a => a.Status == AccountStatus.Online)} / {_accounts.Count}";
                _log.Information("Login batch completed with {OnlineCount} online accounts", _accounts.Count(a => a.Status == AccountStatus.Online));
            }
            catch (Exception ex) { _log.Error(ex, "Login batch failed"); StatusText.Text = $"错误：{ex.Message}"; }
            finally
            {
                CancelButton.IsEnabled = false;
                SetAccountOperationControls(isEnabled: true);
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

    private void SetAccountOperationControls(bool isEnabled)
    {
        Generate100Button.IsEnabled = isEnabled;
        Generate500Button.IsEnabled = isEnabled;
        DeleteSelectedAccountButton.IsEnabled = isEnabled;
        ClearAccountsButton.IsEnabled = isEnabled;
        LoginButton.IsEnabled = isEnabled;
        ConcurrencyBox.IsEnabled = isEnabled;
        ClientModeBox.IsEnabled = isEnabled;
        ApplyClientModeButton.IsEnabled = isEnabled;
        BrokerPipeNameBox.IsEnabled = isEnabled && ClientModeBox.SelectedIndex == 1;
        TestBrokerButton.IsEnabled = isEnabled && ClientModeBox.SelectedIndex == 1;
        SetSingleAccountTestControls(isEnabled && ClientModeBox.SelectedIndex == 1 && _externalBrokerEnabled);
    }

    private void SetSingleAccountTestControls(bool isEnabled)
    {
        RealAccountNameBox.IsEnabled = isEnabled;
        SaveSingleAccountButton.IsEnabled = isEnabled;
    }

    private async Task RefreshTasksAsync()
    {
        var operationLease = _taskHistoryOperationGate.TryEnter();
        if (operationLease is null)
        {
            _taskHistoryRefreshPending = true;
            return;
        }

        using (operationLease)
        {
            SetTaskHistoryControls(isEnabled: false);
            try
            {
                _taskHistoryRefreshPending = false;
                await RefreshTasksCoreAsync();
            }
            finally { SetTaskHistoryControls(isEnabled: true); }
        }
        await RefreshPendingTaskHistoryAsync();
    }

    private async Task RefreshTasksCoreAsync()
    {
        _tasks.Clear();
        _taskHistoryCursor = null;
        await LoadTaskHistoryPageAsync(reset: true);
    }

    private async void LoadMoreTasks_Click(object sender, RoutedEventArgs e)
    {
        var operationLease = _taskHistoryOperationGate.TryEnter();
        if (operationLease is null)
        {
            StatusText.Text = "任务历史操作正在运行";
            return;
        }

        using (operationLease)
        {
            SetTaskHistoryControls(isEnabled: false);
            try { await LoadTaskHistoryPageAsync(); }
            catch (Exception exception)
            {
                _log.Error(exception, "Failed to load additional task history");
                StatusText.Text = $"加载任务历史错误：{exception.Message}";
            }
            finally { SetTaskHistoryControls(isEnabled: true); }
        }
        await RefreshPendingTaskHistoryAsync();
    }

    private async void PruneTaskHistory_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = MessageBox.Show(
            $"将永久删除超过 {TaskHistoryRetentionDays} 天的已完成、失败或已取消任务历史。运行中和待执行任务不会受影响。是否继续？",
            "清理任务历史",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        var operationLease = _taskHistoryOperationGate.TryEnter();
        if (operationLease is null)
        {
            StatusText.Text = "任务历史操作正在运行";
            return;
        }

        using (operationLease)
        {
            SetTaskHistoryControls(isEnabled: false);
            try
            {
                var deleted = await _taskStore.PruneTerminalTasksAsync(DateTimeOffset.UtcNow.AddDays(-TaskHistoryRetentionDays));
                await RefreshTasksCoreAsync();
                StatusText.Text = $"已清理 {deleted} 条过期终态任务历史";
                _log.Information("Pruned {TaskCount} expired terminal task history records", deleted);
            }
            catch (Exception exception)
            {
                _log.Error(exception, "Failed to prune terminal task history");
                StatusText.Text = $"清理任务历史错误：{exception.Message}";
            }
            finally { SetTaskHistoryControls(isEnabled: true); }
        }
        await RefreshPendingTaskHistoryAsync();
    }

    private async Task LoadTaskHistoryPageAsync(bool reset = false)
    {
        var page = await _taskStore.GetPageAfterAsync(_taskHistoryCursor, TaskHistoryPageSize);
        foreach (var task in page.Tasks.Where(task => _tasks.All(existing => existing.Id != task.Id))) _tasks.Add(task);
        _taskHistoryCursor = page.NextCursor;
        if (!reset) _log.Information("Loaded {TaskCount} additional task history records using a stable cursor", page.Tasks.Count);
    }

    private void SetTaskHistoryControls(bool isEnabled)
    {
        PruneTaskHistoryButton.IsEnabled = isEnabled;
        LoadMoreTasksButton.IsEnabled = isEnabled && _taskHistoryCursor is not null;
    }

    private async Task RefreshPendingTaskHistoryAsync()
    {
        if (!_taskHistoryRefreshPending) return;
        _taskHistoryRefreshPending = false;
        await RefreshTasksAsync();
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
            PluginTrustPolicy.TryCalculateHash(failure.AssemblyPath, out var hash);
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
