using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Http;
using RoomRental.Api.Domain;
using RoomRental.Api.Endpoints;
using RoomRental.Api.Services;

internal static class ContractDocumentsAndMoneyTests
{
    public static void Run()
    {
        TestVietnameseMoneyReaderEdgeCases();
        TestCitizenIdAndCoResidentsValidation();
        TestApplyOccupancyContractFieldsBoundaries();
        TestValidateAndApplyDocumentUpdatesAndSanitizeFileSegment();
        TestContractAndCt01WordGeneratorOpenXml();
    }

    private static void TestVietnameseMoneyReaderEdgeCases()
    {
        (decimal Amount, string Expected)[] cases =
        [
            (0m, "Không đồng"),
            (-0.01m, "Không đồng"),
            (-5_000_000m, "Không đồng"),
            (0.49m, "Không đồng"),
            (0.5m, "Một đồng chẵn"),
            (999.5m, "Một nghìn đồng chẵn"),
            (1_500.4m, "Một nghìn năm trăm đồng chẵn"),
            (1_500.5m, "Một nghìn năm trăm lẻ một đồng chẵn"),
            (1m, "Một đồng chẵn"),
            (4m, "Bốn đồng chẵn"),
            (5m, "Năm đồng chẵn"),
            (10m, "Mười đồng chẵn"),
            (11m, "Mười một đồng chẵn"),
            (14m, "Mười bốn đồng chẵn"),
            (15m, "Mười lăm đồng chẵn"),
            (19m, "Mười chín đồng chẵn"),
            (20m, "Hai mươi đồng chẵn"),
            (21m, "Hai mươi mốt đồng chẵn"),
            (24m, "Hai mươi tư đồng chẵn"),
            (25m, "Hai mươi lăm đồng chẵn"),
            (41m, "Bốn mươi mốt đồng chẵn"),
            (44m, "Bốn mươi tư đồng chẵn"),
            (55m, "Năm mươi lăm đồng chẵn"),
            (100m, "Một trăm đồng chẵn"),
            (101m, "Một trăm lẻ một đồng chẵn"),
            (104m, "Một trăm lẻ bốn đồng chẵn"),
            (105m, "Một trăm lẻ năm đồng chẵn"),
            (110m, "Một trăm mười đồng chẵn"),
            (111m, "Một trăm mười một đồng chẵn"),
            (114m, "Một trăm mười bốn đồng chẵn"),
            (115m, "Một trăm mười lăm đồng chẵn"),
            (121m, "Một trăm hai mươi mốt đồng chẵn"),
            (124m, "Một trăm hai mươi tư đồng chẵn"),
            (125m, "Một trăm hai mươi lăm đồng chẵn"),
            (1_001m, "Một nghìn không trăm lẻ một đồng chẵn"),
            (1_010m, "Một nghìn không trăm mười đồng chẵn"),
            (1_015m, "Một nghìn không trăm mười lăm đồng chẵn"),
            (1_021m, "Một nghìn không trăm hai mươi mốt đồng chẵn"),
            (1_024m, "Một nghìn không trăm hai mươi tư đồng chẵn"),
            (1_025m, "Một nghìn không trăm hai mươi lăm đồng chẵn"),
            (1_000_000m, "Một triệu đồng chẵn"),
            (1_000_001m, "Một triệu không trăm lẻ một đồng chẵn"),
            (1_005_000m, "Một triệu không trăm lẻ năm nghìn đồng chẵn"),
            (3_500_000m, "Ba triệu năm trăm nghìn đồng chẵn"),
            (4_250_000m, "Bốn triệu hai trăm năm mươi nghìn đồng chẵn"),
            (1_000_000_000m, "Một tỷ đồng chẵn"),
            (1_000_050_000m, "Một tỷ không trăm năm mươi nghìn đồng chẵn"),
            (1_000_000_000_000m, "Một nghìn tỷ đồng chẵn"),
            (1_000_000_000_000_000m, "Một triệu tỷ đồng chẵn")
        ];

        foreach (var (amount, expected) in cases)
        {
            var actual = VietnameseMoneyReader.ToVietnameseWords(amount);
            Assert(actual == expected, $"VietnameseMoneyReader({amount}) => '{actual}' (expected '{expected}')");
        }
    }

