using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
using RoomRental.Api.Services;

namespace RoomRental.Api.Endpoints;

public static partial class ExpenseEndpoints
{
    private const long MaxUploadRequestBytes = ExpenseLimits.MaxAttachmentBytes + 512 * 1024;

    private static void MapAttachmentRoutes(RouteGroupBuilder routes)
    {
        routes.MapGet("/attachments/usage", async (HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var used = await db.ExpenseAttachments.AsNoTracking().Where(row => row.OwnerId == ownerId)
                .SumAsync(row => (long?)row.SizeBytes, ct) ?? 0;
            return Results.Ok(new ExpenseAttachmentUsageDto(used, ExpenseLimits.MaxAttachmentBytesPerOwner,
                ExpenseLimits.MaxAttachmentBytes, ExpenseLimits.MaxAttachmentsPerExpense));
        });

        routes.MapGet("/{id:guid}/attachments", async (Guid id, HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await FindActiveExpenseAsync(db, ownerId, id, ct);
            var rows = await db.ExpenseAttachments.AsNoTracking().Where(row => row.ExpenseId == id && row.OwnerId == ownerId)
                .OrderBy(row => row.CreatedAt).ThenBy(row => row.Id)
                .Select(row => new ExpenseAttachmentDto(row.Id, row.ExpenseId, row.FileName, row.ContentType, row.SizeBytes, row.CreatedAt))
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        // Body is read manually (no IFormFile binding) so the size cap applies before buffering and the endpoint does not
        // require antiforgery middleware; CSRF protection comes from the same-origin guard for authenticated writes.
        routes.MapPost("/{id:guid}/attachments", async (Guid id, HttpContext context, AppDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var now = clock.GetUtcNow();
            await ExpenseSupport.EnsureProAsync(db, ownerId, now, ct, "Đính kèm chứng từ dành cho gói Pro. Vui lòng nâng cấp để sử dụng.");
            if (context.Request.ContentLength is long declared && declared > MaxUploadRequestBytes)
                throw new ApiException(StatusCodes.Status413PayloadTooLarge, "Tệp chứng từ tối đa 2 MB.");
            var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = MaxUploadRequestBytes;
            if (!context.Request.HasFormContentType)
                throw new ApiException(StatusCodes.Status400BadRequest, "Gửi tệp chứng từ bằng multipart/form-data.");

            IFormCollection form;
            try
            {
                form = await context.Request.ReadFormAsync(ct);
            }
            catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                throw new ApiException(StatusCodes.Status413PayloadTooLarge, "Tệp chứng từ tối đa 2 MB.");
            }
            catch (InvalidDataException)
            {
                throw new ApiException(StatusCodes.Status400BadRequest, "Không đọc được tệp chứng từ.");
            }
            if (form.Files.Count != 1)
                throw new ApiException(StatusCodes.Status400BadRequest, "Chọn đúng một tệp chứng từ.");
            var file = form.Files[0];
            if (file.Length <= 0)
                throw new ApiException(StatusCodes.Status400BadRequest, "Tệp chứng từ trống.");
            if (file.Length > ExpenseLimits.MaxAttachmentBytes)
                throw new ApiException(StatusCodes.Status413PayloadTooLarge, "Tệp chứng từ tối đa 2 MB.");

            byte[] content;
            await using (var stream = file.OpenReadStream())
            using (var buffer = new MemoryStream((int)file.Length))
            {
                await stream.CopyToAsync(buffer, ct);
                content = buffer.ToArray();
            }
            var detected = DetectFileType(content)
                ?? throw new ApiException(StatusCodes.Status400BadRequest, "Chỉ nhận ảnh JPEG, PNG, WebP hoặc tệp PDF.");
            var attachment = new ExpenseAttachment
            {
                OwnerId = ownerId, ExpenseId = id, FileName = SanitizeFileName(file.FileName, detected.Extension),
                ContentType = detected.ContentType, SizeBytes = content.LongLength,
                Sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), Content = content, CreatedAt = now
            };

            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            await FindActiveExpenseAsync(db, ownerId, id, ct);
            var count = await db.ExpenseAttachments.CountAsync(row => row.ExpenseId == id && row.OwnerId == ownerId, ct);
            if (count >= ExpenseLimits.MaxAttachmentsPerExpense)
                throw new ApiException(StatusCodes.Status409Conflict, "Mỗi khoản chi chỉ đính kèm tối đa 5 tệp.");
            var used = await db.ExpenseAttachments.Where(row => row.OwnerId == ownerId).SumAsync(row => (long?)row.SizeBytes, ct) ?? 0;
            if (used + attachment.SizeBytes > ExpenseLimits.MaxAttachmentBytesPerOwner)
                throw new ApiException(StatusCodes.Status409Conflict, "Đã dùng hết dung lượng chứng từ (200 MB). Xóa bớt tệp cũ để tải thêm.");
            db.ExpenseAttachments.Add(attachment);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.Created($"/api/expenses/{id}/attachments/{attachment.Id}",
                new ExpenseAttachmentDto(attachment.Id, id, attachment.FileName, attachment.ContentType, attachment.SizeBytes, attachment.CreatedAt));
        });

        routes.MapGet("/{id:guid}/attachments/{attachmentId:guid}", async (Guid id, Guid attachmentId, bool? download,
            HttpContext context, AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            var row = await (from attachment in db.ExpenseAttachments.AsNoTracking()
                             join expense in db.Expenses.AsNoTracking() on attachment.ExpenseId equals expense.Id
                             where attachment.Id == attachmentId && attachment.ExpenseId == id && attachment.OwnerId == ownerId &&
                                   expense.OwnerId == ownerId && expense.DeletedAt == null
                             select attachment).SingleOrDefaultAsync(ct)
                ?? throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy tệp chứng từ.");
            var inline = download != true && row.ContentType.StartsWith("image/", StringComparison.Ordinal);
            var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
            disposition.SetHttpFileName(row.FileName);
            context.Response.Headers.ContentDisposition = disposition.ToString();
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self'; style-src 'unsafe-inline'; sandbox";
            return Results.Bytes(row.Content, row.ContentType);
        });

        routes.MapDelete("/{id:guid}/attachments/{attachmentId:guid}", async (Guid id, Guid attachmentId, HttpContext context,
            AppDbContext db, CancellationToken ct) =>
        {
            var ownerId = ApiSupport.OwnerId(context);
            await FindActiveExpenseAsync(db, ownerId, id, ct);
            var deleted = await db.ExpenseAttachments
                .Where(row => row.Id == attachmentId && row.ExpenseId == id && row.OwnerId == ownerId).ExecuteDeleteAsync(ct);
            if (deleted == 0) throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy tệp chứng từ.");
            return Results.Ok(new { deleted = true, id = attachmentId });
        });
    }

