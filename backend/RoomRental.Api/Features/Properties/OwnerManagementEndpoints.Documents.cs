using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static partial class OwnerManagementEndpoints
{
    private const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private static readonly JsonElement SupportedBanks = JsonDocument.Parse(
        typeof(OwnerManagementEndpoints).Assembly.GetManifestResourceStream("RoomRental.Payments.Banks.json")!)
        .RootElement.Clone();
    private static readonly JsonSerializerOptions CoResidentJsonOptions = new(JsonSerializerDefaults.Web);

    private static void MapDocumentRoutes(RouteGroupBuilder routes)
    {
        routes.MapGet("/rooms/{id:guid}/documents", async (Guid id, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var dto = await LoadOccupancyDocumentDtoAsync(db, ownerId, id, ct);
            return Results.Ok(dto);
        });

        routes.MapPut("/rooms/{id:guid}/documents", async (Guid id, UpdateOccupancyDocumentRequest request, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var (room, building) = await PropertyLookup.FindRoomAsync(db, ownerId, id, ct);
            var owner = await db.Users.SingleOrDefaultAsync(x => x.Id == ownerId, ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Owner was not found.");
            var occupancy = await db.Occupancies.SingleOrDefaultAsync(x => x.Id == request.OccupancyId && x.RoomId == id && x.IsActive, ct)
                ?? throw new ApiException(StatusCodes.Status409Conflict, "Lượt thuê đã thay đổi hoặc không còn hoạt động. Vui lòng tải lại phòng.");

            ValidateAndApplyDocumentUpdates(request, owner, building, occupancy);

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            var dto = await BuildOccupancyDocumentDtoAsync(db, owner, building, room, occupancy, ct);
            return Results.Ok(dto);
        });

        routes.MapGet("/rooms/{id:guid}/documents/contract.docx", async (Guid id, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var dto = await LoadOccupancyDocumentDtoAsync(db, ownerId, id, ct);
            var today = VietnamCalendar.TodayVietnam();
            var bytes = ContractWordGenerator.GenerateRentalContractDocx(dto, today);
            var safeRoom = SanitizeFileSegment(dto.RoomName);
            var fileName = $"hop-dong-thue-tro-{safeRoom}-{today:yyyyMMdd}.docx";
            return Results.File(bytes, DocxContentType, fileName);
        });

        routes.MapGet("/rooms/{id:guid}/documents/ct01.docx", async (Guid id, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var dto = await LoadOccupancyDocumentDtoAsync(db, ownerId, id, ct);
            var today = VietnamCalendar.TodayVietnam();
            var bytes = ContractWordGenerator.GenerateCt01Docx(dto, today);
            var safeRoom = SanitizeFileSegment(dto.RoomName);
            var fileName = $"to-khai-ct01-{safeRoom}-{today:yyyyMMdd}.docx";
            return Results.File(bytes, DocxContentType, fileName);
        });

        routes.MapGet("/rooms/{id:guid}/documents/identity-photo", async (Guid id, HttpContext context, AppDbContext db, CancellationToken ct, string? target = "tenant", bool download = false) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            _ = await PropertyLookup.FindRoomAsync(db, ownerId, id, ct);
            var occupancy = await db.Occupancies.AsNoTracking()
                .SingleOrDefaultAsync(x => x.RoomId == id && x.IsActive, ct)
                ?? throw new ApiException(StatusCodes.Status409Conflict, "Phòng chưa có người thuê đang ở.");
            var targetKey = NormalizeIdentityTargetKey(target);
            var row = await db.TenantIdentityDocuments.AsNoTracking()
                .Where(x => x.OccupancyId == occupancy.Id && x.TargetKey == targetKey)
                .Select(x => new { x.PhotoFileName, x.PhotoContentType, x.PhotoContent })
                .SingleOrDefaultAsync(ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy ảnh chụp CCCD/Hộ chiếu.");

            var disposition = new ContentDispositionHeaderValue(download ? "attachment" : "inline");
            disposition.SetHttpFileName(row.PhotoFileName);
            context.Response.Headers.ContentDisposition = disposition.ToString();
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox";
            return Results.Bytes(row.PhotoContent, row.PhotoContentType);
        });
    }

    private static async Task<OccupancyDocumentDto> LoadOccupancyDocumentDtoAsync(AppDbContext db, Guid ownerId, Guid roomId, CancellationToken ct)
    {
        var (room, building) = await PropertyLookup.FindRoomAsync(db, ownerId, roomId, ct);
        var owner = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ownerId, ct)
            ?? throw new ApiException(StatusCodes.Status404NotFound, "Owner was not found.");
        var occupancy = await db.Occupancies.AsNoTracking().SingleOrDefaultAsync(x => x.RoomId == roomId && x.IsActive, ct)
            ?? throw new ApiException(StatusCodes.Status409Conflict, "Phòng chưa có người thuê đang ở để xuất hợp đồng.");
        return await BuildOccupancyDocumentDtoAsync(db, owner, building, room, occupancy, ct);
    }

    private static async Task<OccupancyDocumentDto> BuildOccupancyDocumentDtoAsync(
        AppDbContext db,
        AppUser owner,
        Building building,
        Room room,
        Occupancy occupancy,
        CancellationToken ct)
    {
        var depositInvoice = await db.Invoices.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OccupancyId == occupancy.Id && x.Kind == "Deposit", ct);
        var tenantIdentityPhotoFileName = await db.TenantIdentityDocuments.AsNoTracking()
            .Where(x => x.OccupancyId == occupancy.Id && x.TargetKey == "tenant")
            .Select(x => x.PhotoFileName)
            .FirstOrDefaultAsync(ct);
        var depositRequired = depositInvoice?.Total ?? 0m;
        var depositPaid = depositInvoice?.Paid ?? 0m;

        string? bankName = null;
        string? bankShortName = null;
        if (!string.IsNullOrWhiteSpace(owner.ManualBankBin))
        {
            foreach (var bank in SupportedBanks.EnumerateArray())
            {
                if (bank.GetProperty("bin").GetString() == owner.ManualBankBin)
                {
                    bankName = bank.TryGetProperty("name", out var n) ? n.GetString() : null;
                    bankShortName = bank.TryGetProperty("shortName", out var s) ? s.GetString() : null;
                    break;
                }
            }
        }

        var coResidents = DeserializeCoResidents(occupancy.CoResidentsJson);

        var paymentDueDay = occupancy.PaymentDueDay is >= 1 and <= 28 ? occupancy.PaymentDueDay : 5;
        var roomAreaSqm = occupancy.RoomAreaSqm is > 0m and <= 10_000m ? occupancy.RoomAreaSqm : null;
        var householdRelation = string.IsNullOrWhiteSpace(occupancy.HouseholdHeadRelation)
            ? "Người thuê trọ"
            : occupancy.HouseholdHeadRelation;

        return new OccupancyDocumentDto(
            RoomId: room.Id,
            BuildingId: building.Id,
            OccupancyId: occupancy.Id,
            OwnerName: owner.Name,
            OwnerPhone: owner.Phone,
            OwnerEmail: owner.Email,
            OwnerCitizenId: owner.CitizenId,
            OwnerDateOfBirth: owner.DateOfBirth,
            OwnerCitizenIdIssuedDate: owner.CitizenIdIssuedDate,
            OwnerCitizenIdIssuedPlace: owner.CitizenIdIssuedPlace,
            OwnerPermanentAddress: owner.PermanentAddress,
            BankBin: owner.ManualBankBin,
            BankName: bankName,
            BankShortName: bankShortName,
            BankAccountNumber: owner.ManualAccountNumber,
            BankAccountName: owner.ManualAccountName,
            BuildingName: building.Name,
            BuildingAddress: building.Address,
            PoliceStationName: building.PoliceStationName,
            RoomName: room.Name,
            RoomAreaSqm: roomAreaSqm,
            Rent: room.Rent,
            RentInWords: VietnameseMoneyReader.ToVietnameseWords(room.Rent),
            ElectricityRate: room.ElectricityRate,
            WaterFee: room.WaterFee,
            WaterPerPerson: room.WaterPerPerson,
            ServiceFee: room.ServiceFee,
            InitialReading: room.InitialReading,
            TenantName: occupancy.TenantName,
            TenantPhone: occupancy.Phone,
            TenantEmail: occupancy.TenantEmail,
            People: occupancy.People,
            TenantCitizenId: occupancy.TenantCitizenId,
            TenantDateOfBirth: occupancy.TenantDateOfBirth,
            TenantGender: occupancy.TenantGender,
            TenantCitizenIdIssuedDate: occupancy.TenantCitizenIdIssuedDate,
            TenantCitizenIdIssuedPlace: occupancy.TenantCitizenIdIssuedPlace,
            TenantPermanentAddress: occupancy.TenantPermanentAddress,
            StartDate: occupancy.StartDate,
            ContractEndDate: occupancy.ContractEndDate,
            PaymentDueDay: paymentDueDay,
            DepositRequired: depositRequired,
            DepositInWords: VietnameseMoneyReader.ToVietnameseWords(depositRequired),
            DepositPaid: depositPaid,
            HandoverAssets: occupancy.HandoverAssets,
            AdditionalTerms: occupancy.AdditionalTerms,
            HouseholdHeadRelation: householdRelation,
            CoResidents: coResidents,
            TenantSelfCheckinAt: occupancy.TenantSelfCheckinAt,
            HasTenantIdentityPhoto: !string.IsNullOrEmpty(tenantIdentityPhotoFileName),
            TenantIdentityPhotoFileName: tenantIdentityPhotoFileName);
    }

    internal static void ValidateAndApplyOwnerProfileUpdates(
        UpdateProfileRequest request,
        AppUser owner)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
            throw new ApiException(StatusCodes.Status400BadRequest, "Vui lòng nhập họ tên tối đa 160 ký tự.");
        var ownerCitizenId = ValidateAndCleanCitizenId(request.CitizenId, "Số CCCD/Định danh của chủ trọ");

        owner.Name = name;
        owner.Phone = CleanOptional(request.Phone, 60, "Số điện thoại chủ trọ");
        owner.CitizenId = ownerCitizenId;
        owner.DateOfBirth = request.DateOfBirth;
        owner.CitizenIdIssuedDate = request.CitizenIdIssuedDate;
        owner.CitizenIdIssuedPlace = CleanOptional(request.CitizenIdIssuedPlace, 200, "Nơi cấp CCCD của chủ trọ");
        owner.PermanentAddress = CleanOptional(request.PermanentAddress, 500, "Địa chỉ thường trú của chủ trọ");
    }

    internal static void ValidateAndApplyDocumentUpdates(
        UpdateOccupancyDocumentRequest request,
        AppUser owner,
        Building building,
        Occupancy occupancy)
    {
        var ownerCitizenId = ValidateAndCleanCitizenId(request.OwnerCitizenId, "Số CCCD/Định danh của chủ trọ");
        var tenantCitizenId = ValidateAndCleanCitizenId(request.TenantCitizenId, "Số CCCD/Định danh của người thuê");

        if (!string.IsNullOrWhiteSpace(request.OwnerName))
        {
            var ownerName = request.OwnerName.Trim();
            if (ownerName.Length > 160)
                throw new ApiException(StatusCodes.Status400BadRequest, "Vui lòng nhập họ tên tối đa 160 ký tự.");
            owner.Name = ownerName;
        }

        if (request.OwnerPhone is not null)
            owner.Phone = CleanOptional(request.OwnerPhone, 60, "Số điện thoại chủ trọ");
        owner.CitizenId = ownerCitizenId;
        owner.DateOfBirth = request.OwnerDateOfBirth;
        owner.CitizenIdIssuedDate = request.OwnerCitizenIdIssuedDate;
        owner.CitizenIdIssuedPlace = CleanOptional(request.OwnerCitizenIdIssuedPlace, 200, "Nơi cấp CCCD của chủ trọ");
        owner.PermanentAddress = CleanOptional(request.OwnerPermanentAddress, 500, "Địa chỉ thường trú của chủ trọ");

        if (request.BuildingAddress is not null)
            building.Address = CleanOptional(request.BuildingAddress, 500, "Địa chỉ dãy trọ");
        building.PoliceStationName = CleanOptional(request.PoliceStationName, 250, "Cơ quan công an tiếp nhận");

        if (!string.IsNullOrWhiteSpace(request.TenantName))
        {
            var tenantName = request.TenantName.Trim();
            if (tenantName.Length > 160)
                throw new ApiException(StatusCodes.Status400BadRequest, "Tên người thuê tối đa 160 ký tự.");
            occupancy.TenantName = tenantName;
        }

        if (request.TenantPhone is not null)
            occupancy.Phone = CleanOptional(request.TenantPhone, 60, "Số điện thoại người thuê");
        occupancy.TenantEmail = CleanOptional(request.TenantEmail, 320, "Email người thuê");
        occupancy.TenantCitizenId = tenantCitizenId;
        occupancy.TenantDateOfBirth = request.TenantDateOfBirth;
        occupancy.TenantGender = CleanOptional(request.TenantGender, 20, "Giới tính");
        occupancy.TenantCitizenIdIssuedDate = request.TenantCitizenIdIssuedDate;
        occupancy.TenantCitizenIdIssuedPlace = CleanOptional(request.TenantCitizenIdIssuedPlace, 200, "Nơi cấp CCCD của người thuê");
        occupancy.TenantPermanentAddress = CleanOptional(request.TenantPermanentAddress, 500, "Địa chỉ thường trú của người thuê");

        if (request.ContractEndDate is DateOnly endDate && endDate < occupancy.StartDate)
            throw new ApiException(StatusCodes.Status400BadRequest, "Ngày kết thúc hợp đồng không được nhỏ hơn ngày bắt đầu thuê.");
        occupancy.ContractEndDate = request.ContractEndDate;

        if (request.PaymentDueDay is int dueDay)
        {
            if (dueDay is < 1 or > 28)
                throw new ApiException(StatusCodes.Status400BadRequest, "Ngày hạn thanh toán hàng tháng phải từ ngày 1 đến ngày 28.");
            occupancy.PaymentDueDay = dueDay;
        }

        if (request.RoomAreaSqm is decimal area)
        {
            if (area is < 0 or > 10_000)
                throw new ApiException(StatusCodes.Status400BadRequest, "Diện tích phòng phải từ 0 đến 10.000 m².");
            occupancy.RoomAreaSqm = area == 0 ? null : decimal.Round(area, 2);
        }
        else
        {
            occupancy.RoomAreaSqm = null;
        }

        occupancy.HandoverAssets = CleanOptional(request.HandoverAssets, 2000, "Danh mục tài sản bàn giao");
        occupancy.AdditionalTerms = CleanOptional(request.AdditionalTerms, 2000, "Điều khoản bổ sung");
        occupancy.HouseholdHeadRelation = CleanOptional(request.HouseholdHeadRelation, 100, "Mối quan hệ với chủ hộ") ?? "Người thuê trọ";

        if (request.CoResidents is not null)
        {
            occupancy.CoResidentsJson = ValidateAndSerializeCoResidents(request.CoResidents, out _);
        }
    }

    internal static IReadOnlyList<CoResidentDto> DeserializeCoResidents(string? coResidentsJson)
    {
        if (string.IsNullOrWhiteSpace(coResidentsJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<CoResidentDto>>(coResidentsJson, CoResidentJsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    internal static string? ValidateAndCleanCitizenId(string? value, string fieldLabel)
    {
        var cleaned = CleanOptional(value, 20, fieldLabel);
        if (cleaned is not null)
        {
            cleaned = cleaned.ToUpperInvariant();
            if (!Regex.IsMatch(cleaned, "^([0-9]{9,12}|[A-Z]{1,3}[A-Z0-9]{5,9})$"))
                throw new ApiException(StatusCodes.Status400BadRequest, $"{fieldLabel} phải gồm từ 9 đến 12 chữ số (CCCD/CMND) hoặc 6 đến 12 ký tự Hộ chiếu hợp lệ.");
        }
        return cleaned;
    }

    internal static string? ValidateAndSerializeCoResidents(
        IReadOnlyList<CoResidentDto> coResidents,
        out List<CoResidentDto> cleanedCoResidents)
    {
        if (coResidents.Count > 29)
            throw new ApiException(StatusCodes.Status400BadRequest, "Danh sách thành viên ở cùng tối đa 29 người (tổng số người ở tối đa 30).");
        cleanedCoResidents = new List<CoResidentDto>();
        foreach (var member in coResidents)
        {
            var fullName = CleanOptional(member.FullName, 160, "Họ tên thành viên ở cùng");
            if (fullName is null) continue;
            var memberCitizenId = ValidateAndCleanCitizenId(member.CitizenId, $"Số CCCD/Định danh của thành viên '{fullName}'");
            cleanedCoResidents.Add(new CoResidentDto(
                FullName: fullName,
                DateOfBirth: member.DateOfBirth,
                Gender: CleanOptional(member.Gender, 20, "Giới tính thành viên"),
                CitizenId: memberCitizenId,
                Relation: CleanOptional(member.Relation, 100, "Mối quan hệ")));
        }
        return cleanedCoResidents.Count == 0
            ? null
            : JsonSerializer.Serialize(cleanedCoResidents, CoResidentJsonOptions);
    }

    internal static void ApplyOccupancyContractFields(
        Occupancy occupancy,
        string? tenantCitizenId,
        DateOnly? tenantDateOfBirth,
        string? tenantGender,
        DateOnly? tenantCitizenIdIssuedDate,
        string? tenantCitizenIdIssuedPlace,
        string? tenantPermanentAddress,
        string? tenantEmail,
        DateOnly? contractEndDate,
        int? paymentDueDay,
        decimal? roomAreaSqm,
        string? handoverAssets,
        string? additionalTerms,
        string? householdHeadRelation,
        bool preserveExistingWhenNull)
    {
        if (tenantCitizenId is not null || !preserveExistingWhenNull)
            occupancy.TenantCitizenId = ValidateAndCleanCitizenId(tenantCitizenId, "Số CCCD/Định danh của người thuê");
        if (tenantDateOfBirth is not null || !preserveExistingWhenNull)
            occupancy.TenantDateOfBirth = tenantDateOfBirth;
        if (tenantGender is not null || !preserveExistingWhenNull)
            occupancy.TenantGender = CleanOptional(tenantGender, 20, "Giới tính");
        if (tenantCitizenIdIssuedDate is not null || !preserveExistingWhenNull)
            occupancy.TenantCitizenIdIssuedDate = tenantCitizenIdIssuedDate;
        if (tenantCitizenIdIssuedPlace is not null || !preserveExistingWhenNull)
            occupancy.TenantCitizenIdIssuedPlace = CleanOptional(tenantCitizenIdIssuedPlace, 200, "Nơi cấp CCCD của người thuê");
        if (tenantPermanentAddress is not null || !preserveExistingWhenNull)
            occupancy.TenantPermanentAddress = CleanOptional(tenantPermanentAddress, 500, "Địa chỉ thường trú của người thuê");
        if (tenantEmail is not null || !preserveExistingWhenNull)
            occupancy.TenantEmail = CleanOptional(tenantEmail, 320, "Email người thuê");

        if (contractEndDate is DateOnly endDate)
        {
            if (endDate < occupancy.StartDate)
                throw new ApiException(StatusCodes.Status400BadRequest, "Ngày kết thúc hợp đồng không được nhỏ hơn ngày bắt đầu thuê.");
            occupancy.ContractEndDate = endDate;
        }
        else if (!preserveExistingWhenNull)
        {
            occupancy.ContractEndDate = null;
        }

        if (paymentDueDay is int dueDay)
        {
            if (dueDay is < 1 or > 28)
                throw new ApiException(StatusCodes.Status400BadRequest, "Ngày hạn thanh toán hàng tháng phải từ ngày 1 đến ngày 28.");
            occupancy.PaymentDueDay = dueDay;
        }

        if (roomAreaSqm is decimal area)
        {
            if (area is < 0 or > 10_000)
                throw new ApiException(StatusCodes.Status400BadRequest, "Diện tích phòng phải từ 0 đến 10.000 m².");
            occupancy.RoomAreaSqm = area == 0 ? null : decimal.Round(area, 2);
        }
        else if (!preserveExistingWhenNull)
        {
            occupancy.RoomAreaSqm = null;
        }

        if (handoverAssets is not null || !preserveExistingWhenNull)
            occupancy.HandoverAssets = CleanOptional(handoverAssets, 2000, "Danh mục tài sản bàn giao");
        if (additionalTerms is not null || !preserveExistingWhenNull)
            occupancy.AdditionalTerms = CleanOptional(additionalTerms, 2000, "Điều khoản bổ sung");
        if (householdHeadRelation is not null || !preserveExistingWhenNull)
            occupancy.HouseholdHeadRelation = CleanOptional(householdHeadRelation, 100, "Mối quan hệ với chủ hộ") ?? "Người thuê trọ";
    }

    internal static string NormalizeIdentityTargetKey(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return "tenant";
        var trimmed = target.Trim();
        if (trimmed.Length > 40)
            throw new ApiException(StatusCodes.Status400BadRequest, "Đối tượng giấy tờ tùy thân tối đa 40 ký tự.");
        return string.Equals(trimmed, "tenant", StringComparison.OrdinalIgnoreCase) ? "tenant" : trimmed;
    }

    internal static string? CleanOptional(string? value, int maxLength, string fieldLabel)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
            throw new ApiException(StatusCodes.Status400BadRequest, $"{fieldLabel} tối đa {maxLength} ký tự.");
        return trimmed;
    }

    internal static string SanitizeFileSegment(string value)
    {
        var lower = value.Trim().ToLowerInvariant();
        var cleaned = Regex.Replace(lower, "[^a-z0-9_-]+", "-").Trim('-');
        return string.IsNullOrEmpty(cleaned) ? "room" : cleaned;
    }
}
