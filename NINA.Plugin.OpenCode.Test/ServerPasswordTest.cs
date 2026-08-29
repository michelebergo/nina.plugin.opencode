using FluentAssertions;
using NINA.Plugin.OpenCode.OpenCode;
using NUnit.Framework;
using System;

namespace NINA.Plugin.OpenCode.Test {

    /// <summary>
    /// The password the launched server asks for, and the trip it makes to reach the panel.
    ///
    /// There is none unless somebody sets one. Inventing one per launch has been tried twice:
    /// the first time the embedded browser never answered the challenge, and the second time
    /// the page loaded but the interface's own calls back to the server did not carry the
    /// credential, came back 401, and the panel died with "Settings context must be used
    /// within a context provider". What keeps the server private meanwhile is the address it
    /// listens on - 127.0.0.1, so nothing off this machine can reach it.
    /// </summary>
    [TestFixture]
    public class ServerPasswordTest {

        [Test]
        public void APasswordSomebodySetIsUsedAsItIs() {
            OpenCodeServerProcess.ResolvePassword("hunter2").Should().Be("hunter2");
            OpenCodeServerProcess.ResolvePassword("  hunter2  ").Should().Be("hunter2",
                "a value pasted with spaces around it is the same value");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void WithNothingSetThereIsNoPassword(string? configured) {
            OpenCodeServerProcess.ResolvePassword(configured).Should().BeNull(
                "one invented here does not reach the interface, and a panel that cannot talk " +
                "to its own server is worse than an open one on the loopback address");
        }

        [TestCase("hunter2", TestName = "an ordinary one")]
        [TestCase("p@ssw0rd", TestName = "with an at sign, which ends the credentials in a URL")]
        [TestCase("a:b:c", TestName = "with colons, which separate user from password")]
        [TestCase("pa/ss?wo#rd", TestName = "with the characters that end the authority")]
        [TestCase("100% sure", TestName = "with a percent and a space")]
        public void APasswordSurvivesTheTripThroughTheAddress(string password) {
            // The panel does not ask the server for the credential, it puts it in the address
            // it navigates to. Somebody is free to type any of these into Options, and each one
            // means something in a URL - so what comes out the other end has to be what went in.
            var address = OpenCodeTerminalView.WithCredentials("http://127.0.0.1:4096/", password);
            var parsed = new Uri(address);

            var carried = Uri.UnescapeDataString(parsed.UserInfo.Substring(parsed.UserInfo.IndexOf(':') + 1));
            carried.Should().Be(password);
            parsed.Host.Should().Be("127.0.0.1", "and it must still be the same server");
            parsed.Port.Should().Be(4096);
        }

        [Test]
        public void AnAddressWithNoPasswordIsLeftAlone() {
            OpenCodeTerminalView.WithCredentials("http://127.0.0.1:4096/", null)
                .Should().Be("http://127.0.0.1:4096/");
        }
    }
}
