using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RoomRental.Api.Data;
using RoomRental.Api.Domain;

namespace RoomRental.Api.Services;

public sealed class PaymentReconciliationHostedService(IServiceScopeFactory scopeFactory, ILogger<PaymentReconciliationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var tracker = scope.ServiceProvider.GetService<RoomRental.Api.Features.Monitoring.IWorkerStateTracker>();
                if (tracker != null) await tracker.RecordWorkerHeartbeatAsync("reconcile", isEnabled: true, status: "Healthy", errorMessage: null, stoppingToken);

                var payOsService = scope.ServiceProvider.GetRequiredService<PayOsService>();
                await payOsService.ReconcilePendingBatchAsync(stoppingToken);

                if (tracker != null) await tracker.RecordWorkerHeartbeatAsync("reconcile", isEnabled: true, status: "Healthy", errorMessage: null, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogWarning("The scheduled payOS reconciliation pass failed.");
                try
                {
                    await using var errScope = scopeFactory.CreateAsyncScope();
                    var tracker = errScope.ServiceProvider.GetService<RoomRental.Api.Features.Monitoring.IWorkerStateTracker>();
                    if (tracker != null) await tracker.RecordWorkerHeartbeatAsync("reconcile", isEnabled: true, status: "Failed", errorMessage: ex.Message, stoppingToken);
                }
                catch { }
            }

            try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}

internal sealed record VerifiedPayOsPayment(Guid AttemptId, long OrderCode, decimal Amount, string Reference);