    private static void TestCitizenIdAndCoResidentsValidation()
    {
        Assert(OwnerManagementEndpoints.ValidateAndCleanCitizenId(null, "CCCD") is null, "null CitizenId returns null");
        Assert(OwnerManagementEndpoints.ValidateAndCleanCitizenId("", "CCCD") is null, "empty CitizenId returns null");
        Assert(OwnerManagementEndpoints.ValidateAndCleanCitizenId("   ", "CCCD") is null, "whitespace CitizenId returns null");
        Assert(OwnerManagementEndpoints.ValidateAndCleanCitizenId("012345678", "CCCD") == "012345678", "9-digit CMND accepted");
        Assert(OwnerManagementEndpoints.ValidateAndCleanCitizenId("0123456789", "CCCD") == "0123456789", "10-digit ID accepted");
        Assert(OwnerManagementEndpoints.ValidateAndCleanCitizenId("01234567890", "CCCD") == "01234567890", "11-digit ID accepted");
        Assert(OwnerManagementEndpoints.ValidateAndCleanCitizenId("  079098001234  ", "CCCD") == "079098001234", "12-digit CCCD trimmed and accepted");

        foreach (var invalidId in new[] { "12345678", "0123456789012", "07909800123A", "079 098 0012", "079-098-0012", new string('1', 21) })
        {
            ExpectBadRequest(() => OwnerManagementEndpoints.ValidateAndCleanCitizenId(invalidId, "CCCD"),
                $"CitizenId rejects invalid input '{invalidId}'");
        }

        // Co-residents validation & serialization
        var emptyJson = OwnerManagementEndpoints.ValidateAndSerializeCoResidents([], out var emptyList);
        Assert(emptyJson is null && emptyList.Count == 0, "empty co-residents serializes to null");

        var blankNamesJson = OwnerManagementEndpoints.ValidateAndSerializeCoResidents(
            [new CoResidentDto("   ", new DateOnly(2000, 1, 1), "Nam", "079099001234", "Em")],
            out var skippedList);
        Assert(blankNamesJson is null && skippedList.Count == 0, "co-residents with blank FullName are skipped and serialize to null");

        var validInput = new List<CoResidentDto>
        {
            new("   ", null, null, null, null),
            new("  Trần Thị B  ", new DateOnly(2001, 5, 20), "  Nữ ", " 079201005566 ", " Vợ "),
            new("Lê Văn C", null, null, "012345678", null)
        };
        var serialized = OwnerManagementEndpoints.ValidateAndSerializeCoResidents(validInput, out var cleaned);
        Assert(serialized is not null && cleaned.Count == 2, "valid co-residents skips blank and keeps 2 members");
        Assert(cleaned[0].FullName == "Trần Thị B" && cleaned[0].Gender == "Nữ" && cleaned[0].CitizenId == "079201005566" && cleaned[0].Relation == "Vợ",
            "co-resident fields are trimmed");

        var deserialized = OwnerManagementEndpoints.DeserializeCoResidents(serialized);
        Assert(deserialized.Count == 2 && deserialized[0].FullName == "Trần Thị B" && deserialized[1].CitizenId == "012345678",
            "DeserializeCoResidents round-trips serialized JSON");

        // Malformed / null JSON fallbacks
        Assert(OwnerManagementEndpoints.DeserializeCoResidents(null).Count == 0, "DeserializeCoResidents(null) returns empty");
        Assert(OwnerManagementEndpoints.DeserializeCoResidents("   ").Count == 0, "DeserializeCoResidents(whitespace) returns empty");
        Assert(OwnerManagementEndpoints.DeserializeCoResidents("{not-valid-json").Count == 0, "DeserializeCoResidents(malformed) returns empty safely");
        Assert(OwnerManagementEndpoints.DeserializeCoResidents("null").Count == 0, "DeserializeCoResidents('null') returns empty");

        // Boundary: 29 co-residents allowed, 30 rejected
        var twentyNine = Enumerable.Range(1, 29)
            .Select(i => new CoResidentDto($"Member {i}", null, "Nam", null, "Bạn cùng phòng"))
            .ToList();
        var twentyNineJson = OwnerManagementEndpoints.ValidateAndSerializeCoResidents(twentyNine, out var twentyNineCleaned);
        Assert(twentyNineJson is not null && twentyNineCleaned.Count == 29, "29 co-residents allowed at upper boundary");

        var thirty = Enumerable.Range(1, 30)
            .Select(i => new CoResidentDto($"Member {i}", null, "Nam", null, "Bạn cùng phòng"))
            .ToList();
        ExpectBadRequest(() => OwnerManagementEndpoints.ValidateAndSerializeCoResidents(thirty, out _),
            "30 co-residents (> 29) rejected with 400 BadRequest");

        // Invalid co-resident CitizenId or overlong fields
        ExpectBadRequest(() => OwnerManagementEndpoints.ValidateAndSerializeCoResidents(
            [new CoResidentDto("Nguyễn Văn D", null, null, "12345", null)], out _),
            "co-resident with 5-digit CitizenId rejected");
        ExpectBadRequest(() => OwnerManagementEndpoints.ValidateAndSerializeCoResidents(
            [new CoResidentDto(new string('A', 161), null, null, null, null)], out _),
            "co-resident FullName > 160 chars rejected");
        ExpectBadRequest(() => OwnerManagementEndpoints.ValidateAndSerializeCoResidents(
            [new CoResidentDto("Nguyễn Văn D", null, new string('G', 21), null, null)], out _),
            "co-resident Gender > 20 chars rejected");
        ExpectBadRequest(() => OwnerManagementEndpoints.ValidateAndSerializeCoResidents(
            [new CoResidentDto("Nguyễn Văn D", null, null, null, new string('R', 101))], out _),
            "co-resident Relation > 100 chars rejected");
    }

