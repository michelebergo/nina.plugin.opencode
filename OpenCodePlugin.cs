using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Plugin.OpenCode.OpenCode;
using NINA.Plugin.OpenCode.Properties;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace NINA.Plugin.OpenCode {

    /// <summary>
    /// Plugin entry point and the home of the settings the options page binds to.
    ///
    /// The plugin is a client and only a client: it holds no model, no API key and no tool
    /// of its own. Everything the agent can do comes from the user's own opencode
    /// installation and its configuration, which is also where it must be changed.
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class OpenCodePlugin : PluginBase, INotifyPropertyChanged {

        public static OpenCodePlugin? Instance { get; private set; }

        private readonly Observatory.ObservatoryWatcher? watcher;

        /// <summary>
        /// Every mediator is imported with AllowDefault, which turns an export N.I.N.A. does
        /// not offer into a null instead of a plugin that refuses to load. The recorder is a
        /// convenience; the panel is the plugin. Trading the second for the first would be a
        /// bad bargain, and composition failures are reported in a way that sends people
        /// looking at opencode rather than at a missing import.
        /// </summary>
        [ImportingConstructor]
        public OpenCodePlugin(IProfileService profileService,
            [ImportMany] IEnumerable<ResourceDictionary> resourceDictionaries,
            [Import(AllowDefault = true)] ICameraMediator? cameraMediator,
            [Import(AllowDefault = true)] ITelescopeMediator? telescopeMediator,
            [Import(AllowDefault = true)] IGuiderMediator? guiderMediator,
            [Import(AllowDefault = true)] IFocuserMediator? focuserMediator,
            [Import(AllowDefault = true)] IDomeMediator? domeMediator,
            [Import(AllowDefault = true)] ISafetyMonitorMediator? safetyMonitorMediator,
            [Import(AllowDefault = true)] IWeatherDataMediator? weatherDataMediator,
            [Import(AllowDefault = true)] IImageSaveMediator? imageSaveMediator) {

            Instance = this;

            foreach (var dictionary in resourceDictionaries) {
                Application.Current?.Resources.MergedDictionaries.Add(dictionary);
            }

            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }

            watcher = new Observatory.ObservatoryWatcher(
                new Observatory.ObservatoryLog(Observatory.ObservatoryLog.DefaultDirectory),
                cameraMediator, telescopeMediator, guiderMediator, focuserMediator,
                domeMediator, safetyMonitorMediator, weatherDataMediator, imageSaveMediator);

            if (RecordObservatoryLog) { watcher.Start(); }

            Logger.Info("OpenCode plugin loaded");
        }

        public override Task Teardown() {
            watcher?.Stop();
            return base.Teardown();
        }

        #region Browse commands

        /// <summary>
        /// The three paths this plugin asks for are a program, a script and a folder, and
        /// typing any of them by hand is both tedious and the easiest way to end up with a
        /// setting that looks right and is not. Each box gets a picker that starts where the
        /// current value points.
        /// </summary>
        public System.Windows.Input.ICommand BrowseExecutableCommand => new CommunityToolkit.Mvvm.Input.RelayCommand(() => {
            var picked = PickFile("Select opencode", "opencode (opencode.exe;opencode.cmd)|opencode.exe;opencode.cmd|All files (*.*)|*.*", ExecutablePath);
            if (picked != null) { ExecutablePath = picked; }
        });

        public System.Windows.Input.ICommand BrowseWorkingDirectoryCommand => new CommunityToolkit.Mvvm.Input.RelayCommand(() => {
            var picked = PickFolder("Select the folder the agent works in", WorkingDirectory);
            if (picked != null) { WorkingDirectory = picked; }
        });

        public System.Windows.Input.ICommand BrowseNinaMcpServerCommand => new CommunityToolkit.Mvvm.Input.RelayCommand(() => {
            var picked = PickFile("Select nina_advanced_mcp.py", "Python (*.py)|*.py|All files (*.*)|*.*", NinaMcpServerPath);
            if (picked != null) { NinaMcpServerPath = picked; }
        });

        private static string? PickFile(string title, string filter, string current) {
            var dialog = new Microsoft.Win32.OpenFileDialog {
                Title = title,
                Filter = filter,
                CheckFileExists = true
            };
            TrySetInitialDirectory(current, path => dialog.InitialDirectory = path);
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private static string? PickFolder(string title, string current) {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title };
            TrySetInitialDirectory(current, path => dialog.InitialDirectory = path);
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }

        /// <summary>Opens the picker where the current value points, when it still exists.</summary>
        private static void TrySetInitialDirectory(string current, Action<string> apply) {
            if (string.IsNullOrWhiteSpace(current)) { return; }
            try {
                var directory = System.IO.Directory.Exists(current)
                    ? current
                    : System.IO.Path.GetDirectoryName(current);
                if (!string.IsNullOrEmpty(directory) && System.IO.Directory.Exists(directory)) {
                    apply(directory!);
                }
            } catch (ArgumentException) {
                // An unusable stored value is not worth failing a dialog over.
            }
        }

        #endregion

        #region Connection

        /// <summary>"Launch" (start a local server) or "Attach" (use one already running).</summary>
        public string ConnectionMode {
            get => Settings.Default.ConnectionMode ?? "Launch";
            set {
                Settings.Default.ConnectionMode = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(IsLaunchMode));
                RaisePropertyChanged(nameof(IsAttachMode));
            }
        }

        public bool IsLaunchMode => !string.Equals(ConnectionMode, "Attach", StringComparison.OrdinalIgnoreCase);
        public bool IsAttachMode => !IsLaunchMode;

        public string ExecutablePath {
            get => Settings.Default.ExecutablePath ?? string.Empty;
            set {
                Settings.Default.ExecutablePath = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ExecutableStatus));
            }
        }

        /// <summary>
        /// What the options page says under the path box. The plugin will not install
        /// opencode, so the one useful thing it can do is say plainly whether it can see it.
        /// </summary>
        public string ExecutableStatus
            => OpenCodeServerProcess.Exists(ExecutablePath)
                ? "opencode found."
                : "opencode not found. Install it from https://opencode.ai, then set the full path here if it is not on PATH.";

        public string ServerUrl {
            get => Settings.Default.ServerUrl ?? "http://127.0.0.1:4096";
            set {
                Settings.Default.ServerUrl = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Where the agent works. Empty means the plugin's own workspace, which it creates:
        /// the plugin has to be useful before anybody has opened its options.
        /// </summary>
        public string WorkingDirectory {
            get {
                var configured = Settings.Default.WorkingDirectory;
                return string.IsNullOrWhiteSpace(configured) ? OpenCode.NinaWorkspace.DefaultDirectory : configured!;
            }
            set {
                Settings.Default.WorkingDirectory = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        public string ServerPassword {
            get => Settings.Default.ServerPassword ?? string.Empty;
            set {
                Settings.Default.ServerPassword = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        public string AgentName {
            get => Settings.Default.AgentName ?? string.Empty;
            set {
                Settings.Default.AgentName = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Where nina_advanced_mcp.py lives. Empty means the agent gets the brief but no
        /// tools, which is a usable state - it can still read logs and explain things - but
        /// it cannot ask NINA anything.
        /// </summary>
        public string NinaMcpServerPath {
            get => Settings.Default.NinaMcpServerPath ?? string.Empty;
            set {
                Settings.Default.NinaMcpServerPath = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(NinaMcpStatus));
            }
        }

        /// <summary>
        /// What the options page says under the path box, and the only place the automatic
        /// search becomes visible: a plugin that quietly found the file should say where,
        /// and one that did not should say what to do about it.
        /// </summary>
        public string NinaMcpStatus {
            get {
                var configured = NinaMcpServerPath;
                var found = OpenCode.NinaWorkspace.LocateMcpServer(configured);

                if (found != null && string.IsNullOrWhiteSpace(configured)) {
                    return $"Found automatically at {found} - it will be registered on connect.";
                }
                if (found != null) {
                    return "Found. It will be registered in the working directory on connect.";
                }
                if (!string.IsNullOrWhiteSpace(configured)) {
                    return "That file does not exist.";
                }
                return "Not found on this machine. It will be fetched from " +
                       $"{OpenCode.NinaWorkspace.McpServerRepository} into the working directory the first time " +
                       "you connect, which needs git and a network connection. Set a path here to use your own copy.";
            }
        }

        public string NinaApiHost {
            get => Settings.Default.NinaApiHost ?? "localhost";
            set {
                Settings.Default.NinaApiHost = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int NinaApiPort {
            get => Settings.Default.NinaApiPort;
            set {
                Settings.Default.NinaApiPort = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        #region Local model

        /// <summary>
        /// Run the agent on a model served locally rather than on whatever provider opencode
        /// is configured with. For an observatory this is not only about cost: a machine in a
        /// field with a tethered connection can still have an agent, and nothing about the
        /// night leaves the property.
        /// </summary>
        public bool UseLocalModel {
            get => Settings.Default.UseLocalModel;
            set {
                Settings.Default.UseLocalModel = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        public string LocalModelHost {
            get => Settings.Default.LocalModelHost ?? "http://localhost:11434";
            set {
                Settings.Default.LocalModelHost = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        public string LocalModelId {
            get => Settings.Default.LocalModelId ?? string.Empty;
            set {
                Settings.Default.LocalModelId = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        private string localModelStatus = "Press Check to see which models this host is serving.";
        public string LocalModelStatus {
            get => localModelStatus;
            private set { localModelStatus = value; RaisePropertyChanged(); }
        }

        /// <summary>
        /// Asks the host what it has. Typing a model name from memory is how you end up with
        /// an agent that fails on its first message with an error from a provider you have
        /// never seen, so the names come from the machine that will serve them.
        /// </summary>
        public System.Windows.Input.ICommand CheckLocalModelsCommand
            => new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(async () => {
                Logger.Info($"OpenCode: checking models on {LocalModelHost}");
                LocalModelStatus = "Asking " + LocalModelHost + "...";
                var models = await OpenCode.LocalModels.ListAsync(LocalModelHost, System.Threading.CancellationToken.None);
                if (models == null) {
                    LocalModelStatus = $"No answer from {LocalModelHost}: "
                                     + (OpenCode.LocalModels.LastError ?? "no reason given")
                                     + ". The address needs the scheme, like http://astro10:11434";
                    return;
                }
                if (models.Count == 0) {
                    LocalModelStatus = "That host is answering but has no models pulled yet.";
                    return;
                }

                // Ollama serves cloud-backed models from the same endpoint as the ones on the
                // machine, and they are indistinguishable in the list except for the suffix.
                // Saying "3 available" of three cloud models on localhost is how somebody
                // concludes their local models are missing when they are simply on another
                // host - so the two are counted separately and named for what they are.
                var onThisMachine = models.FindAll(m => !m.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase));
                var cloud = models.Count - onThisMachine.Count;

                if (onThisMachine.Count == 0) {
                    LocalModelStatus = $"{cloud} model(s) here, but all of them are cloud-backed (:cloud) - " +
                                       "they run on Ollama's servers, not on this machine. Point this at a host " +
                                       "that has models pulled locally.";
                    return;
                }

                LocalModelStatus = $"{onThisMachine.Count} on that machine: {string.Join(", ", onThisMachine)}"
                                 + (cloud > 0 ? $" (plus {cloud} cloud-backed, not listed)" : string.Empty);
                if (string.IsNullOrWhiteSpace(LocalModelId)) { LocalModelId = onThisMachine[0]; }
            });

        #endregion

        /// <summary>
        /// Whether the plugin writes down what changes in the observatory. Switching it off
        /// stops the recording immediately rather than at the next restart, because a setting
        /// that only takes effect later is a setting people believe they have already changed.
        /// </summary>
        public bool RecordObservatoryLog {
            get => Settings.Default.RecordObservatoryLog;
            set {
                Settings.Default.RecordObservatoryLog = value;
                CoreUtil.SaveSettings(Settings.Default);
                if (value) { watcher?.Start(); } else { watcher?.Stop(); }
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ObservatoryLogStatus));
            }
        }

        /// <summary>Where the file is, said plainly, so it can be found without searching.</summary>
        public string ObservatoryLogStatus
            => RecordObservatoryLog
                ? "Recording to " + Observatory.ObservatoryLog.DefaultDirectory
                : "Not recording. Nothing is written and nothing is watched.";

        public bool AutoConnect {
            get => Settings.Default.AutoConnect;
            set {
                Settings.Default.AutoConnect = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int LaunchPort {
            get => Settings.Default.LaunchPort;
            set {
                Settings.Default.LaunchPort = value < 0 ? 0 : value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        #endregion
    }
}
