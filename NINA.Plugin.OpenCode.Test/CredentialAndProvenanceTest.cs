using FluentAssertions;
using System;
using System.IO;
using NINA.Plugin.OpenCode.OpenCode;
using NUnit.Framework;

namespace NINA.Plugin.OpenCode.Test {

    /// <summary>
    /// Where the observatory's server password may go, and which copy of the MCP server is
    /// allowed to run.
    /// </summary>
    [TestFixture]
    public class CredentialTest {

        [TestCase("http://localhost:4096/session/1", "http://localhost:4096", true,
                  TestName = "the configured server, on a different path")]
        [TestCase("http://LOCALHOST:4096/x", "http://localhost:4096", true,
                  TestName = "host casing does not make it a different server")]
        [TestCase("http://localhost:4097/", "http://localhost:4096", false,
                  TestName = "another port on this machine is another server")]
        [TestCase("http://evil.example/", "http://localhost:4096", false,
                  TestName = "somewhere else entirely")]
        [TestCase("https://localhost:4096/", "http://localhost:4096", false,
                  TestName = "a different scheme is a different origin")]
        [TestCase("not a uri", "http://localhost:4096", false,
                  TestName = "an address that will not parse gets nothing")]
        [TestCase("http://localhost:4096/", "", false,
                  TestName = "with no server configured there is nobody to trust")]
        public void ThePasswordGoesOnlyToTheServerThePanelWasPointedAt(
            string challenged, string configured, bool expected) {

            // The hosted interface is a web page: it can be made to request a resource
            // elsewhere, and that elsewhere can answer with an authentication challenge.
            // Answering every challenge hands out the password to whoever asks.
            OpenCodeTerminalView.IsConfiguredServer(challenged, configured).Should().Be(expected);
        }
    }

    /// <summary>
    /// Attaching to a server somebody else started means the agent runs under that server's
    /// configuration. That is allowed; going quiet about it is not.
    /// </summary>
    [TestFixture]
    public class AttachedPolicyTest {

        private static string? Missing(string path) => null;

        [Test]
        public void AWorkspaceThisPluginPreparedIsRecognisedAsOurs() {
            NinaWorkspace.InspectAttached(@"C:\work", serverIsLocal: true,
                    _ => "{ \"_generatedBy\": \"nina.plugin.opencode 0.1.0.0\" }")
                .Should().Be(NinaWorkspace.AttachedPolicy.Ours);
        }

        [Test]
        public void AWorkspacePreparedByAnEarlierBuildIsStillOurs() {
            // The stamp carries a version; the policy in an older workspace is still this
            // plugin's policy, so the version must not decide the answer.
            NinaWorkspace.InspectAttached(@"C:\work", serverIsLocal: true,
                    _ => "{ \"_generatedBy\": \"nina.plugin.opencode 0.0.9.0\" }")
                .Should().Be(NinaWorkspace.AttachedPolicy.Ours);
        }

        [Test]
        public void SomebodyElsesConfigurationIsReportedAsForeign() {
            NinaWorkspace.InspectAttached(@"C:\work", serverIsLocal: true, _ => "{ \"permission\": {} }")
                .Should().Be(NinaWorkspace.AttachedPolicy.Foreign);
        }

        [Test]
        public void AWorkspaceWithNoConfigurationAtAllIsForeign() {
            NinaWorkspace.InspectAttached(@"C:\work", serverIsLocal: true, Missing)
                .Should().Be(NinaWorkspace.AttachedPolicy.Foreign);
        }

        [Test]
        public void AServerOnAnotherMachineCannotBeChecked() {
            // The directory it reports is a path on that machine, and reading it here would
            // either fail or - worse - find something unrelated with the same name.
            NinaWorkspace.InspectAttached(@"/home/someone/work", serverIsLocal: false, _ => "anything")
                .Should().Be(NinaWorkspace.AttachedPolicy.Unknown);
        }

        [Test]
        public void AReaderThatThrowsIsNotTakenAsReassurance() {
            NinaWorkspace.InspectAttached(@"C:\work", serverIsLocal: true,
                    _ => throw new UnauthorizedAccessException())
                .Should().Be(NinaWorkspace.AttachedPolicy.Unknown);
        }
    }

    /// <summary>
    /// Which copy of the MCP server this plugin is willing to run.
    /// </summary>
    [TestFixture]
    public class McpServerLocationTest {

        [Test]
        public void APathSomebodyTypedIsHonouredWhenItExists() {
            var file = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".py");
            File.WriteAllText(file, "# stand-in for the MCP server");
            try {
                NinaWorkspace.LocateMcpServer(file).Should().Be(file);
                NinaWorkspace.LocateMcpServer("  " + file + "  ").Should().Be(file,
                    "a path pasted with whitespace around it is the same path");
            } finally {
                File.Delete(file);
            }
        }

        [Test]
        public void APathThatIsNotThereIsNotSilentlyReplaced() {
            NinaWorkspace.LocateMcpServer(@"C:\nowhere\nina_advanced_mcp.py").Should().BeNull();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void WithNoPathConfiguredNothingIsGoneLookingFor(string? configured) {
            // This used to search five common developer directories for any file called
            // nina_advanced_mcp.py and run the first one it found: an unverified script,
            // chosen by filename, with the whole observatory behind it. A path typed into
            // Options is a deliberate act; a filename match in a home directory is not.
            NinaWorkspace.LocateMcpServer(configured).Should().BeNull();
        }
    }
}
