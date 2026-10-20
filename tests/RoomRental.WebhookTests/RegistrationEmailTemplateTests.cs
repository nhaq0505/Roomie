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
        Check(received.Body is Multipart { ContentType.MimeType: "multipart/alternative" } &&
            received.TextBody.Contains("012345") && received.HtmlBody.Contains("012345"),
            "serialized OTP email includes HTML and plain text and preserves a leading zero");
        Check(received.TextBody.Contains("10 phút") && received.HtmlBody.Contains("10 phút") &&
            !received.HtmlBody.Contains("{{CODE}}") && !received.HtmlBody.Contains("{{LOGO_CID}}"),
            "both email alternatives show the same expiry and resolve template placeholders");
        var cid = Regex.Match(received.HtmlBody, "cid:([^\"]+)").Groups[1].Value;
        var logo = received.BodyParts.OfType<MimePart>().Single(part => part.ContentId == cid);
        using var image = new MemoryStream();
        logo.Content.DecodeTo(image);
        Check(cid.Length > 0 && logo.ContentType.MimeType == "image/png" && !logo.IsAttachment &&
            image.ToArray().Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "email embeds a valid inline PNG matching the HTML content ID");
        using var escaped = new MimeMessage { Body = RegistrationEmailTemplate.CreateBody("<script>&") };
        Check(escaped.HtmlBody.Contains("&lt;script&gt;&amp;") && !escaped.HtmlBody.Contains("<script>"),
            "dynamic email content is HTML encoded");
        using var recovery = new MimeMessage { Body = RegistrationEmailTemplate.CreateRecoveryBody("012345") };
        Check(recovery.TextBody.Contains("012345") && recovery.HtmlBody.Contains("012345") &&
            recovery.HtmlBody.Contains("Đặt lại mật khẩu của bạn") && !recovery.HtmlBody.Contains("màn hình đăng ký") &&
            recovery.TextBody.Contains("Mật khẩu hiện tại vẫn được giữ nguyên"),
            "recovery email has its own purpose and preserves leading-zero codes without claiming account changes");
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS: " + label);
    }
}