    private static async Task FindActiveExpenseAsync(AppDbContext db, Guid ownerId, Guid id, CancellationToken ct)
    {
        if (!await db.Expenses.AsNoTracking().AnyAsync(row => row.Id == id && row.OwnerId == ownerId && row.DeletedAt == null, ct))
            throw new ApiException(StatusCodes.Status404NotFound, "Không tìm thấy khoản chi.");
    }

    /// <summary>Accepts only a small allowlist identified by magic bytes; client content types and extensions are ignored.</summary>
    internal static (string ContentType, string Extension)? DetectFileType(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF) return ("image/jpeg", ".jpg");
        if (content.Length >= 8 && content[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ("image/png", ".png");
        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content.Slice(8, 4).SequenceEqual("WEBP"u8)) return ("image/webp", ".webp");
        if (content.Length >= 5 && content[..5].SequenceEqual("%PDF-"u8)) return ("application/pdf", ".pdf");
        return null;
    }

    internal static string SanitizeFileName(string? original, string extension)
    {
        var baseName = Path.GetFileNameWithoutExtension(Path.GetFileName((original ?? "").Replace('\\', '/')));
        var builder = new StringBuilder(baseName.Length);
        foreach (var character in baseName.Normalize(NormalizationForm.FormC))
        {
            if (char.IsLetterOrDigit(character) || character is ' ' or '-' or '_' or '(' or ')' or '.') builder.Append(character);
            else if (!char.IsControl(character)) builder.Append('_');
        }
        var cleaned = builder.ToString().Trim().Trim('.');
        if (cleaned.Length == 0) cleaned = "chung-tu";
        var maxBase = 120 - extension.Length;
        if (cleaned.Length > maxBase) cleaned = cleaned[..maxBase].TrimEnd();
        return cleaned + extension;
    }
}