    private static void TestApplyOccupancyContractFieldsBoundaries()
    {
        var occ = new Occupancy
        {
            RoomId = Guid.NewGuid(),
            TenantName = "Khách Thuê",
            StartDate = new DateOnly(2026, 10, 15)
        };

        // Valid full application
        OwnerManagementEndpoints.ApplyOccupancyContractFields(
            occ,
            tenantCitizenId: " 079099001122 ",
            tenantDateOfBirth: new DateOnly(1999, 1, 2),
            tenantGender: " Nam ",
            tenantCitizenIdIssuedDate: new DateOnly(2022, 3, 4),
            tenantCitizenIdIssuedPlace: " Cục CS QLHC ",
            tenantPermanentAddress: " 123 Lê Lợi ",
            tenantEmail: " tenant@example.com ",
            contractEndDate: new DateOnly(2026, 10, 15), // Equal to StartDate is allowed
            paymentDueDay: 1, // Lower boundary = 1
            roomAreaSqm: 24.567m, // Rounded to 24.57
            handoverAssets: " 01 điều hòa ",
            additionalTerms: " Giữ vệ sinh ",
            householdHeadRelation: " Em họ ",
            preserveExistingWhenNull: false);

        Assert(occ.TenantCitizenId == "079099001122" && occ.TenantGender == "Nam" && occ.TenantEmail == "tenant@example.com",
            "ApplyOccupancyContractFields trims and assigns identity fields");
        Assert(occ.ContractEndDate == new DateOnly(2026, 10, 15) && occ.PaymentDueDay == 1 && occ.RoomAreaSqm == 24.57m,
            "ApplyOccupancyContractFields accepts boundary StartDate == EndDate, DueDay = 1, and rounds RoomAreaSqm to 2 decimals");
        Assert(occ.HouseholdHeadRelation == "Em họ", "ApplyOccupancyContractFields assigns custom HouseholdHeadRelation");

        // preserveExistingWhenNull = true keeps existing values when null is passed
        OwnerManagementEndpoints.ApplyOccupancyContractFields(
            occ,
            tenantCitizenId: null,
            tenantDateOfBirth: null,
            tenantGender: null,
            tenantCitizenIdIssuedDate: null,
            tenantCitizenIdIssuedPlace: null,
            tenantPermanentAddress: null,
            tenantEmail: null,
            contractEndDate: null,
            paymentDueDay: 28, // Upper boundary = 28
            roomAreaSqm: null,
            handoverAssets: null,
            additionalTerms: null,
            householdHeadRelation: null,
            preserveExistingWhenNull: true);

        Assert(occ.TenantCitizenId == "079099001122" && occ.ContractEndDate == new DateOnly(2026, 10, 15) &&
               occ.RoomAreaSqm == 24.57m && occ.HandoverAssets == "01 điều hòa" &&
               occ.AdditionalTerms == "Giữ vệ sinh" && occ.HouseholdHeadRelation == "Em họ" && occ.PaymentDueDay == 28,
            "preserveExistingWhenNull = true preserves existing fields while updating non-null dueDay = 28");

        // roomAreaSqm = 0 normalizes to null; roomAreaSqm = 10_000 allowed
        OwnerManagementEndpoints.ApplyOccupancyContractFields(
            occ, null, null, null, null, null, null, null, null, null,
            roomAreaSqm: 0m, null, null, null, preserveExistingWhenNull: true);
        Assert(occ.RoomAreaSqm is null, "roomAreaSqm = 0 is normalized to null");

        OwnerManagementEndpoints.ApplyOccupancyContractFields(
            occ, null, null, null, null, null, null, null, null, null,
            roomAreaSqm: 10_000m, null, null, null, preserveExistingWhenNull: true);
        Assert(occ.RoomAreaSqm == 10_000m, "roomAreaSqm = 10,000 upper boundary accepted");

        // preserveExistingWhenNull = false clears optional fields and resets HouseholdHeadRelation to default
        OwnerManagementEndpoints.ApplyOccupancyContractFields(
            occ, null, null, null, null, null, null, null, null, null, null, null, null, null,
            preserveExistingWhenNull: false);
        Assert(occ.TenantCitizenId is null && occ.ContractEndDate is null && occ.RoomAreaSqm is null &&
               occ.HandoverAssets is null && occ.AdditionalTerms is null && occ.HouseholdHeadRelation == "Người thuê trọ",
            "preserveExistingWhenNull = false clears optional fields and defaults HouseholdHeadRelation to 'Người thuê trọ'");

        // Invalid boundary cases
        ExpectBadRequest(() => OwnerManagementEndpoints.ApplyOccupancyContractFields(
            occ, null, null, null, null, null, null, null,
            contractEndDate: new DateOnly(2026, 10, 14), null, null, null, null, null, false),
            "contractEndDate < StartDate rejected with 400");

        foreach (var badDueDay in new[] { 0, -5, 29, 31 })
        {
            ExpectBadRequest(() => OwnerManagementEndpoints.ApplyOccupancyContractFields(
                occ, null, null, null, null, null, null, null, null,
                paymentDueDay: badDueDay, null, null, null, null, false),
                $"paymentDueDay = {badDueDay} rejected with 400");
        }

        foreach (var badArea in new[] { -0.01m, -10m, 10_000.01m })
        {
            ExpectBadRequest(() => OwnerManagementEndpoints.ApplyOccupancyContractFields(
                occ, null, null, null, null, null, null, null, null, null,
                roomAreaSqm: badArea, null, null, null, false),
                $"roomAreaSqm = {badArea} rejected with 400");
        }

        ExpectBadRequest(() => OwnerManagementEndpoints.ApplyOccupancyContractFields(
            occ, null, null, tenantGender: new string('X', 21), null, null, null, null, null, null, null, null, null, null, false),
            "tenantGender > 20 chars rejected");
        ExpectBadRequest(() => OwnerManagementEndpoints.ApplyOccupancyContractFields(
            occ, null, null, null, null, tenantCitizenIdIssuedPlace: new string('X', 201), null, null, null, null, null, null, null, null, false),
            "tenantCitizenIdIssuedPlace > 200 chars rejected");
        ExpectBadRequest(() => OwnerManagementEndpoints.ApplyOccupancyContractFields(
            occ, null, null, null, null, null, tenantPermanentAddress: new string('X', 501), null, null, null, null, null, null, null, false),
            "tenantPermanentAddress > 500 chars rejected");
        ExpectBadRequest(() => OwnerManagementEndpoints.ApplyOccupancyContractFields(
            occ, null, null, null, null, null, null, tenantEmail: new string('X', 321), null, null, null, null, null, null, false),
            "tenantEmail > 320 chars rejected");
        ExpectBadRequest(() => OwnerManagementEndpoints.ApplyOccupancyContractFields(
            occ, null, null, null, null, null, null, null, null, null, null, handoverAssets: new string('X', 2001), null, null, false),
            "handoverAssets > 2000 chars rejected");
        ExpectBadRequest(() => OwnerManagementEndpoints.ApplyOccupancyContractFields(
            occ, null, null, null, null, null, null, null, null, null, null, null, additionalTerms: new string('X', 2001), null, false),
            "additionalTerms > 2000 chars rejected");
        ExpectBadRequest(() => OwnerManagementEndpoints.ApplyOccupancyContractFields(
            occ, null, null, null, null, null, null, null, null, null, null, null, null, householdHeadRelation: new string('X', 101), false),
            "householdHeadRelation > 100 chars rejected");
    }

