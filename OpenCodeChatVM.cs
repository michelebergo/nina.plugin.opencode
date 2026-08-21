using CommunityToolkit.Mvvm.Input;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Plugin.OpenCode.OpenCode;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;

namespace NINA.Plugin.OpenCode {

    /// <summary>
    /// The dock panel: opencode itself, inside N.I.N.A.
    ///
    /// An earlier version of this panel re-implemented the conversation in WPF - messages,
    /// tool calls, permission prompts, a usage readout - and every piece had to be measured
    /// against opencode's API and then kept in step with it. Then the editor extension
    /// turned out to be three commands and a terminal: what people admire about opencode in
    /// an editor is opencode's own interface, not anything the editor draws.
    ///
    /// The server already serves that interface, so this panel hosts it. What is left is the
    /// part only this plugin can do: start or find the server, tell the agent it is attached
    /// to an observatory, and then get out of the way. Nothing here needs changing when
    /// opencode changes how it presents a tool call.
    /// </summary>
    [Export(typeof(IDockableVM))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class OpenCodeChatVM : DockableVM {

        private OpenCodeServerProcess? server;

        /// <summary>
        /// The agent a new session opens with unless the user named another. The read-only
        /// one on purpose: it can explain the session without a single permission prompt, and
        /// switching to the one that can act is a deliberate choice made in the interface.
        /// </summary>
        private const string DefaultAgent = "observer";

        [ImportingConstructor]
        public OpenCodeChatVM(IProfileService profileService) : base(profileService) {
            Title = "OpenCode";

            // The same mark as the plugin's icon, drawn as geometry for the imaging tab: the
            // upright frame with its lower half filled, and a star beside it. An icon that
            // differs between the plugin list and the tab strip is two plugins as far as the
            // eye is concerned.
            //
            // The group fills EvenOdd, which is what makes this work in one colour: the outer
            // rectangle fills, the inner one cuts a window out of it, and the block inside
            // that window is enclosed by two boundaries again, so it fills.
            var geometry = new GeometryGroup();
            geometry.Children.Add(new RectangleGeometry(new System.Windows.Rect(2.5, 1.5, 8, 12)));
            geometry.Children.Add(new RectangleGeometry(new System.Windows.Rect(4.2, 3.2, 4.6, 8.6)));
            geometry.Children.Add(new RectangleGeometry(new System.Windows.Rect(4.2, 7.6, 4.6, 4.2)));

            var star = new PathGeometry();
            var starFig = new PathFigure { StartPoint = new System.Windows.Point(13.2, 1.2) };
            starFig.Segments.Add(new LineSegment(new System.Windows.Point(13.9, 2.9), true));
            starFig.Segments.Add(new LineSegment(new System.Windows.Point(15.6, 3.6), true));
            starFig.Segments.Add(new LineSegment(new System.Windows.Point(13.9, 4.3), true));
            starFig.Segments.Add(new LineSegment(new System.Windows.Point(13.2, 6.0), true));
            starFig.Segments.Add(new LineSegment(new System.Windows.Point(12.5, 4.3), true));
            starFig.Segments.Add(new LineSegment(new System.Windows.Point(10.8, 3.6), true));
            starFig.Segments.Add(new LineSegment(new System.Windows.Point(12.5, 2.9), true));
            starFig.IsClosed = true;
            star.Figures.Add(starFig);
            geometry.Children.Add(star);

            geometry.Freeze();
            ImageGeometry = geometry;

            ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy && !IsConnected);
            DisconnectCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(Disconnect, () => IsConnected);
        }

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }

