using NINA.Core.Utility;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.OpenCode.OpenCode {

    /// <summary>
    /// Starts and stops a local opencode server, and nothing else. This plugin does not
    /// download, install, update or repair opencode: the user installs it, the plugin runs
    /// the copy it finds. That boundary is deliberate. Fetching and executing a 171 MB
    /// binary from inside a NINA plugin is a promise about someone else's software that
    /// this plugin is in no position to keep, and an observatory machine is the last place
    /// to discover a failed download at two in the morning.
    /// </summary>
    internal sealed class OpenCodeServerProcess : IDisposable {

        /// <summary>The line opencode prints once it is accepting requests, e.g. "opencode server listening on http://127.0.0.1:47821".</summary>
        private static readonly Regex ListeningLine = new Regex(
            @"listening on\s+(?<url>https?://\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private Process? process;

        public Uri? ServerUrl { get; private set; }
        public string? Password { get; private set; }
        public bool IsRunning => process is { HasExited: false };

        /// <summary>
        /// The names PATH may hold, most directly runnable first. An npm install leaves a
        /// .cmd shim rather than an executable, and a shim cannot be started directly with
        /// UseShellExecute off - which is how "opencode is right there on my PATH" turns
        /// into "the system cannot find the file specified".
        /// </summary>
        private static readonly string[] CandidateNames = { "opencode.exe", "opencode.cmd", "opencode.bat", "opencode" };

        /// <summary>
        /// Finds the file to run. An explicit path wins; otherwise PATH is searched here
        /// rather than left to the process launcher, so the answer is a real file that can
        /// be reported, checked and shown to the user.
        /// </summary>
        public static string? ResolveExecutable(string? configuredPath) {
            if (!string.IsNullOrWhiteSpace(configuredPath)) {
                var explicitPath = configuredPath!.Trim();
                return File.Exists(explicitPath) ? explicitPath : null;
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var name in CandidateNames) {
                foreach (var dir in path.Split(Path.PathSeparator)) {
                    if (string.IsNullOrWhiteSpace(dir)) { continue; }
                    try {
                        var candidate = Path.Combine(dir.Trim(), name);
                        if (File.Exists(candidate)) { return candidate; }
                    } catch (ArgumentException) {
                        // A malformed PATH entry is not a reason to stop looking at the rest.
                    }
                }
            }
            return null;
        }

        /// <summary>Whether the executable can actually be found before trying to run it.</summary>
        public static bool Exists(string? configuredPath) => ResolveExecutable(configuredPath) != null;

        /// <summary>
        /// Launches the server and waits for it to announce its address. The port is read
        /// from that announcement rather than assumed, so port 0 can be used to let the
        /// machine pick a free one - which matters on a PC that is also running NINA's own
        /// services, a guider and whatever else the observatory has bound.
        /// </summary>
        public async Task<Uri> StartAsync(string? executablePath, string? workingDirectory, int port,
                                          string? password, CancellationToken token) {
            if (IsRunning) { return ServerUrl!; }

            // Only the password the user asked for, and none if they asked for none.
            //
            // This used to generate one per launch, on the reasoning that an unpassworded
            // opencode server accepts a prompt from anything that can reach its port. The
            // reasoning was right and the conclusion was wrong twice over. The server is
            // bound to 127.0.0.1, so "anything that can reach the port" means a process
            // already running as this user - which could equally read the password out of the
            // configuration. And the embedded browser turned out not to answer the
            // authentication challenge at all: WebView2 never raised the event for it, so the
            // panel got a 401 and stayed blank, with the credential the plugin had invented
            // sitting in memory where nobody could type it.
            //
            // What actually keeps this server private is the hostname it listens on.
            Password = string.IsNullOrWhiteSpace(password) ? null : password!.Trim();

            var exe = ResolveExecutable(executablePath);
            if (exe == null) {
                throw new OpenCodeException(
                    "opencode was not found. Install it from https://opencode.ai, or set the full path to it in " +
                    "Options - this plugin does not install it.");
            }

            var startInfo = new ProcessStartInfo {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : workingDirectory!
            };

            // A .cmd or .bat is a script, not an image the process loader can run, and with
            // UseShellExecute off Windows refuses it with "the system cannot find the file
            // specified" - which names the wrong problem entirely. An npm install of
            // opencode leaves exactly such a shim on PATH, so it is run through the command
            // interpreter, which is what a shell would have done anyway.
            var isShim = exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                      || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
            if (isShim) {
                startInfo.FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add(exe);
            } else {
                startInfo.FileName = exe;
            }

            startInfo.ArgumentList.Add("serve");
            startInfo.ArgumentList.Add("--hostname");
            startInfo.ArgumentList.Add("127.0.0.1");
            startInfo.ArgumentList.Add("--port");
            startInfo.ArgumentList.Add(port.ToString());
            if (Password != null) {
                startInfo.EnvironmentVariables["OPENCODE_SERVER_PASSWORD"] = Password;
            }

            var announced = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);

            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => OnLine(e.Data, announced);
            process.ErrorDataReceived += (_, e) => OnLine(e.Data, announced);
            process.Exited += (_, _) => announced.TrySetException(
                new OpenCodeException($"opencode exited with code {process?.ExitCode} before it began listening"));

            try {
                process.Start();
            } catch (Exception ex) {
                // A Process that never started has no handle behind it, so every later
                // question about it throws too. Letting it go here keeps a failed launch to
                // one honest error instead of following it with a misleading second one
                // about not being able to stop the server cleanly.
                process.Dispose();
                process = null;
                throw new OpenCodeException(
                    $"Could not start '{exe}'. Install opencode from https://opencode.ai, or set its full path in " +
                    $"Options - the plugin does not install it. ({ex.Message})");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            using (cts.Token.Register(() => announced.TrySetCanceled())) {
                ServerUrl = await announced.Task.ConfigureAwait(false);
            }

            Logger.Info($"OpenCode: server started at {ServerUrl}");
            return ServerUrl!;
        }

        private void OnLine(string? line, TaskCompletionSource<Uri> announced) {
            if (string.IsNullOrWhiteSpace(line)) { return; }
            Logger.Debug($"OpenCode: {line}");

            var match = ListeningLine.Match(line!);
            if (match.Success && Uri.TryCreate(match.Groups["url"].Value, UriKind.Absolute, out var url)) {
                announced.TrySetResult(url);
            }
        }

        /// <summary>
        /// Stops the server this plugin started. A server the user was already running is
        /// never touched: it is not ours to close, and someone may be working in it.
        /// </summary>
        public void Stop() {
            if (process == null) { return; }
            try {
                if (!process.HasExited) {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            } catch (Exception ex) {
                Logger.Warning($"OpenCode: could not stop the server cleanly ({ex.Message})");
            } finally {
                process.Dispose();
                process = null;
                ServerUrl = null;
            }
        }

        public void Dispose() => Stop();
    }
}
