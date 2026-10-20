using Microsoft.EntityFrameworkCore;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;
namespace RoomRental.Api.Services;

public static class PaymentReservations
{
    // Includes legacy single-invoice orders and every member of a new batch.
    public static IQueryable<Invoice> Pending(AppDbContext db) => db.Invoices.AsNoTracking().Where(i =>
        db.PayOsAttempts.Any(a => a.OwnerId == i.OwnerId && a.Status == PaymentAttemptStatus.Pending &&
            (a.InvoiceId == i.Id || a.Id == i.PaymentReservationId)));
}
