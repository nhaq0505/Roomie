using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Net.Http.Headers;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static class PaymentEndpoints
{
    private const long MaxMeterPhotoBytes = 2 * 1024 * 1024;
    private const long MaxMeterPhotoRequestBytes = MaxMeterPhotoBytes + 512 * 1024;

    public static void MapPaymentEndpoints(this WebApplication app)
    {
        var ownerRoutes = app.MapGroup("/api").RequireAuthorization(policy => policy.RequireRole("Owner"));

        ownerRoutes.MapGet("/settings/payment", async (HttpContext context, PaymentWebhookService webhookService, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            return Results.Ok(await webhookService.GetAsync(ownerId, cancellationToken));
        });

        ownerRoutes.MapPut("/settings/payment", async (PaymentSettingsRequest request, HttpContext context, AppDbContext db, IDataProtectionProvider protectionProvider, PaymentWebhookService webhookService, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var clientId = request.ClientId?.Trim();
            var apiKey = request.ApiKey?.Trim();
            var checksumKey = request.ChecksumKey?.Trim();
            if (string.IsNullOrWhiteSpace(clientId) || clientId.Length > 160)
                throw new ApiException(StatusCodes.Status400BadRequest, "Vui lòng nhập Client ID của payOS.");
            if (apiKey is { Length: > 1000 } || checksumKey is { Length: > 1000 })
                throw new ApiException(StatusCodes.Status400BadRequest, "Thông tin xác thực payOS quá dài.");

            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            if (await db.PayOsAttempts.AnyAsync(row => row.OwnerId == ownerId && row.Status == PaymentAttemptStatus.Pending, cancellationToken))
                throw new ApiException(StatusCodes.Status409Conflict, "Vui lòng đối soát các giao dịch payOS đang chờ trước khi đổi thông tin thanh toán.");

            var settings = await db.PaymentSettings.SingleOrDefaultAsync(row => row.OwnerId == ownerId, cancellationToken);
            if (settings is null && (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(checksumKey)))
                throw new ApiException(StatusCodes.Status400BadRequest, "Cần nhập API key và checksum key khi thiết lập payOS lần đầu.");
            var protector = protectionProvider.CreateProtector("RoomRental.OwnerPayOsCredentials.v1");
            if (settings is null)
            {
                settings = new OwnerPaymentSettings
                {
                    OwnerId = ownerId,
                    ClientId = clientId,
                    ProtectedApiKey = protector.Protect(apiKey!),
                    ProtectedChecksumKey = protector.Protect(checksumKey!),
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                db.PaymentSettings.Add(settings);
            }
            else
            {
                settings.ClientId = clientId;
                if (!string.IsNullOrWhiteSpace(apiKey)) settings.ProtectedApiKey = protector.Protect(apiKey);
                if (!string.IsNullOrWhiteSpace(checksumKey)) settings.ProtectedChecksumKey = protector.Protect(checksumKey);
                settings.UpdatedAt = DateTimeOffset.UtcNow;
            }
            var owner = await db.Users.SingleOrDefaultAsync(row => row.Id == ownerId, cancellationToken);
            if (owner is not null) owner.PaymentConfirmationMode = PaymentConfirmationMode.Automatic;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(await webhookService.ConfirmAsync(ownerId, cancellationToken));
        });

        ownerRoutes.MapPost("/settings/payment/verify-webhook", async (HttpContext context, PaymentWebhookService webhookService, CancellationToken cancellationToken) =>
            Results.Ok(await webhookService.ConfirmAsync(ApiSupport.OwnerId(context), cancellationToken)))
            .RequireRateLimiting("tenant-payment");

        ownerRoutes.MapPost("/payments/reconcile/{orderCode:long}", async (long orderCode, HttpContext context, PayOsService payOsService, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            return Results.Ok(await payOsService.ReconcileAsync(ownerId, orderCode, cancellationToken));
        });

        app.MapGet("/api/tenant/{token}", async (string token, int? page, int? pageSize, Guid? invoiceId, AppDbContext db, TenantTokenService tokenService, PaymentWebhookService webhookService, [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null, CancellationToken cancellationToken = default) =>
        {
            var occupancy = await FindTenantAsync(db, token, cancellationToken);
            var roomBuilding = await (from room in db.Rooms.AsNoTracking()
                                      join building in db.Buildings.AsNoTracking() on room.BuildingId equals building.Id
                                      where room.Id == occupancy.RoomId
                                      select new { Room = room, Building = building }).SingleAsync(cancellationToken);
            var currentPeriod = VietnamCalendar.CurrentVietnamPeriod();
            var roomDto = await OwnerManagementEndpoints.ToRoomDtoAsync(db, tokenService, webhookService, roomBuilding.Room, roomBuilding.Building,
                occupancy, currentPeriod, cancellationToken);
            var paging = PageRequest.Parse(page, pageSize);
            var invoiceQuery = db.Invoices.Include(x => x.DepositSettlements).AsNoTracking().Where(invoice => invoice.OccupancyId == occupancy.Id && invoice.LifecycleState == InvoiceLifecycleState.Issued);
            if (invoiceId is not null) invoiceQuery = invoiceQuery.Where(x => x.Id == invoiceId);
            var total = await invoiceQuery.CountAsync(cancellationToken);
            var totals = new { outstanding = await QueryAggregates.SumMoneyAsync(db, invoiceQuery.Select(x => x.Total - x.Paid), cancellationToken), unpaidCount = await invoiceQuery.CountAsync(x => x.Total > x.Paid, cancellationToken) };
            var invoices = await invoiceQuery.OrderByDescending(x => x.Period).ThenByDescending(x => x.IssuedAt).ThenBy(x => x.Id)
                .Skip(paging.Offset).Take(paging.Size).ToListAsync(cancellationToken);
            var rowIds = invoices.Select(x => x.Id).ToList();
            var pendingIds = await PaymentReservations.Pending(db)
                .Where(x => x.OwnerId == roomBuilding.Building.OwnerId && rowIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            var pending = pendingIds.ToHashSet();
            var occupancyDto = roomDto.Occupancy ?? throw new ApiException(StatusCodes.Status404NotFound, "Tenant link is no longer active.");
            var paymentSettings = await webhookService.GetAsync(roomBuilding.Building.OwnerId, cancellationToken);
            var currentPeriodHasReading =
                await db.Readings.AsNoTracking().AnyAsync(x => x.OccupancyId == occupancy.Id && x.Period == currentPeriod, cancellationToken) ||
                await db.Invoices.AsNoTracking().AnyAsync(x => x.OccupancyId == occupancy.Id && ((x.Period == currentPeriod && x.Kind == "Monthly") || x.IsFinalMeterReading), cancellationToken);
            var meterSubmission = await db.TenantMeterSubmissions.AsNoTracking()
                .Where(x => x.OccupancyId == occupancy.Id && x.Period == currentPeriod)
                .Select(x => new TenantMeterSubmissionDto(
                    x.Id,
                    x.RoomId,
                    x.OccupancyId,
                    x.Period,
                    x.PreviousReading,
                    x.SubmittedReading,
                    x.ApprovedReading,
                    x.TenantNote,
                    x.RejectReason,
                    x.Status.ToString(),
                    x.PhotoFileName,
                    x.PhotoContentType,
                    x.PhotoSizeBytes,
                    x.SubmittedAt,
                    x.ReviewedAt,
                    x.ReadingId,
                    x.InvoiceId))
                .SingleOrDefaultAsync(cancellationToken);
            if (milestoneTracker != null)
                await milestoneTracker.RecordOwnerActivityAsync(roomBuilding.Building.OwnerId, ActorType.System, "TenantPortalOpen", cancellationToken);
            return Results.Ok(new
            {
                confirmationMode = paymentSettings.ConfirmationMode,
                room = roomDto,
                occupancy = occupancyDto,
                invoices = invoices.Select(invoice => InvoiceService.ToDto(invoice, pending.Contains(invoice.Id))).ToList(),
                total,
                totals,
                page = paging.Number,
                pageSize = paging.Size,
                currentPeriod,
                currentPeriodHasReading,
                meterSubmission
            });
        });

        app.MapPost("/api/tenant/{token}/self-checkin", async (string token, TenantSelfCheckinRequest request, AppDbContext db, TenantTokenService tokenService, PaymentWebhookService webhookService, CancellationToken ct) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var occupancy = await FindTenantAsync(db, token, ct);
            var fullName = OwnerManagementEndpoints.CleanOptional(request.FullName, 160, "Họ và tên")
                ?? throw new ApiException(StatusCodes.Status400BadRequest, "Vui lòng nhập họ và tên (tối đa 160 ký tự).");
            var citizenId = OwnerManagementEndpoints.ValidateAndCleanCitizenId(request.CitizenId, "Số CCCD/Định danh/Hộ chiếu")
                ?? throw new ApiException(StatusCodes.Status400BadRequest, "Số CCCD/Định danh/Hộ chiếu phải gồm từ 9 đến 12 chữ số hoặc 6 đến 12 ký tự Hộ chiếu hợp lệ.");

            if (string.Equals(request.Target?.Trim(), "coResident", StringComparison.OrdinalIgnoreCase))
            {
                var existing = OwnerManagementEndpoints.DeserializeCoResidents(occupancy.CoResidentsJson).ToList();
                var member = new CoResidentDto(
                    FullName: fullName,
                    DateOfBirth: request.DateOfBirth,
                    Gender: OwnerManagementEndpoints.CleanOptional(request.Gender, 20, "Giới tính"),
                    CitizenId: citizenId,
                    Relation: OwnerManagementEndpoints.CleanOptional(request.Relation, 100, "Mối quan hệ") ?? "Người thuê trọ");
                var existingIndex = existing.FindIndex(x => string.Equals(x.CitizenId, citizenId, StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0)
                    existing[existingIndex] = member;
                else
                    existing.Add(member);
                occupancy.CoResidentsJson = OwnerManagementEndpoints.ValidateAndSerializeCoResidents(existing, out var cleaned);
                occupancy.People = Math.Max(occupancy.People, 1 + cleaned.Count);
                occupancy.TenantSelfCheckinAt = DateTimeOffset.UtcNow;
            }
            else
            {
                occupancy.TenantName = fullName;
                occupancy.TenantCitizenId = citizenId;
                occupancy.TenantDateOfBirth = request.DateOfBirth;
                occupancy.TenantGender = OwnerManagementEndpoints.CleanOptional(request.Gender, 20, "Giới tính");
                occupancy.TenantCitizenIdIssuedDate = request.CitizenIdIssuedDate;
                occupancy.TenantCitizenIdIssuedPlace = OwnerManagementEndpoints.CleanOptional(request.CitizenIdIssuedPlace, 200, "Nơi cấp CCCD")
                    ?? "Cục Cảnh sát QLHC về TTXH";
                occupancy.TenantPermanentAddress = OwnerManagementEndpoints.CleanOptional(request.PermanentAddress, 500, "Địa chỉ thường trú");
                var phone = OwnerManagementEndpoints.CleanOptional(request.Phone, 60, "Số điện thoại");
                if (phone is not null)
                    occupancy.Phone = phone;
                var email = OwnerManagementEndpoints.CleanOptional(request.Email, 320, "Email");
                if (email is not null)
                    occupancy.TenantEmail = email;
                occupancy.TenantSelfCheckinAt = DateTimeOffset.UtcNow;
            }

            var roomBuilding = await (from room in db.Rooms.AsNoTracking()
                                      join building in db.Buildings.AsNoTracking() on room.BuildingId equals building.Id
                                      where room.Id == occupancy.RoomId
                                      select new { Room = room, Building = building }).SingleAsync(ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            var roomDto = await OwnerManagementEndpoints.ToRoomDtoAsync(db, tokenService, webhookService,
                roomBuilding.Room, roomBuilding.Building, occupancy, VietnamCalendar.CurrentVietnamPeriod(), ct);
            return Results.Ok(roomDto.Occupancy!);
        }).RequireRateLimiting("tenant-payment");

        app.MapDelete("/api/tenant/{token}/co-residents/{citizenId}", async (string token, string citizenId, AppDbContext db, TenantTokenService tokenService, PaymentWebhookService webhookService, CancellationToken ct) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var occupancy = await FindTenantAsync(db, token, ct);
            var targetCitizenId = citizenId?.Trim().ToUpperInvariant();
            var existing = OwnerManagementEndpoints.DeserializeCoResidents(occupancy.CoResidentsJson);
            var remaining = existing.Where(x => !string.Equals(x.CitizenId, targetCitizenId, StringComparison.OrdinalIgnoreCase)).ToList();
            occupancy.CoResidentsJson = OwnerManagementEndpoints.ValidateAndSerializeCoResidents(remaining, out var cleaned);
            occupancy.People = Math.Max(1, 1 + cleaned.Count);
            occupancy.TenantSelfCheckinAt = DateTimeOffset.UtcNow;

            var roomBuilding = await (from room in db.Rooms.AsNoTracking()
                                      join building in db.Buildings.AsNoTracking() on room.BuildingId equals building.Id
                                      where room.Id == occupancy.RoomId
                                      select new { Room = room, Building = building }).SingleAsync(ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            var roomDto = await OwnerManagementEndpoints.ToRoomDtoAsync(db, tokenService, webhookService,
                roomBuilding.Room, roomBuilding.Building, occupancy, VietnamCalendar.CurrentVietnamPeriod(), ct);
            return Results.Ok(roomDto.Occupancy!);
        }).RequireRateLimiting("tenant-payment");

        app.MapPost("/api/tenant/{token}/meter-submission", async (string token, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            if (context.Request.ContentLength is long declared && declared > MaxMeterPhotoRequestBytes)
                throw new ApiException(StatusCodes.Status413PayloadTooLarge, "Ảnh đồng hồ điện tối đa 2 MB.");
            var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = MaxMeterPhotoRequestBytes;
            if (!context.Request.HasFormContentType)
                throw new ApiException(StatusCodes.Status400BadRequest, "Gửi ảnh đồng hồ điện bằng multipart/form-data.");

            IFormCollection form;
            try
            {
                form = await context.Request.ReadFormAsync(ct);
            }
            catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                throw new ApiException(StatusCodes.Status413PayloadTooLarge, "Ảnh đồng hồ điện tối đa 2 MB.");
            }
            catch (InvalidDataException)
            {
                throw new ApiException(StatusCodes.Status400BadRequest, "Không đọc được tệp ảnh đồng hồ điện.");
            }

            if (form.Files.Count != 1)
                throw new ApiException(StatusCodes.Status400BadRequest, "Vui lòng chọn đúng một ảnh chụp đồng hồ điện.");
            var file = form.Files[0];
            if (file.Length <= 0)
                throw new ApiException(StatusCodes.Status400BadRequest, "Tệp ảnh đồng hồ điện trống.");
            if (file.Length > MaxMeterPhotoBytes)
                throw new ApiException(StatusCodes.Status413PayloadTooLarge, "Ảnh đồng hồ điện tối đa 2 MB.");

            byte[] content;
            await using (var stream = file.OpenReadStream())
            using (var buffer = new MemoryStream((int)file.Length))
            {
                await stream.CopyToAsync(buffer, ct);
                content = buffer.ToArray();
            }

            var detected = ExpenseEndpoints.DetectFileType(content);
            if (detected is null || !detected.Value.ContentType.StartsWith("image/", StringComparison.Ordinal))
                throw new ApiException(StatusCodes.Status400BadRequest, "Chỉ nhận ảnh chụp đồng hồ điện định dạng JPEG, PNG hoặc WebP.");

            var rawReading = form["submittedReading"].FirstOrDefault() ?? form["currentReading"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(rawReading) ||
                !decimal.TryParse(rawReading.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var submittedReading) ||
                submittedReading < 0 || submittedReading > 1_000_000_000 || decimal.Round(submittedReading, 3) != submittedReading)
            {
                throw new ApiException(StatusCodes.Status400BadRequest, "Chỉ số điện phải là số không âm tối đa ba chữ số thập phân trong giới hạn cho phép.");
            }

            var rawNote = form["note"].FirstOrDefault() ?? form["tenantNote"].FirstOrDefault();
            var tenantNote = OwnerManagementEndpoints.CleanOptional(rawNote, 500, "Ghi chú");
            var rawPeriod = form["period"].FirstOrDefault();
            var period = string.IsNullOrWhiteSpace(rawPeriod)
                ? VietnamCalendar.CurrentVietnamPeriod()
                : ApiSupport.NormalizePeriod(rawPeriod);

            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var occupancy = await FindTenantAsync(db, token, ct);
            var roomBuilding = await (from r in db.Rooms
                                      join b in db.Buildings on r.BuildingId equals b.Id
                                      where r.Id == occupancy.RoomId
                                      select new { Room = r, Building = b }).SingleOrDefaultAsync(ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy phòng thuê.");
            var room = roomBuilding.Room;
            var building = roomBuilding.Building;
            if (room.ArchivedAt is not null || building.ArchivedAt is not null)
                throw new ApiException(StatusCodes.Status409Conflict, "Không thể gửi chỉ số cho phòng hoặc dãy đã lưu trữ.");
            if (await db.Invoices.AnyAsync(x => x.OccupancyId == occupancy.Id && x.IsFinalMeterReading, ct))
                throw new ApiException(StatusCodes.Status409Conflict, "Đã chốt điện trả phòng; không thể gửi thêm chỉ số điện tháng.");
            if (await db.Readings.AnyAsync(row => row.OccupancyId == occupancy.Id && row.Period == period, ct))
                throw new ApiException(StatusCodes.Status409Conflict, "Lượt thuê đã có chỉ số điện cho tháng này.");
            if (await db.Invoices.AnyAsync(row => row.OccupancyId == occupancy.Id && row.Period == period && row.Kind == "Monthly", ct))
                throw new ApiException(StatusCodes.Status409Conflict, "Hóa đơn của lượt thuê trong tháng này đã được tạo.");

            var newest = await db.Readings.AsNoTracking()
                .Where(row => row.RoomId == room.Id)
                .OrderByDescending(row => row.Period)
                .ThenByDescending(row => row.RecordedAt)
                .FirstOrDefaultAsync(ct);
            var previousReading = Math.Max(
                newest?.CurrentReading ?? room.InitialReading,
                await MoveoutEndpoints.MeterBaselineAsync(db, room, ct));
            if (submittedReading < previousReading)
                throw new ApiException(StatusCodes.Status400BadRequest, $"Chỉ số mới không thể thấp hơn chỉ số trước ({previousReading}).");

            var existingSubmission = await db.TenantMeterSubmissions
                .SingleOrDefaultAsync(x => x.OccupancyId == occupancy.Id && x.Period == period, ct);
            if (existingSubmission is not null && existingSubmission.Status == TenantMeterSubmissionStatus.Approved)
                throw new ApiException(StatusCodes.Status409Conflict, "Chỉ số điện tháng này đã được chủ trọ duyệt.");

            var fileName = ExpenseEndpoints.SanitizeFileName(file.FileName, detected.Value.Extension);
            var sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            var now = DateTimeOffset.UtcNow;

            if (existingSubmission is null)
            {
                existingSubmission = new TenantMeterSubmission
                {
                    OwnerId = building.OwnerId,
                    BuildingId = building.Id,
                    RoomId = room.Id,
                    OccupancyId = occupancy.Id,
                    Period = period,
                    PreviousReading = previousReading,
                    SubmittedReading = submittedReading,
                    ApprovedReading = null,
                    TenantNote = tenantNote,
                    RejectReason = null,
                    Status = TenantMeterSubmissionStatus.Pending,
                    PhotoFileName = fileName,
                    PhotoContentType = detected.Value.ContentType,
                    PhotoSizeBytes = content.LongLength,
                    PhotoSha256 = sha256,
                    PhotoContent = content,
                    SubmittedAt = now,
                    ReviewedAt = null,
                    ReadingId = null,
                    InvoiceId = null
                };
                db.TenantMeterSubmissions.Add(existingSubmission);
            }
            else
            {
                existingSubmission.OwnerId = building.OwnerId;
                existingSubmission.BuildingId = building.Id;
                existingSubmission.RoomId = room.Id;
                existingSubmission.PreviousReading = previousReading;
                existingSubmission.SubmittedReading = submittedReading;
                existingSubmission.ApprovedReading = null;
                existingSubmission.TenantNote = tenantNote;
                existingSubmission.RejectReason = null;
                existingSubmission.Status = TenantMeterSubmissionStatus.Pending;
                existingSubmission.PhotoFileName = fileName;
                existingSubmission.PhotoContentType = detected.Value.ContentType;
                existingSubmission.PhotoSizeBytes = content.LongLength;
                existingSubmission.PhotoSha256 = sha256;
                existingSubmission.PhotoContent = content;
                existingSubmission.SubmittedAt = now;
                existingSubmission.ReviewedAt = null;
                existingSubmission.ReadingId = null;
                existingSubmission.InvoiceId = null;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(ToSubmissionDto(existingSubmission));
        }).RequireRateLimiting("tenant-payment");

        app.MapGet("/api/tenant/{token}/meter-submission/photo", async (string token, string? period, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var occupancy = await FindTenantAsync(db, token, ct);
            var requestedPeriod = string.IsNullOrWhiteSpace(period)
                ? VietnamCalendar.CurrentVietnamPeriod()
                : ApiSupport.NormalizePeriod(period);
            var row = await db.TenantMeterSubmissions.AsNoTracking()
                .Where(x => x.OccupancyId == occupancy.Id && x.Period == requestedPeriod)
                .Select(x => new { x.PhotoFileName, x.PhotoContentType, x.PhotoContent })
                .SingleOrDefaultAsync(ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy ảnh chụp đồng hồ điện.");

            var disposition = new ContentDispositionHeaderValue("inline");
            disposition.SetHttpFileName(row.PhotoFileName);
            context.Response.Headers.ContentDisposition = disposition.ToString();
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox";
            return Results.Bytes(row.PhotoContent, row.PhotoContentType);
        });

        app.MapPost("/api/tenant/{token}/identity-photo", async (string token, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            if (context.Request.ContentLength is long declared && declared > MaxMeterPhotoRequestBytes)
                throw new ApiException(StatusCodes.Status413PayloadTooLarge, "Ảnh giấy tờ tùy thân tối đa 2 MB.");
            var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = MaxMeterPhotoRequestBytes;
            if (!context.Request.HasFormContentType)
                throw new ApiException(StatusCodes.Status400BadRequest, "Gửi ảnh giấy tờ tùy thân bằng multipart/form-data.");

            IFormCollection form;
            try
            {
                form = await context.Request.ReadFormAsync(ct);
            }
            catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                throw new ApiException(StatusCodes.Status413PayloadTooLarge, "Ảnh giấy tờ tùy thân tối đa 2 MB.");
            }
            catch (InvalidDataException)
            {
                throw new ApiException(StatusCodes.Status400BadRequest, "Không đọc được tệp ảnh giấy tờ tùy thân.");
            }

            if (form.Files.Count != 1)
                throw new ApiException(StatusCodes.Status400BadRequest, "Vui lòng chọn đúng một ảnh chụp CCCD/Hộ chiếu.");
            var file = form.Files[0];
            if (file.Length <= 0)
                throw new ApiException(StatusCodes.Status400BadRequest, "Tệp ảnh giấy tờ tùy thân trống.");
            if (file.Length > MaxMeterPhotoBytes)
                throw new ApiException(StatusCodes.Status413PayloadTooLarge, "Ảnh giấy tờ tùy thân tối đa 2 MB.");

            byte[] content;
            await using (var stream = file.OpenReadStream())
            using (var buffer = new MemoryStream((int)file.Length))
            {
                await stream.CopyToAsync(buffer, ct);
                content = buffer.ToArray();
            }

            var detected = ExpenseEndpoints.DetectFileType(content);
            if (detected is null || !detected.Value.ContentType.StartsWith("image/", StringComparison.Ordinal))
                throw new ApiException(StatusCodes.Status400BadRequest, "Chỉ nhận ảnh chụp CCCD/Hộ chiếu định dạng JPEG, PNG hoặc WebP.");

            var targetKey = NormalizeIdentityTargetKey(form["target"].FirstOrDefault());

            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var occupancy = await FindTenantAsync(db, token, ct);
            var roomBuilding = await (from r in db.Rooms
                                      join b in db.Buildings on r.BuildingId equals b.Id
                                      where r.Id == occupancy.RoomId
                                      select new { Room = r, Building = b }).SingleOrDefaultAsync(ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy phòng thuê.");
            var room = roomBuilding.Room;
            var building = roomBuilding.Building;
            if (room.ArchivedAt is not null || building.ArchivedAt is not null)
                throw new ApiException(StatusCodes.Status409Conflict, "Không thể tải giấy tờ cho phòng hoặc dãy đã lưu trữ.");

            var fileName = ExpenseEndpoints.SanitizeFileName(file.FileName, detected.Value.Extension);
            var sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            var now = DateTimeOffset.UtcNow;

            var existingDoc = await db.TenantIdentityDocuments
                .SingleOrDefaultAsync(x => x.OccupancyId == occupancy.Id && x.TargetKey == targetKey, ct);
            if (existingDoc is null)
            {
                existingDoc = new TenantIdentityDocument
                {
                    OwnerId = building.OwnerId,
                    BuildingId = building.Id,
                    RoomId = room.Id,
                    OccupancyId = occupancy.Id,
                    TargetKey = targetKey,
                    PhotoFileName = fileName,
                    PhotoContentType = detected.Value.ContentType,
                    PhotoSizeBytes = content.LongLength,
                    PhotoSha256 = sha256,
                    PhotoContent = content,
                    UploadedAt = now
                };
                db.TenantIdentityDocuments.Add(existingDoc);
            }
            else
            {
                existingDoc.OwnerId = building.OwnerId;
                existingDoc.BuildingId = building.Id;
                existingDoc.RoomId = room.Id;
                existingDoc.PhotoFileName = fileName;
                existingDoc.PhotoContentType = detected.Value.ContentType;
                existingDoc.PhotoSizeBytes = content.LongLength;
                existingDoc.PhotoSha256 = sha256;
                existingDoc.PhotoContent = content;
                existingDoc.UploadedAt = now;
            }

            occupancy.TenantSelfCheckinAt = now;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return Results.Ok(new TenantIdentityPhotoDto(
                existingDoc.TargetKey,
                existingDoc.PhotoFileName,
                existingDoc.PhotoContentType,
                existingDoc.PhotoSizeBytes,
                existingDoc.UploadedAt));
        }).RequireRateLimiting("tenant-payment");

        app.MapGet("/api/tenant/{token}/identity-photo", async (string token, HttpContext context, AppDbContext db, CancellationToken ct, string? target = "tenant") =>
        {
            var occupancy = await FindTenantAsync(db, token, ct);
            var targetKey = NormalizeIdentityTargetKey(target);
            var row = await db.TenantIdentityDocuments.AsNoTracking()
                .Where(x => x.OccupancyId == occupancy.Id && x.TargetKey == targetKey)
                .Select(x => new { x.PhotoFileName, x.PhotoContentType, x.PhotoContent })
                .SingleOrDefaultAsync(ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy ảnh chụp CCCD/Hộ chiếu.");

            var disposition = new ContentDispositionHeaderValue("inline");
            disposition.SetHttpFileName(row.PhotoFileName);
            context.Response.Headers.ContentDisposition = disposition.ToString();
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox";
            return Results.Bytes(row.PhotoContent, row.PhotoContentType);
        });

        app.MapPost("/api/tenant/{token}/payment", async (string token, AppDbContext db, PayOsService service, [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null, CancellationToken ct = default) => {
            var occupancy = await FindTenantAsync(db, token, ct);
            var invoice = await db.Invoices.Where(i => i.OccupancyId == occupancy.Id && i.LifecycleState == InvoiceLifecycleState.Issued && i.Total > i.Paid)
                .OrderBy(i => i.Id).FirstOrDefaultAsync(ct) ?? throw new ApiException(409, "Không còn hóa đơn cần thanh toán.");
            if (milestoneTracker != null)
                await milestoneTracker.RecordOwnerActivityAsync(invoice.OwnerId, ActorType.System, "TenantCheckoutAttempt", ct);
            return Results.Ok(await service.CreateOrGetCheckoutAsync(invoice.OwnerId, invoice, token, ct, allInvoices: true));
        }).RequireRateLimiting("tenant-payment");

        app.MapPost("/api/tenant/{token}/orders/{orderCode:long}/reconcile", async (string token, long orderCode, AppDbContext db, PayOsService service, CancellationToken ct) => {
            var occupancy = await FindTenantAsync(db, token, ct);
            var attempt = await db.PayOsAttempts.AsNoTracking().SingleOrDefaultAsync(a => a.OrderCode == orderCode && a.Kind == "Invoice" &&
                db.Invoices.Any(i => i.Id == a.InvoiceId && i.OccupancyId == occupancy.Id && i.OwnerId == a.OwnerId), ct)
                ?? throw new ApiException(404, "Payment order was not found for this tenant.");
            return Results.Ok(await service.ReconcileAsync(attempt.OwnerId, orderCode, ct));
        }).RequireRateLimiting("tenant-payment");

        app.MapPost("/api/tenant/{token}/invoices/{id:guid}/payment", async (string token, Guid id, AppDbContext db, PayOsService payOsService, [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null, CancellationToken cancellationToken = default) =>
        {
            var occupancy = await FindTenantAsync(db, token, cancellationToken);
            var invoice = await db.Invoices.SingleOrDefaultAsync(row => row.Id == id && row.OccupancyId == occupancy.Id && row.LifecycleState == InvoiceLifecycleState.Issued, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Invoice was not found for this tenant.");
            if (milestoneTracker != null)
                await milestoneTracker.RecordOwnerActivityAsync(invoice.OwnerId, ActorType.System, "TenantCheckoutAttempt", cancellationToken);
            var checkout = await payOsService.CreateOrGetCheckoutAsync(invoice.OwnerId, invoice, token, cancellationToken);
            return Results.Ok(checkout);
        }).RequireRateLimiting("tenant-payment");

        app.MapPost("/api/tenant/{token}/invoices/{id:guid}/reconcile", async (string token, Guid id, AppDbContext db, PayOsService payOsService, CancellationToken cancellationToken) =>
        {
            var occupancy = await FindTenantAsync(db, token, cancellationToken);
            var invoice = await db.Invoices.Include(x => x.DepositSettlements).AsNoTracking().SingleOrDefaultAsync(row => row.Id == id && row.OccupancyId == occupancy.Id && row.LifecycleState == InvoiceLifecycleState.Issued, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Invoice was not found for this tenant.");
            var status = await payOsService.ReconcileInvoiceAsync(invoice.OwnerId, invoice.Id, cancellationToken);
            var refreshed = await db.Invoices.Include(x => x.DepositSettlements).AsNoTracking().SingleAsync(row => row.Id == invoice.Id, cancellationToken);
            var isPending = await PaymentReservations.Pending(db).AnyAsync(x => x.OwnerId == invoice.OwnerId && x.Id == refreshed.Id, cancellationToken);
            return Results.Ok(new { status, invoice = InvoiceService.ToDto(refreshed, isPending) });
        }).RequireRateLimiting("tenant-payment");

        app.MapPost("/api/payments/webhook/{ownerId:guid}", async (Guid ownerId, HttpRequest request, PayOsService payOsService, [FromServices] IMemoryCache? cache, CancellationToken cancellationToken) =>
        {
            JsonDocument body;
            try { body = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken); }
            catch (JsonException) { throw new ApiException(StatusCodes.Status400BadRequest, "Malformed payOS callback payload."); }
            using (body)
                await payOsService.ProcessWebhookAsync(ownerId, body.RootElement, cancellationToken);
            ApiSupport.InvalidateDashboardCache(cache, ownerId);
            return Results.Ok(new { code = "00", desc = "success" });
        });
    }

    internal static TenantMeterSubmissionDto ToSubmissionDto(TenantMeterSubmission submission) => new(
        submission.Id,
        submission.RoomId,
        submission.OccupancyId,
        submission.Period,
        submission.PreviousReading,
        submission.SubmittedReading,
        submission.ApprovedReading,
        submission.TenantNote,
        submission.RejectReason,
        submission.Status.ToString(),
        submission.PhotoFileName,
        submission.PhotoContentType,
        submission.PhotoSizeBytes,
        submission.SubmittedAt,
        submission.ReviewedAt,
        submission.ReadingId,
        submission.InvoiceId);

    internal static string NormalizeIdentityTargetKey(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return "tenant";
        var trimmed = target.Trim();
        if (trimmed.Length > 40)
            throw new ApiException(StatusCodes.Status400BadRequest, "Đối tượng giấy tờ tùy thân tối đa 40 ký tự.");
        return string.Equals(trimmed, "tenant", StringComparison.OrdinalIgnoreCase) ? "tenant" : trimmed;
    }

    internal static async Task<Occupancy> FindTenantAsync(AppDbContext db, string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
            throw new ApiException(StatusCodes.Status404NotFound, "Tenant link was not found.");
        var tokenHash = TenantTokenService.Hash(token);
        return await db.Occupancies.SingleOrDefaultAsync(row => row.IsActive && row.TenantTokenHash == tokenHash, cancellationToken)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Tenant link is no longer active.");
    }
}
