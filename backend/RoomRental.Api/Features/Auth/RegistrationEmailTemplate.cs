using System.Net;
using MimeKit;
using MimeKit.Utils;

namespace RoomRental.Api.Services;

public static class RegistrationEmailTemplate
{
    private static readonly string HtmlTemplate = ReadTemplate();
    private static readonly byte[] Logo = ReadLogo();

    public static MimeEntity CreateBody(string code)
    {
        var builder = new BodyBuilder
        {
            TextBody = $"Mã xác nhận đăng ký Roomie của bạn là: {code}\n\nMã có hiệu lực tối đa 10 phút. Không chia sẻ mã này với bất kỳ ai.\nNếu bạn không đăng ký Roomie, hãy bỏ qua email này."
        };
        var logo = builder.LinkedResources.Add("roomie-logo.png", Logo, new ContentType("image", "png"));
        logo.ContentId = MimeUtils.GenerateMessageId();
        logo.ContentDisposition = new ContentDisposition(ContentDisposition.Inline);
        builder.HtmlBody = HtmlTemplate
            .Replace("{{LOGO_CID}}", WebUtility.HtmlEncode(logo.ContentId))
            .Replace("{{CODE}}", WebUtility.HtmlEncode(code));
        return builder.ToMessageBody();
    }

    private static Stream OpenResource(string name) => typeof(RegistrationEmailTemplate).Assembly
        .GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing email resource: {name}");

    private static string ReadTemplate()
    {
        using var stream = OpenResource("RoomRental.Email.RegistrationVerification.html");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static byte[] ReadLogo()
    {
        using var stream = OpenResource("RoomRental.Email.Logo.png");
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
}
