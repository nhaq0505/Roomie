using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

public static class ContractWordGenerator
{
    private const string DefaultFont = "Times New Roman";

    public static byte[] GenerateRentalContractDocx(OccupancyDocumentDto data, DateOnly issuedDate)
    {
        using var stream = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            body.AppendChild(CreateParagraph("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM", bold: true, fontSizeHalfPt: 26, align: JustificationValues.Center, afterTwips: 40));
            body.AppendChild(CreateParagraph("Độc lập - Tự do - Hạnh phúc", bold: true, underline: true, fontSizeHalfPt: 26, align: JustificationValues.Center, afterTwips: 200));
            body.AppendChild(CreateParagraph("HỢP ĐỒNG THUÊ PHÒNG TRỌ", bold: true, fontSizeHalfPt: 30, align: JustificationValues.Center, afterTwips: 120));

            var locationAddress = NullIfBlank(data.BuildingAddress) ?? data.BuildingName;
            body.AppendChild(CreateParagraph(
                $"Hôm nay, ngày {issuedDate.Day:00} tháng {issuedDate.Month:00} năm {issuedDate.Year}, tại địa chỉ: {locationAddress}",
                italic: true, align: JustificationValues.Left, afterTwips: 120));
            body.AppendChild(CreateParagraph("Chúng tôi gồm các bên dưới đây:", afterTwips: 100));

            // Bên A
            body.AppendChild(CreateParagraph("1. BÊN CHO THUÊ PHÒNG TRỌ (gọi tắt là Bên A):", bold: true, afterTwips: 60));
            body.AppendChild(CreateBulletOrLine($"- Ông/Bà: {data.OwnerName}"));
            body.AppendChild(CreateBulletOrLine($"- Ngày sinh: {FormatDateOrDots(data.OwnerDateOfBirth)}    Số CCCD/Định danh cá nhân: {NullIfBlank(data.OwnerCitizenId) ?? ".............................."}"));
            body.AppendChild(CreateBulletOrLine($"- Ngày cấp: {FormatDateOrDots(data.OwnerCitizenIdIssuedDate)}    Nơi cấp: {NullIfBlank(data.OwnerCitizenIdIssuedPlace) ?? "...................................................."}"));
            body.AppendChild(CreateBulletOrLine($"- Địa chỉ thường trú: {NullIfBlank(data.OwnerPermanentAddress) ?? NullIfBlank(data.BuildingAddress) ?? "................................................................................"}"));
            body.AppendChild(CreateBulletOrLine($"- Số điện thoại liên hệ: {NullIfBlank(data.OwnerPhone) ?? "................................................"}"));

            // Bên B
            body.AppendChild(CreateParagraph("2. BÊN THUÊ PHÒNG TRỌ (gọi tắt là Bên B):", bold: true, beforeTwips: 100, afterTwips: 60));
            body.AppendChild(CreateBulletOrLine($"- Ông/Bà: {data.TenantName}"));
            body.AppendChild(CreateBulletOrLine($"- Ngày sinh: {FormatDateOrDots(data.TenantDateOfBirth)}    Giới tính: {NullIfBlank(data.TenantGender) ?? ".........."}    Số CCCD/Định danh: {NullIfBlank(data.TenantCitizenId) ?? "........................"}"));
            body.AppendChild(CreateBulletOrLine($"- Ngày cấp: {FormatDateOrDots(data.TenantCitizenIdIssuedDate)}    Nơi cấp: {NullIfBlank(data.TenantCitizenIdIssuedPlace) ?? "...................................................."}"));
            body.AppendChild(CreateBulletOrLine($"- Địa chỉ thường trú: {NullIfBlank(data.TenantPermanentAddress) ?? "................................................................................"}"));
            body.AppendChild(CreateBulletOrLine($"- Số điện thoại liên hệ: {NullIfBlank(data.TenantPhone) ?? "................................................"}    Số người ở: {data.People} người"));

            if (data.CoResidents.Count > 0)
            {
                var names = string.Join("; ", data.CoResidents.Select(c =>
                    string.IsNullOrWhiteSpace(c.CitizenId) ? c.FullName : $"{c.FullName} (CCCD: {c.CitizenId})"));
                body.AppendChild(CreateBulletOrLine($"- Thành viên ở cùng: {names}"));
            }

            body.AppendChild(CreateParagraph(
                "Sau khi bàn bạc trên tinh thần tự nguyện, hai bên thống nhất ký kết Hợp đồng thuê phòng trọ với các điều khoản sau đây:",
                italic: true, beforeTwips: 100, afterTwips: 120));

            // Điều 1
            body.AppendChild(CreateParagraph("ĐIỀU 1: ĐỐI TƯỢNG VÀ THỜI HẠN THUÊ PHÒNG", bold: true, afterTwips: 60));
            var fullRoomAddress = string.IsNullOrWhiteSpace(data.BuildingAddress)
                ? $"{data.RoomName} - {data.BuildingName}"
                : $"{data.RoomName}, {data.BuildingName}, địa chỉ: {data.BuildingAddress}";
            body.AppendChild(CreateBulletOrLine($"1.1. Bên A đồng ý cho Bên B thuê phòng trọ: {fullRoomAddress}."));
            if (data.RoomAreaSqm is decimal area && area > 0)
                body.AppendChild(CreateBulletOrLine($"1.2. Diện tích sử dụng: {area:0.##} m². Mục đích thuê: Để ở sinh hoạt hợp pháp, không sử dụng vào mục đích vi phạm pháp luật."));
            else
                body.AppendChild(CreateBulletOrLine("1.2. Mục đích thuê: Để ở sinh hoạt hợp pháp, không sử dụng để chứa hàng cấm hoặc kinh doanh trái pháp luật."));

            var endDateText = data.ContractEndDate is DateOnly endDate
                ? $"đến hết ngày {endDate:dd/MM/yyyy}"
                : "theo từng tháng (hoặc đến khi hai bên có thỏa thuận chấm dứt hợp đồng)";
            body.AppendChild(CreateBulletOrLine($"1.3. Thời hạn thuê: Bắt đầu tính từ ngày {data.StartDate:dd/MM/yyyy} {endDateText}."));

            // Điều 2
            body.AppendChild(CreateParagraph("ĐIỀU 2: GIÁ THUÊ, CHI PHÍ DỊCH VỤ VÀ PHƯƠNG THỨC THANH TOÁN", bold: true, beforeTwips: 100, afterTwips: 60));
            body.AppendChild(CreateBulletOrLine($"2.1. Giá thuê phòng: {FormatVnd(data.Rent)} đồng/tháng (Bằng chữ: {data.RentInWords}). Tiền thuê phòng được tính tròn theo tháng."));
            body.AppendChild(CreateBulletOrLine($"2.2. Tiền điện: {FormatVnd(data.ElectricityRate)} đồng/kWh theo chỉ số công tơ riêng của phòng. Chỉ số công tơ điện bàn giao lúc nhận phòng: {data.InitialReading:0.###} kWh."));
            var waterUnit = data.WaterPerPerson ? "đồng/người/tháng" : "đồng/phòng/tháng";
            body.AppendChild(CreateBulletOrLine($"2.3. Tiền nước sinh hoạt: {FormatVnd(data.WaterFee)} {waterUnit}."));
            body.AppendChild(CreateBulletOrLine($"2.4. Phí dịch vụ chung (rác, vệ sinh, wifi/an ninh...): {FormatVnd(data.ServiceFee)} đồng/tháng."));
            var dueDay = Math.Clamp(data.PaymentDueDay, 1, 28);
            body.AppendChild(CreateBulletOrLine($"2.5. Kỳ hạn thanh toán: Thanh toán định kỳ 01 tháng/lần, từ ngày 01 đến chậm nhất ngày {dueDay:00} hàng tháng."));
            if (!string.IsNullOrWhiteSpace(data.BankAccountNumber))
            {
                var bankLabel = NullIfBlank(data.BankShortName) ?? NullIfBlank(data.BankName) ?? NullIfBlank(data.BankBin) ?? "Ngân hàng";
                body.AppendChild(CreateBulletOrLine(
                    $"2.6. Phương thức thanh toán: Tiền mặt hoặc chuyển khoản qua tài khoản của Bên A — Số TK: {data.BankAccountNumber} ({bankLabel}) - Chủ TK: {data.BankAccountName ?? data.OwnerName}."));
            }
            else
            {
                body.AppendChild(CreateBulletOrLine("2.6. Phương thức thanh toán: Tiền mặt hoặc chuyển khoản theo thông báo hóa đơn hàng tháng của Bên A."));
            }

            // Điều 3
            body.AppendChild(CreateParagraph("ĐIỀU 3: TIỀN ĐẶT CỌC BẢO ĐẢM THỰC HIỆN HỢP ĐỒNG", bold: true, beforeTwips: 100, afterTwips: 60));
            body.AppendChild(CreateBulletOrLine($"3.1. Bên B đặt cọc cho Bên A số tiền: {FormatVnd(data.DepositRequired)} đồng (Bằng chữ: {data.DepositInWords})."));
            body.AppendChild(CreateBulletOrLine("3.2. Khoản tiền đặt cọc dùng để bảo đảm việc thực hiện hợp đồng, không được tự ý khấu trừ vào tiền thuê hàng tháng khi đang ở."));
            body.AppendChild(CreateBulletOrLine("3.3. Khi chấm dứt hợp đồng đúng thời hạn và đã thanh toán đầy đủ tiền thuê, điện, nước, phí dịch vụ cũng như bàn giao nguyên trạng tài sản phòng trọ, Bên A hoàn trả lại tiền cọc còn dư cho Bên B."));

            // Điều 4
            body.AppendChild(CreateParagraph("ĐIỀU 4: TÀI SẢN BÀN GIAO VÀ THỎA THUẬN BỔ SUNG", bold: true, beforeTwips: 100, afterTwips: 60));
            var assets = NullIfBlank(data.HandoverAssets)
                ?? "Phòng trọ và hệ thống điện, nước, cửa khóa, thiết bị vệ sinh chiếu sáng trong tình trạng hoạt động bình thường.";
            body.AppendChild(CreateBulletOrLine($"4.1. Trang thiết bị, tài sản bàn giao khi nhận phòng: {assets}"));
            body.AppendChild(CreateBulletOrLine("4.2. Bên B có trách nhiệm kiểm tra và bảo quản tài sản trong suốt quá trình sử dụng; nếu làm hư hỏng, mất mát do lỗi chủ quan thì phải sửa chữa hoặc bồi thường theo thực tế."));
            if (!string.IsNullOrWhiteSpace(data.AdditionalTerms))
            {
                body.AppendChild(CreateBulletOrLine($"4.3. Thỏa thuận bổ sung: {data.AdditionalTerms.Trim()}"));
            }

            // Điều 5
            body.AppendChild(CreateParagraph("ĐIỀU 5: QUYỀN VÀ NGHĨA VỤ CỦA CÁC BÊN", bold: true, beforeTwips: 100, afterTwips: 60));
            body.AppendChild(CreateBulletOrLine("5.1. Bên A: Bàn giao phòng đúng hẹn; đảm bảo quyền sử dụng ổn định, an toàn cho Bên B; phối hợp và xác nhận thủ tục đăng ký tạm trú theo quy định pháp luật."));
            body.AppendChild(CreateBulletOrLine("5.2. Bên B: Thanh toán tiền thuê và các khoản phí đúng hạn; cung cấp đầy đủ thông tin CCCD để khai báo tạm trú (Mẫu CT01); giữ gìn vệ sinh, an ninh trật tự, phòng cháy chữa cháy; không tự ý cải tạo phòng hoặc cho người khác thuê lại khi chưa có sự đồng ý của Bên A."));

            // Điều 6
            body.AppendChild(CreateParagraph("ĐIỀU 6: CHẤM DỨT HỢP ĐỒNG VÀ CAM KẾT CHUNG", bold: true, beforeTwips: 100, afterTwips: 60));
            body.AppendChild(CreateBulletOrLine("6.1. Trường hợp một trong hai bên muốn chấm dứt hợp đồng trước thời hạn thì phải thông báo trước cho bên kia ít nhất 30 ngày."));
            body.AppendChild(CreateBulletOrLine("6.2. Hợp đồng này được lập thành 02 (hai) bản có giá trị pháp lý như nhau, mỗi bên giữ 01 (một) bản và có hiệu lực kể từ ngày ký."));

            // Signature table
            body.AppendChild(CreateContractSignatureTable(data.OwnerName, data.TenantName));
            body.AppendChild(CreateA4SectionProperties());
            mainPart.Document.Save();
        }
        return stream.ToArray();
    }

    public static byte[] GenerateCt01Docx(OccupancyDocumentDto data, DateOnly issuedDate)
    {
        using var stream = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            body.AppendChild(CreateParagraph("Mẫu CT01 ban hành kèm theo Thông tư số 66/2023/TT-BCA ngày 16/11/2023 của Bộ trưởng Bộ Công an",
                italic: true, fontSizeHalfPt: 20, align: JustificationValues.Right, afterTwips: 60));
            body.AppendChild(CreateParagraph("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM", bold: true, fontSizeHalfPt: 26, align: JustificationValues.Center, afterTwips: 40));
            body.AppendChild(CreateParagraph("Độc lập - Tự do - Hạnh phúc", bold: true, underline: true, fontSizeHalfPt: 26, align: JustificationValues.Center, afterTwips: 160));
            body.AppendChild(CreateParagraph("TỜ KHAI THAY ĐỔI THÔNG TIN CƯ TRÚ", bold: true, fontSizeHalfPt: 28, align: JustificationValues.Center, afterTwips: 100));

            var policeStation = NullIfBlank(data.PoliceStationName) ?? "Công an ........................................................................................";
            body.AppendChild(CreateParagraph($"Kính gửi(1): {policeStation}", bold: false, align: JustificationValues.Center, afterTwips: 140));

            body.AppendChild(CreateBulletOrLine($"1. Họ, chữ đệm và tên: {data.TenantName.ToUpperInvariant()}"));
            body.AppendChild(CreateBulletOrLine($"2. Ngày, tháng, năm sinh: {FormatDateOrDots(data.TenantDateOfBirth)}        3. Giới tính: {NullIfBlank(data.TenantGender) ?? "............"}"));
            body.AppendChild(CreateBulletOrLine("4. Số định danh cá nhân:"));
            body.AppendChild(CreateCitizenIdBoxesTable(data.TenantCitizenId));

            body.AppendChild(CreateBulletOrLine($"5. Số điện thoại liên hệ: {NullIfBlank(data.TenantPhone) ?? "...................................."}        6. Email: {NullIfBlank(data.TenantEmail) ?? "...................................."}"));
            body.AppendChild(CreateBulletOrLine($"7. Họ, chữ đệm và tên chủ hộ: {data.OwnerName.ToUpperInvariant()}"));
            body.AppendChild(CreateBulletOrLine($"8. Mối quan hệ với chủ hộ: {NullIfBlank(data.HouseholdHeadRelation) ?? "Người thuê trọ"}"));
            body.AppendChild(CreateBulletOrLine("9. Số định danh cá nhân của chủ hộ:"));
            body.AppendChild(CreateCitizenIdBoxesTable(data.OwnerCitizenId));

            var residenceAddress = string.IsNullOrWhiteSpace(data.BuildingAddress)
                ? $"{data.RoomName}, {data.BuildingName}"
                : $"{data.RoomName}, {data.BuildingName}, {data.BuildingAddress}";
            body.AppendChild(CreateBulletOrLine($"10. Nội dung đề nghị(2): Đăng ký tạm trú tại địa chỉ: {residenceAddress}."));
            body.AppendChild(CreateBulletOrLine("11. Những thành viên trong hộ gia đình cùng thay đổi:"));
            body.AppendChild(CreateCoResidentsTable(data.CoResidents));

            body.AppendChild(CreateCt01SignatureTable(issuedDate, data.OwnerName, data.TenantName));
            body.AppendChild(CreateParagraph(
                "Chú thích: (1) Cơ quan đăng ký cư trú; (2) Ghi rõ nội dung đề nghị (ví dụ: đăng ký thường trú; đăng ký tạm trú; tách hộ; xác nhận thông tin về cư trú...).",
                italic: true, fontSizeHalfPt: 20, beforeTwips: 120, afterTwips: 40));

            body.AppendChild(CreateA4SectionProperties());
            mainPart.Document.Save();
        }
        return stream.ToArray();
    }

    private static Paragraph CreateParagraph(
        string text,
        bool bold = false,
        bool italic = false,
        bool underline = false,
        int fontSizeHalfPt = 25,
        JustificationValues? align = null,
        int beforeTwips = 0,
        int afterTwips = 80)
    {
        var runProps = new RunProperties(
            new RunFonts { Ascii = DefaultFont, HighAnsi = DefaultFont, ComplexScript = DefaultFont },
            new FontSize { Val = fontSizeHalfPt.ToString(CultureInfo.InvariantCulture) },
            new FontSizeComplexScript { Val = fontSizeHalfPt.ToString(CultureInfo.InvariantCulture) });
        if (bold) runProps.AppendChild(new Bold());
        if (italic) runProps.AppendChild(new Italic());
        if (underline) runProps.AppendChild(new Underline { Val = UnderlineValues.Single });

        var run = new Run(runProps, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        var paraProps = new ParagraphProperties(
            new SpacingBetweenLines { Before = beforeTwips.ToString(CultureInfo.InvariantCulture), After = afterTwips.ToString(CultureInfo.InvariantCulture), Line = "276", LineRule = LineSpacingRuleValues.Auto });
        if (align.HasValue)
            paraProps.AppendChild(new Justification { Val = align.Value });

        return new Paragraph(paraProps, run);
    }

    private static Paragraph CreateBulletOrLine(string text) =>
        CreateParagraph(text, fontSizeHalfPt: 25, align: JustificationValues.Both, beforeTwips: 20, afterTwips: 60);

    private static Table CreateCitizenIdBoxesTable(string? citizenId)
    {
        var digits = (citizenId ?? "").Trim();
        var table = new Table(new TableProperties(
            new TableStyle { Val = "TableGrid" },
            new TableJustification { Val = TableRowAlignmentValues.Left },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 6 },
                new BottomBorder { Val = BorderValues.Single, Size = 6 },
                new LeftBorder { Val = BorderValues.Single, Size = 6 },
                new RightBorder { Val = BorderValues.Single, Size = 6 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 6 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 6 })));

        var row = new TableRow();
        for (var i = 0; i < 12; i++)
        {
            var ch = i < digits.Length ? digits[i].ToString() : " ";
            var cell = new TableCell(
                new TableCellProperties(
                    new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "480" },
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }),
                CreateParagraph(ch, bold: true, fontSizeHalfPt: 25, align: JustificationValues.Center, beforeTwips: 40, afterTwips: 40));
            row.AppendChild(cell);
        }
        table.AppendChild(row);
        return table;
    }

    private static Table CreateCoResidentsTable(IReadOnlyList<CoResidentDto> coResidents)
    {
        var table = new Table(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 6 },
                new BottomBorder { Val = BorderValues.Single, Size = 6 },
                new LeftBorder { Val = BorderValues.Single, Size = 6 },
                new RightBorder { Val = BorderValues.Single, Size = 6 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 6 },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 6 })));

        string[] headers = ["TT", "Họ, chữ đệm và tên", "Ngày, tháng, năm sinh", "Giới tính", "Số định danh cá nhân", "Mối quan hệ với chủ hộ"];
        var headerRow = new TableRow();
        foreach (var header in headers)
        {
            headerRow.AppendChild(new TableCell(
                CreateParagraph(header, bold: true, fontSizeHalfPt: 22, align: JustificationValues.Center, beforeTwips: 40, afterTwips: 40)));
        }
        table.AppendChild(headerRow);

        var rowsCount = Math.Max(3, coResidents.Count);
        for (var i = 0; i < rowsCount; i++)
        {
            var item = i < coResidents.Count ? coResidents[i] : null;
            var row = new TableRow();
            string[] values =
            [
                (i + 1).ToString(CultureInfo.InvariantCulture),
                item?.FullName ?? "",
                item?.DateOfBirth is DateOnly dob ? dob.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "",
                item?.Gender ?? "",
                item?.CitizenId ?? "",
                item?.Relation ?? ""
            ];
            for (var c = 0; c < values.Length; c++)
            {
                var align = c is 0 or 2 or 3 or 4 ? JustificationValues.Center : JustificationValues.Left;
                row.AppendChild(new TableCell(
                    CreateParagraph(values[c], fontSizeHalfPt: 22, align: align, beforeTwips: 40, afterTwips: 40)));
            }
            table.AppendChild(row);
        }
        return table;
    }

    private static Table CreateContractSignatureTable(string ownerName, string tenantName)
    {
        var table = new Table(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.None },
                new BottomBorder { Val = BorderValues.None },
                new LeftBorder { Val = BorderValues.None },
                new RightBorder { Val = BorderValues.None },
                new InsideHorizontalBorder { Val = BorderValues.None },
                new InsideVerticalBorder { Val = BorderValues.None })));

        var row = new TableRow();
        var leftCell = new TableCell(
            new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "2500" }),
            CreateParagraph("ĐẠI DIỆN BÊN CHO THUÊ (BÊN A)", bold: true, align: JustificationValues.Center, beforeTwips: 160, afterTwips: 20),
            CreateParagraph("(Ký và ghi rõ họ tên)", italic: true, fontSizeHalfPt: 22, align: JustificationValues.Center, afterTwips: 900),
            CreateParagraph(ownerName, bold: true, align: JustificationValues.Center));

        var rightCell = new TableCell(
            new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "2500" }),
            CreateParagraph("ĐẠI DIỆN BÊN THUÊ PHÒNG (BÊN B)", bold: true, align: JustificationValues.Center, beforeTwips: 160, afterTwips: 20),
            CreateParagraph("(Ký và ghi rõ họ tên)", italic: true, fontSizeHalfPt: 22, align: JustificationValues.Center, afterTwips: 900),
            CreateParagraph(tenantName, bold: true, align: JustificationValues.Center));

        row.Append(leftCell, rightCell);
        table.AppendChild(row);
        return table;
    }

    private static Table CreateCt01SignatureTable(DateOnly issuedDate, string ownerName, string tenantName)
    {
        var dateLine = $"..., ngày {issuedDate.Day:00} tháng {issuedDate.Month:00} năm {issuedDate.Year}";
        var table = new Table(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.None },
                new BottomBorder { Val = BorderValues.None },
                new LeftBorder { Val = BorderValues.None },
                new RightBorder { Val = BorderValues.None },
                new InsideHorizontalBorder { Val = BorderValues.None },
                new InsideVerticalBorder { Val = BorderValues.None })));

        var row = new TableRow();
        var col1 = new TableCell(
            new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "1666" }),
            CreateParagraph(dateLine, italic: true, fontSizeHalfPt: 20, align: JustificationValues.Center, beforeTwips: 140, afterTwips: 20),
            CreateParagraph("Ý KIẾN CỦA CHỦ HỘ", bold: true, fontSizeHalfPt: 22, align: JustificationValues.Center, afterTwips: 20),
            CreateParagraph("(Ghi rõ nội dung, ký và ghi rõ họ tên)", italic: true, fontSizeHalfPt: 19, align: JustificationValues.Center, afterTwips: 700),
            CreateParagraph(ownerName, bold: true, fontSizeHalfPt: 22, align: JustificationValues.Center));

        var col2 = new TableCell(
            new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "1667" }),
            CreateParagraph(dateLine, italic: true, fontSizeHalfPt: 20, align: JustificationValues.Center, beforeTwips: 140, afterTwips: 20),
            CreateParagraph("Ý KIẾN CỦA CHỦ SỞ HỮU CHỖ Ở HỢP PHÁP", bold: true, fontSizeHalfPt: 22, align: JustificationValues.Center, afterTwips: 20),
            CreateParagraph("(Ký và ghi rõ họ tên)", italic: true, fontSizeHalfPt: 19, align: JustificationValues.Center, afterTwips: 700),
            CreateParagraph(ownerName, bold: true, fontSizeHalfPt: 22, align: JustificationValues.Center));

        var col3 = new TableCell(
            new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "1667" }),
            CreateParagraph(dateLine, italic: true, fontSizeHalfPt: 20, align: JustificationValues.Center, beforeTwips: 140, afterTwips: 20),
            CreateParagraph("NGƯỜI KÊ KHAI", bold: true, fontSizeHalfPt: 22, align: JustificationValues.Center, afterTwips: 20),
            CreateParagraph("(Ký và ghi rõ họ tên)", italic: true, fontSizeHalfPt: 19, align: JustificationValues.Center, afterTwips: 700),
            CreateParagraph(tenantName, bold: true, fontSizeHalfPt: 22, align: JustificationValues.Center));

        row.Append(col1, col2, col3);
        table.AppendChild(row);
        return table;
    }

    private static SectionProperties CreateA4SectionProperties() =>
        new(
            new PageSize { Width = 11906U, Height = 16838U },
            new PageMargin { Top = 1134, Right = 1134, Bottom = 1134, Left = 1417, Header = 720U, Footer = 720U, Gutter = 0U });

    private static string FormatVnd(decimal amount) =>
        Math.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("#,0", CultureInfo.GetCultureInfo("vi-VN"));

    private static string FormatDateOrDots(DateOnly? date) =>
        date is DateOnly value ? value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "....../....../............";

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
