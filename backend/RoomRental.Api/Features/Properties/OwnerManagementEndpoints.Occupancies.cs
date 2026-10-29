using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static partial class OwnerManagementEndpoints
{
    private static void MapOccupanciesRoutes(RouteGroupBuilder routes)
    {
        routes.MapMethods("/rooms/{id:guid}/occupancy", ["PATCH"], async (Guid id, OccupancyUpdateRequest request,
            HttpContext context, AppDbContext db, TenantTokenService tokenService, PaymentWebhookService webhookService, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var (room, building) = await PropertyLookup.FindRoomAsync(db, ownerId, id, ct);
            var occupancy = await db.Occupancies.SingleOrDefaultAsync(x => x.Id == request.OccupancyId && x.RoomId == id && x.IsActive, ct)
                ?? throw new ApiException(409, "Lượt thuê đã thay đổi hoặc không còn hoạt động. Vui lòng tải lại phòng.");
            if (string.IsNullOrWhiteSpace(request.TenantName) || request.TenantName.Trim().Length > 160)
                throw new ApiException(400, "Tên người thuê cần từ 1 đến 160 ký tự.");
            if (request.Phone?.Trim().Length > 60) throw new ApiException(400, "Số điện thoại tối đa 60 ký tự.");
            ApplyOccupancyContractFields(
                occupancy,
                request.TenantCitizenId,
                request.TenantDateOfBirth,
                request.TenantGender,
                request.TenantCitizenIdIssuedDate,
                request.TenantCitizenIdIssuedPlace,
                request.TenantPermanentAddress,
                request.TenantEmail,
                request.ContractEndDate,
                request.PaymentDueDay,
                request.RoomAreaSqm,
                request.HandoverAssets,
                request.AdditionalTerms,
                request.HouseholdHeadRelation,
                preserveExistingWhenNull: request.TenantCitizenId is null);
            var effectivePeople = request.People;
            if (request.CoResidents is not null)
            {
                occupancy.CoResidentsJson = ValidateAndSerializeCoResidents(request.CoResidents, out var cleanedCoResidents);
                effectivePeople = Math.Max(effectivePeople, 1 + cleanedCoResidents.Count);
            }
            if (request.People < 1 || effectivePeople is < 1 or > 30) throw new ApiException(400, "Số người ở cần từ 1 đến 30.");
            var peopleChanged = occupancy.People != effectivePeople;
            occupancy.TenantName = request.TenantName.Trim();
            occupancy.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
            occupancy.People = effectivePeople;
            if (peopleChanged)
            {
                // Issued invoices (including unpaid ones) are finalized snapshots. Only unpaid drafts may change.
                var drafts = await db.Invoices.Where(x => x.OwnerId == ownerId && x.OccupancyId == occupancy.Id &&
                    x.Kind == "Monthly" && x.LifecycleState == InvoiceLifecycleState.Draft && x.WaterPerPerson == true && x.WaterByMeter != true).ToListAsync(ct);
                foreach (var draft in drafts)
                {
                    if (draft.Paid != 0 || draft.Status == InvoiceStatus.Paid ||
                        await db.Receipts.AnyAsync(x => x.InvoiceId == draft.Id, ct) ||
                        await db.PayOsAttempts.AnyAsync(x => (x.InvoiceId == draft.Id || db.Invoices.Any(i => i.Id == draft.Id && i.PaymentReservationId == x.Id)) && x.Status == PaymentAttemptStatus.Pending, ct))
                        throw new ApiException(409, "Hóa đơn nháp có thanh toán hoặc giao dịch đang chờ. Cần xử lý trước khi đổi số người.");
                    if (draft.WaterRate is null) throw new ApiException(409, "Hóa đơn nháp thiếu bảng phí nước đã lưu.");
                    var before = BillingEndpoints.SerializeSnapshot(draft);
                    draft.WaterAmount = ApiSupport.RoundVnd(draft.WaterRate.Value * occupancy.People);
                    draft.Total = InvoiceService.ValidateTotal(draft.Rent, draft.ElectricityAmount, draft.WaterAmount, draft.ServiceAmount);
                    draft.Version++;
                    InvoiceService.RecalculateStatus(draft);
                    await BillingEndpoints.AddRevisionAsync(db, draft, Guid.NewGuid(), RequestFingerprint.Fingerprint(request),
                        "Cập nhật số người ở cho hóa đơn nháp.", ownerId, before, BillingEndpoints.SerializeSnapshot(draft), ct);
                }
            }
            if (request.InitialReading is decimal newInitialReading)
            {
                if (newInitialReading < 0 || newInitialReading > 1_000_000_000)
                    throw new ApiException(400, "Chỉ số điện phải là số không âm trong giới hạn cho phép.");
                if (decimal.Round(newInitialReading, 3) != newInitialReading)
                    throw new ApiException(400, "Chỉ số điện tối đa 3 chữ số thập phân.");
                if (newInitialReading != room.InitialReading)
                {
                    if (await db.Invoices.AnyAsync(x => x.OwnerId == ownerId && x.OccupancyId == occupancy.Id &&
                        x.Kind == "Monthly" && x.LifecycleState == InvoiceLifecycleState.Issued, ct))
                        throw new ApiException(409, "Phòng đã phát hành hóa đơn điện tháng nên không thể sửa chỉ số điện ban đầu. Hãy điều chỉnh trực tiếp trên hóa đơn.");
                    var priorMaxReading = await QueryAggregates.MaxReadingAsync(db,
                        db.Readings.AsNoTracking().Where(x => x.RoomId == id && x.OccupancyId != occupancy.Id).Select(x => x.CurrentReading), ct);
                    var priorMaxFinal = await QueryAggregates.MaxReadingAsync(db,
                        db.Invoices.AsNoTracking().Where(x => x.RoomId == id && x.OccupancyId != occupancy.Id && x.IsFinalMeterReading).Select(x => x.CurrentReading), ct);
                    var priorFloor = Math.Max(priorMaxReading, priorMaxFinal);
                    if (priorFloor > 0 && newInitialReading < priorFloor)
                        throw new ApiException(400, $"Chỉ số điện ban đầu không thể thấp hơn chỉ số đã chốt của lượt thuê trước ({priorFloor} kWh).");

                    var firstReading = await db.Readings
                        .Where(x => x.RoomId == id && x.OccupancyId == occupancy.Id)
                        .OrderBy(x => x.Period)
                        .ThenBy(x => x.RecordedAt)
                        .FirstOrDefaultAsync(ct);
                    var firstDraft = await db.Invoices
                        .Where(x => x.OwnerId == ownerId && x.OccupancyId == occupancy.Id &&
                                    x.Kind == "Monthly" && x.LifecycleState == InvoiceLifecycleState.Draft)
                        .OrderBy(x => x.Period)
                        .ThenBy(x => x.IssuedAt)
                        .FirstOrDefaultAsync(ct);

                    if (firstReading is not null &&
                        (firstDraft is null || string.CompareOrdinal(firstReading.Period, firstDraft.Period) <= 0))
                    {
                        if (firstReading.CurrentReading < newInitialReading)
                            throw new ApiException(400, $"Chỉ số điện ban đầu ({newInitialReading} kWh) không thể lớn hơn chỉ số cuối kỳ đã ghi ({firstReading.CurrentReading} kWh).");
                        firstReading.PreviousReading = newInitialReading;
                        firstReading.Consumption = firstReading.CurrentReading - newInitialReading;
                    }

                    if (firstDraft is not null &&
                        (firstReading is null || string.CompareOrdinal(firstDraft.Period, firstReading.Period) <= 0))
                    {
                        if (firstDraft.Paid != 0 || firstDraft.Status == InvoiceStatus.Paid ||
                            await db.Receipts.AnyAsync(x => x.InvoiceId == firstDraft.Id, ct) ||
                            await db.PayOsAttempts.AnyAsync(x => (x.InvoiceId == firstDraft.Id || db.Invoices.Any(i => i.Id == firstDraft.Id && i.PaymentReservationId == x.Id)) && x.Status == PaymentAttemptStatus.Pending, ct))
                            throw new ApiException(409, "Hóa đơn nháp có thanh toán hoặc giao dịch đang chờ. Cần xử lý trước khi đổi chỉ số điện ban đầu.");
                        if (firstDraft.CurrentReading is decimal cur && cur < newInitialReading)
                            throw new ApiException(400, $"Chỉ số điện ban đầu ({newInitialReading} kWh) không thể lớn hơn chỉ số cuối kỳ trong bản nháp ({cur} kWh).");
                        var before = BillingEndpoints.SerializeSnapshot(firstDraft);
                        firstDraft.PreviousReading = newInitialReading;
                        if (firstDraft.CurrentReading is decimal currentVal)
                        {
                            firstDraft.Consumption = currentVal - newInitialReading;
                            firstDraft.ElectricityAmount = ApiSupport.RoundVnd(firstDraft.Consumption * (firstDraft.ElectricityRate ?? room.ElectricityRate));
                            firstDraft.Total = InvoiceService.ValidateTotal(firstDraft.Rent, firstDraft.ElectricityAmount, firstDraft.WaterAmount, firstDraft.ServiceAmount);
                        }
                        firstDraft.Version++;
                        InvoiceService.RecalculateStatus(firstDraft);
                        await BillingEndpoints.AddRevisionAsync(db, firstDraft, Guid.NewGuid(), RequestFingerprint.Fingerprint(request),
                            "Cập nhật chỉ số điện ban đầu cho hóa đơn nháp.", ownerId, before, BillingEndpoints.SerializeSnapshot(firstDraft), ct);
                    }

                    room.InitialReading = newInitialReading;
                }
            }
            if (request.InitialWaterReading is decimal newInitialWaterReading)
            {
                if (newInitialWaterReading < 0 || newInitialWaterReading > 1_000_000_000)
                    throw new ApiException(400, "Chỉ số nước phải là số không âm trong giới hạn cho phép.");
                if (decimal.Round(newInitialWaterReading, 3) != newInitialWaterReading)
                    throw new ApiException(400, "Chỉ số nước tối đa 3 chữ số thập phân.");
                if (newInitialWaterReading != room.InitialWaterReading)
                {
                    if (await db.Invoices.AnyAsync(x => x.OwnerId == ownerId && x.OccupancyId == occupancy.Id &&
                        x.Kind == "Monthly" && x.LifecycleState == InvoiceLifecycleState.Issued, ct))
                        throw new ApiException(409, "Phòng đã phát hành hóa đơn tháng nên không thể sửa chỉ số nước ban đầu. Hãy điều chỉnh trực tiếp trên hóa đơn.");
                    var priorMaxWater = await QueryAggregates.MaxReadingAsync(db,
                        db.Readings.AsNoTracking().Where(x => x.RoomId == id && x.OccupancyId != occupancy.Id && x.CurrentWaterReading != null).Select(x => x.CurrentWaterReading!.Value), ct);
                    var priorMaxFinalWater = await QueryAggregates.MaxReadingAsync(db,
                        db.Invoices.AsNoTracking().Where(x => x.RoomId == id && x.OccupancyId != occupancy.Id && x.IsFinalMeterReading && x.CurrentWaterReading != null).Select(x => x.CurrentWaterReading!.Value), ct);
                    var priorWaterFloor = Math.Max(priorMaxWater, priorMaxFinalWater);
                    if (priorWaterFloor > 0 && newInitialWaterReading < priorWaterFloor)
                        throw new ApiException(400, $"Chỉ số nước ban đầu không thể thấp hơn chỉ số đã chốt của lượt thuê trước ({priorWaterFloor} m³).");

                    var firstReading = await db.Readings
                        .Where(x => x.RoomId == id && x.OccupancyId == occupancy.Id)
                        .OrderBy(x => x.Period)
                        .ThenBy(x => x.RecordedAt)
                        .FirstOrDefaultAsync(ct);
                    var firstDraft = await db.Invoices
                        .Where(x => x.OwnerId == ownerId && x.OccupancyId == occupancy.Id &&
                                    x.Kind == "Monthly" && x.LifecycleState == InvoiceLifecycleState.Draft)
                        .OrderBy(x => x.Period)
                        .ThenBy(x => x.IssuedAt)
                        .FirstOrDefaultAsync(ct);

                    if (firstReading is not null && firstReading.CurrentWaterReading is decimal curWaterReading &&
                        (firstDraft is null || string.CompareOrdinal(firstReading.Period, firstDraft.Period) <= 0))
                    {
                        if (curWaterReading < newInitialWaterReading)
                            throw new ApiException(400, $"Chỉ số nước ban đầu ({newInitialWaterReading} m³) không thể lớn hơn chỉ số cuối kỳ đã ghi ({curWaterReading} m³).");
                        firstReading.PreviousWaterReading = newInitialWaterReading;
                        firstReading.WaterConsumption = curWaterReading - newInitialWaterReading;
                    }

                    if (firstDraft is not null && firstDraft.WaterByMeter == true &&
                        (firstReading is null || string.CompareOrdinal(firstDraft.Period, firstReading.Period) <= 0))
                    {
                        if (firstDraft.Paid != 0 || firstDraft.Status == InvoiceStatus.Paid ||
                            await db.Receipts.AnyAsync(x => x.InvoiceId == firstDraft.Id, ct) ||
                            await db.PayOsAttempts.AnyAsync(x => (x.InvoiceId == firstDraft.Id || db.Invoices.Any(i => i.Id == firstDraft.Id && i.PaymentReservationId == x.Id)) && x.Status == PaymentAttemptStatus.Pending, ct))
                            throw new ApiException(409, "Hóa đơn nháp có thanh toán hoặc giao dịch đang chờ. Cần xử lý trước khi đổi chỉ số nước ban đầu.");
                        if (firstDraft.CurrentWaterReading is decimal curWaterDraft && curWaterDraft < newInitialWaterReading)
                            throw new ApiException(400, $"Chỉ số nước ban đầu ({newInitialWaterReading} m³) không thể lớn hơn chỉ số cuối kỳ trong bản nháp ({curWaterDraft} m³).");
                        var before = BillingEndpoints.SerializeSnapshot(firstDraft);
                        firstDraft.PreviousWaterReading = newInitialWaterReading;
                        if (firstDraft.CurrentWaterReading is decimal currentWaterVal)
                        {
                            firstDraft.WaterConsumption = currentWaterVal - newInitialWaterReading;
                            firstDraft.WaterAmount = ApiSupport.RoundVnd(firstDraft.WaterConsumption.Value * (firstDraft.WaterRate ?? room.WaterFee));
                            firstDraft.Total = InvoiceService.ValidateTotal(firstDraft.Rent, firstDraft.ElectricityAmount, firstDraft.WaterAmount, firstDraft.ServiceAmount);
                        }
                        firstDraft.Version++;
                        InvoiceService.RecalculateStatus(firstDraft);
                        await BillingEndpoints.AddRevisionAsync(db, firstDraft, Guid.NewGuid(), RequestFingerprint.Fingerprint(request),
                            "Cập nhật chỉ số nước ban đầu cho hóa đơn nháp.", ownerId, before, BillingEndpoints.SerializeSnapshot(firstDraft), ct);
                    }

                    room.InitialWaterReading = newInitialWaterReading;
                }
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Ok(await ToRoomDtoAsync(db, tokenService, webhookService, room, building, occupancy, VietnamCalendar.CurrentVietnamPeriod(), ct));
        });

        routes.MapPost("/rooms/{id:guid}/occupancy", async (Guid id, OccupancyRequest request, HttpContext context, AppDbContext db, InvoiceService invoiceService, TenantTokenService tokenService, PaymentWebhookService webhookService, [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null, CancellationToken cancellationToken = default) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            var (room, building) = await PropertyLookup.FindRoomAsync(db, ownerId, id, cancellationToken);
            if (await db.Occupancies.AnyAsync(row => row.RoomId == id && row.IsActive, cancellationToken))
                throw new ApiException(StatusCodes.Status409Conflict, "This room already has an active occupancy.");
            if (string.IsNullOrWhiteSpace(request.TenantName) || request.TenantName.Trim().Length > 160)
                throw new ApiException(StatusCodes.Status400BadRequest, "Tenant name is required and must be 160 characters or fewer.");
            var tenantCitizenId = ValidateAndCleanCitizenId(request.TenantCitizenId, "Số CCCD/Định danh của người thuê");
            List<CoResidentDto> cleanedCoResidents = [];
            string? coResidentsJson = null;
            if (request.CoResidents is not null)
            {
                coResidentsJson = ValidateAndSerializeCoResidents(request.CoResidents, out cleanedCoResidents);
            }
            var effectivePeople = Math.Max(request.People, 1 + cleanedCoResidents.Count);
            if (request.People < 1 || effectivePeople is < 1 or > 30)
                throw new ApiException(StatusCodes.Status400BadRequest, "People must be between 1 and 30.");
            var today = VietnamCalendar.TodayVietnam();
            if (request.StartDate > today)
                throw new ApiException(StatusCodes.Status400BadRequest, "Start date cannot be in the future.");
            var currentPeriod = VietnamCalendar.CurrentVietnamPeriod();

            if (request.DepositAmount < 0 || request.DepositAmount > int.MaxValue || request.DepositAmount != decimal.Truncate(request.DepositAmount))
                throw new ApiException(400, "Tiền cọc phải là số nguyên VND từ 0 đến 2.147.483.647.");

            if (request.InitialReading is decimal initialReading)
            {
                if (initialReading < 0 || initialReading > 1_000_000_000)
                    throw new ApiException(400, "Chỉ số điện phải là số không âm trong giới hạn cho phép.");
                if (decimal.Round(initialReading, 3) != initialReading)
                    throw new ApiException(400, "Chỉ số điện tối đa 3 chữ số thập phân.");
                var maxReading = await QueryAggregates.MaxReadingAsync(db,
                    db.Readings.AsNoTracking().Where(x => x.RoomId == id).Select(x => x.CurrentReading), cancellationToken);
                var maxFinal = await QueryAggregates.MaxReadingAsync(db,
                    db.Invoices.AsNoTracking().Where(x => x.RoomId == id && x.IsFinalMeterReading).Select(x => x.CurrentReading), cancellationToken);
                var priorPhysical = Math.Max(room.InitialReading, Math.Max(maxReading, maxFinal));
                if (priorPhysical > 0 && initialReading < priorPhysical)
                    throw new ApiException(400, $"Chỉ số điện không thể thấp hơn chỉ số trước đó ({priorPhysical} kWh).");
                room.InitialReading = initialReading;
            }
            if (request.InitialWaterReading is decimal initialWaterReading)
            {
                if (initialWaterReading < 0 || initialWaterReading > 1_000_000_000)
                    throw new ApiException(400, "Chỉ số nước phải là số không âm trong giới hạn cho phép.");
                if (decimal.Round(initialWaterReading, 3) != initialWaterReading)
                    throw new ApiException(400, "Chỉ số nước tối đa 3 chữ số thập phân.");
                var maxWaterReading = await QueryAggregates.MaxReadingAsync(db,
                    db.Readings.AsNoTracking().Where(x => x.RoomId == id && x.CurrentWaterReading != null).Select(x => x.CurrentWaterReading!.Value), cancellationToken);
                var maxFinalWater = await QueryAggregates.MaxReadingAsync(db,
                    db.Invoices.AsNoTracking().Where(x => x.RoomId == id && x.IsFinalMeterReading && x.CurrentWaterReading != null).Select(x => x.CurrentWaterReading!.Value), cancellationToken);
                var priorPhysicalWater = Math.Max(room.InitialWaterReading, Math.Max(maxWaterReading, maxFinalWater));
                if (priorPhysicalWater > 0 && initialWaterReading < priorPhysicalWater)
                    throw new ApiException(400, $"Chỉ số nước không thể thấp hơn chỉ số trước đó ({priorPhysicalWater} m³).");
                room.InitialWaterReading = initialWaterReading;
            }

            var token = tokenService.Create();
            var occupancy = new Occupancy
            {
                RoomId = id,
                TenantName = request.TenantName.Trim(),
                Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim()[..Math.Min(request.Phone.Trim().Length, 60)],
                People = effectivePeople,
                StartDate = request.StartDate,
                TenantCitizenId = tenantCitizenId,
                CoResidentsJson = coResidentsJson,
                TenantTokenHash = token.Hash,
                ProtectedTenantToken = token.ProtectedToken
            };
            ApplyOccupancyContractFields(
                occupancy,
                tenantCitizenId,
                request.TenantDateOfBirth,
                request.TenantGender,
                request.TenantCitizenIdIssuedDate,
                request.TenantCitizenIdIssuedPlace,
                request.TenantPermanentAddress,
                request.TenantEmail,
                request.ContractEndDate,
                request.PaymentDueDay,
                request.RoomAreaSqm,
                request.HandoverAssets,
                request.AdditionalTerms,
                request.HouseholdHeadRelation,
                preserveExistingWhenNull: false);
            db.Occupancies.Add(occupancy);
            if (request.DepositAmount > 0)
            {
                var deposit = new Invoice { OwnerId = ownerId, RoomId = room.Id, OccupancyId = occupancy.Id,
                    BuildingId = building.Id, RoomName = room.Name, BuildingName = building.Name, TenantName = occupancy.TenantName,
                    Period = currentPeriod, Kind = "Deposit", Total = request.DepositAmount, DueDate = today };
                db.Invoices.Add(deposit);
                await invoiceService.StageZaloReminderAsync(deposit, cancellationToken);
            }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (milestoneTracker != null)
            {
                try { await milestoneTracker.RecordOwnerActivityAsync(ownerId, ActorType.Owner, "OccupancyMoveIn", cancellationToken); }
                catch { /* Telemetry failure must not block product */ }
            }
            return Results.Ok(await ToRoomDtoAsync(db, tokenService, webhookService, room, building, occupancy, currentPeriod, cancellationToken));
        });

        routes.MapPost("/rooms/{id:guid}/end-occupancy", async (Guid id, CompleteMoveoutRequest request,
            HttpContext context, AppDbContext db, [FromServices] RoomRental.Api.Features.Monitoring.IOwnerMilestoneTracker? milestoneTracker = null, CancellationToken cancellationToken = default) =>
            await MoveoutEndpoints.CompleteMoveoutAsync(id, request, context, db, milestoneTracker, cancellationToken));

        routes.MapPost("/rooms/{id:guid}/rotate-link", async (Guid id, HttpContext context, AppDbContext db,
            TenantTokenService tokenService, PaymentWebhookService webhookService, CancellationToken cancellationToken) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            if (!await tokenService.CanShareAsync(ownerId, webhookService, cancellationToken))
                throw new ApiException(StatusCodes.Status400BadRequest, "Chưa hoàn tất cấu hình nhận tiền (VietQR thủ công hoặc payOS tự động). Vui lòng thiết lập thanh toán trước khi cấp link cho khách.");

            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
            _ = await PropertyLookup.FindRoomAsync(db, ownerId, id, cancellationToken);
            var occupancy = await db.Occupancies.SingleOrDefaultAsync(row => row.RoomId == id && row.IsActive, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status409Conflict, "This room has no active occupancy.");
            var token = tokenService.Create();
            occupancy.TenantTokenHash = token.Hash;
            occupancy.ProtectedTenantToken = token.ProtectedToken;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new RotateLinkDto(token.Token, tokenService.Url(token.Token)));
        });
    }
}
