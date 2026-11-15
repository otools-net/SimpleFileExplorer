namespace Otools.Net.Sfe.Client.Sample;

/// <summary>
/// The sample's command line: <c>Sfe.Client.Sample &lt;server address&gt; &lt;token&gt; [folder]</c>, or the address
/// and token in <c>SFE_URL</c> and <c>SFE_TOKEN</c>.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var address = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("SFE_URL");
        var token = args.Length > 1 ? args[1] : Environment.GetEnvironmentVariable("SFE_TOKEN");
        var folder = args.Length > 2 ? args[2] : SampleRun.DefaultFolder;
        if (string.IsNullOrEmpty(address) || string.IsNullOrEmpty(token))
        {
            Console.Error.WriteLine("Usage: Sfe.Client.Sample <server address> <token> [folder of the library]");
            Console.Error.WriteLine("       (or SFE_URL and SFE_TOKEN; a token is made in My account › Access tokens)");
            return 2;
        }

        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        using var sfe = new SfeClient(new Uri(address), token);
        try
        {
            await SampleRun.RunAsync(sfe, folder, Console.Out, cancel.Token);
            return 0;
        }
        catch (SfeException error)
        {
            Console.Error.WriteLine($"The server refused: {error.Message}");
            return 1;
        }
    }
}
