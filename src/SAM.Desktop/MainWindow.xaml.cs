using System.Collections.ObjectModel;
using System.Windows;
using System.IO;
using SAM.Core;
using SAM.Core.Tasks;
using SAM.Infrastructure.Data;
using SAM.Infrastructure.Logging;
using Serilog;

namespace SAM.Desktop;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Account> _accounts = [];
    private readonly ObservableCollection<SamTaskRecord> _tasks = [];
    private readonly WorkerPool _pool = new(new FakeSteamClient());
    private readonly SamDatabase _database;
    private readonly SqliteTaskStore _taskStore;
    private readonly SamTaskCenter _taskCenter;
    private readonly ILogger _log;
    private CancellationTokenSource? _loginCancellation;

    public MainWindow()
    {
        InitializeComponent();
        var appDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SAM");
        _database = new SamDatabase(Path.Combine(appDirectory, "sam.db"));
        _taskStore = new SqliteTaskStore(Path.Combine(appDirectory, "sam.db"));
        _taskCenter = new SamTaskCenter(_taskStore);
        _log = SamLog.Create(Path.Combine(appDirectory, "logs"));
        AccountsGrid.ItemsSource = _accounts;
        TasksGrid.ItemsSource = _tasks;
        Loaded += MainWindow_Loaded;
        Closed += (_, _) => { _loginCancellation?.Cancel(); (_log as IDisposable)?.Dispose(); };
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _database.InitializeAsync();
            await _taskStore.InitializeAsync();
            foreach (var account in await _database.GetAccountsAsync()) _accounts.Add(account);
            await RefreshTasksAsync();
            StatusText.Text = $"就绪：已加载 {_accounts.Count} 个模拟账号";
            _log.Information("SAM desktop initialized with {AccountCount} accounts", _accounts.Count);
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
        await Task.WhenAll(_accounts.Select(_database.SaveAccountAsync));
        _log.Information("Generated and persisted {AccountCount} fake accounts", count);
        StatusText.Text = $"已生成 {count} 个模拟账号";
    }

    private async void Generate100_Click(object sender, RoutedEventArgs e) => await GenerateAsync(100);
    private async void Generate500_Click(object sender, RoutedEventArgs e) => await GenerateAsync(500);

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ConcurrencyBox.Text, out var concurrency)) concurrency = 10;
        if (_accounts.Count == 0) { StatusText.Text = "请先生成模拟账号"; return; }
        _loginCancellation = new CancellationTokenSource();
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
        finally { CancelButton.IsEnabled = false; _loginCancellation?.Dispose(); _loginCancellation = null; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _loginCancellation?.Cancel();
        StatusText.Text = "正在取消任务…";
    }

    private async Task RefreshTasksAsync()
    {
        _tasks.Clear();
        foreach (var task in await _taskStore.GetRecentAsync(200)) _tasks.Add(task);
    }
}
