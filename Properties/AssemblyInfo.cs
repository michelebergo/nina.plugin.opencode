using System.Reflection;
using System.Runtime.InteropServices;

// General Information
[assembly: AssemblyTitle("OpenCode")]
[assembly: AssemblyDescription("Runs your own opencode agent inside NINA, with every tool call it wants to make shown for approval")]
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
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

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
[assembly: AssemblyMetadata("ShortDescription", "A panel for the opencode agent you already run: ask it to work on your observatory, and approve every tool call before it happens")]

// Long description
[assembly: AssemblyMetadata("LongDescription", @"A seat inside NINA for opencode, the open-source AI agent.

This plugin does not contain an AI and does not install one. It talks to an opencode server that you install and configure yourself, which means your models, your keys, your agents and your MCP servers - including the ones that already speak to NINA - all keep working exactly as they do in your terminal.

WHAT IT GIVES YOU
- The agent, in a NINA dock panel: ask a question or hand it a job while you are imaging, without leaving NINA and without a terminal.
- Every permission request in front of you. opencode asks before it runs a command, edits a file or uses a tool; this panel is where you answer. Nothing runs unseen.
- The agent's own questions, answered in the same place.
- Interrupt at any time.
- Two ways to connect: launch a local opencode server from the executable on this machine, or attach to one already running elsewhere - on the observatory PC, for instance, while you sit at another.
- Three agents, differing in permissions rather than in tone: observer reads and explains, copilot acts and stops for your approval first, and autopilot is the unattended one - every permission decided in advance, and a short list of actions that recover a night (restart guiding, plate solve and recentre, stop or start the sequence, set tracking, park the mount, close the shutter). It cannot slew, unpark, open a shutter, throw a switch or connect a device.
- An observatory log: equipment state changes, autofocus runs and one line per saved frame, written to a file per night. It moves nothing and calls no model, and the agent can read it, so a diagnosis starts from what happened rather than from what is happening now.

WHAT YOU NEED FIRST
opencode, installed by you: https://opencode.ai. The plugin will not download it, will not update it and will not manage it. If opencode is not there, the panel says so and does nothing else.

WHY IT IS SEPARATE FROM THE AI ASSISTANT PLUGIN
The AI Assistant plugin is self-contained: it holds its own providers, its own keys and its own tool loop. This one holds none of that on purpose. It is for people who already run opencode and would rather have one agent, configured once, reachable from where they work.

WHAT IT DOES NOT DO YET
Nothing wakes the agent up. The autopilot agent exists and its policy is enforced, but no event triggers it: there is no classifier deciding what deserves a turn, no counters bounding how often it may act, and no notification when it stops. Chosen by hand it behaves as described; left to itself it does nothing. Unattended operation is being built in that order on purpose - the log first, so that what the agent should react to is decided from what your observatory actually does.")]