    private static void TestValidateAndApplyDocumentUpdatesAndSanitizeFileSegment()
    {
        var owner = new AppUser
        {
            Id = Guid.NewGuid(),
            Name = "Nguyễn Chủ Trọ",
            Email = "owner@example.com",
            PasswordHash = "hash",
            Role = UserRole.Owner,
            Phone = "0901112222"
        };
        var building = new Building
        {
            Id = Guid.NewGuid(),
            OwnerId = owner.Id,
            Name = "Dãy Trọ A",
            Address = "100 Lê Lợi"
        };
        var occ = new Occupancy
        {
            Id = Guid.NewGuid(),
            RoomId = Guid.NewGuid(),
            TenantName = "Khách Cũ",
            Phone = "0912345678",
            StartDate = new DateOnly(2026, 10, 10),
            PaymentDueDay = 5
        };

        var req = new UpdateOccupancyDocumentRequest(
            OccupancyId: occ.Id,
            OwnerName: " Nguyễn Chủ Trọ Mới ",
            OwnerPhone: " 0909888777 ",
            OwnerCitizenId: " 079088001122 ",
            OwnerDateOfBirth: new DateOnly(1985, 5, 15),
            OwnerCitizenIdIssuedDate: new DateOnly(2021, 6, 1),
            OwnerCitizenIdIssuedPlace: " Cục CS QLHC ",
            OwnerPermanentAddress: " 12 Nguyễn Huệ, Quận 1 ",
            BuildingAddress: " 200 Trần Hưng Đạo, Quận 1 ",
            PoliceStationName: " Công an Phường Cầu Kho ",
            TenantName: " Trần Văn Mới ",
            TenantPhone: " 0987654321 ",
            TenantEmail: " tranvanmoi@example.com ",
            TenantCitizenId: " 079099003344 ",
            TenantDateOfBirth: new DateOnly(1999, 9, 9),
            TenantGender: " Nam ",
            TenantCitizenIdIssuedDate: new DateOnly(2022, 1, 10),
            TenantCitizenIdIssuedPlace: " Cục CS QLHC ",
            TenantPermanentAddress: " 45 Hai Bà Trưng ",
            ContractEndDate: new DateOnly(2027, 10, 10),
            PaymentDueDay: 15,
            RoomAreaSqm: 28.456m,
            HandoverAssets: " 01 Giường, 01 Tủ ",
            AdditionalTerms: " Không nuôi thú cưng ",
            HouseholdHeadRelation: " Bạn thuê cùng ",
            CoResidents: [new CoResidentDto(" Lê Thị C ", new DateOnly(2000, 2, 2), " Nữ ", " 079200009988 ", " Vợ ")]);

        OwnerManagementEndpoints.ValidateAndApplyDocumentUpdates(req, owner, building, occ);

        Assert(owner.Name == "Nguyễn Chủ Trọ Mới" && owner.Phone == "0909888777" && owner.CitizenId == "079088001122" &&
               owner.CitizenIdIssuedPlace == "Cục CS QLHC" && owner.PermanentAddress == "12 Nguyễn Huệ, Quận 1",
            "ValidateAndApplyDocumentUpdates updates and trims owner fields including OwnerName");
        Assert(building.Address == "200 Trần Hưng Đạo, Quận 1" && building.PoliceStationName == "Công an Phường Cầu Kho",
            "ValidateAndApplyDocumentUpdates updates and trims building fields");
        Assert(occ.TenantName == "Trần Văn Mới" && occ.Phone == "0987654321" && occ.TenantCitizenId == "079099003344" &&
               occ.PaymentDueDay == 15 && occ.RoomAreaSqm == 28.46m && occ.HouseholdHeadRelation == "Bạn thuê cùng",
            "ValidateAndApplyDocumentUpdates updates, trims, and rounds occupancy fields");
        Assert(OwnerManagementEndpoints.DeserializeCoResidents(occ.CoResidentsJson).Count == 1,
            "ValidateAndApplyDocumentUpdates serializes CoResidents");

        // Null OwnerPhone / BuildingAddress / TenantPhone / CoResidents preserves existing phone/address/coResidents while clearing nullable contract fields
        var preserveReq = new UpdateOccupancyDocumentRequest(
            OccupancyId: occ.Id,
            OwnerPhone: null,
            BuildingAddress: null,
            TenantName: "   ", // Blank TenantName preserves existing TenantName
            TenantPhone: null,
            RoomAreaSqm: 0m,
            HouseholdHeadRelation: "   ",
            CoResidents: null);
        OwnerManagementEndpoints.ValidateAndApplyDocumentUpdates(preserveReq, owner, building, occ);
        Assert(owner.Phone == "0909888777" && building.Address == "200 Trần Hưng Đạo, Quận 1" &&
               occ.TenantName == "Trần Văn Mới" && occ.Phone == "0987654321" &&
               occ.RoomAreaSqm is null && occ.HouseholdHeadRelation == "Người thuê trọ" &&
               OwnerManagementEndpoints.DeserializeCoResidents(occ.CoResidentsJson).Count == 1,
            "ValidateAndApplyDocumentUpdates preserves OwnerPhone/BuildingAddress/TenantName/TenantPhone/CoResidents when null/blank and defaults HouseholdHeadRelation");

        // Validation failure paths on ValidateAndApplyDocumentUpdates
        ExpectBadRequest(() => OwnerManagementEndpoints.ValidateAndApplyDocumentUpdates(
            new UpdateOccupancyDocumentRequest(occ.Id, OwnerCitizenId: "12345"), owner, building, occ),
            "ValidateAndApplyDocumentUpdates rejects invalid OwnerCitizenId");
        ExpectBadRequest(() => OwnerManagementEndpoints.ValidateAndApplyDocumentUpdates(
            new UpdateOccupancyDocumentRequest(occ.Id, TenantCitizenId: "ABC123456"), owner, building, occ),
            "ValidateAndApplyDocumentUpdates rejects invalid TenantCitizenId");
        ExpectBadRequest(() => OwnerManagementEndpoints.ValidateAndApplyDocumentUpdates(
            new UpdateOccupancyDocumentRequest(occ.Id, OwnerName: new string('O', 161)), owner, building, occ),
            "ValidateAndApplyDocumentUpdates rejects OwnerName > 160 chars");
        ExpectBadRequest(() => OwnerManagementEndpoints.ValidateAndApplyDocumentUpdates(
            new UpdateOccupancyDocumentRequest(occ.Id, TenantName: new string('T', 161)), owner, building, occ),
            "ValidateAndApplyDocumentUpdates rejects TenantName > 160 chars");

        // ValidateAndApplyOwnerProfileUpdates (PUT /api/auth/profile)
        OwnerManagementEndpoints.ValidateAndApplyOwnerProfileUpdates(
            new UpdateProfileRequest(
                Name: "  Trần Chủ Hộ Mới  ",
                Phone: " 0988777666 ",
                CitizenId: " 079085009988 ",
                DateOfBirth: new DateOnly(1985, 8, 20),
                CitizenIdIssuedDate: new DateOnly(2022, 10, 1),
                CitizenIdIssuedPlace: " Cục Cảnh sát QLHC về TTXH ",
                PermanentAddress: " 88 Lê Lợi, Quận 1, TP.HCM "),
            owner);
        Assert(owner.Name == "Trần Chủ Hộ Mới" && owner.Phone == "0988777666" && owner.CitizenId == "079085009988" &&
               owner.DateOfBirth == new DateOnly(1985, 8, 20) && owner.PermanentAddress == "88 Lê Lợi, Quận 1, TP.HCM",
            "ValidateAndApplyOwnerProfileUpdates updates and trims owner profile fields");
        ExpectBadRequest(() => OwnerManagementEndpoints.ValidateAndApplyOwnerProfileUpdates(
            new UpdateProfileRequest("   "), owner),
            "ValidateAndApplyOwnerProfileUpdates rejects blank Name");
        ExpectBadRequest(() => OwnerManagementEndpoints.ValidateAndApplyOwnerProfileUpdates(
            new UpdateProfileRequest(new string('N', 161)), owner),
            "ValidateAndApplyOwnerProfileUpdates rejects Name > 160 chars");
        ExpectBadRequest(() => OwnerManagementEndpoints.ValidateAndApplyDocumentUpdates(
            new UpdateOccupancyDocumentRequest(occ.Id, PoliceStationName: new string('P', 251)), owner, building, occ),
            "ValidateAndApplyDocumentUpdates rejects PoliceStationName > 250 chars");
        ExpectBadRequest(() => OwnerManagementEndpoints.ValidateAndApplyDocumentUpdates(
            new UpdateOccupancyDocumentRequest(occ.Id, ContractEndDate: new DateOnly(2026, 10, 9)), owner, building, occ),
            "ValidateAndApplyDocumentUpdates rejects ContractEndDate < StartDate");
        ExpectBadRequest(() => OwnerManagementEndpoints.ValidateAndApplyDocumentUpdates(
            new UpdateOccupancyDocumentRequest(occ.Id, PaymentDueDay: 29), owner, building, occ),
            "ValidateAndApplyDocumentUpdates rejects PaymentDueDay > 28");
        ExpectBadRequest(() => OwnerManagementEndpoints.ValidateAndApplyDocumentUpdates(
            new UpdateOccupancyDocumentRequest(occ.Id, RoomAreaSqm: -1m), owner, building, occ),
            "ValidateAndApplyDocumentUpdates rejects negative RoomAreaSqm");

        // SanitizeFileSegment edge cases
        Assert(OwnerManagementEndpoints.SanitizeFileSegment("Phòng 101 Khu A") == "ph-ng-101-khu-a",
            "SanitizeFileSegment normalizes Vietnamese room name");
        Assert(OwnerManagementEndpoints.SanitizeFileSegment("Phòng 101 - Khu A") == "ph-ng-101---khu-a",
            "SanitizeFileSegment preserves existing hyphen while replacing spaces");
        Assert(OwnerManagementEndpoints.SanitizeFileSegment("  A_102-B  ") == "a_102-b",
            "SanitizeFileSegment preserves underscores and hyphens");
        Assert(OwnerManagementEndpoints.SanitizeFileSegment("   ///---***   ") == "room",
            "SanitizeFileSegment falls back to 'room' when all characters are stripped");
    }

