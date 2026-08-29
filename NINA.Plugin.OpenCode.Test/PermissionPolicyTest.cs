using FluentAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NINA.Plugin.OpenCode.OpenCode;
using NUnit.Framework;

namespace NINA.Plugin.OpenCode.Test {

    /// <summary>
    /// The permission policy is the only thing standing between an agent reading a hostile
    /// instruction out of a log and that agent opening the dome. It was once written entirely
    /// correctly against names that do not exist - 121 rules, 179 tools, zero matches, and
    /// every tool falling through to opencode's own default of allow - and nothing said a
    /// word, because a policy that matches nothing is a policy that loads without complaint.
    ///
    /// So these tests do not count rules. They compare the generated keys against the names
    /// the pinned MCP server actually registers, read from a fixture regenerated from that
    /// server rather than typed here.
    /// </summary>
    [TestFixture]
    public class PermissionPolicyTest {

        private const string ServerId = "nina";

        private JObject config = null!;
        private HashSet<string> exposedNames = null!;

        [OneTimeSetUp]
        public void GenerateConfig() {
            var fixture = Path.Combine(TestContext.CurrentContext.TestDirectory,
                                       "Fixtures", "McpTools.v1.0.1.txt");
            exposedNames = File.ReadAllLines(fixture)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .Select(tool => ServerId + "_" + tool)
                .ToHashSet(StringComparer.Ordinal);

            config = JObject.Parse(NinaWorkspace.BuildMcpConfig(
                @"C:\tools\nina_advanced_mcp.py", "localhost", 1888, null, null, null));
        }

        private static IEnumerable<TestCaseData> Policies() {
            yield return new TestCaseData("project", "permission").SetName("project policy");
            yield return new TestCaseData("observer", "agent.observer.permission").SetName("observer policy");
            yield return new TestCaseData("autopilot", "agent.autopilot.permission").SetName("autopilot policy");
        }

        private JObject PolicyAt(string path) {
            var node = config.SelectToken(path);
            node.Should().NotBeNull($"the generated configuration should contain {path}");
            return (JObject)node!;
        }

        /// <summary>
        /// The rule for one tool, or a failure that says which rule was missing.
        ///
        /// Reading the value straight threw ArgumentNullException when the key was absent,
        /// which is the single most likely way for this policy to be wrong and the least
        /// helpful thing to be told about it.
        /// </summary>
        private string RuleFor(string path, string tool) {
            var policy = PolicyAt(path);
            policy.Should().ContainKey(tool, $"{path} should carry a rule for {tool}");
            return policy[tool]!.Value<string>()!;
        }

