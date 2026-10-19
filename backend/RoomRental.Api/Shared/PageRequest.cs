namespace RoomRental.Api.Services;
public sealed record PageRequest(int Number, int Size)
{
    public int Offset => (Number - 1) * Size;
    public static PageRequest Parse(int? page, int? size)
    {
        if (page is < 1 or > 100000 || size is < 1 or > 100) throw new ApiException(400, "Invalid page or pageSize (maximum100).");
        return new(page ?? 1, size ?? (page is null ? 100 : 10));
    }
}
