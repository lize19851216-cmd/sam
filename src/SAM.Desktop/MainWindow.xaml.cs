using System.Collections.ObjectModel;
using System.Windows;
using SAM.Core;

namespace SAM.Desktop;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<Account> _accounts = [];
    private readonly WorkerPool _pool = new(new FakeSteamClient());

    public MainWindow()
    {
        InitializeComponent();
        AccountsGrid.ItemsSource = _accounts;
    }

    private void Generate(int count)
    {
        _accounts.Clear();
        for (var i = 1; i <= count; i++)
            _accounts.Add(new Account {
                AccountName = $"mock_{i:0000}",
                SteamId = $"7656119{Random.Shared.NextInt64(10000000000, 99999999999)}"
            });
        StatusText.Text = $"已生成 {count} 个模拟账号";
    }

    private void Generate100_Click(object sender, RoutedEventArgs e) => Generate(100);
    private void Generate500_Click(object sender, RoutedEventArgs e) => Generate(500);

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ConcurrencyBox.Text, out var concurrency)) concurrency = 10;
        StatusText.Text = $"运行中：{_accounts.Count} 个账号，并发 {concurrency}";
        try
        {
            await _pool.RunLoginBatchAsync(_accounts, concurrency, _ =>
                Dispatcher.Invoke(() => AccountsGrid.Items.Refresh()));
            StatusText.Text = $"完成：在线 {_accounts.Count(a => a.Status == AccountStatus.Online)} / {_accounts.Count}";
        }
        catch (Exception ex) { StatusText.Text = $"错误：{ex.Message}"; }
    }
}
