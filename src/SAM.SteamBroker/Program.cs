using SAM.Infrastructure.Steam;
using SteamKit2;
using System.Runtime.InteropServices;

var pipeName = args.Length == 1 ? args[0] : "sam-steam-auth";
try
{
    pipeName = SteamAuthenticationBrokerEndpoint.ValidatePipeName(pipeName);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

Console.WriteLine("SAM Steam authentication broker is waiting for local requests.");
Console.WriteLine("Credentials are requested only after a request arrives and are never written to disk.");
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    var configurator = new ConsoleSteamLogOnConfigurator();
    var transport = new SteamKitAuthenticationTransport(new SteamKitAuthenticationSessionFactory(configurator));
    var host = new SteamAuthenticationBrokerHost(transport);
    while (!cancellation.IsCancellationRequested)
    {
        await host.ServeOnceAsync(pipeName, cancellation.Token);
        Console.WriteLine("SAM Steam authentication broker completed a local request and is waiting for another.");
    }

    return 0;
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("SAM Steam authentication broker was cancelled.");
    return 1;
}
catch
{
    Console.Error.WriteLine("SAM Steam authentication broker stopped unexpectedly.");
    return 1;
}

internal sealed class ConsoleSteamLogOnConfigurator : IExternalSteamLogOnConfigurator
{
    public void Configure(SteamUser.LogOnDetails logOnDetails)
    {
        Console.Write($"Password for {logOnDetails.Username}: ");
        logOnDetails.Password = ReadSecret();
        Console.Write("Steam Guard one-time code (press Enter if none): ");
        var code = ReadSecret();
        if (!string.IsNullOrWhiteSpace(code)) logOnDetails.TwoFactorCode = code;
        logOnDetails.ShouldRememberPassword = false;
    }

    private static string ReadSecret()
    {
        var value = new List<char>();
        while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Count > 0) value.RemoveAt(value.Count - 1);
                continue;
            }

            if (!char.IsControl(key.KeyChar)) value.Add(key.KeyChar);
        }

        Console.WriteLine();
        try
        {
            return new string(CollectionsMarshal.AsSpan(value));
        }
        finally
        {
            CollectionsMarshal.AsSpan(value).Clear();
            value.Clear();
        }
    }
}