    private static void TestContractAndCt01WordGeneratorOpenXml()
    {
        var issuedDate = new DateOnly(2026, 10, 23);

        // Case A: Minimal / null optional fields
        var minimalDto = new OccupancyDocumentDto(
            RoomId: Guid.NewGuid(),
            BuildingId: Guid.NewGuid(),
            OccupancyId: Guid.NewGuid(),
            OwnerName: "Nguyễn Chủ Trọ",
            OwnerPhone: null,
            OwnerEmail: "owner@example.com",
            OwnerCitizenId: null,
            OwnerDateOfBirth: null,
            OwnerCitizenIdIssuedDate: null,
            OwnerCitizenIdIssuedPlace: null,
            OwnerPermanentAddress: null,
            BankBin: null,
            BankName: null,
            BankShortName: null,
            BankAccountNumber: null,
            BankAccountName: null,
            BuildingName: "Dãy Trọ A",
            BuildingAddress: "",
            PoliceStationName: null,
            RoomName: "Phòng 101",
            RoomAreaSqm: null,
            Rent: 3_000_000m,
            RentInWords: VietnameseMoneyReader.ToVietnameseWords(3_000_000m),
            ElectricityRate: 3_500m,
            WaterFee: 80_000m,
            WaterPerPerson: false,
            ServiceFee: 50_000m,
            InitialReading: 120m,
            TenantName: "Trần Khách Thuê",
            TenantPhone: null,
            TenantEmail: null,
            People: 1,
            TenantCitizenId: null,
            TenantDateOfBirth: null,
            TenantGender: null,
            TenantCitizenIdIssuedDate: null,
            TenantCitizenIdIssuedPlace: null,
            TenantPermanentAddress: null,
            StartDate: new DateOnly(2026, 10, 1),
            ContractEndDate: null,
            PaymentDueDay: 35, // Clamped to 28 in generator
            DepositRequired: 0m,
            DepositInWords: VietnameseMoneyReader.ToVietnameseWords(0m),
            DepositPaid: 0m,
            HandoverAssets: null,
            AdditionalTerms: "   ",
            HouseholdHeadRelation: "Người thuê trọ",
            CoResidents: []);

        var minContractBytes = ContractWordGenerator.GenerateRentalContractDocx(minimalDto, issuedDate);
        using (var ms = new MemoryStream(minContractBytes))
        using (var doc = WordprocessingDocument.Open(ms, false))
        {
            var body = doc.MainDocumentPart!.Document.Body!;
            var fullText = body.InnerText;
            Assert(fullText.Contains("HỢP ĐỒNG THUÊ PHÒNG TRỌ"), "minimal contract contains title");
            Assert(fullText.Contains("Phòng 101 - Dãy Trọ A"), "minimal contract falls back to 'Room - Building' when BuildingAddress is empty");
            Assert(!fullText.Contains("Diện tích sử dụng:"), "minimal contract omits area when RoomAreaSqm is null");
            Assert(fullText.Contains("theo từng tháng"), "minimal contract uses monthly fallback when ContractEndDate is null");
            Assert(fullText.Contains("đồng/phòng/tháng"), "minimal contract uses per-room water unit when WaterPerPerson is false");
            Assert(fullText.Contains("chậm nhất ngày 28 hàng tháng"), "minimal contract clamps PaymentDueDay 35 to 28");
            Assert(fullText.Contains("theo thông báo hóa đơn hàng tháng của Bên A"), "minimal contract uses fallback payment text when BankAccountNumber is null");
            Assert(!fullText.Contains("4.3. Thỏa thuận bổ sung"), "minimal contract omits clause 4.3 when AdditionalTerms is blank");
            Assert(!fullText.Contains("Thành viên ở cùng:"), "minimal contract omits co-residents line when empty");

            var pageSize = body.Elements<SectionProperties>().Single().GetFirstChild<PageSize>()!;
            Assert(pageSize.Width?.Value == 11906U && pageSize.Height?.Value == 16838U, "contract uses A4 page dimensions");
        }

        var minCt01Bytes = ContractWordGenerator.GenerateCt01Docx(minimalDto, issuedDate);
        using (var ms = new MemoryStream(minCt01Bytes))
        using (var doc = WordprocessingDocument.Open(ms, false))
        {
            var body = doc.MainDocumentPart!.Document.Body!;
            var fullText = body.InnerText;
            Assert(fullText.Contains("TỜ KHAI THAY ĐỔI THÔNG TIN CƯ TRÚ"), "minimal CT01 contains title");
            Assert(fullText.Contains("TRẦN KHÁCH THUÊ") && fullText.Contains("NGUYỄN CHỦ TRỌ"), "minimal CT01 uppercases tenant and owner names");

            var tables = body.Elements<Table>().ToList();
            Assert(tables.Count == 4, "CT01 has 4 tables (tenant CCCD boxes, owner CCCD boxes, co-residents, signature)");
            var tenantBoxes = tables[0].Elements<TableRow>().Single().Elements<TableCell>().Select(c => c.InnerText).ToList();
            Assert(tenantBoxes.Count == 12 && tenantBoxes.All(c => c == " "), "null TenantCitizenId renders 12 blank boxes in CT01");

            var coResidentRows = tables[2].Elements<TableRow>().ToList();
            Assert(coResidentRows.Count == 4, "empty CoResidents renders 1 header row + 3 blank rows in CT01");
        }

        // Case B: Full optional fields + 4 co-residents + 12-digit CCCD & 9-digit CMND
        var fullDto = minimalDto with
        {
            OwnerCitizenId = "025456789", // 9-digit old CMND
            BankShortName = "Techcombank",
            BankAccountNumber = "1903456789",
            BankAccountName = "NGUYEN CHU TRO",
            BuildingAddress = "456 Nguyễn Thị Minh Khai, Quận 3",
            PoliceStationName = "Công an Phường 5, Quận 3",
            RoomAreaSqm = 25.5m,
            WaterPerPerson = true,
            TenantCitizenId = "079099001234", // 12-digit CCCD
            ContractEndDate = new DateOnly(2027, 10, 1),
            PaymentDueDay = 5,
            HandoverAssets = "01 máy lạnh, 01 tủ lạnh",
            AdditionalTerms = "Không hút thuốc trong phòng",
            CoResidents =
            [
                new CoResidentDto("Thành Viên 1", new DateOnly(2000, 1, 1), "Nữ", "079200001111", "Vợ"),
                new CoResidentDto("Thành Viên 2", new DateOnly(2002, 2, 2), "Nam", null, "Em"),
                new CoResidentDto("Thành Viên 3", new DateOnly(2003, 3, 3), "Nữ", "079203003333", "Em"),
                new CoResidentDto("Thành Viên 4", new DateOnly(2004, 4, 4), "Nam", "079204004444", "Con")
            ]
        };

        var fullContractBytes = ContractWordGenerator.GenerateRentalContractDocx(fullDto, issuedDate);
        using (var ms = new MemoryStream(fullContractBytes))
        using (var doc = WordprocessingDocument.Open(ms, false))
        {
            var fullText = doc.MainDocumentPart!.Document.Body!.InnerText;
            Assert(fullText.Contains("Diện tích sử dụng:") && fullText.Contains("m²"), "full contract includes RoomAreaSqm");
            Assert(fullText.Contains("đến hết ngày 01/10/2027"), "full contract includes ContractEndDate");
            Assert(fullText.Contains("đồng/người/tháng"), "full contract uses per-person water unit");
            Assert(fullText.Contains("1903456789 (Techcombank) - Chủ TK: NGUYEN CHU TRO"), "full contract includes bank account details");
            Assert(fullText.Contains("4.3. Thỏa thuận bổ sung: Không hút thuốc trong phòng"), "full contract includes clause 4.3");
            Assert(fullText.Contains("Thành Viên 1 (CCCD: 079200001111); Thành Viên 2; Thành Viên 3"), "full contract formats co-residents with and without CitizenId");
        }

        var fullCt01Bytes = ContractWordGenerator.GenerateCt01Docx(fullDto, issuedDate);
        using (var ms = new MemoryStream(fullCt01Bytes))
        using (var doc = WordprocessingDocument.Open(ms, false))
        {
            var tables = doc.MainDocumentPart!.Document.Body!.Elements<Table>().ToList();
            var tenantDigits = string.Concat(tables[0].Elements<TableRow>().Single().Elements<TableCell>().Select(c => c.InnerText));
            Assert(tenantDigits == "079099001234", "12-digit TenantCitizenId fills all 12 boxes in CT01");

            var ownerCells = tables[1].Elements<TableRow>().Single().Elements<TableCell>().Select(c => c.InnerText).ToList();
            Assert(string.Concat(ownerCells.Take(9)) == "025456789" && ownerCells.Skip(9).All(c => c == " "),
                "9-digit OwnerCitizenId fills first 9 boxes and leaves 3 trailing blank boxes in CT01");

            var coResidentRows = tables[2].Elements<TableRow>().ToList();
            Assert(coResidentRows.Count == 5, "4 CoResidents renders 1 header row + 4 data rows in CT01");
        }

        // Case C: XML special characters (& < > " '), formula-like strings, and maximum 29 Unicode co-residents
        var maxCoResidents = Enumerable.Range(1, 29)
            .Select(i => new CoResidentDto(
                $"Thành Viên #{i} <A&B>",
                new DateOnly(1990 + (i % 20), (i % 12) + 1, (i % 28) + 1),
                i % 2 == 0 ? "Nữ" : "Nam",
                $"07909900{i:D4}",
                $"Quan hệ \"{i}\" & 'bạn'"))
            .ToList();

        var xmlEdgeDto = fullDto with
        {
            OwnerName = "Chủ Trọ <A & B> \"C\" 'D'",
            TenantName = "Khách Thuê =SUM(1,2) & <Tag>",
            BuildingName = "Dãy Trọ \"Alpha & Beta\"",
            BuildingAddress = "12/3 Đường <Lê Lợi> & 'Nguyễn Huệ'",
            PoliceStationName = "Công an P.1 <Quận & Huyện>",
            HandoverAssets = "=CMD|' /C calc'!A0 & <01 máy lạnh> \"Sharp\"",
            AdditionalTerms = "+Không tụ tập > 23h & < 6h sáng",
            CoResidents = maxCoResidents
        };

        var xmlContractBytes = ContractWordGenerator.GenerateRentalContractDocx(xmlEdgeDto, issuedDate);
        using (var ms = new MemoryStream(xmlContractBytes))
        using (var doc = WordprocessingDocument.Open(ms, false))
        {
            var text = doc.MainDocumentPart!.Document.Body!.InnerText;
            Assert(text.Contains("Chủ Trọ <A & B> \"C\" 'D'") && text.Contains("Khách Thuê =SUM(1,2) & <Tag>"),
                "contract DOCX safely escapes XML special characters and formula-like strings in party names");
            Assert(text.Contains("Thành Viên #29 <A&B> (CCCD: 079099000029)"),
                "contract DOCX includes all 29 co-residents with XML special characters");
        }

        var xmlCt01Bytes = ContractWordGenerator.GenerateCt01Docx(xmlEdgeDto, issuedDate);
        using (var ms = new MemoryStream(xmlCt01Bytes))
        using (var doc = WordprocessingDocument.Open(ms, false))
        {
            var tables = doc.MainDocumentPart!.Document.Body!.Elements<Table>().ToList();
            var coResidentRows = tables[2].Elements<TableRow>().ToList();
            Assert(coResidentRows.Count == 30, "29 CoResidents renders 1 header row + 29 data rows in CT01");
            Assert(tables[2].InnerText.Contains("Thành Viên #29 <A&B>") && tables[2].InnerText.Contains("Quan hệ \"29\" & 'bạn'"),
                "CT01 DOCX preserves XML special characters across all 29 co-resident table rows");
        }
    }

    private static void ExpectBadRequest(Action action, string name)
    {
        try
        {
            action();
            throw new Exception($"Expected ApiException(400) but succeeded: {name}");
        }
        catch (ApiException ex) when (ex.StatusCode == StatusCodes.Status400BadRequest)
        {
            Console.WriteLine("PASS: " + name);
        }
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("Assertion failed: " + name);
        Console.WriteLine("PASS: " + name);
    }
}