        private void RefreshCommands() {
            (ConnectCommand as IRelayCommand)?.NotifyCanExecuteChanged();
            (DisconnectCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        }

        private string status = "Not connected.";
        public string Status {
            get => status;
            private set { status = value; RaisePropertyChanged(); }
        }

        private string? serverAddress;
        /// <summary>
        /// Where opencode's own interface is being served. The view watches this and
        /// navigates to it; null means show nothing, rather than an error page.
        /// </summary>
        public string? ServerAddress {
            get => serverAddress;
            private set { serverAddress = value; RaisePropertyChanged(); }
        }

        /// <summary>
        /// The password the server is using, so the embedded browser can answer the
        /// authentication challenge itself instead of putting a login box in front of the
        /// user for a server this plugin just started.
        /// </summary>
        public string? ServerPassword { get; private set; }

        private bool isConnected;
        public bool IsConnected {
            get => isConnected;
            private set { isConnected = value; RaisePropertyChanged(); RefreshCommands(); }
        }

        private bool isBusy;
        public bool IsBusy {
            get => isBusy;
            private set { isBusy = value; RaisePropertyChanged(); RefreshCommands(); }
        }

        private bool autoConnectAttempted;

        /// <summary>
        /// Called when the panel becomes visible. Connects once per session if the user has
        /// not turned that off - and only once, so a docking layout that shows and hides the
        /// panel does not start a server every time it comes back.
        /// </summary>
        public void OnPanelShown() {
            if (autoConnectAttempted || IsConnected || IsBusy) { return; }
            if (OpenCodePlugin.Instance?.AutoConnect != true) { return; }
            autoConnectAttempted = true;
            _ = ConnectAsync();
        }

        private async Task ConnectAsync() {
            var plugin = OpenCodePlugin.Instance;
            if (plugin == null) { return; }

            IsBusy = true;
            try {
                Uri baseUrl;

                if (plugin.IsAttachMode) {
                    if (!Uri.TryCreate(plugin.ServerUrl, UriKind.Absolute, out var parsed)) {
                        Status = $"'{plugin.ServerUrl}' is not a valid address.";
                        return;
                    }
                    baseUrl = parsed;
                    ServerPassword = plugin.ServerPassword;
                    Status = $"Attaching to {baseUrl}...";
                } else {
                    if (!OpenCodeServerProcess.Exists(plugin.ExecutablePath)) {
                        Status = "opencode was not found. Install it from https://opencode.ai and set its path in " +
                                 "Options; this plugin does not install it.";
                        return;
                    }

                    // Said out loud because it is a setting whose effect is invisible until
                    // something goes wrong: the panel looks identical whether the agent is
                    // running on a model down the hall or on somebody's API.
                    // Asked at connect rather than remembered from the options panel: the
                    // list belongs to the machine serving it, and a model pulled or removed
                    // since the last time somebody opened the options should show up here.
                    System.Collections.Generic.IReadOnlyList<string>? localModels = null;
                    if (plugin.UseLocalModel) {
                        localModels = await LocalModels.ListAsync(plugin.LocalModelHost, CancellationToken.None);
                    }

                    Logger.Info(plugin.UseLocalModel
                        ? $"OpenCode: local model on - {plugin.LocalModelHost} / {plugin.LocalModelId}"
                        : "OpenCode: local model off - using whatever provider opencode is configured with");

                    // The tools first: without them the agent knows it is attached to an
                    // observatory and has no way to ask it anything, which is worse than
                    // useless because it looks like it should work.
                    var mcpServer = await NinaWorkspace.EnsureMcpServerAsync(
                        plugin.WorkingDirectory, plugin.NinaMcpServerPath,
                        message => Status = message, CancellationToken.None);

                    // Written before the server starts, because opencode reads AGENTS.md and
                    // opencode.json for the directory when a session is created. Doing it
                    // afterwards would give the agent its N.I.N.A. brief one session too late.
                    Logger.Info("OpenCode: " + NinaWorkspace.Prepare(
                        plugin.WorkingDirectory, mcpServer, plugin.NinaApiHost, plugin.NinaApiPort,
                        plugin.UseLocalModel ? plugin.LocalModelHost : null,
                        plugin.UseLocalModel ? plugin.LocalModelId : null,
                        localModels));

                    Status = "Starting opencode...";
                    server = new OpenCodeServerProcess();
                    baseUrl = await server.StartAsync(plugin.ExecutablePath, plugin.WorkingDirectory,
                                                     plugin.LaunchPort, plugin.ServerPassword, CancellationToken.None);
                    ServerPassword = server.Password;
                }

                Uri? sessionUrl;
                using (var probe = new OpenCodeClient(baseUrl, ServerPassword)) {
                    if (!await probe.IsHealthyAsync(CancellationToken.None)) {
                        Status = $"No opencode server answered at {baseUrl}.";
                        Disconnect();
                        return;
                    }

                    // Opened here rather than left to the user: the interface otherwise lands
                    // on an empty project picker, which asks them to do the one piece of setup
                    // the plugin already has everything it needs to do.
                    sessionUrl = await probe.OpenSessionAsync(
                        string.IsNullOrWhiteSpace(plugin.AgentName) ? DefaultAgent : plugin.AgentName,
                        CancellationToken.None);
                }

                // Logged because it is the one thing that decides what the panel shows and
                // the one thing nobody can see: a session address opens the conversation, the
                // bare server address opens a project picker, and from the outside those two
                // failures look identical.
                if (sessionUrl == null) {
                    Logger.Warning("OpenCode: could not open a session; the panel will show the project list instead");
                } else {
                    Logger.Info($"OpenCode: opening {sessionUrl}");
                }

                ServerAddress = (sessionUrl ?? baseUrl).ToString();
                IsConnected = true;
                Status = sessionUrl == null
                    ? $"Connected to {baseUrl}"
                    : $"Connected to {baseUrl} - session open";
            } catch (Exception ex) {
                Logger.Error("OpenCode: connection failed", ex);
                Status = ex.Message;
                Disconnect();
            } finally {
                IsBusy = false;
            }
        }

        private void Disconnect() {
            ServerAddress = null;
            ServerPassword = null;

            // Only a server this panel started gets stopped; an attached one is left alone,
            // because somebody may be working in it.
            server?.Dispose();
            server = null;

            IsConnected = false;
            Status = "Not connected.";
        }
    }
}
