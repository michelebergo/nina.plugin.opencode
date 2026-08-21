# Changelog

## 0.1.0.0 - not released

First working version. Not published to the N.I.N.A. plugin manager.

- **opencode itself, in a N.I.N.A. dock panel.** The panel hosts opencode's own interface rather
  than re-implementing the conversation in WPF. The editor extension people admire turned out to
  be three commands that open opencode in a terminal: what is worth having is opencode's own
  presentation, not anything the editor draws. So the conversation, tool calls with their output,
  permission prompts and the token and cost readout are all opencode's, and stay current when it
  changes.
- **Two ways to connect.** Launch a local server from the opencode you installed, or attach to
  one already running elsewhere - on the observatory PC while you sit at another machine. An
  attached server is never started or stopped by the plugin; somebody may be working in it.
- **Ready without configuring anything.** The plugin creates its own working directory, finds
  `nina_mcp_server` on the machine or clones it from GitHub on first connect, and writes the
  brief and the tool configuration into that directory.
- **The agent is told where it is.** `AGENTS.md` says this is a live observatory, that moving
  hardware is a physical act, that the sequencer is in charge while it runs, how to report a
  diagnosis so it can be checked, and when to stop and ask rather than retry.
- **A permission policy covering all 179 N.I.N.A. tools explicitly.** Reads and low-risk imaging
  actions allowed; anything that moves hardware or changes configuration asks first; and
  disconnecting the safety monitor or the weather source refused outright, because those are the
  devices that exist to stop the agent. Written out in full rather than left to a default: an
  unlisted tool inherits a default nobody has checked.
- **Three agents.** `observer` reads and explains and has the tools that move switched off;
  `copilot` acts and stops to ask first; `autopilot` runs unattended. The third one is not the
  second with the prompts turned off: with nobody at the keyboard an "ask" is not caution, it is a
  session that stops at three in the morning and stays stopped, so every permission it has is
  decided in advance and there is no `ask` anywhere in its policy. It may restart guiding, plate
  solve and recentre, stop or start the sequence, set the tracking mode, and park the mount and
  close the shutter. It may not slew, unpark, open a shutter, throw a power switch, connect or
  disconnect anything, or touch the profile - an agent that cannot open the dome cannot open it
  onto rain. Tools added to the MCP server later arrive denied for it rather than allowed by
  omission.
- **Observatory log.** The plugin records what changes - equipment state transitions, autofocus
  runs, and one line per saved frame with the frame's metrics and a numeric snapshot of every
  device - as JSON Lines, one file per night, in its own folder. It moves nothing, asks nothing
  and calls no model. The events an unattended agent would react to (star lost, device gone,
  weather turned unsafe) are not events N.I.N.A. raises: they are transitions between states it
  reports continuously, and which of them actually happen is a question about a particular
  observatory. This is how that question gets answered with data instead of a guess. On by
  default; switchable in the options.
- **A launched server binds to 127.0.0.1 and carries a password only if you set one.** Generating
  one per launch was tried and removed: the embedded browser never answered the challenge - the
  event simply did not fire - so the panel took a 401 and stayed blank while a credential nobody
  could type sat in memory. What keeps the server private is the address it listens on.
- **Generated files are refreshed, not frozen.** `AGENTS.md` and `opencode.json` carry a stamp:
  a file this plugin wrote is replaced when it is out of date, with the previous one kept beside
  it; a file somebody edited is left alone. The first version refused to touch either once they
  existed, which sounded respectful and meant a workspace prepared by an early build kept a thin
  brief and an empty policy forever.
