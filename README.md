# OpenCode for N.I.N.A.

You are imaging and something is off — guiding went bad an hour ago, the focus drifted, a frame
came back empty. Instead of digging through tabs and log files yourself, you ask in plain
English and an agent looks: it reads your equipment state, the night's log and the frames you
saved, and tells you what it finds. If the fix needs an action — restart guiding, plate solve
and recentre, stop the sequence — it asks you first. Every time.

The agent is **[opencode](https://opencode.ai)**: a free, open-source AI agent you install
separately, plus a model for it to use — a paid API key, or one running on your own machine.
This plugin contains no AI. It has no models, no API keys and no tools of its own. It talks to
an opencode server that you install and configure, so your providers, your agents, your
credentials and your MCP servers keep working exactly as they do in your terminal.

## What it gives you

- **The agent where you work.** Ask a question or hand it a job while you are imaging, without
  leaving N.I.N.A. and without a terminal.
- **Every permission in front of you.** opencode stops and asks before it runs a command, edits
  a file or uses a tool. This panel is where you answer: *Allow once*, *Always*, *Reject*.
  Nothing runs unseen on a machine that is pointing a telescope.
- **Interrupt** a turn at any time.
- **Two ways to connect.** *Launch* starts a local opencode server and stops it when you
  disconnect. *Attach* uses one already running — on the observatory PC, for instance, while
  you sit at another machine — and never starts or stops it.
- **It tells the agent where it is.** On connect the plugin writes a standing brief into the
  working directory saying that this is a live observatory: that slewing, parking, focusing and
  opening a dome move hardware, that an exposure in progress is somebody's night, and that
  reading logs and explaining a failure costs nothing. It is kept up to date as the plugin
  improves, unless you have edited it - then it is yours and is left alone.
- **Three agents.** `observer` reads and explains, `copilot` acts and asks first, and
  `autopilot` is the unattended one: every permission decided in advance, and a short list of
  actions that recover a night. Nothing triggers it automatically yet.

## What you need first

**opencode, installed by you.** The plugin will not download it, update it or manage it.
Fetching and running somebody else's 171 MB binary from inside a N.I.N.A. plugin is a promise
about software this plugin is in no position to keep — and an observatory machine is the last
place to discover a failed download at two in the morning.

```
npm install -g opencode-ai
```

or see https://opencode.ai for the other install methods. If `opencode` is on your PATH the
plugin will find it; otherwise set the full path in the options.

## What it sets up for you

The first time you connect, the plugin prepares a workspace so the agent arrives knowing where
it is and what it may do. None of this needs configuring.

**A working directory of its own** - `%LOCALAPPDATA%\NINA\Plugins\OpenCode\workspace` unless you
choose another. Not a source tree and not your N.I.N.A. profile: the agent's own room, safe to
read and safe to delete.

**N.I.N.A.'s tools.** [`nina_mcp_server`](https://github.com/michelebergo/nina_mcp_server) turns
N.I.N.A.'s Advanced API into MCP tools - camera, mount, focuser, filter wheel, guiding, plate
solving and the rest. The plugin fetches it into the workspace on first connect (needs `git`,
about 800 KB) and registers it. It fetches a **fixed release tag**, and after the checkout it
checks that what it got really is the commit that tag points at - a branch moves by definition
and a tag can be moved, and what runs here has an observatory behind it. If the check fails it
does not run. Dependencies come from that release's own pinned requirements, indirect ones
included, so two machines installing a month apart get the same server. It does not go looking
for copies you may have lying around: set a path in the options to use your own, and it is used
as it is, with the panel saying it is not the verified release. Running it needs `uv` on the
machine.

**A standing brief** (`AGENTS.md`) telling the agent it is attached to a live observatory: that
slewing, parking, focusing and opening a dome move hardware; that an exposure in progress is
somebody's night; that N.I.N.A.'s sequencer is in charge while it runs; how to report a diagnosis
so it can be checked; and when to stop and ask instead of retrying.

**A permission policy** (`opencode.json`) that opens with a deny floor and then grants. Reads and
the low-risk imaging actions run freely. Anything that moves hardware or changes configuration
asks first. Refused outright are disconnecting the safety monitor or the weather source - the
devices that exist to stop the agent, so working around them must not be something it can
choose - and `alert_human`, which takes any file path and any webhook and uploads the file.

All 179 tools are named, and named the way opencode actually offers them: it exposes an MCP tool
as `{server}_{tool}`, so a rule has to say `nina_nina_slew_mount`. The floor is there because
getting 179 names right is something you can get wrong once and in silence, and a tool that
matches no rule inherits opencode's own default, which is to allow it. With the floor, a tool
that is misnamed, renamed or added by a later version of the server arrives refused, and the
agent says so instead of doing it. A test compares the generated rules against the names the
pinned server really registers, and the release does not publish if it is red.

**Three agents**, differing in permissions rather than in tone:

| Agent | What it may do |
|---|---|
| `observer` *(default)* | Reads and explains. The tools that move are switched off for it. |
| `copilot` | Acts, and is stopped for your approval before anything that moves hardware. |
| `autopilot` | Runs unattended. Nothing is asked at the time, because nobody is there to answer. |

`autopilot` is not `copilot` with the prompts turned off. With nobody at the keyboard an "ask" is
not caution: it is a session that stops at three in the morning and stays stopped until somebody
wakes up. So every permission it has is decided in advance and there is no `ask` anywhere in its
policy - which only works if the list of things it may do is short. It may restart guiding, plate
solve and recentre, stop or start the sequence, set the tracking mode, and park the mount and
close the shutter. It may not slew, unpark, open a shutter, throw a power switch, connect or
disconnect anything, or edit the profile. An agent that cannot open the dome cannot open it onto
rain. Tools added to the MCP server later arrive denied for it rather than allowed by omission.

**An observatory log.** The plugin writes down what changes during the night - equipment state
transitions, autofocus runs, and one line per saved frame with its metrics and a numeric snapshot
of every device - as JSON Lines, one file per night, in
`%LOCALAPPDATA%\NINA\Plugins\OpenCode\observatory`. It moves nothing, asks nothing and calls no
model. Two reasons it exists: the agent can read it, so a diagnosis can start from what happened
rather than from what is happening now; and the events an unattended operator would react to -
star lost, device gone, weather turned unsafe - are not events N.I.N.A. raises, they are
transitions between states it reports continuously, and which of them actually occur is a
question about a particular observatory. On by default, switchable in the options.

Generated files are refreshed rather than frozen: `AGENTS.md` and `opencode.json` carry a stamp,
so a file this plugin wrote is replaced when it falls out of date, with the previous one kept
beside it as `.previous`. A file you have edited is left alone.

## Security

A server this plugin launches listens on `127.0.0.1` only, so it is reachable from this machine
and nowhere else. That, rather than a password, is what keeps it private: anything that could
reach a loopback port is already running as you, and could read a password out of the
configuration just as easily.

No password is set unless you ask for one. If you attach to a server that has one, put it in
the options and the panel will use it - and only for that server: the challenge is answered
when scheme, host and port match the address you configured, and ignored otherwise. Attaching
refuses an address that is neither on this machine nor `https`, because sending a password in
the clear across a network, to something that could be answering in the server's place, is not
a thing to do quietly.

A server somebody else started runs under its own configuration, so none of the policy above
applies to it. The panel reports whether the attached workspace carries this plugin's policy,
does not, or is on another machine and cannot be checked. It does not write into somebody
else's working directory to fix it.

The permission prompts are not decoration. An agent that can run shell commands on the machine
controlling your mount should be watched, and that is what they are for - which is also why the
default agent is the one that cannot move anything at all.

## What it does not do yet

**Nothing wakes the agent up.** The `autopilot` agent exists and its policy is enforced, but no
event triggers it: there is no classifier deciding which transitions deserve a turn, no counters
bounding how often it may act, and no notification when it stops. Selected by hand it behaves as
described; left to itself it does nothing at all. Unattended operation is being built in that
order on purpose - the log comes first, so that what the agent should react to is decided from
what this observatory actually does rather than from a plausible list of events.

## Building

```
dotnet build NINA.Plugin.OpenCode.sln -c Release
```

A Debug build copies itself into `%localappdata%\NINA\Plugins\3.0.0\OpenCode\`.

## Licence

MPL-2.0.
