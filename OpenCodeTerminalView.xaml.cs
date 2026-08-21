using Microsoft.Web.WebView2.Core;
using NINA.Core.Utility;
using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace NINA.Plugin.OpenCode {

    /// <summary>
    /// Hosts opencode's own web interface. The control does three things the view model
    /// should not know about: it initialises the browser late, it answers the server's
    /// authentication challenge, and it navigates when the address changes.
    /// </summary>
    public partial class OpenCodeTerminalView : UserControl {

        private INotifyPropertyChanged? watched;

        public OpenCodeTerminalView() {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += (_, _) => (DataContext as OpenCodeChatVM)?.OnPanelShown();
            Unloaded += OnUnloaded;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
            if (watched != null) { watched.PropertyChanged -= OnViewModelChanged; }
            watched = e.NewValue as INotifyPropertyChanged;
            if (watched != null) { watched.PropertyChanged += OnViewModelChanged; }
            _ = SyncAsync();
        }

        private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName is nameof(OpenCodeChatVM.ServerAddress) or nameof(OpenCodeChatVM.IsConnected)) {
                _ = Dispatcher.InvokeAsync(async () => await SyncAsync());
            }
        }

        private async System.Threading.Tasks.Task SyncAsync() {
            if (DataContext is not OpenCodeChatVM vm) { return; }

            var address = vm.ServerAddress;
            if (string.IsNullOrWhiteSpace(address)) {
                Browser.Visibility = Visibility.Collapsed;
                return;
            }

            try {
                await EnsureBrowserAsync(vm);
                Browser.Visibility = Visibility.Visible;

                // Credentials go in the address when there are any. WebView2 does not raise
                // its authentication event for this navigation - measured, not assumed: the
                // panel took a 401 and the handler never ran - so the only way through a
                // protected server is to carry them in the URL.
                var target = WithCredentials(address!, vm.ServerPassword);
                if (Browser.Source?.ToString() != target) {
                    Browser.Source = new Uri(target);
                }
            } catch (Exception ex) {
                // A missing WebView2 runtime is the one failure a user can actually fix, so
                // it is worth naming rather than leaving as a blank panel.
                Logger.Error("OpenCode: could not host the opencode interface", ex);
                Browser.Visibility = Visibility.Collapsed;
            }
        }

        private static string WithCredentials(string address, string? password) {
            if (string.IsNullOrEmpty(password)) { return address; }
            var builder = new UriBuilder(address) { UserName = "opencode", Password = password };
            return builder.Uri.ToString();
        }

        private bool initialised;

        private async System.Threading.Tasks.Task EnsureBrowserAsync(OpenCodeChatVM vm) {
            if (initialised) { return; }

            // A process gets one WebView2 environment, and the first control to ask for one
            // decides it. Another plugin in this N.I.N.A. may already have done so - the AI
            // Assistant ships WebView2 too - and asking for a different environment after
            // that is refused outright with "already initialized with a different
            // CoreWebView2Environment", which leaves the panel blank.
            //
            // So the shared one is used when there is one, and an environment of our own is
            // created only if that fails. The fallback still needs somewhere writable to put
            // its profile: the default is beside the host executable, which on a normal
            // installation is in Program Files.
            try {
                await Browser.EnsureCoreWebView2Async();
            } catch (Exception ex) {
                Logger.Debug($"OpenCode: no shared WebView2 environment ({ex.Message}); creating one");

                var userData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA", "Plugins", "OpenCode", "WebView2");
                Directory.CreateDirectory(userData);

                var environment = await CoreWebView2Environment.CreateAsync(null, userData);
                await Browser.EnsureCoreWebView2Async(environment);
            }

            // Kept as a second chance, not as the mechanism. WebView2 does not raise this
            // for the panel's own navigation - the credentials travel in the address instead -
            // but a page that asks for them later would otherwise stop dead.
            Browser.CoreWebView2.BasicAuthenticationRequested += (_, e) => {
                if (DataContext is OpenCodeChatVM current && !string.IsNullOrEmpty(current.ServerPassword)) {
                    e.Response.UserName = "opencode";
                    e.Response.Password = current.ServerPassword;
                    Logger.Debug($"OpenCode: answered an authentication challenge for {e.Uri}");
                } else {
                    Logger.Warning($"OpenCode: {e.Uri} asked for credentials and none were available");
                }
            };

            // A hosted page that fails silently is the worst thing to debug: the panel is
            // blank and nothing anywhere says why. These two say which request failed and
            // with what, which turns "it does not work" into a line in the log.
            Browser.CoreWebView2.NavigationCompleted += (_, e) => {
                if (!e.IsSuccess) {
                    Logger.Error($"OpenCode: navigation failed ({e.WebErrorStatus})");
                }
            };

            Browser.CoreWebView2.WebResourceResponseReceived += (_, e) => {
                var status = e.Response?.StatusCode ?? 0;
                if (status >= 400) {
                    Logger.Warning($"OpenCode: the interface asked for {e.Request?.Uri} and got {status}");
                }
            };

            initialised = true;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) {
            if (watched != null) {
                watched.PropertyChanged -= OnViewModelChanged;
                watched = null;
            }
        }
    }
}
