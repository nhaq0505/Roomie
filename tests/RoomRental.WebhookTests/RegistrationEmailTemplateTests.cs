using System.Text.RegularExpressions;
using MimeKit;
using RoomRental.Api.Services;

internal static class RegistrationEmailTemplateTests
{
    public static void Run()
    {
        using var message = new MimeMessage { Body = RegistrationEmailTemplate.CreateBody("012345") };
        using var serialized = new MemoryStream();
        message.WriteTo(serialized);
        serialized.Position = 0;
        using var received = MimeMessage.Load(serialized);
        var receivedText = received.TextBody ?? "";
        var receivedHtml = received.HtmlBody ?? "";
        Check(received.Body is Multipart { ContentType.MimeType: "multipart/alternative" } &&
            receivedText.Contains("012345") && receivedHtml.Contains("012345"),
            "serialized OTP email includes HTML and plain text and preserves a leading zero");
        Check(receivedText.Contains("10 phút") && receivedHtml.Contains("10 phút") &&
            !receivedHtml.Contains("{{CODE}}") && !receivedHtml.Contains("{{LOGO_CID}}"),
            "both email alternatives show the same expiry and resolve template placeholders");
        var cid = Regex.Match(receivedHtml, "cid:([^\"]+)").Groups[1].Value;
        var logo = received.BodyParts.OfType<MimePart>().Single(part => part.ContentId == cid);
        using var image = new MemoryStream();
        logo.Content!.DecodeTo(image);
        Check(cid.Length > 0 && logo.ContentType.MimeType == "image/png" && !logo.IsAttachment &&
            image.ToArray().Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "email embeds a valid inline PNG matching the HTML content ID");
        using var escaped = new MimeMessage { Body = RegistrationEmailTemplate.CreateBody("<script>&") };
        var escapedHtml = escaped.HtmlBody ?? "";
        Check(escapedHtml.Contains("&lt;script&gt;&amp;") && !escapedHtml.Contains("<script>"),
            "dynamic email content is HTML encoded");
        using var recovery = new MimeMessage { Body = RegistrationEmailTemplate.CreateRecoveryBody("012345") };
        var recoveryText = recovery.TextBody ?? "";
        var recoveryHtml = recovery.HtmlBody ?? "";
        Check(recoveryText.Contains("012345") && recoveryHtml.Contains("012345") &&
            recoveryHtml.Contains("Đặt lại mật khẩu của bạn") && !recoveryHtml.Contains("màn hình đăng ký") &&
            recoveryText.Contains("Mật khẩu hiện tại vẫn được giữ nguyên"),
            "recovery email has its own purpose and preserves leading-zero codes without claiming account changes");
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS: " + label);
    }
}
