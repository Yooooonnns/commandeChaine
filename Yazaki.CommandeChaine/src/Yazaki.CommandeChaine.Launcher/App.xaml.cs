using System;
using System.IO;
using System.Windows;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;

namespace Yazaki.CommandeChaine.Launcher;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
	private Process? _piProcess;
	private Process? _apiProcess;
	private SplashWindow? _splash;
	private string? _logFilePath;

	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		try
		{
			_logFilePath = InitLogFile();
			Log("Launcher start");

			_splash = new SplashWindow();
			MainWindow = _splash;
			_splash.Show();
			_splash.SetStatus("Vérification des services…");
			await Dispatcher.Yield(DispatcherPriority.Background);

			var settings = LauncherSettings.Load();
			var apiBaseUrl = settings.ApiBaseUrl;
			var raspberryPiApiUrl = settings.RaspberryPiApiUrl;
			var raspberryPiHealthPath = settings.RaspberryPiHealthPath;
			Log($"Settings: ApiBaseUrl={apiBaseUrl}, HealthPath={settings.HealthPath}, ApiDll={settings.ApiDllName}, DesktopExe={settings.DesktopExeName}");

			// Start Raspberry Pi API if available
			var piHealthUrl = BuildEndpointUrl(raspberryPiApiUrl, raspberryPiHealthPath);
			if (!await IsHealthyAsync(piHealthUrl, "", TimeSpan.FromSeconds(1)))
			{
				var canStartLocalPi = settings.RaspberryPiAutoStart && IsLocalhostUrl(raspberryPiApiUrl);
				if (canStartLocalPi)
				{
					_splash.SetStatus("Démarrage de Raspberry Pi…");
					await Dispatcher.Yield(DispatcherPriority.Background);

					if (IsPythonAvailable())
					{
						try
						{
							_piProcess = StartRaspberryPi();
							Log($"Raspberry Pi started. PID={_piProcess.Id}");

							var timeout = TimeSpan.FromSeconds(5);
							_splash.SetStatus("Initialisation Raspberry Pi…");
							var piStarted = await WaitHealthyAsync(piHealthUrl, "", timeout, status => _splash?.SetStatus(status));
							if (piStarted)
							{
								Log("Raspberry Pi healthy");
							}
							else
							{
								Log("WARNING: Raspberry Pi not healthy, continuing anyway");
							}
						}
						catch (Exception piEx)
						{
							Log($"WARNING: Raspberry Pi startup failed, continuing anyway: {piEx.Message}");
						}
					}
					else
					{
						Log("Python not available, skipping Raspberry Pi");
					}
				}
				else
				{
					Log($"Raspberry Pi is offline and local auto-start is disabled. URL={raspberryPiApiUrl}");
				}
			}
			else
			{
				_splash.SetStatus("Raspberry Pi détecté…");
				await Task.Delay(250);
			}

			// Check and start Yazaki API
			if (!await IsHealthyAsync(apiBaseUrl, settings.HealthPath, TimeSpan.FromSeconds(2)))
			{
				_splash.SetStatus("Démarrage de l'API…");
				await Dispatcher.Yield(DispatcherPriority.Background);

				var apiTarget = ResolveApiTarget(settings.ApiDllName);
				Log($"API target: {apiTarget}");
				_apiProcess = StartApi(apiTarget, apiBaseUrl, raspberryPiApiUrl);
				Log($"API started. PID={_apiProcess.Id}");

				var timeout = TimeSpan.FromSeconds(Math.Max(3, Math.Min(10, settings.StartupTimeoutSeconds)));
				_splash.SetStatus("Initialisation API…");
				var started = await WaitHealthyAsync(apiBaseUrl, settings.HealthPath, timeout, status => _splash?.SetStatus(status));
				if (!started)
				{
					throw new InvalidOperationException($"API did not become healthy within {timeout.TotalSeconds:0}s ({apiBaseUrl}{settings.HealthPath}).");
				}
				Log("API healthy");
			}
			else
			{
				_splash.SetStatus("API détectée (déjà démarrée)…");
				await Task.Delay(250);
			}

			// Launch Desktop and forward API base url.
			_splash.SetStatus("Ouverture de l'application…");
			await Dispatcher.Yield(DispatcherPriority.Background);

			var desktopTarget = ResolveDesktopTarget(settings.DesktopExeName);
			Log($"Desktop target: {desktopTarget}");
			var desktopProcess = StartDesktop(desktopTarget, apiBaseUrl);
			Log($"Desktop started. PID={desktopProcess.Id}");

			// If Desktop crashes instantly, keep splash and show error instead of disappearing.
			await Task.Delay(900);
			if (desktopProcess.HasExited)
			{
				throw new InvalidOperationException($"Desktop process exited immediately (code {desktopProcess.ExitCode}). See log: {_logFilePath}");
			}

			_splash.Close();
			_splash = null;

			// Close launcher window, keep message loop until Desktop exits.
			MainWindow?.Hide();
			await desktopProcess.WaitForExitAsync();
		}
		catch (Exception ex)
		{
			Log($"ERROR: {ex}");
			try
			{
				_splash?.Close();
			}
			catch { }

			var msg = _logFilePath is null ? ex.Message : $"{ex.Message}\n\nLog: {_logFilePath}";
			MessageBox.Show(msg, "Launcher error", MessageBoxButton.OK, MessageBoxImage.Error);
		}
		finally
		{
			try
			{
				if (_piProcess is { HasExited: false })
				{
					_piProcess.Kill(entireProcessTree: true);
				}
			}
			catch { }

			try
			{
				if (_apiProcess is { HasExited: false })
				{
					_apiProcess.Kill(entireProcessTree: true);
				}
			}
			catch { }

			Shutdown();
		}
	}

	private static Process StartApi(string apiTarget, string apiBaseUrl, string raspberryPiApiUrl)
	{
		var workDir = Path.GetDirectoryName(apiTarget) ?? AppContext.BaseDirectory;

		var psi = new ProcessStartInfo
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
			WorkingDirectory = workDir,
		};

		// If it's an EXE, run directly; otherwise run dll via dotnet.
		if (apiTarget.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			psi.FileName = apiTarget;
		}
		else
		{
			psi.FileName = "dotnet";
			psi.ArgumentList.Add(apiTarget);
		}

		psi.Environment["ASPNETCORE_URLS"] = apiBaseUrl;
		psi.Environment["YAZAKI_API_BASE_URL"] = apiBaseUrl;
		psi.Environment["RaspberryPi__ApiUrl"] = raspberryPiApiUrl;

		return Process.Start(psi) ?? throw new InvalidOperationException("Failed to start API process.");
	}

	private static string BuildEndpointUrl(string baseUrl, string endpointPath)
	{
		var root = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
		return new Uri(new Uri(root), endpointPath.TrimStart('/')).ToString();
	}

	private static bool IsLocalhostUrl(string url)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
		{
			return false;
		}

		var host = uri.Host;
		return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
	}

	private static Process StartDesktop(string desktopExeOrDll, string apiBaseUrl)
	{
		var workDir = Path.GetDirectoryName(desktopExeOrDll) ?? AppContext.BaseDirectory;

		var psi = new ProcessStartInfo
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
			WorkingDirectory = workDir,
		};

		if (desktopExeOrDll.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
		{
			psi.FileName = desktopExeOrDll;
			psi.CreateNoWindow = false;
			psi.WindowStyle = ProcessWindowStyle.Normal;
		}
		else
		{
			psi.FileName = "dotnet";
			psi.ArgumentList.Add(desktopExeOrDll);
		}

		psi.Environment["YAZAKI_API_BASE_URL"] = apiBaseUrl;
		return Process.Start(psi) ?? throw new InvalidOperationException("Failed to start Desktop process.");
	}

	private static bool IsPythonAvailable()
	{
		try
		{
			var psi = new ProcessStartInfo
			{
				FileName = "python",
				ArgumentList = { "--version" },
				UseShellExecute = false,
				RedirectStandardOutput = true,
				CreateNoWindow = true,
			};

			using var process = Process.Start(psi);
			process?.WaitForExit(3000);
			return process?.ExitCode == 0;
		}
		catch
		{
			return false;
		}
	}

	private static Process StartRaspberryPi()
	{
		var (workDir, pythonPath) = ResolveRaspberryPiWorkingDir();

		var psi = new ProcessStartInfo
		{
			FileName = "python",
			ArgumentList = { "-m", "raspberry_module.main" },
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
			WorkingDirectory = workDir,
		};

		if (!string.IsNullOrWhiteSpace(pythonPath))
		{
			psi.Environment["PYTHONPATH"] = pythonPath;
		}

		return Process.Start(psi) ?? throw new InvalidOperationException("Failed to start Raspberry Pi process.");
	}

	private static (string WorkDir, string? PythonPath) ResolveRaspberryPiWorkingDir()
	{
		var baseDir = AppContext.BaseDirectory;
		var repoRoot = FindRepoRoot(baseDir);

		// Layout in this workspace: <workspace>/RasberryPi/src/raspberry_module
		var workspacePiSrc = Path.GetFullPath(Path.Combine(repoRoot, "..", "RasberryPi", "src"));
		var workspacePiModule = Path.Combine(workspacePiSrc, "raspberry_module");
		if (Directory.Exists(workspacePiModule))
		{
			return (workspacePiSrc, null);
		}

		// Alternate layout: <repoRoot>/src/raspberry_module
		var moduleDir = Path.Combine(repoRoot, "src", "raspberry_module");
		if (Directory.Exists(moduleDir))
		{
			return (repoRoot, "src");
		}

		// Legacy relative layout fallback
		var legacyDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "..", "RasberryPi", "src"));
		if (Directory.Exists(legacyDir))
		{
			return (legacyDir, null);
		}

		throw new DirectoryNotFoundException(
			$"Raspberry module not found. Checked '{moduleDir}' and '{legacyDir}'.");
	}

	private static async Task<bool> WaitHealthyAsync(string apiBaseUrl, string healthPath, TimeSpan timeout, Action<string>? status)
	{
		var deadline = DateTimeOffset.UtcNow.Add(timeout);
		var lastShown = DateTimeOffset.MinValue;
		while (DateTimeOffset.UtcNow < deadline)
		{
			if (await IsHealthyAsync(apiBaseUrl, healthPath, TimeSpan.FromSeconds(2)))
			{
				return true;
			}

			if (DateTimeOffset.UtcNow - lastShown > TimeSpan.FromSeconds(2))
			{
				var remaining = Math.Max(0, (deadline - DateTimeOffset.UtcNow).TotalSeconds);
				status?.Invoke($"Initialisation… ({remaining:0}s)");
				lastShown = DateTimeOffset.UtcNow;
			}

			await Task.Delay(500);
		}

		return false;
	}

	private static string InitLogFile()
	{
		var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Yazaki", "CommandeChaine");
		Directory.CreateDirectory(dir);
		return Path.Combine(dir, "launcher.log");
	}

	private void Log(string message)
	{
		try
		{
			if (_logFilePath is null)
			{
				return;
			}

			File.AppendAllText(_logFilePath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
		}
		catch { }
	}

	private static async Task<bool> IsHealthyAsync(string apiBaseUrl, string healthPath, TimeSpan timeout)
	{
		try
		{
			using var http = new HttpClient { Timeout = timeout };
			var healthUrl = new Uri(new Uri(apiBaseUrl.EndsWith('/') ? apiBaseUrl : apiBaseUrl + "/"), healthPath.TrimStart('/'));
			var resp = await http.GetAsync(healthUrl);
			return resp.IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	private static string ResolveApiTarget(string apiDllName)
	{
		var baseDir = AppContext.BaseDirectory;
		var repoRoot = FindRepoRoot(baseDir);
		var apiExeName = Path.ChangeExtension(apiDllName, ".exe");

		var candidates = new[]
		{
			Path.Combine(baseDir, apiExeName),
			Path.Combine(baseDir, apiDllName),
			Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Yazaki.CommandeChaine.Api", "bin", "Release", "net9.0", apiExeName)),
			Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Yazaki.CommandeChaine.Api", "bin", "Release", "net9.0", apiDllName)),
			Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Yazaki.CommandeChaine.Api", "bin", "Release", "net9.0", "publish", apiExeName)),
			Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Yazaki.CommandeChaine.Api", "bin", "Release", "net9.0", "publish", apiDllName)),
			Path.GetFullPath(Path.Combine(repoRoot, "src", "Yazaki.CommandeChaine.Api", "bin", "Release", "net9.0", apiExeName)),
			Path.GetFullPath(Path.Combine(repoRoot, "src", "Yazaki.CommandeChaine.Api", "bin", "Release", "net9.0", apiDllName)),
			Path.GetFullPath(Path.Combine(repoRoot, "src", "Yazaki.CommandeChaine.Api", "bin", "Release", "net9.0", "publish", apiExeName)),
			Path.GetFullPath(Path.Combine(repoRoot, "src", "Yazaki.CommandeChaine.Api", "bin", "Release", "net9.0", "publish", apiDllName)),
			Path.GetFullPath(Path.Combine(repoRoot, "src", "Yazaki.CommandeChaine.Api", "bin", "Debug", "net9.0", apiExeName)),
			Path.GetFullPath(Path.Combine(repoRoot, "src", "Yazaki.CommandeChaine.Api", "bin", "Debug", "net9.0", apiDllName)),
		};

		foreach (var candidate in candidates)
		{
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}

		throw new FileNotFoundException($"API binary not found. Looked for {apiExeName} / {apiDllName} near launcher and in Release/Debug output folders.");
	}

	private static string ResolveDesktopTarget(string desktopExeName)
	{
		var baseDir = AppContext.BaseDirectory;
		var direct = Path.Combine(baseDir, desktopExeName);
		if (File.Exists(direct))
		{
			return direct;
		}

		var repoRoot = FindRepoRoot(baseDir);

		var relExe = Path.GetFullPath(Path.Combine(repoRoot, "src", "Yazaki.CommandeChaine.Desktop", "bin", "Release", "net9.0-windows", desktopExeName));
		if (File.Exists(relExe))
		{
			return relExe;
		}

		var relPublishExe = Path.GetFullPath(Path.Combine(repoRoot, "src", "Yazaki.CommandeChaine.Desktop", "bin", "Release", "net9.0-windows", "publish", desktopExeName));
		if (File.Exists(relPublishExe))
		{
			return relPublishExe;
		}

		// Release fallback to dll name if exe not found
		var relDllName = Path.ChangeExtension(desktopExeName, ".dll");
		var relDll = Path.GetFullPath(Path.Combine(repoRoot, "src", "Yazaki.CommandeChaine.Desktop", "bin", "Release", "net9.0-windows", relDllName));
		if (File.Exists(relDll))
		{
			return relDll;
		}

		var relPublishDll = Path.GetFullPath(Path.Combine(repoRoot, "src", "Yazaki.CommandeChaine.Desktop", "bin", "Release", "net9.0-windows", "publish", relDllName));
		if (File.Exists(relPublishDll))
		{
			return relPublishDll;
		}

		#if DEBUG
		var devExe = Path.GetFullPath(Path.Combine(repoRoot, "src", "Yazaki.CommandeChaine.Desktop", "bin", "Debug", "net9.0-windows", desktopExeName));
		if (File.Exists(devExe))
		{
			return devExe;
		}
		#endif

		// Fallback to dll name if exe not found
		var dllName = Path.ChangeExtension(desktopExeName, ".dll");
		#if DEBUG
		var devDll = Path.GetFullPath(Path.Combine(repoRoot, "src", "Yazaki.CommandeChaine.Desktop", "bin", "Debug", "net9.0-windows", dllName));
		if (File.Exists(devDll))
		{
			return devDll;
		}
		#endif

		throw new FileNotFoundException($"Desktop executable not found. Looked for {desktopExeName} near launcher and in dev output folders.");
	}

	private static string FindRepoRoot(string startDir)
	{
		var current = new DirectoryInfo(startDir);
		for (var i = 0; i < 12 && current is not null; i++)
		{
			var srcRoot = Path.Combine(current.FullName, "src", "Yazaki.CommandeChaine.Api");
			if (Directory.Exists(srcRoot))
			{
				return current.FullName;
			}

			var nestedRoot = Path.Combine(current.FullName, "Yazaki.CommandeChaine", "src", "Yazaki.CommandeChaine.Api");
			if (Directory.Exists(nestedRoot))
			{
				return Path.Combine(current.FullName, "Yazaki.CommandeChaine");
			}

			if (!string.Equals(current.Name, "publish", StringComparison.OrdinalIgnoreCase))
			{
				var settingsPath = Path.Combine(current.FullName, "yazaki.settings.json");
				if (File.Exists(settingsPath))
				{
					return current.FullName;
				}
			}
			current = current.Parent;
		}

		return Path.GetFullPath(Path.Combine(startDir, "..", "..", "..", "..", "..", ".."));
	}
}

file sealed record LauncherSettings(
	string ApiBaseUrl,
	string HealthPath,
	string ApiDllName,
	string DesktopExeName,
	int StartupTimeoutSeconds,
	string RaspberryPiApiUrl,
	string RaspberryPiHealthPath,
	bool RaspberryPiAutoStart)
{
	public static LauncherSettings Load()
	{
		var baseDir = AppContext.BaseDirectory;

		// (1) Prefer settings file next to launcher
		var filePath = Path.Combine(baseDir, "yazaki.settings.json");
		if (!File.Exists(filePath))
		{
			// (2) Or repo root (handy when running from VS)
			var repoRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
			var repoFile = Path.Combine(repoRoot, "yazaki.settings.json");
			if (File.Exists(repoFile))
			{
				filePath = repoFile;
			}
		}

		LauncherSettings settings;
		if (File.Exists(filePath))
		{
			var json = File.ReadAllText(filePath);
			settings = JsonSerializer.Deserialize<LauncherSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
				?? new LauncherSettings("http://localhost:5000", "/health", "Yazaki.CommandeChaine.Api.dll", "Yazaki.CommandeChaine.Desktop.exe", 30, "http://localhost:8000", "/api/v1/health", true);
		}
		else
		{
			settings = new LauncherSettings("http://localhost:5000", "/health", "Yazaki.CommandeChaine.Api.dll", "Yazaki.CommandeChaine.Desktop.exe", 30, "http://localhost:8000", "/api/v1/health", true);
		}

		// Environment override
		var envUrl = Environment.GetEnvironmentVariable("YAZAKI_API_BASE_URL");
		if (!string.IsNullOrWhiteSpace(envUrl))
		{
			settings = settings with { ApiBaseUrl = envUrl.Trim() };
		}

		var envRaspberryPiUrl = Environment.GetEnvironmentVariable("YAZAKI_RASPBERRY_API_URL");
		if (!string.IsNullOrWhiteSpace(envRaspberryPiUrl))
		{
			settings = settings with { RaspberryPiApiUrl = envRaspberryPiUrl.Trim() };
		}

		if (string.IsNullOrWhiteSpace(settings.RaspberryPiApiUrl))
		{
			settings = settings with { RaspberryPiApiUrl = "http://localhost:8000" };
		}

		if (string.IsNullOrWhiteSpace(settings.RaspberryPiHealthPath))
		{
			settings = settings with { RaspberryPiHealthPath = "/api/v1/health" };
		}

		return settings;
	}
}


