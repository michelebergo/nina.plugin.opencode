namespace NINA.Plugin.OpenCode.Properties {

    /// <summary>
    /// Persisted plugin settings. Hand-written rather than designer-generated so every
    /// value can carry the reason it exists; the shape is the one NINA expects from
    /// ApplicationSettingsBase, and <c>UpdateSettings</c> drives the upgrade on first run
    /// after a new version.
    /// </summary>
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "17.0.0.0")]
    internal sealed partial class Settings : global::System.Configuration.ApplicationSettingsBase {

        private static Settings defaultInstance = ((Settings)(global::System.Configuration.ApplicationSettingsBase.Synchronized(new Settings())));

        public static Settings Default {
            get {
                return defaultInstance;
            }
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool UpdateSettings {
            get {
                return ((bool)(this["UpdateSettings"]));
            }
            set {
                this["UpdateSettings"] = value;
            }
        }

        /// <summary>"Launch" starts a local server from ExecutablePath; "Attach" uses ServerUrl.</summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("Launch")]
        public string ConnectionMode {
            get {
                return ((string)(this["ConnectionMode"]));
            }
            set {
                this["ConnectionMode"] = value;
            }
        }

        /// <summary>
        /// Full path to opencode.exe. Empty means "look for it on PATH", which is where an
        /// npm or installer-based install leaves it.
        /// </summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("")]
        public string ExecutablePath {
            get {
                return ((string)(this["ExecutablePath"]));
            }
            set {
                this["ExecutablePath"] = value;
            }
        }

        /// <summary>Base URL of an opencode server already running, used in Attach mode.</summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("http://127.0.0.1:4096")]
        public string ServerUrl {
            get {
                return ((string)(this["ServerUrl"]));
            }
            set {
                this["ServerUrl"] = value;
            }
        }

        /// <summary>
        /// The directory the agent works in. opencode reads its project configuration and
        /// AGENTS.md from here, so this is what decides which agents and MCP servers the
        /// session gets - not the plugin.
        /// </summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("")]
        public string WorkingDirectory {
            get {
                return ((string)(this["WorkingDirectory"]));
            }
            set {
                this["WorkingDirectory"] = value;
            }
        }

        /// <summary>
        /// Password for the launched server, passed as OPENCODE_SERVER_PASSWORD. opencode
        /// warns on startup when this is unset, and it is right to: an unsecured server on
        /// an observatory PC accepts a prompt from anything that can reach the port. Left
        /// empty, the plugin generates one per launch and keeps it in memory only.
        /// </summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("")]
        public string ServerPassword {
            get {
                return ((string)(this["ServerPassword"]));
            }
            set {
                this["ServerPassword"] = value;
            }
        }

        /// <summary>Which of the user's configured agents to open the session with; empty means opencode's default.</summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("")]
        public string AgentName {
            get {
                return ((string)(this["AgentName"]));
            }
            set {
                this["AgentName"] = value;
            }
        }

        /// <summary>
        /// Full path to nina_advanced_mcp.py from https://github.com/michelebergo/nina_mcp_server.
        /// Set, the plugin registers it in the working directory so the agent gets NINA's own
        /// tools instead of having to reason about an API it cannot call.
        /// </summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("")]
        public string NinaMcpServerPath {
            get {
                return ((string)(this["NinaMcpServerPath"]));
            }
            set {
                this["NinaMcpServerPath"] = value;
            }
        }

        /// <summary>Host of NINA's Advanced API, which is what the MCP server talks to.</summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("localhost")]
        public string NinaApiHost {
            get {
                return ((string)(this["NinaApiHost"]));
            }
            set {
                this["NinaApiHost"] = value;
            }
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("1888")]
        public int NinaApiPort {
            get {
                return ((int)(this["NinaApiPort"]));
            }
            set {
                this["NinaApiPort"] = value;
            }
        }

        /// <summary>
        /// Whether opening the panel connects by itself. On by default: a panel that shows a
        /// button and nothing else, every time, is a panel that makes the user do the
        /// computer's job.
        /// </summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool AutoConnect {
            get {
                return ((bool)(this["AutoConnect"]));
            }
            set {
                this["AutoConnect"] = value;
            }
        }

        /// <summary>
        /// Whether the plugin writes down what changes in the observatory. On by default: it
        /// records state transitions and one line per saved frame into its own folder, moves
        /// nothing and calls no model, and it is the only way to find out which events this
        /// observatory actually produces before anything is asked to react to them.
        /// </summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("True")]
        public bool RecordObservatoryLog {
            get {
                return ((bool)(this["RecordObservatoryLog"]));
            }
            set {
                this["RecordObservatoryLog"] = value;
            }
        }

        /// <summary>
        /// Whether the agent runs on a local model instead of whatever opencode is already
        /// configured to use. Off by default: the user's own provider setup is the sensible
        /// starting point, and this only overrides it when asked.
        /// </summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("False")]
        public bool UseLocalModel {
            get {
                return ((bool)(this["UseLocalModel"]));
            }
            set {
                this["UseLocalModel"] = value;
            }
        }

        /// <summary>Where Ollama answers. No "/v1": that is appended for the API and left off for the model list.</summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("http://localhost:11434")]
        public string LocalModelHost {
            get {
                return ((string)(this["LocalModelHost"]));
            }
            set {
                this["LocalModelHost"] = value;
            }
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("")]
        public string LocalModelId {
            get {
                return ((string)(this["LocalModelId"]));
            }
            set {
                this["LocalModelId"] = value;
            }
        }

        /// <summary>Port for a launched server. 0 lets opencode pick a free one and report it.</summary>
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("0")]
        public int LaunchPort {
            get {
                return ((int)(this["LaunchPort"]));
            }
            set {
                this["LaunchPort"] = value;
            }
        }
    }
}
