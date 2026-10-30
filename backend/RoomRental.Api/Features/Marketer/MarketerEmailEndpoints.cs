using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static class MarketerEmailEndpoints
{
    public static readonly TimeSpan StaleSendingTimeout = TimeSpan.FromMinutes(2);
    private static readonly ConcurrentDictionary<Guid, Task<MarketingEmailCampaignDto>> ActiveDispatches = new();

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
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var scope = app.Services.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var clock = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;
                    await RecoverStaleSendingCampaignsAsync(db, clock.GetUtcNow() + StaleSendingTimeout, CancellationToken.None);
                }
                catch
                {
                    // Best-effort startup recovery; request-time recovery also handles stale rows.
                }
            });
        });

        var group = app.MapGroup("/api/marketer/emails")
            .RequireAuthorization(policy => policy.RequireRole("Admin", "Marketer"));

        group.MapGet("", async (AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            await RecoverStaleSendingCampaignsAsync(db, clock.GetUtcNow(), ct);
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
            var resolvedCtaLabel = !string.IsNullOrWhiteSpace(request.CtaLabel) ? request.CtaLabel.Trim() : (!string.IsNullOrWhiteSpace(request.CtaText) ? request.CtaText.Trim() : null);
            ValidateRequest(request, resolvedSegment, resolvedCtaLabel);
            var now = clock.GetUtcNow();
            var actorId = ApiSupport.UserId(context);
            var actorName = context.User.Identity?.Name ?? "Marketer";

            var allLeads = await MarketerLeadsEndpoints.LoadAllOwnerLeadsAsync(db, now, ct);
            var matchingCount = allLeads.Count(x =>
                x.Lead.Status == OwnerStatus.Approved &&
                MarketerLeadsEndpoints.MatchesSegment(x.Lead, x.LastActivityDate, resolvedSegment, now));

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
            var now = clock.GetUtcNow();
            await RecoverStaleSendingCampaignsAsync(db, now, ct);

            var campaign = await db.MarketingEmailCampaigns.SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy chiến dịch email.");

            if (campaign.Status is MarketingEmailCampaignStatus.Completed or MarketingEmailCampaignStatus.Sending)
                throw new ApiException(StatusCodes.Status409Conflict, "Chiến dịch email đã hoặc đang được gửi không thể chỉnh sửa.");

            var resolvedSegment = ResolveSegment(request);
            var resolvedCtaLabel = !string.IsNullOrWhiteSpace(request.CtaLabel) ? request.CtaLabel.Trim() : (!string.IsNullOrWhiteSpace(request.CtaText) ? request.CtaText.Trim() : null);
            ValidateRequest(request, resolvedSegment, resolvedCtaLabel);
            var allLeads = await MarketerLeadsEndpoints.LoadAllOwnerLeadsAsync(db, now, ct);
            var matchingCount = allLeads.Count(x =>
                x.Lead.Status == OwnerStatus.Approved &&
                MarketerLeadsEndpoints.MatchesSegment(x.Lead, x.LastActivityDate, resolvedSegment, now));

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
            [FromServices] IMarketingEmailSender? sender,
            CancellationToken ct) =>
        {
            var campaign = await db.MarketingEmailCampaigns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy chiến dịch email.");

            var rawEmail = !string.IsNullOrWhiteSpace(request.TestEmail) ? request.TestEmail : request.ToEmail;
            var testEmail = rawEmail?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(testEmail) || !testEmail.Contains('@') || !MailboxAddress.TryParse(testEmail, out _))
                throw new ApiException(StatusCodes.Status400BadRequest, "Địa chỉ email nhận thử không hợp lệ.");

            var emailSender = sender ?? new SmtpRegistrationEmailSender(config, env, NullLogger<SmtpRegistrationEmailSender>.Instance);
            emailSender.EnsureConfigured();

            var renderedSubject = "[TEST] " + RenderTokens(campaign.Subject, "Chủ trọ mẫu", 4, campaign.PromoCode);
            var renderedHtml = BuildEmailHtml(campaign, "Chủ trọ mẫu", 4);

            await emailSender.SendMarketingEmailAsync(testEmail, renderedSubject, renderedHtml, ct);
            return Results.Ok(new { success = true, recipient = testEmail, message = $"Đã gửi email thử nghiệm tới {testEmail}." });
        }).RequireRateLimiting("auth");

        group.MapPost("/{id:guid}/send", async (
            Guid id,
            AppDbContext db,
            IServiceScopeFactory scopeFactory,
            IHostApplicationLifetime appLifetime,
            IConfiguration config,
            IHostEnvironment env,
            TimeProvider clock,
            [FromServices] IMarketingEmailSender? sender,
            CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            await RecoverStaleSendingCampaignsAsync(db, now, ct);

            var existingCampaign = await db.MarketingEmailCampaigns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy chiến dịch email.");

            if (existingCampaign.Status == MarketingEmailCampaignStatus.Completed)
                throw new ApiException(StatusCodes.Status409Conflict, "Chiến dịch email này đã được gửi.");
            if (existingCampaign.Status == MarketingEmailCampaignStatus.Sending)
                throw new ApiException(StatusCodes.Status409Conflict, "Chiến dịch email đang được gửi. Vui lòng chờ hoàn tất.");

            var emailSender = sender ?? new SmtpRegistrationEmailSender(config, env, NullLogger<SmtpRegistrationEmailSender>.Instance);
            emailSender.EnsureConfigured();

            var claimed = await db.MarketingEmailCampaigns
                .Where(x => x.Id == id && (x.Status == MarketingEmailCampaignStatus.Draft || x.Status == MarketingEmailCampaignStatus.Failed))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, MarketingEmailCampaignStatus.Sending)
                    .SetProperty(x => x.UpdatedAt, now), ct);

            if (claimed == 0)
            {
                var current = await db.MarketingEmailCampaigns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
                if (current is null)
                    throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy chiến dịch email.");
                if (current.Status == MarketingEmailCampaignStatus.Completed)
                    throw new ApiException(StatusCodes.Status409Conflict, "Chiến dịch email này đã được gửi.");
                throw new ApiException(StatusCodes.Status409Conflict, "Chiến dịch email đang được gửi. Vui lòng chờ hoàn tất.");
            }

            // Run the SMTP dispatch loop in an independent background scope tied only to
            // ApplicationStopping, never to the HTTP request's CancellationToken.
            var dispatchTask = Task.Run(() => DispatchCampaignCoreAsync(
                id,
                scopeFactory,
                config,
                env,
                clock,
                sender,
                appLifetime.ApplicationStopping));
            ActiveDispatches[id] = dispatchTask;

            try
            {
                var completedDto = await dispatchTask.WaitAsync(TimeSpan.FromSeconds(15), ct);
                return Results.Ok(completedDto);
            }
            catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                // Proxy timeout or client disconnect: the background task continues sending and
                // persisting each recipient log with CancellationToken.None.
                var inFlight = await db.MarketingEmailCampaigns.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == id, CancellationToken.None);
                return inFlight is not null ? Results.Ok(ToDto(inFlight)) : Results.StatusCode(StatusCodes.Status202Accepted);
            }
        });
    }

    private static async Task<MarketingEmailCampaignDto> DispatchCampaignCoreAsync(
        Guid id,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        IHostEnvironment env,
        TimeProvider clock,
        IMarketingEmailSender? sender,
        CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emailSender = sender
            ?? scope.ServiceProvider.GetService<IMarketingEmailSender>()
            ?? new SmtpRegistrationEmailSender(config, env, NullLogger<SmtpRegistrationEmailSender>.Instance);

        try
        {
            var now = clock.GetUtcNow();
            var campaign = await db.MarketingEmailCampaigns.SingleAsync(x => x.Id == id, CancellationToken.None);
            var allLeads = await MarketerLeadsEndpoints.LoadAllOwnerLeadsAsync(db, now, CancellationToken.None);
            var recipients = allLeads
                .Where(x => x.Lead.Status == OwnerStatus.Approved &&
                            MarketerLeadsEndpoints.MatchesSegment(x.Lead, x.LastActivityDate, campaign.TargetSegment, now))
                .Select(x => x.Lead)
                .ToList();

            var existingLogsByOwner = await db.MarketingEmailRecipientLogs
                .Where(l => l.CampaignId == campaign.Id)
                .ToDictionaryAsync(l => l.OwnerId, CancellationToken.None);

            campaign.TotalRecipients = recipients.Count;
            campaign.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);

            foreach (var lead in recipients)
            {
                stoppingToken.ThrowIfCancellationRequested();

                if (existingLogsByOwner.TryGetValue(lead.OwnerId, out var existingLog) && existingLog.Status == "Sent")
                    continue;

                var subject = RenderTokens(campaign.Subject, lead.Name, lead.RoomCount, campaign.PromoCode);
                var html = BuildEmailHtml(campaign, lead.Name, lead.RoomCount);
                var stepTime = clock.GetUtcNow();
                try
                {
                    await emailSender.SendMarketingEmailAsync(lead.Email, subject, html, stoppingToken);
                    if (existingLog is not null)
                    {
                        existingLog.Email = lead.Email;
                        existingLog.Status = "Sent";
                        existingLog.ErrorMessage = null;
                        existingLog.SentAt = stepTime;
                    }
                    else
                    {
                        var newLog = new MarketingEmailRecipientLog
                        {
                            CampaignId = campaign.Id,
                            OwnerId = lead.OwnerId,
                            Email = lead.Email,
                            Status = "Sent",
                            SentAt = stepTime
                        };
                        db.MarketingEmailRecipientLogs.Add(newLog);
                        existingLogsByOwner[lead.OwnerId] = newLog;
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    var errMsg = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                    if (existingLog is not null)
                    {
                        existingLog.Email = lead.Email;
                        existingLog.Status = "Failed";
                        existingLog.ErrorMessage = errMsg;
                        existingLog.SentAt = stepTime;
                    }
                    else
                    {
                        var failedLog = new MarketingEmailRecipientLog
                        {
                            CampaignId = campaign.Id,
                            OwnerId = lead.OwnerId,
                            Email = lead.Email,
                            Status = "Failed",
                            ErrorMessage = errMsg,
                            SentAt = stepTime
                        };
                        db.MarketingEmailRecipientLogs.Add(failedLog);
                        existingLogsByOwner[lead.OwnerId] = failedLog;
                    }
                }

                // Persist every single recipient outcome immediately with CancellationToken.None
                // so a client disconnect or process interruption never loses a sent log.
                campaign.SentCount = existingLogsByOwner.Values.Count(x => x.Status == "Sent");
                campaign.FailedCount = existingLogsByOwner.Values.Count(x => x.Status != "Sent");
                campaign.UpdatedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(CancellationToken.None);
            }

            var finishTime = clock.GetUtcNow();
            var totalSent = existingLogsByOwner.Values.Count(x => x.Status == "Sent");
            var totalFailed = existingLogsByOwner.Values.Count(x => x.Status != "Sent");

            campaign.TotalRecipients = recipients.Count;
            campaign.SentCount = totalSent;
            campaign.FailedCount = totalFailed;
            campaign.Status = totalFailed > 0
                ? MarketingEmailCampaignStatus.Failed
                : MarketingEmailCampaignStatus.Completed;
            campaign.SentAt = finishTime;
            campaign.UpdatedAt = finishTime;

            await db.SaveChangesAsync(CancellationToken.None);
            return ToDto(campaign);
        }
        catch
        {
            await db.MarketingEmailCampaigns
                .Where(x => x.Id == id && x.Status == MarketingEmailCampaignStatus.Sending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, MarketingEmailCampaignStatus.Failed)
                    .SetProperty(x => x.UpdatedAt, clock.GetUtcNow()), CancellationToken.None);
            throw;
        }
        finally
        {
            ActiveDispatches.TryRemove(id, out _);
        }
    }

    private static async Task RecoverStaleSendingCampaignsAsync(
        AppDbContext db,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var staleCutoff = now - StaleSendingTimeout;
        var staleCampaigns = await db.MarketingEmailCampaigns
            .Where(x => x.Status == MarketingEmailCampaignStatus.Sending && x.UpdatedAt <= staleCutoff)
            .ToListAsync(ct);

        var changed = false;
        foreach (var stale in staleCampaigns)
        {
            if (ActiveDispatches.TryGetValue(stale.Id, out var activeTask) && !activeTask.IsCompleted)
                continue;

            var logs = await db.MarketingEmailRecipientLogs.AsNoTracking()
                .Where(l => l.CampaignId == stale.Id)
                .ToListAsync(ct);

            stale.SentCount = logs.Count(l => l.Status == "Sent");
            stale.FailedCount = logs.Count(l => l.Status != "Sent");
            stale.Status = MarketingEmailCampaignStatus.Failed;
            stale.UpdatedAt = now;
            changed = true;
        }

        if (changed)
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static string ResolveSegment(UpsertMarketingEmailCampaignRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.TargetSegment)) return request.TargetSegment.Trim();
        if (!string.IsNullOrWhiteSpace(request.Segment)) return request.Segment.Trim();
        return string.Empty;
    }

    private static void ValidateRequest(UpsertMarketingEmailCampaignRequest request, string resolvedSegment, string? resolvedCtaLabel)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
            throw new ApiException(StatusCodes.Status400BadRequest, "Tên chiến dịch phải từ 1 đến 200 ký tự.");
        if (string.IsNullOrWhiteSpace(resolvedSegment) || !ValidSegments.Contains(resolvedSegment))
            throw new ApiException(StatusCodes.Status400BadRequest, "Tệp chủ trọ mục tiêu không hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Trim().Length > 250)
            throw new ApiException(StatusCodes.Status400BadRequest, "Tiêu đề email phải từ 1 đến 250 ký tự.");
        if (string.IsNullOrWhiteSpace(request.BodyHtml))
            throw new ApiException(StatusCodes.Status400BadRequest, "Nội dung email không được để trống.");
        if (!string.IsNullOrWhiteSpace(request.PromoCode) && request.PromoCode.Trim().Length > 32)
            throw new ApiException(StatusCodes.Status400BadRequest, "Mã khuyến mãi tối đa 32 ký tự.");
        if (resolvedCtaLabel is { Length: > 120 })
            throw new ApiException(StatusCodes.Status400BadRequest, "Nhãn nút CTA tối đa 120 ký tự.");
        if (!string.IsNullOrWhiteSpace(request.CtaUrl))
        {
            var ctaUrl = request.CtaUrl.Trim();
            if (ctaUrl.Length > 500 || !IsAllowedCtaUrl(ctaUrl))
                throw new ApiException(StatusCodes.Status400BadRequest, "Đường dẫn CTA không hợp lệ (chỉ chấp nhận http/https hoặc đường dẫn nội bộ).");
        }
    }

    private static bool IsAllowedCtaUrl(string url)
    {
        if (url.StartsWith('/') && !url.StartsWith("//"))
            return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var parsed) &&
               (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
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
