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
        private const string GeneratorStamp = "nina.plugin.opencode 0.1.0.0";

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
            "nina_auto_brightness_flats", "nina_auto_exposure_flats", "nina_calibrate_guider",
            "nina_change_profile_value", "nina_clear_guider_calibration", "nina_close_dome_shutter",
            "nina_connect", "nina_connect_camera", "nina_connect_dome",
            "nina_connect_filterwheel", "nina_connect_flatpanel", "nina_connect_focuser",
            "nina_connect_guider", "nina_connect_mount", "nina_connect_rotator",
            "nina_connect_safetymonitor", "nina_connect_switch", "nina_connect_weather",
            "nina_control_dew_heater", "nina_determine_framingassistant_rotation", "nina_disconnect",
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
            "nina_auto_brightness_flats", "nina_auto_exposure_flats", "nina_calibrate_guider",
            "nina_change_profile_value", "nina_clear_guider_calibration", "nina_close_dome_shutter",
            "nina_connect", "nina_connect_camera", "nina_connect_dome",
            "nina_connect_filterwheel", "nina_connect_flatpanel", "nina_connect_focuser",
            "nina_connect_guider", "nina_connect_mount", "nina_connect_rotator",
            "nina_connect_safetymonitor", "nina_connect_switch", "nina_connect_weather",
            "nina_control_dew_heater", "nina_determine_framingassistant_rotation", "nina_disconnect",
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
        private const string McpDependencies =
            "fastmcp==1.0,fastapi,uvicorn,pydantic,aiohttp,requests,python-dotenv,structlog,python-dateutil,pytz";

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
        /// Finds the N.I.N.A. MCP server. An explicit setting wins; otherwise the places a
        /// clone normally lands are checked, because asking someone to paste a path to a
        /// file they already have is a configuration step that exists only to be forgotten.
        /// </summary>
        public static string? LocateMcpServer(string? configured) {
            if (!string.IsNullOrWhiteSpace(configured)) {
                var explicitPath = configured!.Trim();
                return File.Exists(explicitPath) ? explicitPath : null;
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] candidates = {
                Path.Combine(home, "Desktop", "github", "nina_mcp_server", McpScriptName),
                Path.Combine(home, "github", "nina_mcp_server", McpScriptName),
                Path.Combine(home, "Documents", "nina_mcp_server", McpScriptName),
                Path.Combine(home, "source", "repos", "nina_mcp_server", McpScriptName),
                Path.Combine(home, "nina_mcp_server", McpScriptName),
                // Where this plugin puts it when it fetches it itself.
                Path.Combine(DefaultDirectory, "nina_mcp_server", McpScriptName),
            };

            foreach (var candidate in candidates) {
                try {
                    if (File.Exists(candidate)) { return candidate; }
                } catch (ArgumentException) {
                    // A path this machine cannot express is not a reason to stop looking.
                }
            }
            return null;
        }

        private const string McpScriptName = "nina_advanced_mcp.py";

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
            if (existing != null) { return existing; }

            var target = Path.Combine(
                string.IsNullOrWhiteSpace(workingDirectory) ? DefaultDirectory : workingDirectory,
                "nina_mcp_server");
            var script = Path.Combine(target, McpScriptName);
            if (File.Exists(script)) { return script; }

            if (!HasGit()) {
                report?.Invoke($"git is not installed, so the N.I.N.A. tools cannot be fetched. " +
                               $"Clone {McpServerRepository} yourself and set its path in Options.");
                return null;
            }

            report?.Invoke("Fetching the N.I.N.A. tools...");
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var clone = new ProcessStartInfo {
                    FileName = "git",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                clone.ArgumentList.Add("clone");
                // Shallow: this is a working copy of somebody else's tool, not a repository
                // anybody is going to develop in from here.
                clone.ArgumentList.Add("--depth");
                clone.ArgumentList.Add("1");
                clone.ArgumentList.Add(McpServerRepository);
                clone.ArgumentList.Add(target);

                using var process = Process.Start(clone);
                if (process == null) { return null; }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(TimeSpan.FromMinutes(3));
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

                if (process.ExitCode != 0) {
                    var error = await process.StandardError.ReadToEndAsync(token).ConfigureAwait(false);
                    Logger.Warning($"OpenCode: could not clone the MCP server ({error.Trim()})");
                    report?.Invoke($"Could not fetch the N.I.N.A. tools from {McpServerRepository}.");
                    return null;
                }
            } catch (Exception ex) {
                Logger.Warning($"OpenCode: could not clone the MCP server ({ex.Message})");
                report?.Invoke($"Could not fetch the N.I.N.A. tools: {ex.Message}");
                return null;
            }

            if (!File.Exists(script)) {
                report?.Invoke($"The repository was fetched but {McpScriptName} is not in it.");
                return null;
            }

            Logger.Info($"OpenCode: cloned the N.I.N.A. MCP server into {target}");
            return script;
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
        private static string BuildMcpConfig(string ninaMcpServerPath, string ninaHost, int ninaPort,
                                             string? localModelHost, string? localModelId,
                                             IReadOnlyList<string>? localModels) {
            // Forward slashes on purpose: the path lands inside JSON, where a Windows
            // backslash would have to be escaped and is the classic way this file ends up
            // silently invalid.
            var script = ninaMcpServerPath.Replace(@"\", "/");

            var permissions = new StringBuilder();

            // The built-in tools first. bash is a shell on the observatory machine and edit
            // rewrites files there, so both ask; reading and searching change nothing.
            AppendPermission(permissions, "read", "allow");
            AppendPermission(permissions, "grep", "allow");
            AppendPermission(permissions, "glob", "allow");
            AppendPermission(permissions, "list", "allow");
            AppendPermission(permissions, "webfetch", "allow");
            AppendPermission(permissions, "websearch", "allow");
            AppendPermission(permissions, "question", "allow");
            AppendPermission(permissions, "todowrite", "allow");
            AppendPermission(permissions, "bash", "ask");
            AppendPermission(permissions, "edit", "ask");
            AppendPermission(permissions, "task", "ask");
            AppendPermission(permissions, "external_directory", "ask");

            foreach (var tool in NeverAllowed) { AppendPermission(permissions, tool, "deny"); }
            foreach (var tool in AskBeforeUsing) { AppendPermission(permissions, tool, "ask"); }

            var policy = Close(permissions);
            var observerTools = BuildObserverTools();
            var autopilotPolicy = BuildAutopilotPolicy();

            // A local model is declared as an OpenAI-compatible provider, which is what
            // Ollama serves on /v1, and then named on each agent. Left empty, none of this is
            // written and opencode keeps using whatever provider the user already had.
            var useLocal = !string.IsNullOrWhiteSpace(localModelHost) && !string.IsNullOrWhiteSpace(localModelId);
            var provider = useLocal ? BuildLocalProvider(localModelHost!, localModelId!, localModels) : string.Empty;
            var agentModel = useLocal ? "      " + Quote("model") + ": " + Quote("ollama/" + localModelId) + "," + Environment.NewLine : string.Empty;

            return $@"{{
  ""$schema"": ""https://opencode.ai/config.json"",
  ""_generatedBy"": ""{GeneratorStamp}"",
  ""instructions"": [""AGENTS.md""],
  ""default_agent"": ""observer"",
  ""mcp"": {{
    ""nina"": {{
      ""type"": ""local"",
      ""command"": [
        ""uv"", ""run"",
        ""--with"", ""{McpDependencies}"",
        ""fastmcp"", ""run"",
        ""{script}""
      ],
      ""environment"": {{
        ""NINA_HOST"": ""{ninaHost}"",
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
      ""prompt"": ""{ObserverPrompt}"",
      ""tools"": {{
{observerTools}
      }}
    }},
    ""copilot"": {{
{agentModel}      ""description"": ""Can act on the observatory, asking before anything that moves hardware."",
      ""mode"": ""primary"",
      ""temperature"": 0.1,
      ""prompt"": ""{CopilotPrompt}""
    }},
    ""autopilot"": {{
{agentModel}      ""description"": ""Unattended recovery. Every permission is decided in advance; it never asks."",
      ""mode"": ""primary"",
      ""temperature"": 0.1,
      ""steps"": 20,
      ""prompt"": ""{AutopilotPrompt}"",
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

            AppendPermission(permissions, "read", "allow");
            AppendPermission(permissions, "grep", "allow");
            AppendPermission(permissions, "glob", "allow");
            AppendPermission(permissions, "list", "allow");
            AppendPermission(permissions, "todowrite", "allow");
            AppendPermission(permissions, "bash", "deny");
            AppendPermission(permissions, "edit", "deny");
            AppendPermission(permissions, "task", "deny");
            AppendPermission(permissions, "external_directory", "deny");
            AppendPermission(permissions, "question", "deny");
            AppendPermission(permissions, "webfetch", "deny");
            AppendPermission(permissions, "websearch", "deny");

            // Everything that moves is refused, and then the recovery actions are granted
            // back. Written in that order on purpose: a tool added to the MCP server later
            // arrives denied for the unattended agent rather than allowed by omission.
            var allowed = new HashSet<string>(RecoveryActions, StringComparer.Ordinal);
            foreach (var tool in MutatingTools) {
                AppendPermission(permissions, tool, allowed.Contains(tool) ? "allow" : "deny");
            }
            foreach (var tool in RecoveryActions) {
                if (Array.IndexOf(MutatingTools, tool) < 0) { AppendPermission(permissions, tool, "allow"); }
            }
            foreach (var tool in NeverAllowed) {
                if (Array.IndexOf(MutatingTools, tool) < 0) { AppendPermission(permissions, tool, "deny"); }
            }

            return Close(permissions);
        }

        /// <summary>The tools the observer is denied, rendered as opencode's on/off map.</summary>
        private static string BuildObserverTools() {
            var builder = new StringBuilder();
            foreach (var tool in MutatingTools) {
                builder.AppendLine("        " + Quote(tool) + ": false,");
            }
            return Close(builder);
        }

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
        private static void AppendPermission(StringBuilder builder, string tool, string action)
            => builder.Append("    \"").Append(tool).Append("\": \"").Append(action).Append("\",\n");
    }
}
