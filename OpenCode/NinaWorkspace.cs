using Newtonsoft.Json;
using NINA.Core.Utility;
using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.OpenCode.OpenCode {

    /// <summary>
    /// Prepares the directory the agent works in so that it knows it is attached to a
    /// running observatory rather than to a source tree.
    ///
    /// This is done the way opencode itself expects rather than by injecting a hidden
    /// system prompt: two files on disk, which the user can read, edit, keep under version
    /// control or delete. AGENTS.md is the standing brief opencode reads for every session
    /// in this directory; opencode.json is where the tools, the permissions and the agents
    /// are declared. Files this plugin wrote are kept up to date as it improves, with the
    /// previous copy left beside them; files somebody has written or edited are left alone,
    /// because silently reverting somebody's agent instructions is the fastest way to lose
    /// their trust.
    /// </summary>
    internal static class NinaWorkspace {

        internal const string McpServerRepository = "https://github.com/michelebergo/nina_mcp_server";

        /// <summary>
        /// The release of the N.I.N.A. MCP server this plugin was built and tested against.
        ///
        /// The tag is what a person reads; the commit is what gets checked. A tag can be
        /// moved with a force push and a branch moves by definition, so fetching "whatever is
        /// on main" means two installations a month apart run different code, and anyone who
        /// can push to that repository can reach every future installation. The commit is
        /// verified after the checkout and the server is not run if it does not match.
        ///
        /// Raising these two is a deliberate act with a test attached: the permission policy
        /// enumerates the 179 tools this release registers, so a release that adds one fails
        /// the policy test until somebody classifies it.
        /// </summary>
        /// <summary>
        /// What the permission policy of an attached server is, as far as this plugin can
        /// tell.
        /// </summary>
        internal enum AttachedPolicy {
            /// <summary>The workspace carries a configuration this plugin wrote.</summary>
            Ours,
            /// <summary>A workspace this plugin can read, holding somebody else's configuration.</summary>
            Foreign,
            /// <summary>On another machine, or otherwise unreadable from here.</summary>
            Unknown
        }

        /// <summary>
        /// Attaching to a server somebody else started means the agent runs under that
        /// server's configuration, not the one this plugin generates: none of the deny floor,
        /// none of the per-tool rules, none of the three agents. That is a legitimate thing to
        /// want - it is somebody else's session and this plugin has no business writing into
        /// their directory - but it is not a thing to leave unsaid, because the panel looks
        /// identical either way.
        ///
        /// Pure on purpose: the caller supplies the reader, so the decision can be tested
        /// without a filesystem.
        /// </summary>
        internal static AttachedPolicy InspectAttached(string? remoteDirectory, bool serverIsLocal,
                                                       Func<string, string?> readText) {
            if (!serverIsLocal || string.IsNullOrWhiteSpace(remoteDirectory)) { return AttachedPolicy.Unknown; }

            string? config;
            try {
                config = readText(Path.Combine(remoteDirectory!, "opencode.json"));
            } catch {
                return AttachedPolicy.Unknown;
            }

            if (config == null) { return AttachedPolicy.Foreign; }

            // The stamp without its version: a workspace prepared by an older build of this
            // plugin still had this plugin's policy in it.
            var mark = GeneratorStamp.Substring(0, GeneratorStamp.LastIndexOf(' '));
            return config.Contains(mark) ? AttachedPolicy.Ours : AttachedPolicy.Foreign;
        }

        private const string McpScriptName = "nina_advanced_mcp.py";

        internal const string McpServerTag = "v1.0.1";

        internal const string McpServerCommit = "d063ada60cf3d79affe86500f29214e22640d3e0";

        /// <summary>
        /// Stamped into every file this class writes, so a later version can tell its own
        /// work from the user's.
        ///
        /// The first version of this refused to touch an existing file at all, which sounded
        /// respectful and was in fact the reason none of its improvements ever reached
        /// anybody: a workspace prepared once by an early build kept a thin brief and an
        /// empty policy forever, and the agent stayed ignorant while the plugin got better.
        /// Now a file this plugin wrote is replaced when it is out of date, with the previous
        /// one kept beside it; a file somebody edited is still left alone.
        /// </summary>
        private static readonly string GeneratorStamp =
            "nina.plugin.opencode " +
            (typeof(NinaWorkspace).Assembly.GetName().Version?.ToString() ?? "unknown");

        /// <summary>
        /// The identifier the N.I.N.A. server is registered under, and the prefix every one of
        /// its tools is therefore exposed with.
        ///
        /// opencode names an MCP tool "{server}_{tool}" - catalog.ts, toolName - so a server
        /// called "nina" holding a tool called "nina_slew_mount" offers it as
        /// "nina_nina_slew_mount". Rules written against the bare name match nothing, and a
        /// tool that matches no rule inherits opencode's own default of "*": "allow". That is
        /// how a policy of 121 rules came to permit all 179 tools without asking. The id and
        /// the rules now come from the same place so they cannot disagree again.
        /// </summary>
        private const string McpServerId = "nina";

        /// <summary>The name opencode will expose this tool under.</summary>
        private static string Exposed(string tool) => McpServerId + "_" + tool;

        /// <summary>
        /// Everything the server offers that reads, computes or waits, plus the three imaging
        /// actions the project deliberately lets run without a prompt - take an exposure,
        /// autofocus, change filter - because a prompt for each of those teaches the user to
        /// click Allow without reading.
        ///
        /// Enumerated rather than matched by prefix, and for the same reason the mutating list
        /// is: with a deny floor underneath, a tool that appears in a later version of the
        /// server arrives refused rather than permitted, and someone has to look at it.
        /// </summary>
        private static readonly string[] ReadOnlyTools = {
            // Not hardware, whatever the name suggests: these open and close the HTTP session
            // between the MCP server and N.I.N.A.'s own API, and the server says so itself -
            // "specifically for NINA control and should not be used for direct hardware
            // connections". Every other tool needs the session first, so refusing this one
            // does not make an agent careful, it makes it unable to read anything at all. It
            // cannot be pointed elsewhere either: the host and port come from the environment
            // this plugin sets, which wins over the argument.
            "nina_connect", "nina_disconnect",

            "nina_cancel_autofocus", "nina_capture_image", "nina_change_filter",
            "nina_get_autofocus_status", "nina_get_camera_info", "nina_get_capture_statistics",
            "nina_get_dome_info", "nina_get_event_history", "nina_get_filter_info",
            "nina_get_filterwheel_info", "nina_get_flatpanel_info", "nina_get_flats_progress",
            "nina_get_flats_status", "nina_get_focuser_info", "nina_get_framingassistant_info",
            "nina_get_guider_graph", "nina_get_guider_info", "nina_get_image",
            "nina_get_image_history", "nina_get_image_parameter", "nina_get_image_parameters",
            "nina_get_image_thumbnail", "nina_get_livestack_available_stacks", "nina_get_livestack_stacked_image",
            "nina_get_livestack_stacked_image_info", "nina_get_livestack_status", "nina_get_logs",
            "nina_get_moon_separation", "nina_get_mount_info", "nina_get_plugin_settings",
            "nina_get_plugins", "nina_get_prepared_image", "nina_get_profile_horizon",
            "nina_get_rotator_info", "nina_get_safetymonitor_info", "nina_get_screenshot",
            "nina_get_start_time", "nina_get_status", "nina_get_switch_channels",
            "nina_get_tab", "nina_get_version", "nina_get_weather_info",
            "nina_help", "nina_list_camera_devices", "nina_list_dome_devices",
            "nina_list_filterwheel_devices", "nina_list_flatpanel_devices", "nina_list_focuser_devices",
            "nina_list_guider_devices", "nina_list_mount_devices", "nina_list_rotator_devices",
            "nina_list_safetymonitor_devices", "nina_list_switch_devices", "nina_list_weather_sources",
            "nina_platesolve_status", "nina_poll_events_since", "nina_sequence_json",
            "nina_sequence_list_available", "nina_sequence_state", "nina_show_profile",
            "nina_solve_image", "nina_solve_prepared_image", "nina_start_autofocus",
            "nina_time_now", "nina_ts_get_exposure_plan", "nina_ts_list_projects",
            "nina_ts_next_target", "nina_wait",
        };

        /// <summary>
        /// Tools that move something, change a setting, or take control of a device. Every
        /// one is listed rather than matched by prefix: an unlisted tool inherits whatever
        /// opencode's default happens to be, and on a machine that points a telescope that
        /// is not a default worth discovering at three in the morning.
        ///
        /// The split follows the project's own permission table: reads and the low-risk
        /// imaging actions run freely, anything that moves hardware or changes configuration
        /// asks first.
        /// </summary>
        private static readonly string[] AskBeforeUsing = {
            // Ends the exposure somebody is in the middle of. The standing brief tells the
            // agent that an exposure in progress is somebody's night; the policy has to agree.
            "nina_abort_exposure",
            "nina_auto_brightness_flats", "nina_auto_exposure_flats", "nina_calibrate_guider",
            "nina_change_profile_value", "nina_clear_guider_calibration", "nina_close_dome_shutter",
            "nina_connect_camera", "nina_connect_dome",
            "nina_connect_filterwheel", "nina_connect_flatpanel", "nina_connect_focuser",
            "nina_connect_guider", "nina_connect_mount", "nina_connect_rotator",
            "nina_connect_safetymonitor", "nina_connect_switch", "nina_connect_weather",
            "nina_control_dew_heater", "nina_determine_framingassistant_rotation",
            "nina_disconnect_camera", "nina_disconnect_dome", "nina_disconnect_filterwheel",
            "nina_disconnect_flatpanel", "nina_disconnect_focuser", "nina_disconnect_guider",
            "nina_disconnect_mount", "nina_disconnect_rotator", "nina_disconnect_switch",
            "nina_flip_mount", "nina_framingassistant_slew", "nina_halt_focuser",
            "nina_halt_rotator", "nina_home_dome", "nina_home_mount",
            "nina_move_focuser", "nina_move_rotator", "nina_move_rotator_mechanically",
            "nina_open_dome_shutter", "nina_park_dome", "nina_park_mount",
            "nina_platesolve_cancel", "nina_platesolve_capsolve", "nina_platesolve_center",
            "nina_platesolve_sync", "nina_rescan_dome_devices", "nina_rescan_filterwheel_devices",
            "nina_rescan_flatpanel_devices", "nina_rescan_focuser_devices", "nina_rescan_guider_devices",
            "nina_rescan_mount_devices", "nina_rescan_rotator_devices", "nina_rescan_safetymonitor_devices",
            "nina_rescan_weather_sources", "nina_reset_image_parameters", "nina_reverse_rotator",
            "nina_sequence_edit", "nina_sequence_load", "nina_sequence_load_json",
            "nina_sequence_reset", "nina_sequence_set_target", "nina_sequence_start",
            "nina_sequence_stop", "nina_set_binning", "nina_set_camera_gain",
            "nina_set_camera_offset", "nina_set_camera_subsample", "nina_set_camera_usb_limit",
            "nina_set_dome_follow", "nina_set_dome_park_position", "nina_set_flatpanel_brightness",
            "nina_set_flatpanel_cover", "nina_set_flatpanel_light", "nina_set_focuser_temperature",
            "nina_set_framingassistant_coordinates", "nina_set_framingassistant_rotation", "nina_set_framingassistant_source",
            "nina_set_image_parameter", "nina_set_park_position", "nina_set_readout_mode",
            "nina_set_rotator_range", "nina_set_rotator_reverse", "nina_set_switch",
            "nina_set_tracking_mode", "nina_sky_flats", "nina_slew_dome",
            "nina_slew_mount", "nina_start_cooling", "nina_start_flats",
            "nina_start_guiding", "nina_start_livestack", "nina_start_warming",
            "nina_stop_cooling", "nina_stop_dome_movement", "nina_stop_flats",
            "nina_stop_guiding", "nina_stop_livestack", "nina_stop_slew",
            "nina_switch_profile", "nina_switch_tab", "nina_sync_dome_to_telescope",
            "nina_sync_mount", "nina_sync_rotator", "nina_trained_dark_flat",
            "nina_trained_flats", "nina_unpark_mount",
        };

        /// <summary>
        /// The two the agent may never call. Disconnecting the safety monitor or the weather
        /// source is how an agent would "work around" a device telling it to stop, and that
        /// is precisely the decision it must not have. Deterministic safety stays
        /// authoritative; the agent does not get a vote.
        /// </summary>
        private static readonly string[] NeverAllowed = {
            "nina_disconnect_safetymonitor", "nina_disconnect_weather",
            // Takes a local file path and a webhook URL, opens the file and uploads it, with no
            // restriction on the source, the destination or the size. An agent reading a
            // hostile instruction out of a log or a FITS header can use it to send any file the
            // N.I.N.A. user can read to anywhere. There is no version of this the calibration
            // panel needs.
            "nina_alert_human",
        };

        /// <summary>
        /// The only things that move which the unattended agent may do on its own.
        ///
        /// The list is short because unattended is not "the same agent with the prompts turned
        /// off". With nobody at the keyboard, "ask" is not caution - it is a session that
        /// stops at three in the morning and stays stopped until someone wakes up. So every
        /// tool has to be decided in advance, and the honest way to decide is to allow the
        /// handful of actions that recover a night and refuse the rest.
        ///
        /// What is here undoes a specific failure: guiding that stopped, a target that
        /// drifted off, a sequence that must be halted, and a mount and dome that must be put
        /// somewhere safe. Autofocus and taking an exposure are not listed because they are
        /// already free for every agent.
        ///
        /// What is deliberately absent matters more. Slewing, unparking, opening a shutter,
        /// throwing power switches, connecting or disconnecting anything, editing the profile:
        /// none of those recovers a night, and each of them is a way for a wrong diagnosis to
        /// become a wrecked mount. An agent that cannot open the dome cannot open it onto rain.
        /// </summary>
        private static readonly string[] RecoveryActions = {
            "nina_close_dome_shutter", "nina_park_dome", "nina_park_mount",
            "nina_platesolve_capsolve", "nina_platesolve_center", "nina_sequence_start",
            "nina_sequence_stop", "nina_set_tracking_mode", "nina_start_guiding",
            "nina_stop_guiding",
        };

        /// <summary>
        /// Everything the observer agent is not given. It is not asked to behave: the tools
        /// are switched off for it, so the read-only supervisor cannot move hardware even if
        /// the conversation talks it into wanting to.
        /// </summary>
        private static readonly string[] MutatingTools = {
            "nina_abort_exposure", "nina_auto_brightness_flats", "nina_auto_exposure_flats",
            "nina_calibrate_guider",
            "nina_change_profile_value", "nina_clear_guider_calibration", "nina_close_dome_shutter",
            "nina_connect_camera", "nina_connect_dome",
            "nina_connect_filterwheel", "nina_connect_flatpanel", "nina_connect_focuser",
            "nina_connect_guider", "nina_connect_mount", "nina_connect_rotator",
            "nina_connect_safetymonitor", "nina_connect_switch", "nina_connect_weather",
            "nina_control_dew_heater", "nina_determine_framingassistant_rotation",
            "nina_disconnect_camera", "nina_disconnect_dome", "nina_disconnect_filterwheel",
            "nina_disconnect_flatpanel", "nina_disconnect_focuser", "nina_disconnect_guider",
            "nina_disconnect_mount", "nina_disconnect_rotator", "nina_disconnect_safetymonitor",
            "nina_disconnect_switch", "nina_disconnect_weather", "nina_flip_mount",
            "nina_framingassistant_slew", "nina_halt_focuser", "nina_halt_rotator",
            "nina_home_dome", "nina_home_mount", "nina_move_focuser",
            "nina_move_rotator", "nina_move_rotator_mechanically", "nina_open_dome_shutter",
            "nina_park_dome", "nina_park_mount", "nina_platesolve_cancel",
            "nina_platesolve_capsolve", "nina_platesolve_center", "nina_platesolve_sync",
            "nina_rescan_dome_devices", "nina_rescan_filterwheel_devices", "nina_rescan_flatpanel_devices",
            "nina_rescan_focuser_devices", "nina_rescan_guider_devices", "nina_rescan_mount_devices",
            "nina_rescan_rotator_devices", "nina_rescan_safetymonitor_devices", "nina_rescan_weather_sources",
            "nina_reset_image_parameters", "nina_reverse_rotator", "nina_sequence_edit",
            "nina_sequence_load", "nina_sequence_load_json", "nina_sequence_reset",
            "nina_sequence_set_target", "nina_sequence_start", "nina_sequence_stop",
            "nina_set_binning", "nina_set_camera_gain", "nina_set_camera_offset",
            "nina_set_camera_subsample", "nina_set_camera_usb_limit", "nina_set_dome_follow",
            "nina_set_dome_park_position", "nina_set_flatpanel_brightness", "nina_set_flatpanel_cover",
            "nina_set_flatpanel_light", "nina_set_focuser_temperature", "nina_set_framingassistant_coordinates",
            "nina_set_framingassistant_rotation", "nina_set_framingassistant_source", "nina_set_image_parameter",
            "nina_set_park_position", "nina_set_readout_mode", "nina_set_rotator_range",
            "nina_set_rotator_reverse", "nina_set_switch", "nina_set_tracking_mode",
            "nina_sky_flats", "nina_slew_dome", "nina_slew_mount",
            "nina_start_cooling", "nina_start_flats", "nina_start_guiding",
            "nina_start_livestack", "nina_start_warming", "nina_stop_cooling",
            "nina_stop_dome_movement", "nina_stop_flats", "nina_stop_guiding",
            "nina_stop_livestack", "nina_stop_slew", "nina_switch_profile",
            "nina_switch_tab", "nina_sync_dome_to_telescope", "nina_sync_mount",
            "nina_sync_rotator", "nina_trained_dark_flat", "nina_trained_flats",
            "nina_unpark_mount",
        };

        /// <summary>
        /// What uv installs to run the MCP server.
        ///
        /// fastmcp is pinned, and the pin is the whole difference between a working server
        /// and one that never answers: unpinned, uv resolves fastmcp 3.x, whose "fastmcp run"
        /// does not speak the protocol opencode expects on stdio, and the only symptom is a
        /// connection that times out after thirty seconds with nothing in any log. Measured
        /// against the installed server: 3.4.7 times out, 1.0 connects.
        /// </summary>
        /// <summary>
        /// The interpreter the server is run on.
        ///
        /// Named rather than left to uv, because "whatever Python this machine happens to
        /// offer" resolves differently on different machines, and the pinned dependency set
        /// was compiled against this one. A pin on the packages that leaves the interpreter
        /// free is only half a pin.
        /// </summary>
        private const string McpPythonVersion = "3.11";

        /// <summary>
        /// Where the agent works when the user has not chosen somewhere. A plugin that only
        /// works once three settings have been filled in is a plugin most people conclude is
        /// broken, so there is always a workspace: this one, created on first use, holding
        /// the brief and the tool configuration.
        ///
        /// Deliberately not a source tree and not the N.I.N.A. profile folder. It is the
        /// agent's own room: safe to inspect, safe to delete, and nothing in it belongs to
        /// anything else.
        /// </summary>
        public static string DefaultDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "Plugins", "OpenCode", "workspace");

        /// <summary>
        /// The N.I.N.A. MCP server this plugin will use: the path someone typed into Options,
        /// or nothing.
        ///
        /// An earlier version also searched five common developer directories for any file
        /// called nina_advanced_mcp.py and ran the first one it found. It was meant as a
        /// courtesy - not asking someone to paste a path to a file they already have - but
        /// what it actually did was execute an unverified script, chosen by filename, with
        /// the whole observatory tool surface behind it. A path typed into Options is a
        /// deliberate act by the person at the keyboard; a filename match in a home directory
        /// is not.
        /// </summary>
        public static string? LocateMcpServer(string? configured) {
            if (string.IsNullOrWhiteSpace(configured)) { return null; }
            var explicitPath = configured!.Trim();
            return File.Exists(explicitPath) ? explicitPath : null;
        }

        /// <summary>
        /// Makes sure the N.I.N.A. MCP server is on disk, cloning it if it is not.
        ///
        /// Cloning rather than shipping a copy inside the plugin: the tool set and the
        /// permission policy that gates it have to describe the same server, and a copy
        /// embedded in a plugin drifts from the repository the moment either changes. This
        /// way there is one source of truth, and the user can see it, update it and read it.
        ///
        /// It is a network fetch on an observatory machine, so it happens once, on demand,
        /// with a bound on how long it may take, and it says what it is doing rather than
        /// pausing silently.
        /// </summary>
        public static async Task<string?> EnsureMcpServerAsync(string workingDirectory, string? configured,
                                                              Action<string>? report, CancellationToken token) {
            var existing = LocateMcpServer(configured);
            if (existing != null) {
                // The dependencies are installed from the file the server ships, so a copy
                // without one cannot be started at all. Said here, where the path came from,
                // rather than left to uv to fail on a file that is not there.
                var beside = Path.Combine(Path.GetDirectoryName(existing) ?? string.Empty, "requirements.txt");
                if (!File.Exists(beside)) {
                    report?.Invoke($"The MCP server at {existing} has no requirements.txt beside it, so its " +
                                   "dependencies cannot be installed. Point Options at a full checkout, or clear " +
                                   $"the setting and let the plugin fetch {McpServerTag} itself.");
                    return null;
                }

                // Somebody typed this path in. It is not the pinned release and this plugin
                // has no way to know what it is, so it says so rather than letting the
                // verification below look as though it applies to it.
                Logger.Info($"OpenCode: using the MCP server at the configured path {existing}; " +
                    $"it is not verified against {McpServerTag}");
                return existing;
            }

            var target = Path.Combine(
                string.IsNullOrWhiteSpace(workingDirectory) ? DefaultDirectory : workingDirectory,
                "nina_mcp_server");
            var script = Path.Combine(target, McpScriptName);

            // Already here and already right: nothing to fetch.
            if (File.Exists(script) && await AtPinnedCommit(target, token).ConfigureAwait(false)) {
                return script;
            }

            if (!HasGit()) {
                report?.Invoke("git is not installed, so the N.I.N.A. tools cannot be fetched. " +
                               $"Clone {McpServerRepository} at {McpServerTag} yourself and set its path in Options.");
                return null;
            }

            report?.Invoke($"Fetching the N.I.N.A. tools ({McpServerTag})...");
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                // A directory that exists but sits on the wrong commit is updated in place
                // rather than deleted. Deleting a directory tree because the version is not
                // the expected one is a heavy answer to a light problem, and an interrupted
                // delete leaves nothing behind to retry from.
                var present = Directory.Exists(Path.Combine(target, ".git"));
                if (present) {
                    if (!await RunGit(target, token, "fetch", "--depth", "1", "origin", "tag", McpServerTag).ConfigureAwait(false) ||
                        !await RunGit(target, token, "checkout", "--detach", McpServerTag).ConfigureAwait(false)) {
                        report?.Invoke($"Could not update the N.I.N.A. tools in {target} to {McpServerTag}.");
                        return null;
                    }
                } else if (!await RunGit(null, token, "clone", "--depth", "1",
                                         "--branch", McpServerTag, McpServerRepository, target).ConfigureAwait(false)) {
                    report?.Invoke($"Could not fetch the N.I.N.A. tools from {McpServerRepository}.");
                    return null;
                }
            } catch (Exception ex) {
                Logger.Warning($"OpenCode: could not fetch the MCP server ({ex.Message})");
                report?.Invoke($"Could not fetch the N.I.N.A. tools: {ex.Message}");
                return null;
            }

            if (!File.Exists(script)) {
                report?.Invoke($"The repository was fetched but {McpScriptName} is not in it.");
                return null;
            }

            // The whole point of naming a commit. A tag can be moved, a mirror can serve
            // something else, an interrupted fetch can leave the working tree somewhere
            // nobody intended - and every one of those ends with this plugin running a Python
            // file that has the observatory behind it. Verified, or not run.
            if (!await AtPinnedCommit(target, token).ConfigureAwait(false)) {
                report?.Invoke("The N.I.N.A. tools were fetched but are not at the expected commit " +
                               $"({McpServerTag}). They will not be used. Delete {target} and try again.");
                return null;
            }

            Logger.Info($"OpenCode: N.I.N.A. MCP server {McpServerTag} ready in {target}");
            return script;
        }

        /// <summary>
        /// Whether the checkout really is the pinned commit. Answers false on any doubt - a
        /// directory that is not a repository, a git that will not run, an unreadable answer -
        /// because "I could not tell" and "it is wrong" deserve the same caution here.
        /// </summary>
        private static async Task<bool> AtPinnedCommit(string directory, CancellationToken token) {
            try {
                var head = await ReadGit(directory, token, "rev-parse", "HEAD").ConfigureAwait(false);
                if (head == null) { return false; }
                var match = string.Equals(head.Trim(), McpServerCommit, StringComparison.OrdinalIgnoreCase);
                if (!match) {
                    Logger.Warning($"OpenCode: the MCP server in {directory} is at {head.Trim()}, " +
                        $"not the pinned {McpServerCommit} ({McpServerTag})");
                }
                return match;
            } catch (Exception ex) {
                Logger.Warning($"OpenCode: could not read the MCP server commit ({ex.Message})");
                return false;
            }
        }

        private static ProcessStartInfo GitStart(string? workingDirectory) {
            var info = new ProcessStartInfo {
                FileName = "git",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (!string.IsNullOrWhiteSpace(workingDirectory)) { info.WorkingDirectory = workingDirectory; }
            return info;
        }

        /// <summary>Runs git and reports whether it succeeded. Three minutes is the ceiling.</summary>
        private static async Task<bool> RunGit(string? workingDirectory, CancellationToken token, params string[] args) {
            var info = GitStart(workingDirectory);
            foreach (var a in args) { info.ArgumentList.Add(a); }

            using var process = Process.Start(info);
            if (process == null) { return false; }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromMinutes(3));
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

            if (process.ExitCode == 0) { return true; }
            var error = await process.StandardError.ReadToEndAsync(token).ConfigureAwait(false);
            Logger.Warning($"OpenCode: git {string.Join(" ", args)} failed ({error.Trim()})");
            return false;
        }

        /// <summary>Runs git and returns what it printed, or null if it failed.</summary>
        private static async Task<string?> ReadGit(string workingDirectory, CancellationToken token, params string[] args) {
            var info = GitStart(workingDirectory);
            foreach (var a in args) { info.ArgumentList.Add(a); }

            using var process = Process.Start(info);
            if (process == null) { return null; }

            var output = await process.StandardOutput.ReadToEndAsync(token).ConfigureAwait(false);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(TimeSpan.FromMinutes(1));
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return process.ExitCode == 0 ? output : null;
        }

        private static bool HasGit() {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in path.Split(Path.PathSeparator)) {
                if (string.IsNullOrWhiteSpace(dir)) { continue; }
                try {
                    if (File.Exists(Path.Combine(dir.Trim(), "git.exe"))) { return true; }
                } catch (ArgumentException) {
                    // A malformed PATH entry is not a reason to stop looking.
                }
            }
            return false;
        }

        /// <summary>
        /// Creates the brief and the MCP registration if they are not there. Returns what it
        /// did so the panel can say so instead of working silently.
        /// </summary>
        public static string Prepare(string directory, string? ninaMcpServerPath, string ninaHost, int ninaPort,
                                    string? localModelHost = null, string? localModelId = null,
                                    IReadOnlyList<string>? localModels = null) {
            if (string.IsNullOrWhiteSpace(directory)) { directory = DefaultDirectory; }
            ninaMcpServerPath = LocateMcpServer(ninaMcpServerPath);

            try {
                Directory.CreateDirectory(directory);
            } catch (Exception ex) {
                Logger.Warning($"OpenCode: cannot prepare '{directory}' ({ex.Message})");
                return $"Could not prepare the working directory: {ex.Message}";
            }

            var wrote = new StringBuilder();
            var kept = new StringBuilder();

            Write(Path.Combine(directory, "AGENTS.md"),
                  BuildBrief(ninaMcpServerPath, ninaHost, ninaPort), wrote, kept);

            if (!string.IsNullOrWhiteSpace(ninaMcpServerPath)) {
                Write(Path.Combine(directory, "opencode.json"),
                      BuildMcpConfig(ninaMcpServerPath!, ninaHost, ninaPort, localModelHost, localModelId, localModels), wrote, kept);
            }

            var summary = new StringBuilder();
            if (wrote.Length > 0) { summary.Append("Wrote ").Append(wrote).Append(" into ").Append(directory).Append('.'); }
            if (kept.Length > 0) {
                if (summary.Length > 0) { summary.Append(' '); }
                summary.Append("Left your own ").Append(kept).Append(" untouched.");
            }
            return summary.Length == 0 ? "Working directory already up to date." : summary.ToString();
        }

        /// <summary>
        /// Writes one generated file, unless the copy on disk is somebody else's.
        ///
        /// Three cases. Nothing there: write it. Something this plugin wrote, and it differs
        /// from what this version would write: keep the old one beside it and replace it, so
        /// improvements actually arrive. Something a person wrote or edited: leave it, and
        /// say so - a workspace that silently reverts your edits is worse than one that
        /// never updates.
        /// </summary>
        private static void Write(string path, string content, StringBuilder wrote, StringBuilder kept) {
            var name = Path.GetFileName(path);
            try {
                if (File.Exists(path)) {
                    var existing = File.ReadAllText(path);
                    if (!existing.Contains(GeneratorStamp.Substring(0, GeneratorStamp.LastIndexOf(' ')))) {
                        // Somebody else's file, so it stays. But one shape of it is worth
                        // naming: a config written in the Claude Desktop style declares its
                        // servers under "mcpServers", which opencode does not read, so the
                        // agent ends up with no tools at all and no error to show for it.
                        if (name.Equals("opencode.json", StringComparison.OrdinalIgnoreCase)
                            && existing.Contains("mcpServers") && !existing.Contains("\"mcp\"")) {
                            Logger.Warning($"OpenCode: {path} declares its servers under \"mcpServers\", which opencode " +
                                "ignores - it expects \"mcp\". The agent will have no N.I.N.A. tools until this file is " +
                                "fixed or removed.");
                            Append(kept, name + " (which opencode cannot read - see the log)");
                            return;
                        }
                        Append(kept, name);
                        return;
                    }
                    if (existing == content) { return; }

                    var backup = path + ".previous";
                    File.Copy(path, backup, overwrite: true);
                    Logger.Info($"OpenCode: replaced {name}; the previous one is at {backup}");
                }

                File.WriteAllText(path, content);
                Append(wrote, name);
            } catch (Exception ex) {
                Logger.Warning($"OpenCode: could not write {name} ({ex.Message})");
            }
        }

        private static void Append(StringBuilder builder, string name) {
            if (builder.Length > 0) { builder.Append(" and "); }
            builder.Append(name);
        }

        /// <summary>
        /// The standing brief. It says what the agent is attached to and, just as
        /// importantly, what it must not assume: this is a live observatory, the mount is a
        /// physical object, and a command that would move something is not a command to
        /// issue on a hunch.
        /// </summary>
        private static string BuildBrief(string? ninaMcpServerPath, string ninaHost, int ninaPort) {
            var configured = !string.IsNullOrWhiteSpace(ninaMcpServerPath);
            var observatoryLogFolder = NINA.Plugin.OpenCode.Observatory.ObservatoryLog.DefaultDirectory;
            return $@"<!-- {GeneratorStamp} - edit freely; edited files are never replaced -->
# Working with N.I.N.A.

You are attached to a running instance of N.I.N.A. (Nighttime Imaging 'N' Astronomy) through
its OpenCode panel. This is not a source tree: it is a telescope that is probably pointing at
something right now, on a mount that physically moves.

## What you can reach

N.I.N.A. exposes its state and its equipment over the Advanced API at
`http://{ninaHost}:{ninaPort}`. The MCP server that turns that API into tools is
`nina_mcp_server`: {McpServerRepository}

{(configured
    ? "It is registered in `opencode.json` next to this file, so its tools are available in this\nsession. Prefer those tools over raw HTTP calls: they carry the argument shapes and the\nerror messages that the API alone does not."
    : $"It is **not** registered in this workspace yet. To use it, install it from\n{McpServerRepository} and add it to `opencode.json` here, or point the plugin at your copy\nin the OpenCode options and reconnect.")}

## Which agent you are

There are three, and the difference is not tone but permissions. `observer` has the tools that
move switched off. `copilot` can act and is stopped for approval before anything that moves
hardware. `autopilot` runs with nobody watching, so nothing it does is approved at the time:
every answer was given in advance, and the few actions it has are the ones that recover a night.
Whichever you are, the permission you get back is the decision. A refusal is not an obstacle to
route around.

## The observatory log

The plugin writes down what changes during the night - equipment state transitions, autofocus
runs, and one line per saved frame with its metrics - as JSON Lines, one file per night, in
`{observatoryLogFolder}`. Read it before theorising. ""The guider dropped out at 01:42 and the
frames after it show HFR climbing"" is a fact from that file; the same sentence without it is a
guess.

## What to be careful about

- **Moving anything is a physical act.** Slewing, parking, focusing, rotating and opening a
  dome all move hardware that can hit something, drag a cable or lose an alignment. Say what
  you intend to do and why before you do it.
- **An exposure in progress is somebody's night.** Check whether a sequence is running before
  doing anything that would interrupt it.
- **Read before you write.** The state of the equipment is queryable; guessing at it is never
  necessary.
- **Weather and safety devices exist for a reason.** If a safety monitor says unsafe, that is
  not a value to work around.

## What is useful

Reading logs, explaining what went wrong in a session, checking equipment state, computing
what a target will do tonight, summarising a night's results, and preparing changes for the
person to approve are all things you can do without touching the hardware at all. Start there.

## How to answer when something is wrong

Separate what you saw from what you think it means, and say how sure you are. A diagnosis
stated as a fact is one nobody can check:

    Observation:  what the numbers actually show
    Diagnosis:    what you think is causing it
    Confidence:   high, medium or low - and say what would raise it
    Action:       what you propose doing
    Expected:     what should change if you are right

Then verify. An action whose result nobody looked at is indistinguishable from one that did
nothing.

## When to stop and ask

- **Do not retry indefinitely.** Two attempts at the same recovery is enough; a third means
  the diagnosis was wrong, not that the action needs repeating.
- **Do not work around a device that is telling you to stop.** A safety monitor reporting
  unsafe, a weather source reporting rain, a limit reached: those are conclusions, not
  obstacles. You cannot disconnect them - the permission is refused - and you should not
  look for another route to the same end.
- **Escalate when your confidence drops.** Saying ""I do not know why this is failing"" is a
  useful answer at three in the morning. Guessing is not.

## Who is driving

N.I.N.A.'s sequencer may be running a plan of its own. If a sequence is in progress, it is in
charge: do not slew, change filter or move the focuser underneath it. Ask the person first, or
work through the sequence rather than around it.
";
        }

        /// <summary>
        /// The project configuration the agent runs under: where N.I.N.A.'s tools come from,
        /// and what it may do with them without asking.
        ///
        /// The policy is written out in full rather than left to a default. Reads and the
        /// low-risk imaging actions - take an exposure, autofocus, change filter - run
        /// freely, because a prompt for each of those trains the user to click Allow without
        /// reading. Everything that moves hardware or changes configuration asks. And two
        /// things are refused outright: an agent that can disconnect the safety monitor or
        /// the weather source can work around the very devices that exist to stop it.
        /// </summary>
        internal static string BuildMcpConfig(string ninaMcpServerPath, string ninaHost, int ninaPort,
                                             string? localModelHost, string? localModelId,
                                             IReadOnlyList<string>? localModels) {
            // Forward slashes on purpose: the path lands inside JSON, where a Windows
            // backslash would have to be escaped and is the classic way this file ends up
            // silently invalid.
            var script = ninaMcpServerPath.Replace(@"\", "/");

            // Installed from the file the server itself ships, not from a list kept here.
            // Two lists is one too many: this plugin used to carry its own, the two drifted,
            // and the one that ran was this one - which is how the server came to run without
            // a package it imports, and with a package it never asked for.
            var requirements = Path.Combine(Path.GetDirectoryName(ninaMcpServerPath) ?? string.Empty,
                                            "requirements.txt").Replace(@"\", "/");

            var permissions = new StringBuilder();

            // The built-in tools first. bash is a shell on the observatory machine and edit
            // rewrites files there, so both ask; reading and searching change nothing.
            // The floor, and the single most important line in this file. opencode resolves a
            // tool against the LAST matching rule, and its own default is "*": "allow" - so
            // anything this policy fails to name is permitted. Naming 179 tools correctly is a
            // thing that can be got wrong once and silently; a floor cannot. With this first,
            // a tool that is misnamed, renamed or newly added is refused, and the agent says
            // so instead of doing it.
            AppendPermission(permissions, "*", "deny");

            AppendBuiltins(permissions, reading: "allow", acting: "ask", browsing: "allow");

            // Order is the mechanism: later rules override earlier ones, so the reads open the
            // N.I.N.A. namespace and the two lists below close the parts of it that move.
            foreach (var tool in ReadOnlyTools) { AppendPermission(permissions, Exposed(tool), "allow"); }
            foreach (var tool in AskBeforeUsing) { AppendPermission(permissions, Exposed(tool), "ask"); }
            foreach (var tool in NeverAllowed) { AppendPermission(permissions, Exposed(tool), "deny"); }

            var policy = Close(permissions);
            var observerPolicy = BuildObserverPolicy();
            var autopilotPolicy = BuildAutopilotPolicy();

            // A local model is declared as an OpenAI-compatible provider, which is what
            // Ollama serves on /v1, and then named on each agent. Left empty, none of this is
            // written and opencode keeps using whatever provider the user already had.
            var useLocal = !string.IsNullOrWhiteSpace(localModelHost) && !string.IsNullOrWhiteSpace(localModelId);
            var provider = useLocal ? BuildLocalProvider(localModelHost!, localModelId!, localModels) : string.Empty;
            var agentModel = useLocal ? "      " + Quote("model") + ": " + Quote("ollama/" + localModelId) + "," + Environment.NewLine : string.Empty;

            return $@"{{
  ""$schema"": ""https://opencode.ai/config.json"",
  // {GeneratorStamp}
  //
  // Rewritten by the plugin whenever it has something new to put here. To keep changes of
  // your own, delete the line above: this file is then yours and the plugin leaves it alone -
  // and stops giving you anything it would have added later. The copy it replaced is always
  // kept beside it as opencode.json.previous.
  //
  // To add an agent of your own without giving that up, put a markdown file in
  // .opencode/agents/ next to this file. The plugin never touches that folder, so your agents
  // survive every update. Three come from here: observer reads, copilot acts and asks first,
  // autopilot runs unattended with every answer decided in advance.
  ""instructions"": [""AGENTS.md""],
  ""default_agent"": ""observer"",
  ""mcp"": {{
    ""nina"": {{
      ""type"": ""local"",
      ""command"": [
        ""uv"", ""run"",
        ""--python"", ""{McpPythonVersion}"",
        ""--with-requirements"", {Quote(requirements)},
        ""fastmcp"", ""run"",
        {Quote(script)}
      ],
      ""environment"": {{
        ""NINA_HOST"": {Quote(ninaHost)},
        ""NINA_PORT"": ""{ninaPort}""
      }},
      ""enabled"": true,
      ""timeout"": 120000
    }}
  }},
  ""permission"": {{
{policy}
  }},
  ""watcher"": {{
    ""ignore"": [
      ""**/*.fits"", ""**/*.fit"", ""**/*.xisf"", ""**/*.tif"", ""**/*.tiff"",
      ""**/*.cr2"", ""**/*.nef"", ""**/*.raw"", ""**/*.zip"",
      "".git/**"", ""bin/**"", ""obj/**"", ""logs/**""
    ]
  }},
{provider}  ""agent"": {{
    ""observer"": {{
{agentModel}      ""description"": ""Watches the session and explains it. Cannot move anything."",
      ""mode"": ""primary"",
      ""temperature"": 0.1,
      ""prompt"": {Quote(ObserverPrompt)},
      ""permission"": {{
{observerPolicy}
      }}
    }},
    ""copilot"": {{
{agentModel}      ""description"": ""Can act on the observatory, asking before anything that moves hardware."",
      ""mode"": ""primary"",
      ""temperature"": 0.1,
      ""prompt"": {Quote(CopilotPrompt)}
    }},
    ""autopilot"": {{
{agentModel}      ""description"": ""Unattended recovery. Every permission is decided in advance; it never asks."",
      ""mode"": ""primary"",
      ""temperature"": 0.1,
      ""steps"": 20,
      ""prompt"": {Quote(AutopilotPrompt)},
      ""permission"": {{
{autopilotPolicy}
      }}
    }}
  }}
}}
";
        }


        /// <summary>
        /// The unattended agent's own policy, resolved down to allow or deny with no "ask"
        /// anywhere in it.
        ///
        /// opencode lets an agent carry its own permission map, which is what makes this
        /// possible: the project policy can keep asking - it is meant for someone who is
        /// there - while this one agent runs against a policy where every answer has already
        /// been given. A single "ask" left in here would be a silent hang, and a hang at
        /// night looks exactly like a night that went fine until you check the frames.
        ///
        /// The shell, the file editor and subagents are refused rather than asked for the
        /// same reason, and so is asking the user a question: there is nobody to answer it.
        /// </summary>
        private static string BuildAutopilotPolicy() {
            var permissions = new StringBuilder();

            AppendPermission(permissions, "*", "deny");

            AppendBuiltins(permissions, reading: "allow", acting: "deny", browsing: "deny");

            // Everything that moves is refused, and then the recovery actions are granted
            // back. Written in that order on purpose: a tool added to the MCP server later
            // arrives denied for the unattended agent rather than allowed by omission.
            var allowed = new HashSet<string>(RecoveryActions, StringComparer.Ordinal);
            foreach (var tool in ReadOnlyTools) { AppendPermission(permissions, Exposed(tool), "allow"); }
            foreach (var tool in MutatingTools) {
                AppendPermission(permissions, Exposed(tool), allowed.Contains(tool) ? "allow" : "deny");
            }
            foreach (var tool in RecoveryActions) {
                if (Array.IndexOf(MutatingTools, tool) < 0) { AppendPermission(permissions, Exposed(tool), "allow"); }
            }
            foreach (var tool in NeverAllowed) { AppendPermission(permissions, Exposed(tool), "deny"); }

            return Close(permissions);
        }

        /// <summary>
        /// The observer's own policy: it may read, and nothing else on the observatory.
        ///
        /// This used to be written as opencode's "tools" on/off map, which the schema marks
        /// deprecated in favour of "permission" - and which carried the same naming fault as
        /// everything else, so the switches were thrown for tools that do not exist and the
        /// read-only agent could move the mount. Written as a policy it obeys the same floor
        /// as the others: refused unless named.
        /// </summary>
        private static string BuildObserverPolicy() {
            var permissions = new StringBuilder();

            AppendPermission(permissions, "*", "deny");

            AppendBuiltins(permissions, reading: "allow", acting: "deny", browsing: "allow");

            // Not even the imaging actions the project lets the other two take without asking:
            // an agent whose job is to explain does not need to expose a frame to do it.
            foreach (var tool in ReadOnlyTools) {
                var acts = Array.IndexOf(ObserverDeniedReads, tool) >= 0;
                AppendPermission(permissions, Exposed(tool), acts ? "deny" : "allow");
            }
            foreach (var tool in MutatingTools) { AppendPermission(permissions, Exposed(tool), "deny"); }
            foreach (var tool in NeverAllowed) { AppendPermission(permissions, Exposed(tool), "deny"); }

            return Close(permissions);
        }

        /// <summary>
        /// The entries of <see cref="ReadOnlyTools"/> that do something rather than report
        /// something. The other agents may use them freely; the observer may not, because
        /// "cannot move anything" is the whole of its description.
        /// </summary>
        private static readonly string[] ObserverDeniedReads = {
            "nina_cancel_autofocus", "nina_capture_image", "nina_change_filter",
            "nina_start_autofocus", "nina_wait",
        };

        /// <summary>
        /// A value as JSON, escaping included. Everything that lands in the generated file
        /// goes through here rather than being interpolated between two literal quotes: the
        /// host comes from a text box somebody types into, and a single quotation mark in it
        /// used to be enough to produce a configuration file that will not parse - or, with a
        /// little care, one that carries settings nobody wrote.
        /// </summary>
        private static string Quote(string value) => JsonConvert.ToString(value);

        /// <summary>Trims the trailing separator so the block is valid JSON on its own.</summary>
        private static string Close(StringBuilder builder) => builder.ToString().TrimEnd().TrimEnd(char.Parse(","));
        /// <summary>
        /// What each agent is for. Kept short: a standing brief already tells both of them
        /// where they are, and repeating it here would only give the model two versions of
        /// the same instruction to reconcile.
        /// </summary>
        private const string ObserverPrompt =
            "You watch this observatory and explain it. You have no tools that move anything, "
            + "which is deliberate: your job is to read the state, the logs and the image metrics, "
            + "work out what is happening, and say so clearly. When something is wrong, separate "
            + "what you observed from what you think it means and say how sure you are. If an "
            + "action is needed, describe it and let the person decide - or ask them to switch you "
            + "to the copilot agent, which can act.";

        /// <summary>
        /// The unattended brief. Two things it must carry that the other two do not: nobody
        /// is going to read a proposal, and stopping is a legitimate outcome. An agent that
        /// treats giving up as failure is an agent that keeps trying things on a mount at
        /// four in the morning.
        /// </summary>
        private const string AutopilotPrompt =
            "You are running unattended. Nobody is watching, nobody will answer a question, and "
            + "anything you cannot do has already been refused rather than left to ask - so if a "
            + "tool is denied, that is the answer, not an obstacle to work around. "
            + "Your job is to recover a night that has gone wrong, not to improve one that is going "
            + "well: if nothing is wrong, say so and stop. "
            + "Diagnose from the state and the image metrics before acting, act with one change at a "
            + "time, and verify it: a call that returned without an error is not evidence that the "
            + "hardware did what you asked. If two attempts have not fixed it, stop trying and put "
            + "things somewhere safe - park the mount, close the shutter - then report. "
            + "Weather, safety monitors and hard limits are not yours to reason about; they act on "
            + "their own and you do not get a vote. "
            + "End every turn with a short JSON object: status, diagnosis, action_taken, result, "
            + "and whether a person needs to be told.";

        private const string CopilotPrompt =
            "You can act on this observatory. Anything that moves hardware or changes "
            + "configuration will stop and ask the person first, so propose it plainly and say "
            + "what you expect to happen. After an action, verify the result: a call that returned "
            + "without an error is not evidence that the hardware did what you asked.";

        /// <summary>
        /// Ollama's OpenAI-compatible endpoint, declared the way opencode declares any such
        /// provider. The "/v1" is appended here rather than stored, because the same host
        /// serves its model list on a different path.
        /// </summary>
        /// <summary>
        /// Ollama's OpenAI-compatible endpoint, declared the way opencode declares any such
        /// provider. The "/v1" is appended here rather than stored, because the same host
        /// serves its model list on a different path.
        ///
        /// Every model the host reports is declared, not only the chosen one. opencode can
        /// offer what it has been told about and nothing else, so declaring one model gives
        /// a picker with one entry - which reads as "this host only has one model" rather
        /// than "the plugin only mentioned one". The choice made in the options becomes the
        /// agents' default; the rest are there to switch to.
        /// </summary>
        private static string BuildLocalProvider(string host, string modelId, IReadOnlyList<string>? models) {
            var baseUrl = host.Trim().TrimEnd(char.Parse("/")) + "/v1";

            var declared = new List<string>();
            if (models != null) { declared.AddRange(models); }
            if (!declared.Contains(modelId)) { declared.Insert(0, modelId); }

            var entries = new StringBuilder();
            foreach (var model in declared) {
                entries.AppendLine("        " + Quote(model) + ": { " + Quote("name") + ": " + Quote(model) + " },");
            }

            var builder = new StringBuilder();
            builder.AppendLine("  " + Quote("provider") + ": {");
            builder.AppendLine("    " + Quote("ollama") + ": {");
            builder.AppendLine("      " + Quote("npm") + ": " + Quote("@ai-sdk/openai-compatible") + ",");
            builder.AppendLine("      " + Quote("name") + ": " + Quote("Ollama (" + host.Trim() + ")") + ",");
            builder.AppendLine("      " + Quote("options") + ": { " + Quote("baseURL") + ": " + Quote(baseUrl) + " },");
            builder.AppendLine("      " + Quote("models") + ": {");
            builder.AppendLine(Close(entries));
            builder.AppendLine("      }");
            builder.AppendLine("    }");
            builder.AppendLine("  },");
            return builder.ToString();
        }
        /// <summary>
        /// opencode's own tools, every one of them named.
        ///
        /// The deny floor refuses whatever a policy does not mention, and that cuts both ways:
        /// four of these went unnamed in the first version of the floor and were silently
        /// switched off - including "write", which resolves by its own identifier at call
        /// time and is not covered by naming "edit". An agent that cannot write a file, with
        /// nothing anywhere saying why, is a worse outcome than the one the floor prevents.
        ///
        /// The identifiers come from opencode's tool registry, read at the commit this
        /// version was checked against: bash, read, grep, glob, edit, write, apply_patch,
        /// task, webfetch, websearch, todowrite, question, skill, lsp, invalid. A later
        /// opencode that adds one will have it refused here, which is the safe direction and
        /// the same bargain the N.I.N.A. tools are on.
        /// </summary>
        private static void AppendBuiltins(StringBuilder permissions, string reading, string acting,
                                           string browsing) {
            // Reading the working directory and keeping notes.
            AppendPermission(permissions, "read", reading);
            AppendPermission(permissions, "grep", reading);
            AppendPermission(permissions, "glob", reading);
            AppendPermission(permissions, "todowrite", reading);

            // Not a capability: the tool opencode substitutes when a model calls something
            // that does not exist, so that it gets an error it can read instead of a hard
            // failure. Refusing it makes a mistyped tool name look like a crash.
            AppendPermission(permissions, "invalid", "allow");

            // Reaching outside the session.
            AppendPermission(permissions, "webfetch", browsing);
            AppendPermission(permissions, "websearch", browsing);
            AppendPermission(permissions, "question", browsing);
            AppendPermission(permissions, "skill", browsing);
            AppendPermission(permissions, "lsp", browsing);

            // Changing something on this machine.
            AppendPermission(permissions, "bash", acting);
            AppendPermission(permissions, "edit", acting);
            AppendPermission(permissions, "write", acting);
            AppendPermission(permissions, "apply_patch", acting);
            AppendPermission(permissions, "task", acting);
            AppendPermission(permissions, "external_directory", acting);
        }

        private static void AppendPermission(StringBuilder builder, string tool, string action)
            => builder.Append("    \"").Append(tool).Append("\": \"").Append(action).Append("\",\n");
    }
}
