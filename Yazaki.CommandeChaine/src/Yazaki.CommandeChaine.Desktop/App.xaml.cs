using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

using Microsoft.Extensions.DependencyInjection;
using Yazaki.CommandeChaine.Desktop.Services;
using Yazaki.CommandeChaine.Desktop.Views;

namespace Yazaki.CommandeChaine.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
	public static IServiceProvider Services { get; private set; } = null!;
	private static readonly string CrashLogPath = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"Yazaki",
		"CommandeChaine",
		"desktop-crash.log");

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		DispatcherUnhandledException += (_, args) =>
		{
			LogCrash("DispatcherUnhandledException", args.Exception);
			MessageBox.Show($"Erreur inattendue: {args.Exception.Message}\n\nLog: {CrashLogPath}",
				"Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
			args.Handled = true;
		};

		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			if (args.ExceptionObject is Exception ex)
			{
				LogCrash("UnhandledException", ex);
			}
		};

		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			LogCrash("UnobservedTaskException", args.Exception);
			args.SetObserved();
		};

		var services = new ServiceCollection();

		services.AddSingleton<ApiConfig>();
		services.AddSingleton(sp => new CommandeChaineApiClient(sp.GetRequiredService<ApiConfig>().BaseUrl));
		services.AddSingleton<RealtimeClient>();
		services.AddSingleton<ActiveChainState>();
		services.AddSingleton<HarnessStandardsService>();
		services.AddSingleton<HarnessQueueService>();
		services.AddSingleton<HarnessRegistry>();
		services.AddSingleton<FOImportService>();

		services.AddSingleton<MainWindow>();
		services.AddTransient<DashboardWindow>();
		services.AddTransient<ChainsPage>();
		services.AddTransient<ChainDetailPage>();
		services.AddTransient<SettingsPage>();
		services.AddTransient<FOInputPage>();
		
		// CableValidationWindow is registered as transient with factory for dynamic parameters
		services.AddTransient<CableValidationWindow>();

		Services = services.BuildServiceProvider();

		var mainWindow = Services.GetRequiredService<MainWindow>();
		MainWindow = mainWindow;
		mainWindow.Show();
	}

	private static void LogCrash(string source, Exception ex)
	{
		try
		{
			var dir = Path.GetDirectoryName(CrashLogPath);
			if (!string.IsNullOrWhiteSpace(dir))
			{
				Directory.CreateDirectory(dir);
			}

			var sb = new StringBuilder();
			sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {source}");
			sb.AppendLine(ex.ToString());
			sb.AppendLine();

			File.AppendAllText(CrashLogPath, sb.ToString(), Encoding.UTF8);
		}
		catch
		{
			// Last resort: swallow logging errors.
		}
	}
}


