using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static class MarketerEmailEndpoints
{
    private static readonly HashSet<string> ValidSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "StuckOnboarding",
        "FreeNearLimit",
        "ProExpiringSoon",
        "InactiveOwners",
        "AllFreeOwners"
    };

    public static void MapMarketerEmailEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/marketer/emails")
            .RequireAuthorization(policy => policy.RequireRole("Admin", "Marketer"));

        group.MapGet("", async (AppDbContext db, CancellationToken ct) =>
        {
            var items = await db.MarketingEmailCampaigns.AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => ToDto(x))
                .ToListAsync(ct);
            return Results.Ok(items);
        });

        group.MapPost("", async (
            UpsertMarketingEmailCampaignRequest request,
            HttpContext context,
            AppDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var resolvedSegment = ResolveSegment(request);
            ValidateRequest(request, resolvedSegment);
            var now = clock.GetUtcNow();
            var actorId = ApiSupport.UserId(context);
            var actorName = context.User.Identity?.Name ?? "Marketer";

            var allLeads = await MarketerLeadsEndpoints.LoadAllOwnerLeadsAsync(db, now, ct);
            var matchingCount = allLeads.Count(x => MarketerLeadsEndpoints.MatchesSegment(x.Lead, x.LastActivityDate, resolvedSegment, now));
            var resolvedCtaLabel = !string.IsNullOrWhiteSpace(request.CtaLabel) ? request.CtaLabel.Trim() : (!string.IsNullOrWhiteSpace(request.CtaText) ? request.CtaText.Trim() : null);

            var campaign = new MarketingEmailCampaign
            {
                Name = request.Name.Trim(),
                TargetSegment = resolvedSegment,
                Subject = request.Subject.Trim(),
                BodyHtml = request.BodyHtml.Trim(),
                PromoCode = string.IsNullOrWhiteSpace(request.PromoCode) ? null : request.PromoCode.Trim().ToUpperInvariant(),
                CtaLabel = resolvedCtaLabel,
                CtaUrl = string.IsNullOrWhiteSpace(request.CtaUrl) ? null : request.CtaUrl.Trim(),
                Status = MarketingEmailCampaignStatus.Draft,
                TotalRecipients = matchingCount,
                CreatedByUserId = actorId,
                CreatedByName = actorName,
                CreatedAt = now,
                UpdatedAt = now
            };

            db.MarketingEmailCampaigns.Add(campaign);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/marketer/emails/{campaign.Id}", ToDto(campaign));
        });

        group.MapPut("/{id:guid}", async (
            Guid id,
            UpsertMarketingEmailCampaignRequest request,
            AppDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var campaign = await db.MarketingEmailCampaigns.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy chiến dịch email.");

            if (campaign.Status == MarketingEmailCampaignStatus.Completed)
                throw new ApiException(StatusCodes.Status409Conflict, "Chiến dịch email đã được gửi không thể chỉnh sửa.");

            var resolvedSegment = ResolveSegment(request);
            ValidateRequest(request, resolvedSegment);
            var now = clock.GetUtcNow();
            var allLeads = await MarketerLeadsEndpoints.LoadAllOwnerLeadsAsync(db, now, ct);
            var matchingCount = allLeads.Count(x => MarketerLeadsEndpoints.MatchesSegment(x.Lead, x.LastActivityDate, resolvedSegment, now));
            var resolvedCtaLabel = !string.IsNullOrWhiteSpace(request.CtaLabel) ? request.CtaLabel.Trim() : (!string.IsNullOrWhiteSpace(request.CtaText) ? request.CtaText.Trim() : null);

            campaign.Name = request.Name.Trim();
            campaign.TargetSegment = resolvedSegment;
            campaign.Subject = request.Subject.Trim();
            campaign.BodyHtml = request.BodyHtml.Trim();
            campaign.PromoCode = string.IsNullOrWhiteSpace(request.PromoCode) ? null : request.PromoCode.Trim().ToUpperInvariant();
            campaign.CtaLabel = resolvedCtaLabel;
            campaign.CtaUrl = string.IsNullOrWhiteSpace(request.CtaUrl) ? null : request.CtaUrl.Trim();
            campaign.TotalRecipients = matchingCount;
            campaign.UpdatedAt = now;

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(campaign));
        });

        group.MapPost("/{id:guid}/send-test", async (
            Guid id,
            SendMarketingTestEmailRequest request,
            AppDbContext db,
            IConfiguration config,
            IHostEnvironment env,
            CancellationToken ct) =>
        {
            var campaign = await db.MarketingEmailCampaigns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy chiến dịch email.");

            var rawEmail = !string.IsNullOrWhiteSpace(request.TestEmail) ? request.TestEmail : request.ToEmail;
            var testEmail = rawEmail?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(testEmail) || !testEmail.Contains('@') || !MailboxAddress.TryParse(testEmail, out _))
                throw new ApiException(StatusCodes.Status400BadRequest, "Địa chỉ email nhận thử không hợp lệ.");

            var renderedSubject = "[TEST] " + RenderTokens(campaign.Subject, "Chủ trọ mẫu", 4, campaign.PromoCode);
            var renderedHtml = BuildEmailHtml(campaign, "Chủ trọ mẫu", 4);

            await TryDeliverMarketingEmailAsync(config, env, testEmail, renderedSubject, renderedHtml, ct);
            return Results.Ok(new { success = true, recipient = testEmail, message = $"Đã gửi email thử nghiệm tới {testEmail}." });
        });

        group.MapPost("/{id:guid}/send", async (
            Guid id,
            AppDbContext db,
            IConfiguration config,
            IHostEnvironment env,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var campaign = await db.MarketingEmailCampaigns.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy chiến dịch email.");

            if (campaign.Status == MarketingEmailCampaignStatus.Completed)
                throw new ApiException(StatusCodes.Status409Conflict, "Chiến dịch email này đã được gửi.");

            var now = clock.GetUtcNow();
            var allLeads = await MarketerLeadsEndpoints.LoadAllOwnerLeadsAsync(db, now, ct);
            var recipients = allLeads
                .Where(x => MarketerLeadsEndpoints.MatchesSegment(x.Lead, x.LastActivityDate, campaign.TargetSegment, now))
                .Select(x => x.Lead)
                .ToList();

            var alreadySentIds = (await db.MarketingEmailRecipientLogs.AsNoTracking()
                .Where(l => l.CampaignId == campaign.Id)
                .Select(l => l.OwnerId)
                .ToListAsync(ct)).ToHashSet();

            var sentCount = 0;
            var failedCount = 0;

            foreach (var lead in recipients)
            {
                if (alreadySentIds.Contains(lead.OwnerId))
                    continue;

                var subject = RenderTokens(campaign.Subject, lead.Name, lead.RoomCount, campaign.PromoCode);
                var html = BuildEmailHtml(campaign, lead.Name, lead.RoomCount);
                try
                {
                    await TryDeliverMarketingEmailAsync(config, env, lead.Email, subject, html, ct);
                    db.MarketingEmailRecipientLogs.Add(new MarketingEmailRecipientLog
                    {
                        CampaignId = campaign.Id,
                        OwnerId = lead.OwnerId,
                        Email = lead.Email,
                        Status = "Sent",
                        SentAt = now
                    });
                    sentCount++;
                }
                catch (Exception ex)
                {
                    db.MarketingEmailRecipientLogs.Add(new MarketingEmailRecipientLog
                    {
                        CampaignId = campaign.Id,
                        OwnerId = lead.OwnerId,
                        Email = lead.Email,
                        Status = "Failed",
                        ErrorMessage = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message,
                        SentAt = now
                    });
                    failedCount++;
                }

                if ((sentCount + failedCount) % 10 == 0)
                {
                    await db.SaveChangesAsync(ct);
                }
            }

            campaign.TotalRecipients = recipients.Count;
            campaign.SentCount += sentCount;
            campaign.FailedCount += failedCount;
            campaign.Status = MarketingEmailCampaignStatus.Completed;
            campaign.SentAt = now;
            campaign.UpdatedAt = now;

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(campaign));
        });
    }

    private static string ResolveSegment(UpsertMarketingEmailCampaignRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.TargetSegment)) return request.TargetSegment.Trim();
        if (!string.IsNullOrWhiteSpace(request.Segment)) return request.Segment.Trim();
        return string.Empty;
    }

    private static void ValidateRequest(UpsertMarketingEmailCampaignRequest request, string resolvedSegment)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
            throw new ApiException(StatusCodes.Status400BadRequest, "Tên chiến dịch phải từ 1 đến 200 ký tự.");
        if (string.IsNullOrWhiteSpace(resolvedSegment) || !ValidSegments.Contains(resolvedSegment))
            throw new ApiException(StatusCodes.Status400BadRequest, "Tệp chủ trọ mục tiêu không hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Trim().Length > 250)
            throw new ApiException(StatusCodes.Status400BadRequest, "Tiêu đề email phải từ 1 đến 250 ký tự.");
        if (string.IsNullOrWhiteSpace(request.BodyHtml))
            throw new ApiException(StatusCodes.Status400BadRequest, "Nội dung email không được để trống.");
    }

    private static string RenderTokens(string template, string ownerName, int roomCount, string? promoCode)
    {
        return template
            .Replace("{{OwnerName}}", ownerName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{RoomCount}}", roomCount.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{{PromoCode}}", promoCode ?? "ROOMIEPRO", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildEmailHtml(MarketingEmailCampaign campaign, string ownerName, int roomCount)
    {
        var body = RenderTokens(campaign.BodyHtml, ownerName, roomCount, campaign.PromoCode);
        var ctaBlock = !string.IsNullOrWhiteSpace(campaign.CtaLabel) && !string.IsNullOrWhiteSpace(campaign.CtaUrl)
            ? $"<p style=\"margin-top:20px;\"><a href=\"{System.Net.WebUtility.HtmlEncode(campaign.CtaUrl)}\" style=\"display:inline-block;padding:10px 20px;background:#0f766e;color:#ffffff;text-decoration:none;border-radius:8px;font-weight:600;\">{System.Net.WebUtility.HtmlEncode(campaign.CtaLabel)}</a></p>"
            : string.Empty;

        return $"<div style=\"font-family:'Be Vietnam Pro',sans-serif;line-height:1.6;color:#0f172a;max-width:600px;margin:0 auto;\">{body}{ctaBlock}</div>";
    }

    private static async Task TryDeliverMarketingEmailAsync(
        IConfiguration config,
        IHostEnvironment environment,
        string recipientEmail,
        string subject,
        string htmlBody,
        CancellationToken ct)
    {
        var host = config["Email:Smtp:Host"];
        var fromAddress = config["Email:FromAddress"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromAddress) || !MailboxAddress.TryParse(fromAddress, out _))
        {
            // When SMTP is not configured in local dev/test, complete gracefully without network call.
            return;
        }

        var security = config["Email:Smtp:Security"] ?? "StartTls";
        var localTest = environment.IsDevelopment() && host is "localhost" or "127.0.0.1" or "::1";
        if (!int.TryParse(config["Email:Smtp:Port"] ?? "587", out var port) || port is < 1 or > 65535 ||
            (security != "StartTls" && security != "SslOnConnect" && !(security == "None" && localTest)))
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(config["Email:FromName"] ?? "Roomie", fromAddress));
        message.To.Add(MailboxAddress.Parse(recipientEmail));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient { Timeout = 15000 };
        await client.ConnectAsync(host, port, Enum.Parse<SecureSocketOptions>(security), timeout.Token);
        var username = config["Email:Smtp:Username"];
        if (!string.IsNullOrWhiteSpace(username))
            await client.AuthenticateAsync(username, config["Email:Smtp:Password"] ?? "", timeout.Token);
        await client.SendAsync(message, timeout.Token);
        try { await client.DisconnectAsync(true, timeout.Token); }
        catch (Exception) when (!ct.IsCancellationRequested) { }
    }

    private static MarketingEmailCampaignDto ToDto(MarketingEmailCampaign x) => new(
        x.Id,
        x.Name,
        x.TargetSegment,
        x.Subject,
        x.BodyHtml,
        x.PromoCode,
        x.CtaLabel,
        x.CtaUrl,
        x.Status,
        x.TotalRecipients,
        x.SentCount,
        x.FailedCount,
        x.CreatedByName,
        x.SentAt,
        x.CreatedAt,
        x.UpdatedAt);
}
