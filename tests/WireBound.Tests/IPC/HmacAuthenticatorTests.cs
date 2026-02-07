using WireBound.IPC.Security;

namespace WireBound.Tests.IPC;

public class HmacAuthenticatorTests
{
    [Test]
    public void GenerateSecret_ReturnsThirtyTwoBytes()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        secret.Length.Should().Be(32);
    }

    [Test]
    public void GenerateSecret_ReturnsDifferentValues()
    {
        var secret1 = HmacAuthenticator.GenerateSecret();
        var secret2 = HmacAuthenticator.GenerateSecret();
        secret1.Should().NotBeEquivalentTo(secret2);
    }

    [Test]
    public void Sign_ProducesConsistentSignature()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        var pid = 1234;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var sig1 = HmacAuthenticator.Sign(pid, timestamp, secret);
        var sig2 = HmacAuthenticator.Sign(pid, timestamp, secret);

        sig1.Should().Be(sig2);
    }

    [Test]
    public void Sign_DifferentPids_ProduceDifferentSignatures()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var sig1 = HmacAuthenticator.Sign(1234, timestamp, secret);
        var sig2 = HmacAuthenticator.Sign(5678, timestamp, secret);

        sig1.Should().NotBe(sig2);
    }

    [Test]
    public void Sign_DifferentTimestamps_ProduceDifferentSignatures()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        var pid = 1234;

        var sig1 = HmacAuthenticator.Sign(pid, 1000, secret);
        var sig2 = HmacAuthenticator.Sign(pid, 2000, secret);

        sig1.Should().NotBe(sig2);
    }

    [Test]
    public void Validate_ValidSignature_ReturnsTrue()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        var pid = 1234;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = HmacAuthenticator.Sign(pid, timestamp, secret);

        HmacAuthenticator.Validate(pid, timestamp, signature, secret).Should().BeTrue();
    }

    [Test]
    public void Validate_WrongSecret_ReturnsFalse()
    {
        var secret1 = HmacAuthenticator.GenerateSecret();
        var secret2 = HmacAuthenticator.GenerateSecret();
        var pid = 1234;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = HmacAuthenticator.Sign(pid, timestamp, secret1);

        HmacAuthenticator.Validate(pid, timestamp, signature, secret2).Should().BeFalse();
    }

    [Test]
    public void Validate_WrongPid_ReturnsFalse()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = HmacAuthenticator.Sign(1234, timestamp, secret);

        HmacAuthenticator.Validate(5678, timestamp, signature, secret).Should().BeFalse();
    }

    [Test]
    public void Validate_ExpiredTimestamp_ReturnsFalse()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        var pid = 1234;
        var oldTimestamp = DateTimeOffset.UtcNow.AddSeconds(-60).ToUnixTimeSeconds();
        var signature = HmacAuthenticator.Sign(pid, oldTimestamp, secret);

        HmacAuthenticator.Validate(pid, oldTimestamp, signature, secret, maxAgeSeconds: 30).Should().BeFalse();
    }

    [Test]
    public void Validate_FutureTimestamp_ReturnsFalse()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        var pid = 1234;
        var futureTimestamp = DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds();
        var signature = HmacAuthenticator.Sign(pid, futureTimestamp, secret);

        HmacAuthenticator.Validate(pid, futureTimestamp, signature, secret, maxAgeSeconds: 30).Should().BeFalse();
    }

    [Test]
    public void Validate_TamperedSignature_ReturnsFalse()
    {
        var secret = HmacAuthenticator.GenerateSecret();
        var pid = 1234;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = HmacAuthenticator.Sign(pid, timestamp, secret);

        // Tamper with one character
        var tampered = signature[..^1] + (signature[^1] == 'A' ? 'B' : 'A');
        HmacAuthenticator.Validate(pid, timestamp, tampered, secret).Should().BeFalse();
    }
}
