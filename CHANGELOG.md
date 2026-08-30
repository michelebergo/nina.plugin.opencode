# Changelog

## 1.0.0.0 - 2026-08-30

First published version. An earlier build, 0.1.0.0, was submitted to the plugin catalogue on
21 August and rejected on 25 August: a security audit found that the permission policy it
generated matched none of the tools it meant to govern, so every agent could do everything.
That build was never merged and nobody could install it. What the audit found is fixed here,
and the fixes are what the test suite now holds in place.

- **opencode itself, in a N.I.N.A. dock panel.** The panel hosts opencode's own interface rather
  than re-implementing the conversation in WPF. The editor extension people admire turned out to
  be three commands that open opencode in a terminal: what is worth having is opencode's own
  presentation, not anything the editor draws. So the conversation, tool calls with their output,
  permission prompts and the token and cost readout are all opencode's, and stay current when it
  changes.
- **Two ways to connect.** Launch a local server from the opencode you installed, or attach to
  one already running elsewhere - on the observatory PC while you sit at another machine. An
  attached server is never started or stopped by the plugin; somebody may be working in it.
- **Ready without configuring anything.** The plugin creates its own working directory, fetches
  a fixed release of `nina_mcp_server` from GitHub on first connect, and writes the brief and the
  tool configuration into that directory. It fetches a tag, **v1.0.1**, and checks after the
  checkout that it really is the commit that tag points at - a branch moves by definition and a
  tag can be moved, and what gets run here has an observatory behind it. If the check fails it
  does not run. Dependencies come from that release's own pinned `requirements.txt`, every
  package including the indirect ones, so two machines installing a month apart get the same
  server. Point Options at your own copy if you would rather use one; it is used as it is, and
  the panel says it is not the verified release.
- **The agent is told where it is.** `AGENTS.md` says this is a live observatory, that moving
  hardware is a physical act, that the sequencer is in charge while it runs, how to report a
  diagnosis so it can be checked, and when to stop and ask rather than retry.
- **A permission policy that refuses what it does not name.** Every policy opens with a deny
  floor, and then grants: reads and the low-risk imaging actions allowed, anything that moves
  hardware or changes configuration asks first, and refused outright are disconnecting the safety
  monitor or the weather source - the devices that exist to stop the agent - and `alert_human`,
  which takes any file path and any webhook and uploads the file.

  All 179 tools are named, and named as opencode actually offers them: it exposes an MCP tool as
  `{server}_{tool}`, so what the rules have to say is `nina_nina_slew_mount`. The floor is there
  because getting 179 names right is something you can get wrong once and in silence, and a tool
  matching no rule inherits opencode's own default, which is to allow it. With the floor, a tool
  that is misnamed, renamed or added in a later version of the server arrives refused, and the
  agent says so instead of doing it. opencode's own tools are named too, for the same reason in
  reverse: an unnamed one would be switched off by accident.
- **Three agents.** `observer` reads and explains, and everything that moves is refused to it;
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
- **A launched server binds to 127.0.0.1, and carries a password only if you set one.**
  Generating one per launch has been tried twice and dropped twice. The first time the embedded
  browser never answered the authentication challenge. The second time the page loaded, but the
  interface's own calls back to the server did not carry the credential and came back 401, so
  the panel died with a settings error - a page that opens and then cannot talk to its own
  server is worse than an open one. What keeps it private meanwhile is the address it listens
  on: nothing off this machine can reach it.
- **Attaching says whose rules are in force.** A server somebody else started runs under its own
  configuration, so none of the policy above applies to it. The panel now reports whether the
  attached workspace carries this plugin's policy, does not, or is on another machine and cannot
  be checked. It does not write into somebody else's working directory to fix it. Attaching also
  refuses an address that is neither on this machine nor https: sending the password in the clear
  across a network, to something that could be answering in the server's place, is not a thing to
  do quietly.
- **Tested where it matters, and the tests run before anything is published.** The check that
  earns its keep compares the rules the plugin generates against the tool names the pinned server
  actually registers - read from that server, not typed by hand - so a policy that names things
  nothing will ever call, or misses things that exist, fails. Moving the pin to a newer server
  fails it too, until somebody has decided what the new tools may do. They run on every change
  and again in the release, which does not publish if one is red.
- **Generated files are refreshed, not frozen.** `AGENTS.md` and `opencode.json` carry a stamp:
  a file this plugin wrote is replaced when it is out of date, with the previous one kept beside
  it; a file somebody edited is left alone. The first version refused to touch either once they
  existed, which sounded respectful and meant a workspace prepared by an early build kept a thin
  brief and an empty policy forever.
