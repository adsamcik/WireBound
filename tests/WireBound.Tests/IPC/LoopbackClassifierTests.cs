using AwesomeAssertions;
using WireBound.IPC;

namespace WireBound.Tests.IPC;

/// <summary>
/// Tests for <see cref="LoopbackClassifier"/>, which splits per-app traffic
/// into loopback/localhost vs real network.
/// </summary>
public class LoopbackClassifierTests
{
    [Test]
    [Arguments("127.0.0.1")]
    [Arguments("127.5.5.5")]
    [Arguments("127.255.255.254")]
    [Arguments("::1")]
    [Arguments("0:0:0:0:0:0:0:1")]
    public void IsLoopback_LoopbackAddresses_ReturnsTrue(string address)
    {
        LoopbackClassifier.IsLoopback(address).Should().BeTrue();
    }

    [Test]
    [Arguments("8.8.8.8")]
    [Arguments("192.168.1.10")]
    [Arguments("10.0.0.1")]
    [Arguments("2606:4700:4700::1111")]
    [Arguments("")]
    [Arguments("not-an-ip")]
    [Arguments("localhost")]
    public void IsLoopback_NonLoopbackOrInvalid_ReturnsFalse(string address)
    {
        LoopbackClassifier.IsLoopback(address).Should().BeFalse();
    }

    [Test]
    public void IsLoopback_Null_ReturnsFalse()
    {
        LoopbackClassifier.IsLoopback(null).Should().BeFalse();
    }
}