        [TestCaseSource(nameof(Policies))]
        public void EveryPolicyNamesExactlyTheToolsTheServerRegisters(string label, string path) {
            var keys = PolicyAt(path).Properties()
                .Select(p => p.Name)
                .Where(k => k.StartsWith(ServerId + "_", StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);

            // Both directions, and named, because the two failures are different problems:
            // a rule for a tool that does not exist is dead weight, a tool with no rule is a
            // hole.
            keys.Except(exposedNames).Should().BeEmpty(
                $"the {label} policy should not name tools the server does not register");
            exposedNames.Except(keys).Should().BeEmpty(
                $"every tool the server registers should have a rule in the {label} policy");
        }

        [TestCaseSource(nameof(Policies))]
        public void EveryPolicyStartsWithADenyFloor(string label, string path) {
            var policy = PolicyAt(path);

            // Order is the mechanism, not decoration: opencode resolves a tool against the
            // LAST rule that matches, so the floor only floors what comes after it.
            policy.Properties().First().Name.Should().Be("*",
                $"the {label} policy must open with the catch-all, or it floors nothing");
            policy["*"]!.Value<string>().Should().Be("deny",
                $"anything the {label} policy fails to name must be refused, not permitted");
        }

        [Test]
        public void TheToolNamesCarryTheServerPrefix() {
            // The defect itself, stated as a test. opencode exposes an MCP tool as
            // "{server}_{tool}", so a rule written against the bare name matches nothing.
            var policy = PolicyAt("permission");
            policy.Should().ContainKey("nina_nina_slew_mount");
            policy.Should().NotContainKey("nina_slew_mount",
                "the bare name is what opencode does NOT call it");
        }

        [TestCase("permission", "ask", TestName = "the copilot asks before slewing")]
        [TestCase("agent.observer.permission", "deny", TestName = "the observer cannot slew")]
        [TestCase("agent.autopilot.permission", "deny", TestName = "the autopilot cannot slew unattended")]
        public void MovingTheMountIsGatedPerAgent(string path, string expected) {
            RuleFor(path, "nina_nina_slew_mount").Should().Be(expected);
        }

        [TestCase("permission")]
        [TestCase("agent.observer.permission")]
        [TestCase("agent.autopilot.permission")]
        public void TheFileExfiltrationToolIsRefusedEverywhere(string path) {
            // Takes an arbitrary local path and an arbitrary webhook, opens the file and
            // uploads it. No agent here has a use for it.
            RuleFor(path, "nina_nina_alert_human").Should().Be("deny");
        }

        [TestCaseSource(nameof(Policies))]
        public void OpeningTheSessionIsNotTreatedAsTouchingHardware(string label, string path) {
            // Found in the field, on the first session after the policy started working: every
            // read came back 401 "Not connected to NINA server. Please connect first using
            // nina_connect" - and nina_connect was refused, because its name begins with
            // connect. It opens the HTTP session between the MCP server and N.I.N.A.'s API and
            // touches no device; the server's own description says so. Refusing it does not
            // make an agent careful, it makes it unable to read anything at all.
            RuleFor(path, "nina_nina_connect").Should().Be("allow",
                $"the {label} policy has to let the session be opened, or nothing else it allows can run");
        }

        [TestCaseSource(nameof(Policies))]
        public void ConnectingADeviceIsStillNotFree(string label, string path) {
            // And the distinction that makes the one above safe: the per-device connects are
            // hardware, and stay gated.
            RuleFor(path, "nina_nina_connect_camera").Should().NotBe("allow",
                $"the {label} policy should not let a device be taken over without a word");
        }

        [Test]
        public void ReadingTheObservatoryNeedsNoPrompt() {
            // The other half of the bargain: a policy that asks about everything teaches
            // people to click Allow without reading it.
            RuleFor("permission", "nina_nina_get_status").Should().Be("allow");
            RuleFor("agent.observer.permission", "nina_nina_get_status").Should().Be("allow");
        }

        /// <summary>
        /// opencode's own tool identifiers, read from its registry at the commit this version
        /// was checked against. The deny floor refuses what a policy does not mention, so an
        /// unnamed one is a capability switched off by accident - which is how "write" came to
        /// be denied while "edit" was allowed, since a call is authorised against its own
        /// identifier and not against a family name.
        /// </summary>
        private static readonly string[] OpenCodeBuiltins = {
            "bash", "read", "grep", "glob", "edit", "write", "apply_patch", "task",
            "webfetch", "websearch", "todowrite", "question", "skill", "lsp", "invalid",
            "external_directory",
        };

        [TestCaseSource(nameof(Policies))]
        public void EveryPolicyDecidesEveryBuiltInDeliberately(string label, string path) {
            var policy = PolicyAt(path);
            var missing = OpenCodeBuiltins.Where(b => policy[b] == null).ToList();

            missing.Should().BeEmpty(
                $"the {label} policy refuses whatever it does not name, so leaving a built-in " +
                "unnamed switches it off by accident rather than by decision");
        }

        [Test]
        public void NoRuleNamesSomethingThatDoesNotExist() {
            // A rule for a tool nothing will ever call is not harmless: it reads as cover that
            // is not there. "list" sat here for exactly that reason - opencode has no such
            // tool.
            var known = OpenCodeBuiltins.ToHashSet(StringComparer.Ordinal);
            var strays = PolicyAt("permission").Properties()
                .Select(p => p.Name)
                .Where(k => k != "*" && !k.StartsWith(ServerId + "_", StringComparison.Ordinal))
                .Where(k => !known.Contains(k))
                .ToList();

            strays.Should().BeEmpty("every rule should name either a N.I.N.A. tool or an opencode built-in");
        }

        [Test]
        public void TheObserverIsDeclaredAsAPolicyRatherThanTheDeprecatedToolMap() {
            // Written as the "tools" on/off map, the observer's switches were thrown for
            // tools that do not exist - so the read-only agent could move the mount.
            config.SelectToken("agent.observer.tools").Should().BeNull();
            config.SelectToken("agent.observer.permission").Should().NotBeNull();
        }

        [Test]
        public void ASettingWithAQuotationMarkInItStillProducesValidJson() {
            // The host comes from a text box. It used to be interpolated between two literal
            // quotes, so one quotation mark in it produced a file that will not parse - and,
            // placed with a little care, one carrying settings nobody wrote.
            var hostile = "localhost\", \"permission\": { \"*\": \"allow\" }, \"x\": \"";

            var json = NinaWorkspace.BuildMcpConfig(
                @"C:	ools
ina_advanced_mcp.py", hostile, 1888, null, null, null);

            var parsed = JObject.Parse(json);   // throws if the injection broke out
            parsed.SelectToken("mcp.nina.environment.NINA_HOST")!.Value<string>().Should().Be(hostile,
                "the whole thing should arrive as one string value");
            parsed["permission"]!["*"]!.Value<string>().Should().Be("deny",
                "and nothing it contained should have become a rule");
        }

        [Test]
        public void TheServerIsLaunchedFromThePinnedRequirementsAndInterpreter() {
            var command = config.SelectToken("mcp.nina.command")!.Values<string>().ToList();

            command.Should().Contain("--with-requirements",
                "the dependency set belongs to the server, not to a list kept in this plugin");
            command.Should().Contain(c => c!.EndsWith("requirements.txt", StringComparison.Ordinal));
            command.Should().Contain("--python",
                "a pin on the packages that leaves the interpreter free is only half a pin");
        }
    }
}
