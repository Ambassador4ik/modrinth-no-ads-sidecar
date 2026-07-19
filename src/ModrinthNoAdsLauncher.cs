using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

[assembly: System.Reflection.AssemblyTitle("Modrinth No-Ads Background Launcher")]
[assembly: System.Reflection.AssemblyDescription(
	"Starts Modrinth No-Ads Sidecar without opening a console window."
)]
[assembly: System.Reflection.AssemblyCompany("Ambassador4ik")]
[assembly: System.Reflection.AssemblyProduct("Modrinth No-Ads Background Launcher")]
[assembly: System.Reflection.AssemblyCopyright(
	"Copyright (c) 2026 Ambassador4ik and contributors. MIT License."
)]
[assembly: System.Reflection.AssemblyVersion("1.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.2.0.0")]

namespace ModrinthNoAdsBackgroundLauncher
{
	internal static class Program
	{
		private const uint MessageBoxIconError = 0x10;
		private const string SidecarFileName = "ModrinthNoAdsSidecar.exe";
		private const string ProtectionActiveEventName =
			@"Local\ModrinthNoAdsSidecar.ProtectionActive";
		private const int StartupWaitMilliseconds = 45000;

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		private static extern int MessageBoxW(
			IntPtr owner,
			string text,
			string caption,
			uint type
		);

		[STAThread]
		private static int Main(string[] args)
		{
			try
			{
				string directory = AppDomain.CurrentDomain.BaseDirectory;
				string sidecarPath = Path.Combine(directory, SidecarFileName);
				if (!File.Exists(sidecarPath))
				{
					ShowError(
						SidecarFileName +
						" was not found next to the background launcher. Reinstall the shortcut integration."
					);
					return 3;
				}

				ProcessStartInfo startInfo = new ProcessStartInfo();
				bool diagnostic = IsDiagnosticInvocation(args);
				startInfo.FileName = sidecarPath;
				startInfo.Arguments = JoinArguments(args);
				startInfo.WorkingDirectory = directory;
				startInfo.UseShellExecute = false;
				startInfo.CreateNoWindow = true;
				startInfo.WindowStyle = ProcessWindowStyle.Hidden;

				using (
					EventWaitHandle protectionEvent = diagnostic
						? null
						: new EventWaitHandle(
							false,
							EventResetMode.ManualReset,
							ProtectionActiveEventName
						)
				)
				using (Process process = Process.Start(startInfo))
				{
					if (process == null)
					{
						ShowError("Windows did not start the background sidecar.");
						return 4;
					}

					if (diagnostic)
					{
						process.WaitForExit();
						return process.ExitCode;
					}

					DateTime deadline = DateTime.UtcNow.AddMilliseconds(
						StartupWaitMilliseconds
					);
					DateTime protectionObservedAt = DateTime.MinValue;
					while (DateTime.UtcNow < deadline)
					{
						if (process.HasExited)
						{
							if (process.ExitCode != 0)
							{
								ShowSidecarError(process.ExitCode);
								return process.ExitCode;
							}
						}

						bool protectionActive = protectionEvent.WaitOne(100);
						if (
							protectionActive &&
							IsExpectedSidecarRunning(sidecarPath)
						)
						{
							if (protectionObservedAt == DateTime.MinValue)
							{
								protectionObservedAt = DateTime.UtcNow;
							}
							else if (
								DateTime.UtcNow - protectionObservedAt >
								TimeSpan.FromMilliseconds(500)
							)
							{
								return 0;
							}
							Thread.Sleep(50);
						}
						else
						{
							protectionObservedAt = DateTime.MinValue;
						}
					}

					ShowError(
						"Ad suppression was not verified within 45 seconds. " +
						"Modrinth may have changed or WebView2 may be unavailable." +
						Environment.NewLine +
						Environment.NewLine +
						"Fully quit Modrinth and inspect: " +
						GetLogPath()
					);
					return 6;
				}
			}
			catch (Exception ex)
			{
				ShowError("Could not start Modrinth: " + ex.Message);
				return 7;
			}
		}

		private static void ShowSidecarError(int exitCode)
		{
			ShowError(
				"The background sidecar stopped with error " +
				exitCode +
				"." +
				Environment.NewLine +
				Environment.NewLine +
				"Diagnostics: " +
				GetLogPath()
			);
		}

		private static string GetLogPath()
		{
			return Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"ModrinthNoAdsSidecar",
				"sidecar.log"
			);
		}

		private static bool IsExpectedSidecarRunning(string expectedPath)
		{
			Process[] processes;
			try
			{
				processes = Process.GetProcessesByName(
					Path.GetFileNameWithoutExtension(expectedPath)
				);
			}
			catch
			{
				return false;
			}

			bool found = false;
			for (int index = 0; index < processes.Length; index++)
			{
				try
				{
					string actualPath = processes[index].MainModule.FileName;
					if (
						String.Equals(
							Path.GetFullPath(actualPath),
							Path.GetFullPath(expectedPath),
							StringComparison.OrdinalIgnoreCase
						)
					)
					{
						found = true;
					}
				}
				catch
				{
				}
				finally
				{
					processes[index].Dispose();
				}
			}
			return found;
		}

		private static bool IsDiagnosticInvocation(string[] arguments)
		{
			if (arguments == null)
			{
				return false;
			}

			for (int i = 0; i < arguments.Length; i++)
			{
				string value = arguments[i] ?? String.Empty;
				if (
					String.Equals(value, "--check", StringComparison.OrdinalIgnoreCase) ||
					String.Equals(value, "--help", StringComparison.OrdinalIgnoreCase) ||
					String.Equals(value, "--self-test", StringComparison.OrdinalIgnoreCase) ||
					String.Equals(value, "--version", StringComparison.OrdinalIgnoreCase)
				)
				{
					return true;
				}
			}

			return false;
		}

		private static string JoinArguments(string[] arguments)
		{
			if (arguments == null || arguments.Length == 0)
			{
				return String.Empty;
			}

			List<string> quoted = new List<string>(arguments.Length);
			for (int i = 0; i < arguments.Length; i++)
			{
				quoted.Add(QuoteArgument(arguments[i] ?? String.Empty));
			}
			return String.Join(" ", quoted.ToArray());
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
			for (int i = 0; i < value.Length; i++)
			{
				char character = value[i];
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

		private static void ShowError(string message)
		{
			MessageBoxW(
				IntPtr.Zero,
				message,
				"Modrinth No-Ads Background Launcher",
				MessageBoxIconError
			);
		}
	}
}
