using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

[assembly: AssemblyTitle("Modrinth No-Ads Sidecar")]
[assembly: AssemblyDescription("Launches the official Modrinth App and suppresses its ad WebView through a loopback-only CDP connection.")]
[assembly: AssemblyCompany("Ambassador4ik")]
[assembly: AssemblyProduct("Modrinth No-Ads Sidecar")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Ambassador4ik and contributors. MIT License.")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace ModrinthNoAdsSidecar
{
	internal static class Program
	{
		private const string Version = "1.2.0";
		private const string MutexName = @"Local\ModrinthNoAdsSidecar";
		private const string LaunchRequestEventName =
			@"Local\ModrinthNoAdsSidecar.LaunchRequested";
		private const string ProtectionActiveEventName =
			@"Local\ModrinthNoAdsSidecar.ProtectionActive";

		[STAThread]
		private static int Main(string[] args)
		{
			Console.OutputEncoding = Encoding.UTF8;

			Options options;
			try
			{
				options = Options.Parse(args);
			}
			catch (UsageException ex)
			{
				Console.Error.WriteLine("Error: " + ex.Message);
				Console.Error.WriteLine();
				Options.PrintUsage();
				return 2;
			}

			if (options.ShowHelp)
			{
				Options.PrintUsage();
				return 0;
			}

			if (options.ShowVersion)
			{
				Console.WriteLine("Modrinth No-Ads Sidecar " + Version);
				return 0;
			}

			if (options.SelfTest)
			{
				return SelfTests.Run() ? 0 : 1;
			}

			if (options.CheckOnly)
			{
				try
				{
					Sidecar check = new Sidecar(options, null, null);
					return check.RunAsync().GetAwaiter().GetResult();
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine("Fatal error: " + ex.Message);
					return 7;
				}
			}

			string mutexName = options.AttachOnly
				? MutexName + ".Attach." + options.DebugPort
				: MutexName;
			bool createdNew;
			using (
				EventWaitHandle launchRequest = options.AttachOnly
					? null
					: new EventWaitHandle(
						false,
						EventResetMode.AutoReset,
						LaunchRequestEventName
					)
			)
			using (
				EventWaitHandle protectionActive = options.AttachOnly
					? null
					: new EventWaitHandle(
						false,
						EventResetMode.ManualReset,
						ProtectionActiveEventName
					)
			)
			using (Mutex mutex = new Mutex(true, mutexName, out createdNew))
			{
				if (!createdNew)
				{
					if (options.AttachOnly)
					{
						Console.WriteLine(
							"A sidecar is already attached to port " +
							options.DebugPort +
							"."
						);
					}
					else
					{
						launchRequest.Set();
						Console.WriteLine(
							"Asked the running Modrinth No-Ads Sidecar to show or launch Modrinth."
						);
					}
					return 0;
				}

				try
				{
					if (protectionActive != null)
					{
						protectionActive.Reset();
					}
					Sidecar sidecar = new Sidecar(
						options,
						launchRequest,
						protectionActive
					);
					return sidecar.RunAsync().GetAwaiter().GetResult();
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine("Fatal error: " + ex.Message);
					return 7;
				}
				finally
				{
					try
					{
						mutex.ReleaseMutex();
					}
					catch (ApplicationException)
					{
					}
				}
			}
		}
	}

	internal sealed class Options
	{
		public string AppPath;
		public int DebugPort;
		public bool AttachOnly;
		public bool Verbose;
		public bool CheckOnly;
		public bool SelfTest;
		public bool ShowHelp;
		public bool ShowVersion;
		public int StartupTimeoutSeconds = 30;
		public int PollMilliseconds = 500;

		public static Options Parse(string[] args)
		{
			Options result = new Options();

			for (int i = 0; i < args.Length; i++)
			{
				string arg = args[i];
				string normalized = arg.ToLowerInvariant();

				switch (normalized)
				{
					case "--app":
					case "-a":
						result.AppPath = RequireValue(args, ref i, arg);
						break;
					case "--attach-port":
						result.DebugPort = ParseInteger(RequireValue(args, ref i, arg), arg, 1, 65535);
						result.AttachOnly = true;
						break;
					case "--startup-timeout":
						result.StartupTimeoutSeconds = ParseInteger(
							RequireValue(args, ref i, arg),
							arg,
							5,
							180
						);
						break;
					case "--poll-ms":
						result.PollMilliseconds = ParseInteger(
							RequireValue(args, ref i, arg),
							arg,
							200,
							5000
						);
						break;
					case "--verbose":
					case "-v":
						result.Verbose = true;
						break;
					case "--check":
						result.CheckOnly = true;
						break;
					case "--self-test":
						result.SelfTest = true;
						break;
					case "--version":
						result.ShowVersion = true;
						break;
					case "--help":
					case "-h":
					case "/?":
						result.ShowHelp = true;
						break;
					default:
						throw new UsageException("Unknown argument: " + arg);
				}
			}

			return result;
		}

		private static string RequireValue(string[] args, ref int index, string option)
		{
			if (index + 1 >= args.Length)
			{
				throw new UsageException(option + " requires a value.");
			}

			index++;
			return args[index];
		}

		private static int ParseInteger(string value, string option, int minimum, int maximum)
		{
			int parsed;
			if (!int.TryParse(value, out parsed) || parsed < minimum || parsed > maximum)
			{
				throw new UsageException(
					option + " must be an integer from " + minimum + " through " + maximum + "."
				);
			}

			return parsed;
		}

		public static void PrintUsage()
		{
			Console.WriteLine("Modrinth No-Ads Sidecar");
			Console.WriteLine();
			Console.WriteLine("Usage:");
			Console.WriteLine("  ModrinthNoAdsSidecar.exe [options]");
			Console.WriteLine();
			Console.WriteLine("Options:");
			Console.WriteLine("  --app <path>             Explicit path to Modrinth App.exe.");
			Console.WriteLine("  --attach-port <port>     Attach to an app already launched with CDP.");
			Console.WriteLine("  --startup-timeout <sec>  CDP startup timeout (default: 30).");
			Console.WriteLine("  --poll-ms <ms>           Target discovery interval (default: 500).");
			Console.WriteLine("  --check                  Verify installation without launching.");
			Console.WriteLine("  --verbose                Include diagnostic target information.");
			Console.WriteLine("  --self-test              Run the bundled offline tests.");
			Console.WriteLine("  --version                Print the sidecar version.");
			Console.WriteLine("  --help                   Show this help.");
			Console.WriteLine();
			Console.WriteLine("The normal mode requires Modrinth to be fully closed first.");
		}
	}

	internal sealed class UsageException : Exception
	{
		public UsageException(string message)
			: base(message)
		{
		}
	}

	internal sealed class Logger
	{
		private readonly object gate = new object();
		private readonly bool verbose;
		private readonly string logPath;

		public Logger(bool verbose)
		{
			this.verbose = verbose;
			string directory = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"ModrinthNoAdsSidecar"
			);
			this.logPath = Path.Combine(directory, "sidecar.log");

			try
			{
				Directory.CreateDirectory(directory);
				if (File.Exists(this.logPath) && new FileInfo(this.logPath).Length > 1024 * 1024)
				{
					string oldPath = this.logPath + ".old";
					if (File.Exists(oldPath))
					{
						File.Delete(oldPath);
					}
					File.Move(this.logPath, oldPath);
				}
			}
			catch
			{
				// Logging to the console still works when the local app-data folder is unavailable.
			}
		}

		public string LogPath
		{
			get { return this.logPath; }
		}

		public void Info(string message)
		{
			Write("INFO", message, false);
		}

		public void Success(string message)
		{
			Write("OK", message, false);
		}

		public void Warn(string message)
		{
			Write("WARN", message, false);
		}

		public void Error(string message)
		{
			Write("ERROR", message, false);
		}

		public void Debug(string message)
		{
			Write("DEBUG", message, true);
		}

		private void Write(string level, string message, bool verboseOnly)
		{
			if (verboseOnly && !this.verbose)
			{
				return;
			}

			message = Privacy.SanitizeLogText(message, 2000);
			string consoleLine = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
			string fileLine =
				DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") +
				" [" +
				level +
				"] " +
				message +
				Environment.NewLine;

			lock (this.gate)
			{
				if (level == "ERROR")
				{
					Console.Error.WriteLine(consoleLine);
				}
				else
				{
					Console.WriteLine(consoleLine);
				}

				try
				{
					File.AppendAllText(this.logPath, fileLine, Encoding.UTF8);
				}
				catch
				{
				}
			}
		}
	}

	internal sealed class Sidecar
	{
		private const int RestartGraceSeconds = 90;
		private const int EndpointRequestTimeoutMilliseconds = 2000;
		internal const int MaximumJsonBytes = 4 * 1024 * 1024;
		private const string MainResourceName = "ModrinthNoAds.bootstrap-main.js";
		private const string AdResourceName = "ModrinthNoAds.bootstrap-ad.js";
		private const string MetadataProbe =
			"(function(){" +
			"var i=window.__TAURI_INTERNALS__||null;" +
			"var m=i&&i.metadata||null;" +
			"var w=m&&m.currentWindow||null;" +
			"var v=m&&m.currentWebview||null;" +
			"return {" +
			"windowLabel:w&&w.label||null," +
			"webviewLabel:v&&v.label||null," +
			"href:String(location.href)," +
			"title:String(document.title)," +
			"hasAppShell:!!document.querySelector('.app-viewport')&&!!document.querySelector('.app-sidebar')" +
			"};" +
			"})()";

		private readonly Options options;
		private readonly Logger log;
		private readonly JavaScriptSerializer serializer;
		private readonly Dictionary<string, GuardedTarget> guardedTargets;
		private readonly string mainBootstrap;
		private readonly string adBootstrap;
		private readonly EventWaitHandle launchRequest;
		private readonly EventWaitHandle protectionActive;
		private HttpClient http;
		private Uri endpoint;
		private CancellationTokenSource cancellation;
		private Process launchedProcess;
		private string appPath;
		private string processName;
		private bool unsupportedWarningShown;
		private bool endpointValidated;
		private DateTime endpointReadyAt;

		public Sidecar(
			Options options,
			EventWaitHandle launchRequest,
			EventWaitHandle protectionActive
		)
		{
			this.options = options;
			this.launchRequest = launchRequest;
			this.protectionActive = protectionActive;
			this.log = new Logger(options.Verbose);
			this.serializer = new JavaScriptSerializer();
			this.serializer.MaxJsonLength = MaximumJsonBytes;
			this.guardedTargets = new Dictionary<string, GuardedTarget>(StringComparer.Ordinal);
			this.mainBootstrap = ResourceLoader.ReadText(MainResourceName);
			this.adBootstrap = ResourceLoader.ReadText(AdResourceName);
		}

		public async Task<int> RunAsync()
		{
			if (Environment.OSVersion.Platform != PlatformID.Win32NT)
			{
				this.log.Error("This sidecar supports Windows only.");
				return 9;
			}

			this.appPath = AppDiscovery.Find(this.options.AppPath);
			if (String.IsNullOrEmpty(this.appPath))
			{
				this.log.Error(
					"Modrinth App.exe was not found. Re-run with --app \"C:\\path\\to\\Modrinth App.exe\"."
				);
				return 3;
			}

			this.processName = Path.GetFileNameWithoutExtension(this.appPath);
			FileVersionInfo version = FileVersionInfo.GetVersionInfo(this.appPath);
			string versionText = String.IsNullOrEmpty(version.FileVersion) ? "unknown" : version.FileVersion;
			this.log.Info(
				"Found Modrinth App " +
				versionText +
				" at " +
				Privacy.RedactUserPath(this.appPath)
			);

			if (this.options.CheckOnly)
			{
				this.log.Success("Installation check passed. No process was launched.");
				return 0;
			}

			if (!this.options.AttachOnly)
			{
				Process existing = ProcessTools.FindFirst(this.processName, this.appPath);
				if (existing != null)
				{
					try
					{
						this.log.Error(
							"Modrinth is already running (PID " +
							existing.Id +
							"). Fully quit it, including its tray process, then launch this sidecar again."
						);
					}
					finally
					{
						existing.Dispose();
					}
					return 4;
				}
			}

			int port = this.options.DebugPort;
			if (port == 0)
			{
				port = NetworkTools.GetFreeLoopbackPort();
			}

			this.endpoint = new Uri("http://127.0.0.1:" + port + "/");
			this.http = NetworkTools.CreateLoopbackClient(this.endpoint);
			this.cancellation = new CancellationTokenSource();
			this.log.Debug("CDP endpoint: " + this.endpoint.AbsoluteUri);

			ConsoleCancelEventHandler cancelHandler = delegate(object sender, ConsoleCancelEventArgs eventArgs)
			{
				eventArgs.Cancel = true;
				this.cancellation.Cancel();
			};
			Console.CancelKeyPress += cancelHandler;

			try
			{
				if (this.options.AttachOnly)
				{
					this.log.Info("Attaching to the existing loopback CDP endpoint on port " + port + ".");
				}
				else
				{
					this.launchedProcess = LaunchModrinth(this.appPath, port);
					this.log.Info(
						"Started the official Modrinth App (PID " +
						this.launchedProcess.Id +
						") with a loopback-only CDP port."
					);
				}

				bool ready = await WaitForEndpointAsync(this.cancellation.Token).ConfigureAwait(false);
				if (!ready)
				{
					if (!this.options.AttachOnly && ProcessTools.IsAlive(this.launchedProcess))
					{
						this.log.Error(
							"Modrinth started, but WebView2 did not expose CDP within " +
							this.options.StartupTimeoutSeconds +
							" seconds. See " +
							Privacy.RedactUserPath(this.log.LogPath)
						);
					}
					else
					{
						this.log.Error("The CDP endpoint did not become available.");
					}
					return 6;
				}

				this.endpointReadyAt = DateTime.UtcNow;
				this.log.Success("Connected to WebView2. Watching for Modrinth targets.");
				await WatchAsync(this.cancellation.Token).ConfigureAwait(false);
				this.log.Info("Modrinth exited; the sidecar is stopping.");
				return 0;
			}
			catch (OperationCanceledException)
			{
				this.log.Info("Sidecar stopped.");
				return 0;
			}
			catch (Exception ex)
			{
				this.log.Error(ex.GetType().Name + ": " + ex.Message);
				return 7;
			}
			finally
			{
				if (this.protectionActive != null)
				{
					this.protectionActive.Reset();
				}
				Console.CancelKeyPress -= cancelHandler;
				DisposeAllGuards();
				if (this.http != null)
				{
					this.http.Dispose();
				}
				if (this.launchedProcess != null)
				{
					this.launchedProcess.Dispose();
				}
				if (this.cancellation != null)
				{
					this.cancellation.Dispose();
				}
			}
		}

		private Process LaunchModrinth(string appPath, int port)
		{
			ProcessStartInfo startInfo = new ProcessStartInfo();
			startInfo.FileName = appPath;
			startInfo.WorkingDirectory = Path.GetDirectoryName(appPath);
			startInfo.UseShellExecute = false;
			startInfo.CreateNoWindow = true;
			startInfo.EnvironmentVariables["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] =
				BrowserArguments.Build(
					Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"),
					port
				);

			// Deliberately do not set MODRINTH_EXTERNAL_UPDATE_PROVIDER; the official
			// updater remains enabled and unchanged.
			Process process = Process.Start(startInfo);
			if (process == null)
			{
				throw new InvalidOperationException("Windows did not return a Modrinth process.");
			}
			return process;
		}

		private async Task<bool> WaitForEndpointAsync(CancellationToken token)
		{
			DateTime deadline = DateTime.UtcNow.AddSeconds(this.options.StartupTimeoutSeconds);
			Exception lastError = null;

			while (DateTime.UtcNow < deadline)
			{
				token.ThrowIfCancellationRequested();

				if (!this.options.AttachOnly && !ProcessTools.IsAlive(this.launchedProcess))
				{
					this.log.Error("Modrinth exited before its WebView2 endpoint was ready.");
					return false;
				}

				try
				{
					await GetTargetsAsync(token).ConfigureAwait(false);
					return true;
				}
				catch (Exception ex)
				{
					lastError = ex;
				}

				await Task.Delay(200, token).ConfigureAwait(false);
			}

			if (lastError != null)
			{
				this.log.Debug("Last CDP startup error: " + lastError.Message);
			}
			return false;
		}

		private async Task WatchAsync(CancellationToken token)
		{
			DateTime lastProcessSeen = DateTime.UtcNow;
			bool endpointUnavailable = false;
			bool suppressionReported = false;
			bool waitingForRestart = false;
			bool missingEndpointWarningShown = false;
			DateTime endpointUnavailableSince = DateTime.MinValue;

			while (true)
			{
				token.ThrowIfCancellationRequested();

				bool launchRequested =
					this.launchRequest != null && this.launchRequest.WaitOne(0);
				Process currentProcess = ProcessTools.FindFirst(
					this.processName,
					this.appPath
				);
				bool processPresent = currentProcess != null;
				if (currentProcess != null)
				{
					if (launchRequested)
					{
						ProcessTools.TryActivate(currentProcess);
						this.log.Debug("Activated the existing Modrinth window.");
					}
					if (waitingForRestart)
					{
						this.log.Info("Detected a Modrinth process restart; reconnecting the guards.");
						waitingForRestart = false;
						this.endpointReadyAt = DateTime.UtcNow;
						this.unsupportedWarningShown = false;
						suppressionReported = false;
					}
					lastProcessSeen = DateTime.UtcNow;
					currentProcess.Dispose();
				}
				else if (launchRequested && !this.options.AttachOnly)
				{
					try
					{
						this.log.Info("Received a background shortcut launch request.");
						DisposeAllGuards();
						this.endpointValidated = false;
						suppressionReported = false;
						this.unsupportedWarningShown = false;
						missingEndpointWarningShown = false;

						if (this.launchedProcess != null)
						{
							this.launchedProcess.Dispose();
						}
						this.launchedProcess = LaunchModrinth(this.appPath, this.endpoint.Port);
						processPresent = true;
						waitingForRestart = false;
						lastProcessSeen = DateTime.UtcNow;
						this.endpointReadyAt = DateTime.UtcNow;
						this.log.Info(
							"Restarted the official Modrinth App (PID " +
							this.launchedProcess.Id +
							") from the background shortcut."
						);
					}
					catch (Exception ex)
					{
						this.log.Error(
							"Could not relaunch Modrinth from the background shortcut: " +
							ex.Message
						);
					}
				}
				else
				{
					if (!waitingForRestart)
					{
						waitingForRestart = true;
						DisposeAllGuards();
						this.endpointValidated = false;
						suppressionReported = false;
						this.unsupportedWarningShown = false;
						this.log.Info(
							"Modrinth exited. Waiting up to " +
							RestartGraceSeconds +
							" seconds for a possible updater restart; press Ctrl+C to stop now."
						);
					}

					if (DateTime.UtcNow - lastProcessSeen > TimeSpan.FromSeconds(RestartGraceSeconds))
					{
						break;
					}
				}

				if (!processPresent)
				{
					if (this.protectionActive != null)
					{
						this.protectionActive.Reset();
					}
					await Task.Delay(this.options.PollMilliseconds, token).ConfigureAwait(false);
					continue;
				}

				List<CdpTarget> targets = null;
				Exception endpointError = null;
				try
				{
					targets = await GetTargetsAsync(token).ConfigureAwait(false);
				}
				catch (OperationCanceledException ex)
				{
					if (token.IsCancellationRequested)
					{
						throw;
					}
					endpointError = ex;
				}
				catch (Exception ex)
				{
					endpointError = ex;
				}

				if (endpointError != null)
				{
					if (this.protectionActive != null)
					{
						this.protectionActive.Reset();
					}
					if (!endpointUnavailable)
					{
						endpointUnavailable = true;
						endpointUnavailableSince = DateTime.UtcNow;
						missingEndpointWarningShown = false;
						DisposeAllGuards();
						this.endpointValidated = false;
						suppressionReported = false;
						this.unsupportedWarningShown = false;
						this.log.Warn(
							"WebView2 endpoint is temporarily unavailable; waiting for an update/restart. " +
							"(" +
							endpointError.Message +
							")"
						);
					}

					if (
						!missingEndpointWarningShown &&
						DateTime.UtcNow - endpointUnavailableSince > TimeSpan.FromSeconds(10)
					)
					{
						missingEndpointWarningShown = true;
						this.log.Error(
							"Modrinth is running without the sidecar's CDP endpoint. " +
							"After an update, fully quit Modrinth and launch it through this sidecar again."
						);
					}

					await Task.Delay(this.options.PollMilliseconds, token).ConfigureAwait(false);
					continue;
				}

				if (endpointUnavailable)
				{
					this.log.Info("WebView2 reconnected; all guards will be revalidated.");
					endpointUnavailable = false;
					missingEndpointWarningShown = false;
					this.endpointReadyAt = DateTime.UtcNow;
					this.unsupportedWarningShown = false;
					suppressionReported = false;
				}

				HashSet<string> seenTargetIds = new HashSet<string>(StringComparer.Ordinal);
				for (int i = 0; i < targets.Count; i++)
				{
					CdpTarget target = targets[i];
					if (
						!TargetClassifier.IsPage(target) ||
						String.IsNullOrWhiteSpace(target.id) ||
						target.id.Length > 512 ||
						String.IsNullOrEmpty(target.webSocketDebuggerUrl)
					)
					{
						continue;
					}

					if (!NetworkTools.IsAllowedTargetWebSocket(target.webSocketDebuggerUrl, this.endpoint))
					{
						this.log.Debug(
							"Rejected a non-loopback or unexpected target WebSocket for " +
							TargetClassifier.ShortId(target.id) +
							"."
						);
						continue;
					}

					seenTargetIds.Add(target.id);
					GuardedTarget guard;
					if (this.guardedTargets.TryGetValue(target.id, out guard))
					{
						if (
							!String.Equals(
								guard.WebSocketUrl,
								target.webSocketDebuggerUrl,
								StringComparison.Ordinal
							) ||
							!guard.Session.IsOpen
						)
						{
							await RemoveGuardAsync(target.id).ConfigureAwait(false);
							guard = null;
						}
					}

					if (guard == null)
					{
						this.log.Debug(
							"New target " +
							TargetClassifier.ShortId(target.id) +
							": " +
							TargetClassifier.SafeDisplayUrl(target.url) +
							" [" +
							(target.title ?? String.Empty) +
							"]"
						);

						guard = await InspectAndInstallAsync(target, token).ConfigureAwait(false);
						if (guard != null)
						{
							this.guardedTargets[target.id] = guard;
						}
					}
					else if (
						DateTime.UtcNow - guard.LastHealthCheck > TimeSpan.FromSeconds(2)
					)
					{
						Exception healthError = null;
						try
						{
							await RefreshGuardAsync(guard, token).ConfigureAwait(false);
						}
						catch (OperationCanceledException ex)
						{
							if (token.IsCancellationRequested)
							{
								throw;
							}
							healthError = ex;
						}
						catch (Exception ex)
						{
							healthError = ex;
						}

						if (healthError != null)
						{
							guard.ConsecutiveHealthFailures++;
							this.log.Debug(
								"Guard health check failed for " +
								TargetClassifier.ShortId(target.id) +
								": " +
								healthError.Message
							);

							if (guard.ConsecutiveHealthFailures >= 3 || !guard.Session.IsOpen)
							{
								await RemoveGuardAsync(target.id).ConfigureAwait(false);
							}
						}
					}
				}

				await RemoveMissingGuardsAsync(seenTargetIds).ConfigureAwait(false);

				bool mainActive = HasActiveMainGuard();
				if (this.protectionActive != null)
				{
					if (mainActive)
					{
						this.protectionActive.Set();
					}
					else
					{
						this.protectionActive.Reset();
					}
				}
				if (mainActive && !suppressionReported)
				{
					suppressionReported = true;
					this.log.Success(
						"Suppression is active and the native hide command was verified. " +
						"Keep this sidecar running while Modrinth is open."
					);
				}
				else if (!mainActive && suppressionReported)
				{
					suppressionReported = false;
					this.log.Warn("The verified main guard was lost; attempting to restore it.");
				}

				if (
					!mainActive &&
					!this.unsupportedWarningShown &&
					DateTime.UtcNow - this.endpointReadyAt > TimeSpan.FromSeconds(30)
				)
				{
					this.unsupportedWarningShown = true;
					this.log.Warn(
						"No verified Modrinth main view was protected after 30 seconds. " +
						"The app may have changed; no unmatched target was modified."
					);
				}

				await Task.Delay(this.options.PollMilliseconds, token).ConfigureAwait(false);
			}

			await CloseAllGuardsAsync().ConfigureAwait(false);
		}

		private async Task<GuardedTarget> InspectAndInstallAsync(
			CdpTarget target,
			CancellationToken token
		)
		{
			CdpSession session = new CdpSession(target.webSocketDebuggerUrl, this.serializer);
			try
			{
				await session.ConnectAsync(token).ConfigureAwait(false);

				TargetMetadata metadata = null;
				Exception probeError = null;
				try
				{
					Dictionary<string, object> probeResponse = await session
						.CommandAsync(
							"Runtime.evaluate",
							CdpParameters.Evaluate(MetadataProbe, true, false),
							token
						)
						.ConfigureAwait(false);
					metadata = TargetMetadata.FromEvaluation(probeResponse);
				}
				catch (Exception ex)
				{
					probeError = ex;
				}

				if (probeError != null)
				{
					this.log.Debug(
						"Metadata probe failed for " +
						TargetClassifier.ShortId(target.id) +
						": " +
						probeError.Message
					);
				}

				TargetKind kind = TargetClassifier.Classify(
					target,
					metadata,
					!this.options.AttachOnly
				);
				if (kind == TargetKind.Unknown)
				{
					await session.CloseAsync().ConfigureAwait(false);
					session.Dispose();
					return null;
				}

				string source = kind == TargetKind.Main ? this.mainBootstrap : this.adBootstrap;
				BootstrapHealth health = await InstallBootstrapAsync(session, source, token)
					.ConfigureAwait(false);
				GuardedTarget result = new GuardedTarget(
					target.id,
					target.webSocketDebuggerUrl,
					kind,
					session,
					source,
					health
				);

				this.log.Info(
					"Installed a persistent " +
					(kind == TargetKind.Main ? "main-view" : "ad-view") +
					" guard in target " +
					TargetClassifier.ShortId(target.id) +
					"."
				);
				LogGuardHealth(result, null);
				return result;
			}
			catch (OperationCanceledException)
			{
				session.Dispose();
				throw;
			}
			catch (Exception ex)
			{
				session.Dispose();
				this.log.Debug(
					"Could not inspect target " +
					TargetClassifier.ShortId(target.id) +
					": " +
					ex.Message
				);
			}

			return null;
		}

		private async Task<BootstrapHealth> InstallBootstrapAsync(
			CdpSession session,
			string source,
			CancellationToken token
		)
		{
			Dictionary<string, object> addParameters = new Dictionary<string, object>();
			addParameters["source"] = source;
			await session
				.CommandAsync("Page.addScriptToEvaluateOnNewDocument", addParameters, token)
				.ConfigureAwait(false);

			Dictionary<string, object> response = await session
				.CommandAsync(
					"Runtime.evaluate",
					CdpParameters.Evaluate(source, true, true),
					token
				)
				.ConfigureAwait(false);

			object value = CdpSession.GetEvaluationValue(response);
			BootstrapHealth health = BootstrapHealth.FromValue(value);
			if (health == null)
			{
				throw new InvalidOperationException("The bootstrap returned no structured health result.");
			}
			this.log.Debug("Bootstrap result: " + health.ToString());
			return health;
		}

		private async Task RefreshGuardAsync(GuardedTarget guard, CancellationToken token)
		{
			Dictionary<string, object> response = await guard.Session
				.CommandAsync(
					"Runtime.evaluate",
					CdpParameters.Evaluate(guard.Source, true, true),
					token
				)
				.ConfigureAwait(false);

			BootstrapHealth health = BootstrapHealth.FromValue(
				CdpSession.GetEvaluationValue(response)
			);
			if (health == null)
			{
				throw new InvalidOperationException("The guard returned no structured health result.");
			}

			BootstrapHealth previous = guard.Health;
			guard.Health = health;
			guard.LastHealthCheck = DateTime.UtcNow;
			guard.ConsecutiveHealthFailures = 0;
			LogGuardHealth(guard, previous);
		}

		private void LogGuardHealth(GuardedTarget guard, BootstrapHealth previous)
		{
			bool stateChanged = previous == null || previous.Active != guard.Health.Active;
			if (stateChanged && guard.Health.Active)
			{
				this.log.Info(
					(guard.Kind == TargetKind.Main ? "Main" : "Ad") +
					" guard verified its native hide operation."
				);
				if (guard.Kind == TargetKind.Main && !guard.Health.WrapperInstalled)
				{
					this.log.Debug(
						"Tauri exposes a read-only invoke bridge; rapid native hide enforcement is active."
					);
				}
			}
			else if (stateChanged && !guard.Health.Active)
			{
				this.log.Warn(
					(guard.Kind == TargetKind.Main ? "Main" : "Ad") +
					" guard is installed but not yet verified" +
					(String.IsNullOrEmpty(guard.Health.Error)
						? "."
						: ": " + guard.Health.Error)
				);
			}
			else if (
				previous != null &&
				!String.Equals(previous.Error, guard.Health.Error, StringComparison.Ordinal)
			)
			{
				this.log.Debug(
					"Guard status changed for " +
					TargetClassifier.ShortId(guard.Id) +
					": " +
					(guard.Health.Error ?? "healthy")
				);
			}
		}

		private bool HasActiveMainGuard()
		{
			foreach (GuardedTarget guard in this.guardedTargets.Values)
			{
				if (
					guard.Kind == TargetKind.Main &&
					guard.Health != null &&
					guard.Health.Active
				)
				{
					return true;
				}
			}
			return false;
		}

		private async Task RemoveMissingGuardsAsync(HashSet<string> seenTargetIds)
		{
			List<string> missing = new List<string>();
			foreach (string id in this.guardedTargets.Keys)
			{
				if (!seenTargetIds.Contains(id))
				{
					missing.Add(id);
				}
			}

			for (int i = 0; i < missing.Count; i++)
			{
				await RemoveGuardAsync(missing[i]).ConfigureAwait(false);
			}
		}

		private async Task RemoveGuardAsync(string id)
		{
			GuardedTarget guard;
			if (!this.guardedTargets.TryGetValue(id, out guard))
			{
				return;
			}

			this.guardedTargets.Remove(id);
			try
			{
				await guard.Session.CloseAsync().ConfigureAwait(false);
			}
			finally
			{
				guard.Session.Dispose();
			}
		}

		private async Task CloseAllGuardsAsync()
		{
			List<string> ids = new List<string>(this.guardedTargets.Keys);
			for (int i = 0; i < ids.Count; i++)
			{
				await RemoveGuardAsync(ids[i]).ConfigureAwait(false);
			}
		}

		private void DisposeAllGuards()
		{
			foreach (GuardedTarget guard in this.guardedTargets.Values)
			{
				guard.Session.Dispose();
			}
			this.guardedTargets.Clear();
		}

		private async Task<List<CdpTarget>> GetTargetsAsync(CancellationToken token)
		{
			if (!this.endpointValidated)
			{
				await ValidateEndpointAsync(token).ConfigureAwait(false);
				this.endpointValidated = true;
			}

			string json = await ReadEndpointJsonAsync(
					this.http,
					"json/list",
					EndpointRequestTimeoutMilliseconds,
					token
				)
				.ConfigureAwait(false);
			List<CdpTarget> targets = this.serializer.Deserialize<List<CdpTarget>>(json);
			return targets ?? new List<CdpTarget>();
		}

		private async Task ValidateEndpointAsync(CancellationToken token)
		{
			string json = await ReadEndpointJsonAsync(
					this.http,
					"json/version",
					EndpointRequestTimeoutMilliseconds,
					token
				)
				.ConfigureAwait(false);
			Dictionary<string, object> version = this.serializer.Deserialize<
				Dictionary<string, object>
			>(json);

			object browser;
			object protocol;
			object webSocket;
			if (
				version == null ||
				!version.TryGetValue("Browser", out browser) ||
				!version.TryGetValue("Protocol-Version", out protocol) ||
				!version.TryGetValue("webSocketDebuggerUrl", out webSocket) ||
				browser == null ||
				protocol == null ||
				webSocket == null ||
				!NetworkTools.IsAllowedBrowserWebSocket(webSocket.ToString(), this.endpoint)
			)
			{
				throw new InvalidOperationException(
					"The loopback service did not identify itself as the expected CDP endpoint."
				);
			}

			string browserText = browser.ToString();
			string protocolText = protocol.ToString();
			bool browserMatches =
				browserText.StartsWith(
					"Microsoft Edge/",
					StringComparison.OrdinalIgnoreCase
				) ||
				browserText.StartsWith("WebView2/", StringComparison.OrdinalIgnoreCase) ||
				browserText.StartsWith("Edg/", StringComparison.OrdinalIgnoreCase);
			if (!browserMatches || !Regex.IsMatch(protocolText, @"^\d+\.\d+$"))
			{
				throw new InvalidOperationException(
					"The loopback CDP endpoint was not identified as Microsoft WebView2."
				);
			}

			this.log.Debug(
				"Validated CDP " + browser + " (protocol " + protocol + ")."
			);
		}

		internal static async Task<string> ReadEndpointJsonAsync(
			HttpClient client,
			string relativePath,
			int timeoutMilliseconds,
			CancellationToken token
		)
		{
			using (
				CancellationTokenSource deadline =
					CancellationTokenSource.CreateLinkedTokenSource(token)
			)
			{
				deadline.CancelAfter(timeoutMilliseconds);
				using (
					HttpResponseMessage response = await client
						.GetAsync(
							relativePath,
							HttpCompletionOption.ResponseContentRead,
							deadline.Token
						)
						.ConfigureAwait(false)
				)
				{
					response.EnsureSuccessStatusCode();
					return await ReadLimitedJsonAsync(response.Content, deadline.Token)
						.ConfigureAwait(false);
				}
			}
		}

		internal static async Task<string> ReadLimitedJsonAsync(
			HttpContent content,
			CancellationToken token
		)
		{
			if (
				content.Headers.ContentLength.HasValue &&
				content.Headers.ContentLength.Value > MaximumJsonBytes
			)
			{
				throw new InvalidOperationException("The CDP JSON response was too large.");
			}

			using (Stream input = await content.ReadAsStreamAsync().ConfigureAwait(false))
			using (MemoryStream output = new MemoryStream())
			{
				byte[] buffer = new byte[16384];
				while (true)
				{
					int count = await input
						.ReadAsync(buffer, 0, buffer.Length, token)
						.ConfigureAwait(false);
					if (count == 0)
					{
						break;
					}
					if (output.Length + count > MaximumJsonBytes)
					{
						throw new InvalidOperationException(
							"The CDP JSON response was too large."
						);
					}
					output.Write(buffer, 0, count);
				}
				return Encoding.UTF8.GetString(output.ToArray());
			}
		}
	}

	internal sealed class GuardedTarget
	{
		public GuardedTarget(
			string id,
			string webSocketUrl,
			TargetKind kind,
			CdpSession session,
			string source,
			BootstrapHealth health
		)
		{
			this.Id = id;
			this.WebSocketUrl = webSocketUrl;
			this.Kind = kind;
			this.Session = session;
			this.Source = source;
			this.Health = health;
			this.LastHealthCheck = DateTime.UtcNow;
		}

		public string Id { get; private set; }
		public string WebSocketUrl { get; private set; }
		public TargetKind Kind { get; private set; }
		public CdpSession Session { get; private set; }
		public string Source { get; private set; }
		public BootstrapHealth Health { get; set; }
		public DateTime LastHealthCheck { get; set; }
		public int ConsecutiveHealthFailures { get; set; }
	}

	internal sealed class BootstrapHealth
	{
		public string Kind;
		public bool Verified;
		public bool HideOk;
		public bool WrapperInstalled;
		public bool StyleInstalled;
		public bool Active;
		public string Error;

		public static BootstrapHealth FromValue(object value)
		{
			Dictionary<string, object> dictionary = value as Dictionary<string, object>;
			if (dictionary == null)
			{
				return null;
			}

			BootstrapHealth result = new BootstrapHealth();
			result.Kind = ReadString(dictionary, "kind");
			result.Verified = ReadBoolean(dictionary, "verified");
			result.HideOk = ReadBoolean(dictionary, "hideOk");
			result.WrapperInstalled = ReadBoolean(dictionary, "wrapperInstalled");
			result.StyleInstalled = ReadBoolean(dictionary, "styleInstalled");
			result.Error = ReadString(dictionary, "error");

			bool claimedActive = ReadBoolean(dictionary, "active");
			if (String.Equals(result.Kind, "main", StringComparison.Ordinal))
			{
				result.Active =
					claimedActive &&
					result.Verified &&
					result.HideOk &&
					result.StyleInstalled;
			}
			else if (String.Equals(result.Kind, "ad", StringComparison.Ordinal))
			{
				result.Active = claimedActive && result.Verified && result.HideOk;
			}

			return result;
		}

		private static bool ReadBoolean(Dictionary<string, object> dictionary, string key)
		{
			object value;
			if (!dictionary.TryGetValue(key, out value) || value == null)
			{
				return false;
			}

			try
			{
				return Convert.ToBoolean(value);
			}
			catch
			{
				return false;
			}
		}

		private static string ReadString(Dictionary<string, object> dictionary, string key)
		{
			object value;
			if (!dictionary.TryGetValue(key, out value) || value == null)
			{
				return null;
			}
			return value.ToString();
		}

		public override string ToString()
		{
			return (
				"kind=" +
				(this.Kind ?? "(unknown)") +
				", verified=" +
				this.Verified +
				", hideOk=" +
				this.HideOk +
				", wrapper=" +
				this.WrapperInstalled +
				", style=" +
				this.StyleInstalled +
				", active=" +
				this.Active +
				(String.IsNullOrEmpty(this.Error) ? String.Empty : ", error=" + this.Error)
			);
		}
	}

	internal static class CdpParameters
	{
		public static Dictionary<string, object> Evaluate(
			string expression,
			bool returnByValue,
			bool awaitPromise
		)
		{
			Dictionary<string, object> parameters = new Dictionary<string, object>();
			parameters["expression"] = expression;
			parameters["returnByValue"] = returnByValue;
			parameters["awaitPromise"] = awaitPromise;
			parameters["userGesture"] = false;
			return parameters;
		}
	}

	internal sealed class CdpSession : IDisposable
	{
		private const int MaximumMessageBytes = 4 * 1024 * 1024;
		private readonly ClientWebSocket socket;
		private readonly JavaScriptSerializer serializer;
		private int nextId;
		private bool disposed;

		public CdpSession(string webSocketUrl, JavaScriptSerializer serializer)
		{
			this.socket = new ClientWebSocket();
			this.socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
			this.socket.Options.Proxy = null;
			this.serializer = serializer;
			this.WebSocketUrl = new Uri(webSocketUrl);
		}

		public Uri WebSocketUrl { get; private set; }

		public bool IsOpen
		{
			get { return !this.disposed && this.socket.State == WebSocketState.Open; }
		}

		public async Task ConnectAsync(CancellationToken token)
		{
			using (CancellationTokenSource timeout = CreateTimeout(token, 4000))
			{
				try
				{
					await this.socket
						.ConnectAsync(this.WebSocketUrl, timeout.Token)
						.ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					if (!token.IsCancellationRequested)
					{
						throw new TimeoutException("Timed out while connecting to the CDP target.");
					}
					throw;
				}
			}
		}

		public async Task<Dictionary<string, object>> CommandAsync(
			string method,
			Dictionary<string, object> parameters,
			CancellationToken token
		)
		{
			int id = Interlocked.Increment(ref this.nextId);
			Dictionary<string, object> request = new Dictionary<string, object>();
			request["id"] = id;
			request["method"] = method;
			request["params"] = parameters ?? new Dictionary<string, object>();

			byte[] bytes = Encoding.UTF8.GetBytes(this.serializer.Serialize(request));
			using (CancellationTokenSource timeout = CreateTimeout(token, 4000))
			{
				try
				{
					await this.socket
						.SendAsync(
							new ArraySegment<byte>(bytes),
							WebSocketMessageType.Text,
							true,
							timeout.Token
						)
						.ConfigureAwait(false);

					while (true)
					{
						string text = await ReceiveTextAsync(timeout.Token).ConfigureAwait(false);
						Dictionary<string, object> response = this.serializer.Deserialize<
							Dictionary<string, object>
						>(text);

						object responseId;
						if (
							response != null &&
							response.TryGetValue("id", out responseId) &&
							Convert.ToInt32(responseId) == id
						)
						{
							object error;
							if (response.TryGetValue("error", out error))
							{
								throw new InvalidOperationException(
									"CDP " + method + " failed: " + this.serializer.Serialize(error)
								);
							}
							return response;
						}
					}
				}
				catch (OperationCanceledException)
				{
					if (!token.IsCancellationRequested)
					{
						throw new TimeoutException("Timed out waiting for CDP " + method + ".");
					}
					throw;
				}
			}
		}

		private async Task<string> ReceiveTextAsync(CancellationToken token)
		{
			byte[] buffer = new byte[16384];
			using (MemoryStream stream = new MemoryStream())
			{
				WebSocketReceiveResult result;
				do
				{
					result = await this.socket
						.ReceiveAsync(new ArraySegment<byte>(buffer), token)
						.ConfigureAwait(false);

					if (result.MessageType == WebSocketMessageType.Close)
					{
						throw new InvalidOperationException("CDP closed the WebSocket connection.");
					}

					if (stream.Length + result.Count > MaximumMessageBytes)
					{
						throw new InvalidOperationException(
							"The CDP WebSocket message was too large."
						);
					}
					stream.Write(buffer, 0, result.Count);
				}
				while (!result.EndOfMessage);

				return Encoding.UTF8.GetString(stream.ToArray());
			}
		}

		public async Task CloseAsync()
		{
			if (
				this.socket.State == WebSocketState.Open ||
				this.socket.State == WebSocketState.CloseReceived
			)
			{
				try
				{
					using (CancellationTokenSource timeout = new CancellationTokenSource(1000))
					{
						await this.socket
							.CloseAsync(
								WebSocketCloseStatus.NormalClosure,
								"done",
								timeout.Token
							)
							.ConfigureAwait(false);
					}
				}
				catch
				{
					this.socket.Abort();
				}
			}
		}

		public static object GetEvaluationValue(Dictionary<string, object> response)
		{
			Dictionary<string, object> commandResult = GetDictionary(response, "result");
			if (commandResult == null)
			{
				return null;
			}

			if (commandResult.ContainsKey("exceptionDetails"))
			{
				throw new InvalidOperationException("The injected JavaScript raised an exception.");
			}

			Dictionary<string, object> remoteObject = GetDictionary(commandResult, "result");
			if (remoteObject == null)
			{
				return null;
			}

			object value;
			if (remoteObject.TryGetValue("value", out value))
			{
				return value;
			}

			if (remoteObject.TryGetValue("unserializableValue", out value))
			{
				return value;
			}

			return null;
		}

		internal static Dictionary<string, object> GetDictionary(
			Dictionary<string, object> dictionary,
			string key
		)
		{
			if (dictionary == null)
			{
				return null;
			}

			object value;
			if (!dictionary.TryGetValue(key, out value))
			{
				return null;
			}

			return value as Dictionary<string, object>;
		}

		private static CancellationTokenSource CreateTimeout(CancellationToken parent, int milliseconds)
		{
			CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(parent);
			source.CancelAfter(milliseconds);
			return source;
		}

		public void Dispose()
		{
			if (this.disposed)
			{
				return;
			}
			this.disposed = true;
			this.socket.Dispose();
		}
	}

	internal sealed class CdpTarget
	{
		public string id { get; set; }
		public string type { get; set; }
		public string title { get; set; }
		public string url { get; set; }
		public string webSocketDebuggerUrl { get; set; }
	}

	internal sealed class TargetMetadata
	{
		public string WindowLabel;
		public string WebviewLabel;
		public string Href;
		public string Title;
		public bool HasAppShell;

		public static TargetMetadata FromEvaluation(Dictionary<string, object> response)
		{
			object value = CdpSession.GetEvaluationValue(response);
			Dictionary<string, object> dictionary = value as Dictionary<string, object>;
			if (dictionary == null)
			{
				return null;
			}

			TargetMetadata result = new TargetMetadata();
			result.WindowLabel = ReadString(dictionary, "windowLabel");
			result.WebviewLabel = ReadString(dictionary, "webviewLabel");
			result.Href = ReadString(dictionary, "href");
			result.Title = ReadString(dictionary, "title");

			object appShell;
			if (dictionary.TryGetValue("hasAppShell", out appShell) && appShell != null)
			{
				result.HasAppShell = Convert.ToBoolean(appShell);
			}

			return result;
		}

		private static string ReadString(Dictionary<string, object> dictionary, string key)
		{
			object value;
			if (!dictionary.TryGetValue(key, out value) || value == null)
			{
				return null;
			}
			return value.ToString();
		}
	}

	internal enum TargetKind
	{
		Unknown,
		Main,
		Ad,
	}

	internal static class TargetClassifier
	{
		private static readonly Regex AdPath = new Regex(
			@"^/wrapper/app-ads(?:-cookie)?/?$",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
		);

		public static bool IsPage(CdpTarget target)
		{
			return target != null && String.Equals(target.type, "page", StringComparison.OrdinalIgnoreCase);
		}

		public static TargetKind Classify(
			CdpTarget target,
			TargetMetadata metadata,
			bool allowTransientAd
		)
		{
			string candidateUrl =
				metadata != null && !String.IsNullOrEmpty(metadata.Href) ? metadata.Href : target.url;
			bool explicitAdLabels =
				metadata != null &&
				String.Equals(metadata.WebviewLabel, "ads-window", StringComparison.Ordinal) &&
				String.Equals(metadata.WindowLabel, "main", StringComparison.Ordinal);
			bool normalLaunchAdIdentity =
				metadata == null ||
				String.IsNullOrEmpty(metadata.WebviewLabel) ||
				String.Equals(metadata.WebviewLabel, "ads-window", StringComparison.Ordinal);

			if (
				IsAdUrl(candidateUrl) &&
				(
					(allowTransientAd && normalLaunchAdIdentity) ||
					(!allowTransientAd && explicitAdLabels)
				)
			)
			{
				return TargetKind.Ad;
			}

			if (
				allowTransientAd &&
				metadata != null &&
				String.Equals(candidateUrl, "about:blank", StringComparison.OrdinalIgnoreCase) &&
				String.Equals(metadata.WebviewLabel, "ads-window", StringComparison.Ordinal) &&
				String.Equals(metadata.WindowLabel, "main", StringComparison.Ordinal)
			)
			{
				return TargetKind.Ad;
			}

			string candidateTitle =
				metadata != null && !String.IsNullOrEmpty(metadata.Title) ? metadata.Title : target.title;
			bool explicitMainLabels =
				metadata != null &&
				String.Equals(metadata.WebviewLabel, "main", StringComparison.Ordinal) &&
				String.Equals(metadata.WindowLabel, "main", StringComparison.Ordinal);
			bool compatibleMainLabels =
				metadata != null &&
				(
					String.IsNullOrEmpty(metadata.WebviewLabel) ||
					String.Equals(metadata.WebviewLabel, "main", StringComparison.Ordinal)
				) &&
				(
					String.IsNullOrEmpty(metadata.WindowLabel) ||
					String.Equals(metadata.WindowLabel, "main", StringComparison.Ordinal)
				);

			if (
				metadata != null &&
				IsMainUrl(candidateUrl) &&
				String.Equals(candidateTitle, "Modrinth App", StringComparison.Ordinal) &&
				(
					explicitMainLabels ||
					(metadata.HasAppShell && compatibleMainLabels)
				)
			)
			{
				return TargetKind.Main;
			}

			return TargetKind.Unknown;
		}

		public static bool IsAdUrl(string value)
		{
			Uri uri;
			if (!Uri.TryCreate(value, UriKind.Absolute, out uri))
			{
				return false;
			}

			return (
				String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
				String.Equals(uri.Host, "modrinth.com", StringComparison.OrdinalIgnoreCase) &&
				AdPath.IsMatch(uri.AbsolutePath)
			);
		}

		public static bool IsMainUrl(string value)
		{
			Uri uri;
			if (!Uri.TryCreate(value, UriKind.Absolute, out uri))
			{
				return false;
			}

			if (String.Equals(uri.Scheme, "tauri", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			return (
				(
					String.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
					String.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
				) &&
				String.Equals(uri.Host, "tauri.localhost", StringComparison.OrdinalIgnoreCase)
			);
		}

		public static string ShortId(string id)
		{
			if (String.IsNullOrEmpty(id))
			{
				return "(unknown)";
			}
			return id.Length <= 8 ? id : id.Substring(0, 8);
		}

		public static string SafeDisplayUrl(string value)
		{
			Uri uri;
			if (!Uri.TryCreate(value, UriKind.Absolute, out uri))
			{
				return value ?? String.Empty;
			}

			return uri.GetLeftPart(UriPartial.Path);
		}
	}

	internal static class BrowserArguments
	{
		private static readonly string[] RemoteOptionsWithValues =
		{
			"--remote-debugging-port",
			"--remote-debugging-address",
			"--remote-allow-origins"
		};

		[DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr CommandLineToArgvW(
			string commandLine,
			out int argumentCount
		);

		[DllImport("kernel32.dll")]
		private static extern IntPtr LocalFree(IntPtr memory);

		public static string Build(string existing, int port)
		{
			string[] inherited = Parse(existing);
			List<string> retained = new List<string>();

			for (int index = 0; index < inherited.Length; index++)
			{
				bool consumesValue;
				if (!IsRemoteOption(inherited[index], out consumesValue))
				{
					retained.Add(inherited[index]);
					continue;
				}

				if (
					consumesValue &&
					index + 1 < inherited.Length &&
					!inherited[index + 1].StartsWith("--", StringComparison.Ordinal)
				)
				{
					index++;
				}
			}

			retained.Add("--remote-debugging-address=127.0.0.1");
			retained.Add("--remote-debugging-port=" + port);

			List<string> quoted = new List<string>(retained.Count);
			for (int index = 0; index < retained.Count; index++)
			{
				quoted.Add(QuoteArgument(retained[index]));
			}
			return String.Join(" ", quoted.ToArray());
		}

		private static bool IsRemoteOption(string token, out bool consumesValue)
		{
			consumesValue = false;
			if (
				String.Equals(
					token,
					"--remote-debugging-pipe",
					StringComparison.OrdinalIgnoreCase
				)
			)
			{
				return true;
			}

			for (int index = 0; index < RemoteOptionsWithValues.Length; index++)
			{
				string option = RemoteOptionsWithValues[index];
				if (String.Equals(token, option, StringComparison.OrdinalIgnoreCase))
				{
					consumesValue = true;
					return true;
				}
				if (
					token.StartsWith(option + "=", StringComparison.OrdinalIgnoreCase)
				)
				{
					return true;
				}
			}
			return false;
		}

		private static string[] Parse(string commandLine)
		{
			if (String.IsNullOrWhiteSpace(commandLine))
			{
				return new string[0];
			}

			int argumentCount;
			IntPtr arguments = CommandLineToArgvW(
				"webview2.exe " + commandLine,
				out argumentCount
			);
			if (arguments == IntPtr.Zero)
			{
				throw new Win32Exception(
					Marshal.GetLastWin32Error(),
					"Could not parse inherited WebView2 browser arguments."
				);
			}

			try
			{
				List<string> result = new List<string>();
				for (int index = 1; index < argumentCount; index++)
				{
					IntPtr value = Marshal.ReadIntPtr(arguments, index * IntPtr.Size);
					result.Add(Marshal.PtrToStringUni(value) ?? String.Empty);
				}
				return result.ToArray();
			}
			finally
			{
				LocalFree(arguments);
			}
		}

		private static string QuoteArgument(string value)
		{
			if (
				value.Length > 0 &&
				value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0
			)
			{
				return value;
			}

			StringBuilder result = new StringBuilder();
			result.Append('"');
			int backslashes = 0;
			for (int index = 0; index < value.Length; index++)
			{
				char character = value[index];
				if (character == '\\')
				{
					backslashes++;
					continue;
				}

				if (character == '"')
				{
					result.Append('\\', backslashes * 2 + 1);
					result.Append('"');
					backslashes = 0;
					continue;
				}

				result.Append('\\', backslashes);
				backslashes = 0;
				result.Append(character);
			}

			result.Append('\\', backslashes * 2);
			result.Append('"');
			return result.ToString();
		}
	}

	internal static class Privacy
	{
		public static string SanitizeLogText(string value, int maximumLength)
		{
			if (String.IsNullOrEmpty(value))
			{
				return String.Empty;
			}

			StringBuilder result = new StringBuilder(
				Math.Min(value.Length, maximumLength)
			);
			for (int index = 0; index < value.Length && result.Length < maximumLength; index++)
			{
				char character = value[index];
				if (character == '\r' || character == '\n' || character == '\t')
				{
					result.Append(' ');
				}
				else if (!Char.IsControl(character))
				{
					result.Append(character);
				}
			}
			if (value.Length > maximumLength)
			{
				result.Append("...");
			}
			return result.ToString();
		}

		public static string RedactUserPath(string value)
		{
			if (String.IsNullOrWhiteSpace(value))
			{
				return value ?? String.Empty;
			}

			string redacted = ReplacePathPrefix(
				value,
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"%LOCALAPPDATA%"
			);
			if (!String.Equals(redacted, value, StringComparison.Ordinal))
			{
				return redacted;
			}

			redacted = ReplacePathPrefix(
				value,
				Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
				"%APPDATA%"
			);
			if (!String.Equals(redacted, value, StringComparison.Ordinal))
			{
				return redacted;
			}

			return ReplacePathPrefix(
				value,
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				"%USERPROFILE%"
			);
		}

		private static string ReplacePathPrefix(string value, string root, string token)
		{
			if (String.IsNullOrWhiteSpace(root))
			{
				return value;
			}

			string normalizedRoot = root.TrimEnd(
				Path.DirectorySeparatorChar,
				Path.AltDirectorySeparatorChar
			);
			if (String.Equals(value, normalizedRoot, StringComparison.OrdinalIgnoreCase))
			{
				return token;
			}

			string prefix = normalizedRoot + Path.DirectorySeparatorChar;
			if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return token + value.Substring(normalizedRoot.Length);
			}
			return value;
		}
	}

	internal static class NetworkTools
	{
		public static int GetFreeLoopbackPort()
		{
			TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
			try
			{
				listener.Start();
				return ((IPEndPoint)listener.LocalEndpoint).Port;
			}
			finally
			{
				listener.Stop();
			}
		}

		public static HttpClient CreateLoopbackClient(Uri endpoint)
		{
			HttpClientHandler handler = new HttpClientHandler();
			handler.UseProxy = false;
			handler.AllowAutoRedirect = false;
			HttpClient client = new HttpClient(handler);
			client.BaseAddress = endpoint;
			client.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
			client.MaxResponseContentBufferSize = Sidecar.MaximumJsonBytes;
			client.DefaultRequestHeaders.ConnectionClose = true;
			return client;
		}

		public static bool IsAllowedTargetWebSocket(string value, Uri endpoint)
		{
			return IsAllowedWebSocket(value, endpoint, "/devtools/page/");
		}

		public static bool IsAllowedBrowserWebSocket(string value, Uri endpoint)
		{
			return IsAllowedWebSocket(value, endpoint, "/devtools/browser/");
		}

		private static bool IsAllowedWebSocket(string value, Uri endpoint, string pathPrefix)
		{
			Uri webSocket;
			if (
				endpoint == null ||
				!Uri.TryCreate(value, UriKind.Absolute, out webSocket) ||
				!String.Equals(webSocket.Scheme, "ws", StringComparison.OrdinalIgnoreCase) ||
				!webSocket.IsLoopback ||
				webSocket.Port != endpoint.Port ||
				!webSocket.AbsolutePath.StartsWith(
					pathPrefix,
					StringComparison.Ordinal
				)
			)
			{
				return false;
			}

			return true;
		}
	}

	internal static class ProcessTools
	{
		private const int ShowWindowRestore = 9;

		[DllImport("user32.dll")]
		private static extern bool ShowWindowAsync(IntPtr window, int command);

		[DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(IntPtr window);

		public static Process FindFirst(string processName, string expectedPath)
		{
			Process[] processes;
			try
			{
				processes = Process.GetProcessesByName(processName);
			}
			catch
			{
				return null;
			}

			for (int i = 0; i < processes.Length; i++)
			{
				Process candidate = processes[i];
				if (HasExpectedPath(candidate, expectedPath))
				{
					for (int remainder = i + 1; remainder < processes.Length; remainder++)
					{
						processes[remainder].Dispose();
					}
					return candidate;
				}
				candidate.Dispose();
			}
			return null;
		}

		private static bool HasExpectedPath(Process process, string expectedPath)
		{
			if (process == null || String.IsNullOrWhiteSpace(expectedPath))
			{
				return false;
			}

			try
			{
				string actualPath = process.MainModule.FileName;
				return String.Equals(
					Path.GetFullPath(actualPath),
					Path.GetFullPath(expectedPath),
					StringComparison.OrdinalIgnoreCase
				);
			}
			catch
			{
				// Fail closed when Windows does not allow the process image to be inspected.
				return false;
			}
		}

		public static bool TryActivate(Process process)
		{
			if (!IsAlive(process))
			{
				return false;
			}

			try
			{
				process.Refresh();
				IntPtr window = process.MainWindowHandle;
				if (window == IntPtr.Zero)
				{
					return false;
				}

				ShowWindowAsync(window, ShowWindowRestore);
				return SetForegroundWindow(window);
			}
			catch
			{
				return false;
			}
		}

		public static bool IsAlive(Process process)
		{
			if (process == null)
			{
				return false;
			}

			try
			{
				return !process.HasExited;
			}
			catch
			{
				return false;
			}
		}
	}

	internal static class AppDiscovery
	{
		private const string ExecutableName = "Modrinth App.exe";

		public static string Find(string explicitPath)
		{
			if (!String.IsNullOrEmpty(explicitPath))
			{
				string normalized = NormalizeCandidate(explicitPath);
				return normalized != null && File.Exists(normalized) ? normalized : null;
			}

			List<string> candidates = new List<string>();
			string localAppData = Environment.GetFolderPath(
				Environment.SpecialFolder.LocalApplicationData
			);
			string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
			string programFilesX86 = Environment.GetFolderPath(
				Environment.SpecialFolder.ProgramFilesX86
			);

			AddCandidate(candidates, Path.Combine(localAppData, "Modrinth App", ExecutableName));
			AddCandidate(
				candidates,
				Path.Combine(localAppData, "Programs", "Modrinth App", ExecutableName)
			);
			AddCandidate(candidates, Path.Combine(programFiles, "Modrinth App", ExecutableName));
			AddCandidate(candidates, Path.Combine(programFilesX86, "Modrinth App", ExecutableName));

			AddRegistryCandidates(candidates, RegistryHive.CurrentUser);
			AddRegistryCandidates(candidates, RegistryHive.LocalMachine);

			for (int i = 0; i < candidates.Count; i++)
			{
				if (File.Exists(candidates[i]))
				{
					return Path.GetFullPath(candidates[i]);
				}
			}

			return null;
		}

		private static void AddCandidate(List<string> candidates, string path)
		{
			string normalized = NormalizeCandidate(path);
			if (normalized != null && !candidates.Contains(normalized))
			{
				candidates.Add(normalized);
			}
		}

		private static string NormalizeCandidate(string value)
		{
			if (String.IsNullOrWhiteSpace(value))
			{
				return null;
			}

			try
			{
				string path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
				if (Directory.Exists(path))
				{
					path = Path.Combine(path, ExecutableName);
				}
				if (
					!Path.IsPathRooted(path) ||
					!String.Equals(
						Path.GetFileName(path),
						ExecutableName,
						StringComparison.OrdinalIgnoreCase
					)
				)
				{
					return null;
				}
				return Path.GetFullPath(path);
			}
			catch
			{
				return null;
			}
		}

		private static void AddRegistryCandidates(
			List<string> candidates,
			RegistryHive hive
		)
		{
			RegistryView[] views =
			{
				RegistryView.Registry64,
				RegistryView.Registry32
			};
			for (int index = 0; index < views.Length; index++)
			{
				try
				{
					using (RegistryKey root = RegistryKey.OpenBaseKey(hive, views[index]))
					{
						AddAppPathCandidates(candidates, root);
						AddUninstallCandidates(candidates, root);
					}
				}
				catch
				{
				}
			}
		}

		private static void AddAppPathCandidates(List<string> candidates, RegistryKey root)
		{
			try
			{
				using (
					RegistryKey key = root.OpenSubKey(
						@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + ExecutableName
					)
				)
				{
					if (key != null)
					{
						AddCandidate(candidates, key.GetValue(null) as string);
					}
				}
			}
			catch
			{
			}
		}

		private static void AddUninstallCandidates(List<string> candidates, RegistryKey root)
		{
			try
			{
				using (
					RegistryKey uninstall = root.OpenSubKey(
						@"Software\Microsoft\Windows\CurrentVersion\Uninstall"
					)
				)
				{
					if (uninstall == null)
					{
						return;
					}

					string[] subkeyNames = uninstall.GetSubKeyNames();
					for (int i = 0; i < subkeyNames.Length; i++)
					{
						using (RegistryKey entry = uninstall.OpenSubKey(subkeyNames[i]))
						{
							if (entry == null)
							{
								continue;
							}

							string displayName = entry.GetValue("DisplayName") as string;
							if (
								String.IsNullOrEmpty(displayName) ||
								displayName.IndexOf("Modrinth", StringComparison.OrdinalIgnoreCase) < 0
							)
							{
								continue;
							}

							string installLocation = entry.GetValue("InstallLocation") as string;
							if (!String.IsNullOrEmpty(installLocation))
							{
								AddCandidate(candidates, Path.Combine(installLocation, ExecutableName));
							}

							string displayIcon = entry.GetValue("DisplayIcon") as string;
							if (!String.IsNullOrEmpty(displayIcon))
							{
								AddCandidate(candidates, ParseDisplayIcon(displayIcon));
							}
						}
					}
				}
			}
			catch
			{
			}
		}

		private static string ParseDisplayIcon(string value)
		{
			string result = Environment.ExpandEnvironmentVariables(value.Trim());
			if (result.StartsWith("\"", StringComparison.Ordinal))
			{
				int closingQuote = result.IndexOf('"', 1);
				if (closingQuote > 1)
				{
					return result.Substring(1, closingQuote - 1);
				}
			}

			int comma = result.LastIndexOf(',');
			if (comma > 0)
			{
				int iconIndex;
				if (Int32.TryParse(result.Substring(comma + 1), out iconIndex))
				{
					result = result.Substring(0, comma);
				}
			}
			return result.Trim('"');
		}
	}

	internal static class ResourceLoader
	{
		public static string ReadText(string name)
		{
			Assembly assembly = Assembly.GetExecutingAssembly();
			using (Stream stream = assembly.GetManifestResourceStream(name))
			{
				if (stream == null)
				{
					throw new InvalidOperationException("Missing embedded resource: " + name);
				}

				using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
				{
					return reader.ReadToEnd();
				}
			}
		}
	}

	internal static class SelfTests
	{
		private static int passed;
		private static int failed;

		public static bool Run()
		{
			passed = 0;
			failed = 0;

			Test("exact ad URL", TargetClassifier.IsAdUrl("https://modrinth.com/wrapper/app-ads"));
			Test(
				"cookie ad URL with query",
				TargetClassifier.IsAdUrl(
					"https://modrinth.com/wrapper/app-ads-cookie?source=desktop"
				)
			);
			Test(
				"reject unrelated Modrinth page",
				!TargetClassifier.IsAdUrl("https://modrinth.com/modpack/sodium")
			);
			Test(
				"reject lookalike domain",
				!TargetClassifier.IsAdUrl("https://modrinth.com.example/wrapper/app-ads")
			);
			Test("release Tauri URL", TargetClassifier.IsMainUrl("http://tauri.localhost/"));
			Test("reject website as main", !TargetClassifier.IsMainUrl("https://modrinth.com/"));

			CdpTarget mainTarget = new CdpTarget();
			mainTarget.type = "page";
			mainTarget.title = "Modrinth App";
			mainTarget.url = "http://tauri.localhost/";
			TargetMetadata mainMetadata = new TargetMetadata();
			mainMetadata.WindowLabel = "main";
			mainMetadata.WebviewLabel = "main";
			mainMetadata.Href = "http://tauri.localhost/";
			mainMetadata.Title = "Modrinth App";
			mainMetadata.HasAppShell = false;
			Test(
				"early main classification requires exact labels and title",
				TargetClassifier.Classify(mainTarget, mainMetadata, true) == TargetKind.Main
			);
			mainMetadata.Title = "Another Tauri App";
			Test(
				"reject generic Tauri main target",
				TargetClassifier.Classify(mainTarget, mainMetadata, true) == TargetKind.Unknown
			);

			CdpTarget blankTarget = new CdpTarget();
			blankTarget.type = "page";
			blankTarget.title = "about:blank";
			blankTarget.url = "about:blank";
			TargetMetadata adMetadata = new TargetMetadata();
			adMetadata.WindowLabel = "main";
			adMetadata.WebviewLabel = "ads-window";
			adMetadata.Href = "about:blank";
			Test(
				"allow verified transient ad only for child launch",
				TargetClassifier.Classify(blankTarget, adMetadata, true) == TargetKind.Ad &&
				TargetClassifier.Classify(blankTarget, adMetadata, false) == TargetKind.Unknown
			);
			CdpTarget wrapperTarget = new CdpTarget();
			wrapperTarget.type = "page";
			wrapperTarget.title = "Modrinth App Ad";
			wrapperTarget.url = "https://modrinth.com/wrapper/app-ads";
			TargetMetadata unlabeledWrapper = new TargetMetadata();
			unlabeledWrapper.Href = wrapperTarget.url;
			Test(
				"attach mode rejects unlabeled wrapper tab",
				TargetClassifier.Classify(wrapperTarget, unlabeledWrapper, false) ==
				TargetKind.Unknown
			);

			Uri testEndpoint = new Uri("http://127.0.0.1:54321/");
			Test(
				"allow same-port loopback target WebSocket",
				NetworkTools.IsAllowedTargetWebSocket(
					"ws://localhost:54321/devtools/page/ABC123",
					testEndpoint
				)
			);
			Test(
				"reject external target WebSocket",
				!NetworkTools.IsAllowedTargetWebSocket(
					"ws://example.com:54321/devtools/page/ABC123",
					testEndpoint
				)
			);
			Test(
				"reject wrong-port target WebSocket",
				!NetworkTools.IsAllowedTargetWebSocket(
					"ws://127.0.0.1:54322/devtools/page/ABC123",
					testEndpoint
				)
			);
			Test(
				"allow same-port browser WebSocket",
				NetworkTools.IsAllowedBrowserWebSocket(
					"ws://127.0.0.1:54321/devtools/browser/ABC123",
					testEndpoint
				)
			);

			string browserArgs = BrowserArguments.Build(
				"--disable-features=Example --remote-debugging-port=9222 --remote-allow-origins=* --remote-debugging-pipe",
				54321
			);
			Test(
				"preserve unrelated browser argument",
				browserArgs.IndexOf("--disable-features=Example", StringComparison.Ordinal) >= 0
			);
			Test(
				"replace inherited debugging port",
				browserArgs.IndexOf("9222", StringComparison.Ordinal) < 0 &&
				browserArgs.IndexOf("54321", StringComparison.Ordinal) >= 0
			);
			Test(
				"remove unsafe inherited remote flags",
				browserArgs.IndexOf("remote-allow-origins", StringComparison.OrdinalIgnoreCase) < 0 &&
				browserArgs.IndexOf("remote-debugging-pipe", StringComparison.OrdinalIgnoreCase) < 0
			);
			string quotedRemote = BrowserArguments.Build(
				"\"--remote-debugging-address=0.0.0.0\" \"--enable-features=A B\"",
				54321
			);
			Test(
				"remove quoted inherited remote address",
				quotedRemote.IndexOf("0.0.0.0", StringComparison.Ordinal) < 0 &&
				quotedRemote.IndexOf(
					"--remote-debugging-address=127.0.0.1",
					StringComparison.Ordinal
				) >= 0
			);
			Test(
				"preserve quoted unrelated browser argument",
				quotedRemote.IndexOf("--enable-features=A B", StringComparison.Ordinal) >= 0
			);
			string missingRemoteValue = BrowserArguments.Build(
				"--remote-debugging-port --disable-gpu",
				54321
			);
			Test(
				"do not consume a following switch as a missing remote value",
				missingRemoteValue.IndexOf("--disable-gpu", StringComparison.Ordinal) >= 0
			);
			string sanitizedLog = Privacy.SanitizeLogText(
				"line one\r\nline two\tend",
				100
			);
			Test(
				"sanitize log control characters",
				String.Equals(
					sanitizedLog,
					"line one  line two end",
					StringComparison.Ordinal
				)
			);
			string truncatedLog = Privacy.SanitizeLogText("123456789", 5);
			Test(
				"truncate oversized log text",
				String.Equals(truncatedLog, "12345...", StringComparison.Ordinal)
			);
			bool rejectedOversizedJson = false;
			using (
				HttpContent oversizedJson = new StreamContent(
					new MemoryStream(new byte[Sidecar.MaximumJsonBytes + 1])
				)
			)
			{
				oversizedJson.Headers.ContentLength = null;
				try
				{
					Sidecar
						.ReadLimitedJsonAsync(oversizedJson, CancellationToken.None)
						.GetAwaiter()
						.GetResult();
				}
				catch (InvalidOperationException)
				{
					rejectedOversizedJson = true;
				}
			}
			Test("reject oversized streamed CDP JSON", rejectedOversizedJson);
			Test("cancel stalled CDP response body", StalledResponseTimesOut());

			try
			{
				string main = ResourceLoader.ReadText("ModrinthNoAds.bootstrap-main.js");
				string ad = ResourceLoader.ReadText("ModrinthNoAds.bootstrap-ad.js");
				Test(
					"main bootstrap contains native hide command",
					main.IndexOf("plugin:ads|hide_ads_window", StringComparison.Ordinal) >= 0
				);
				Test(
					"main bootstrap collapses fallback promotion",
					main.IndexOf(".ad-parent", StringComparison.Ordinal) >= 0
				);
				Test(
					"main bootstrap removes the sidebar ad fade",
					main.IndexOf(".app-sidebar::after", StringComparison.Ordinal) >= 0 &&
					main.IndexOf("content:none!important", StringComparison.Ordinal) >= 0
				);
				Test(
					"ad bootstrap verifies the ad label",
					ad.IndexOf("ads-window", StringComparison.Ordinal) >= 0
				);
				Test(
					"ad bootstrap ignores provider frames",
					ad.IndexOf("window !== window.top", StringComparison.Ordinal) >= 0
				);
				Test(
					"main bootstrap returns structured health",
					main.IndexOf("hideOk:", StringComparison.Ordinal) >= 0 &&
					main.IndexOf("active:", StringComparison.Ordinal) >= 0
				);
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine("[FAIL] embedded resources: " + ex.Message);
				failed++;
			}

			Dictionary<string, object> failedStatus = new Dictionary<string, object>();
			failedStatus["kind"] = "main";
			failedStatus["verified"] = true;
			failedStatus["hideOk"] = false;
			failedStatus["wrapperInstalled"] = false;
			failedStatus["styleInstalled"] = true;
			failedStatus["active"] = true;
			BootstrapHealth failedHealth = BootstrapHealth.FromValue(failedStatus);
			Test(
				"native hide failure cannot report active",
				failedHealth != null && !failedHealth.Active
			);

			Console.WriteLine();
			Console.WriteLine("Self-test: " + passed + " passed, " + failed + " failed.");
			return failed == 0;
		}

		private static void Test(string name, bool condition)
		{
			if (condition)
			{
				Console.WriteLine("[PASS] " + name);
				passed++;
			}
			else
			{
				Console.Error.WriteLine("[FAIL] " + name);
				failed++;
			}
		}

		private static bool StalledResponseTimesOut()
		{
			TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
			Task server = null;
			using (ManualResetEventSlim headersSent = new ManualResetEventSlim(false))
			using (ManualResetEventSlim releaseServer = new ManualResetEventSlim(false))
			{
				try
				{
					listener.Start();
					int port = ((IPEndPoint)listener.LocalEndpoint).Port;
					server = Task.Run(
						delegate
						{
							try
							{
								using (TcpClient connection = listener.AcceptTcpClient())
								using (NetworkStream stream = connection.GetStream())
								{
									byte[] headers = Encoding.ASCII.GetBytes(
										"HTTP/1.1 200 OK\r\n" +
										"Content-Type: application/json\r\n" +
										"Content-Length: 1\r\n" +
										"Connection: close\r\n\r\n"
									);
									stream.Write(headers, 0, headers.Length);
									stream.Flush();
									headersSent.Set();
									releaseServer.Wait(3000);
								}
							}
							catch (ObjectDisposedException)
							{
							}
							catch (SocketException)
							{
							}
						}
					);

					using (
						HttpClient client = NetworkTools.CreateLoopbackClient(
							new Uri("http://127.0.0.1:" + port + "/")
						)
					)
					{
						Stopwatch elapsed = Stopwatch.StartNew();
						Task<string> request = Sidecar.ReadEndpointJsonAsync(
							client,
							"json/list",
							250,
							CancellationToken.None
						);
						bool receivedHeaders = headersSent.Wait(1000);
						bool cancelled = false;
						try
						{
							request.GetAwaiter().GetResult();
						}
						catch (OperationCanceledException)
						{
							cancelled = true;
						}
						elapsed.Stop();
						return
							receivedHeaders &&
							cancelled &&
							elapsed.Elapsed < TimeSpan.FromSeconds(1);
					}
				}
				catch
				{
					return false;
				}
				finally
				{
					releaseServer.Set();
					listener.Stop();
					if (server != null)
					{
						try
						{
							server.Wait(1000);
						}
						catch (AggregateException)
						{
						}
					}
				}
			}
		}
	}
}
