using System.Net;
using MimeKit;
using MimeKit.Utils;

namespace RoomRental.Api.Services;

public static class RegistrationEmailTemplate
{
    private static readonly string HtmlTemplate = ReadTemplate();
    private static readonly byte[] Logo = ReadLogo();

    public static MimeEntity CreateBody(string code) => Create(code, false);

    public static MimeEntity CreateRecoveryBody(string code) => Create(code, true);

    private static MimeEntity Create(string code, bool recovery)
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
        if (recovery)
        {
            builder.TextBody = $"Mã đặt lại mật khẩu Roomie của bạn là: {code}\n\nMã có hiệu lực tối đa 10 phút. Không chia sẻ mã này với bất kỳ ai.\nNếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email này. Mật khẩu hiện tại vẫn được giữ nguyên.";
            builder.HtmlBody = builder.HtmlBody
                .Replace("Xác nhận đăng ký Roomie", "Đặt lại mật khẩu Roomie")
                .Replace("CHÀO MỪNG BẠN ĐẾN VỚI ROOMIE", "KHÔI PHỤC TÀI KHOẢN ROOMIE")
                .Replace("Xác nhận email của bạn", "Đặt lại mật khẩu của bạn")
                .Replace("Mã xác nhận của bạn", "Mã đặt lại mật khẩu")
                .Replace("Hoàn tất đăng ký Roomie.", "Khôi phục tài khoản Roomie.")
                .Replace("Cảm ơn bạn đã đăng ký Roomie. Nhập mã bên dưới vào màn hình đăng ký để hoàn tất xác nhận email.", "Bạn đã yêu cầu đặt lại mật khẩu Roomie. Nhập mã bên dưới vào màn hình khôi phục mật khẩu để tiếp tục.")
                .Replace("Nếu bạn không đăng ký Roomie, hãy bỏ qua email này.", "Nếu bạn không yêu cầu đặt lại mật khẩu, hãy bỏ qua email này. Mật khẩu hiện tại vẫn được giữ nguyên.");
        }
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
