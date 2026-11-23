namespace RoushTech.Rangifer.Tests.Mail;

using MimeKit;
using NUnit.Framework;
using RoushTech.Rangifer.Mail;

public class HeaderAndWebhookTests
{
    private const string Secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Test]
    public void OneClickUnsubscribe_AddsBothHeaders()
    {
        var message = new MimeMessage();

        OneClickUnsubscribe.AddTo(message, "https://example.com/unsubscribe?t=abc");

        Assert.Multiple(() =>
        {
            Assert.That(message.Headers["List-Unsubscribe"], Is.EqualTo("<https://example.com/unsubscribe?t=abc>"));
            Assert.That(message.Headers["List-Unsubscribe-Post"], Is.EqualTo("List-Unsubscribe=One-Click"));
        });
    }

    [TestCase("/unsubscribe")]
    [TestCase("mailto:x@example.com")]
    [TestCase("https://example.com/u>, <https://evil.example")]
    [TestCase("https://example.com/u\r\nBcc: x@example.com")]
    public void OneClickUnsubscribe_RejectsUnsafeUrls(string url)
    {
        Assert.That(() => OneClickUnsubscribe.Headers(url), Throws.ArgumentException);
    }

    [Test]
    public void Svix_AcceptsOwnSignature()
    {
        var timestamp = Now.ToUnixTimeSeconds().ToString();
        var signature = SvixSignature.Sign(Secret, "msg_1", timestamp, "{}");

        Assert.That(SvixSignature.Problem(Secret, "msg_1", timestamp, $"v1,bogus v1,{signature}", "{}", Now), Is.Null);
    }

    [Test]
    public void Svix_MatchesPublishedExample()
    {
        const string body = """{"test": 2432232314}""";

        var signature = SvixSignature.Sign(Secret, "msg_p5jXN8AQM9LWM0D4loKWxJek", "1614265330", body);

        Assert.That(signature, Is.EqualTo("g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE="));
    }

    [Test]
    public void Svix_RejectsChangedBody()
    {
        var timestamp = Now.ToUnixTimeSeconds().ToString();
        var signature = SvixSignature.Sign(Secret, "msg_1", timestamp, "{}");

        Assert.That(SvixSignature.IsValid(Secret, "msg_1", timestamp, $"v1,{signature}", "{\"a\":1}", Now), Is.False);
    }

    [Test]
    public void Svix_RejectsStaleTimestamp()
    {
        var timestamp = (Now - TimeSpan.FromMinutes(6)).ToUnixTimeSeconds().ToString();
        var signature = SvixSignature.Sign(Secret, "msg_1", timestamp, "{}");

        Assert.That(SvixSignature.Problem(Secret, "msg_1", timestamp, $"v1,{signature}", "{}", Now), Does.Contain("seconds off"));
    }

    [TestCase(null, "1700000000", "v1,x")]
    [TestCase("msg_1", null, "v1,x")]
    [TestCase("msg_1", "-1700000000", "v1,x")]
    [TestCase("msg_1", "1700000000", null)]
    public void Svix_RejectsMissingHeaders(string? id, string? timestamp, string? signatures)
    {
        Assert.That(SvixSignature.Problem(Secret, id, timestamp, signatures, "{}", Now), Does.StartWith("missing"));
    }

    [Test]
    public void Svix_RejectsOutOfRangeTimestamp()
    {
        Assert.That(SvixSignature.IsValid(Secret, "msg_1", "99999999999999999", "v1,x", "{}", Now), Is.False);
    }

    [Test]
    public void Svix_BadSecret_IsAProblemNotAnException()
    {
        var timestamp = Now.ToUnixTimeSeconds().ToString();

        Assert.That(SvixSignature.Problem("not base64!", "msg_1", timestamp, "v1,x", "{}", Now), Does.Contain("base64"));
    }
}
