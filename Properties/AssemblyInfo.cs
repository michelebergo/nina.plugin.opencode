using System.Reflection;
using System.Runtime.InteropServices;

// General Information
[assembly: AssemblyTitle("OpenCode")]
// This, not AssemblyMetadata("ShortDescription"), is what CreateManifest.ps1 publishes as the
// plugin's short description - the line people read in the list. The two must say the same thing.
[assembly: AssemblyDescription("Ask an AI agent about your rig in plain English: it reads your equipment, logs and frames, and asks before it touches anything")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Michele Bergo")]
[assembly: AssemblyProduct("NINA.Plugins")]
[assembly: AssemblyCopyright("Copyright © 2026 Michele Bergo")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// COM visibility
[assembly: ComVisible(false)]

// Plugin GUID - must match manifest.json Identifier
[assembly: Guid("75c31a01-6606-4cff-9703-4ee53c2f4e44")]

// Version information
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

// Plugin metadata - aligned with NINA manifest standards
[assembly: AssemblyMetadata("Identifier", "75c31a01-6606-4cff-9703-4ee53c2f4e44")]
[assembly: AssemblyMetadata("Author", "Michele Bergo")]
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.0")]
[assembly: AssemblyMetadata("License", "MPL-2.0")]
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
[assembly: AssemblyMetadata("Repository", "https://github.com/michelebergo/nina.plugin.opencode")]
[assembly: AssemblyMetadata("Homepage", "https://github.com/michelebergo/nina.plugin.opencode")]
[assembly: AssemblyMetadata("FeaturedImageURL", "https://raw.githubusercontent.com/michelebergo/nina.plugin.opencode/main/icon.png")]
[assembly: AssemblyMetadata("ScreenshotURL", "")]
[assembly: AssemblyMetadata("AltScreenshotURL", "")]
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/michelebergo/nina.plugin.opencode/releases")]
[assembly: AssemblyMetadata("Tags", "AI,Agent,opencode,Automation,MCP,Assistant")]

// Short description (required by NINA plugin manager)
[assembly: AssemblyMetadata("ShortDescription", "Ask an AI agent about your rig in plain English: it reads your equipment, logs and frames, and asks before it touches anything")]

// Long description
[assembly: AssemblyMetadata("LongDescription", @"WHAT IT IS FOR
You are imaging and something is off - guiding went bad an hour ago, the focus drifted, a frame came back empty. Instead of digging through tabs and log files yourself, you ask in plain English and the agent looks: it reads your equipment state, the night's log and the frames you saved, and tells you what it finds. If the fix needs an action - restart guiding, plate solve and recentre, stop the sequence - it asks you first. Every time.

WHAT YOU NEED
The agent itself is opencode (https://opencode.ai): a free, open-source AI agent that you install separately, plus a model for it to use - a paid API key, or one running locally on your own machine. Your choice. This plugin contains no AI and installs none, so your models, your keys and your MCP servers stay yours and keep working exactly as they do in your terminal. If opencode is not installed, the panel says so and does nothing else.

WHAT IT GIVES YOU
- The agent in a dock panel: ask a question or hand it a job while you are imaging, without leaving NINA and without a terminal.
- Every permission request in front of you. It asks before it runs a command, edits a file or uses a tool, and this panel is where you answer. Nothing runs unseen. Interrupt at any time.
- NINA itself as tools: camera, mount, focuser, filter wheel, guiding, plate solving - 179 of them, through an MCP server the plugin sets up for you.
- Three agents, differing in what they are allowed to do rather than in tone. observer reads and explains and moves nothing - that is what you get by default. copilot acts, but stops and asks first. autopilot is the unattended one: no prompts anywhere, every permission decided in advance, and a short list of actions that can save a night (restart guiding, plate solve and recentre, stop or start the sequence, set tracking, park the mount, close the shutter). It cannot slew, unpark, open a shutter, throw a switch or connect a device.
- An observatory log: equipment state changes, autofocus runs and one line per saved frame, one file per night. It moves nothing and calls no model, and the agent can read it - so a diagnosis starts from what happened, not only from what is happening now.
- Two ways to connect: launch a local opencode server from the executable on this machine, or attach to one already running elsewhere - on the observatory PC, for instance, while you sit at another.

WHY IT IS SEPARATE FROM THE AI ASSISTANT PLUGIN
The AI Assistant plugin is self-contained: its own providers, its own keys, its own tool loop. This one holds none of that on purpose. Use it if you already run opencode, or if you would rather configure one agent once and reach it from everywhere instead of keeping a second AI setup inside NINA.

WHAT IT DOES NOT DO YET
Nothing wakes the agent up. The autopilot agent exists and its policy is enforced, but no event triggers it: you choose it by hand. Unattended operation is being built log-first on purpose, so that what the agent reacts to is decided from what your observatory actually does rather than from a plausible list.")]

// The test project asserts things this plugin has no reason to expose publicly: that the
// generated permission policy names the tools opencode actually exposes, that a credential
// is only offered to the server the panel was pointed at, and that no copy of the MCP
// server runs unless somebody named it. Declared here rather than in the project file,
// because GenerateAssemblyInfo is off and the SDK item would be ignored.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("NINA.Plugin.OpenCode.Test")]
