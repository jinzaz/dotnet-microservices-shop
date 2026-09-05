using MediatR;
using Microsoft.EntityFrameworkCore;
using MSA.PaymentService.Domain;
using MSA.Repositories.Persistence;
namespace MSA.PaymentService.Infrastructure;

public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> o, IMediator m) : MessagingDbContext(o, m) { public DbSet<Payment> Payments => Set<Payment>(); protected override void OnModelCreating(ModelBuilder b) { base.OnModelCreating(b); b.Entity<Payment>(e => { e.ToTable("payments"); e.HasKey(x => x.Id); e.HasIndex(x => x.OrderId).IsUnique(); e.HasIndex(x => x.MerchantTradeNo).IsUnique(); e.HasIndex(x => x.ProviderTransactionId); e.HasIndex(x => new { x.Status, x.ExpiresOnUtc }); e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.Currency).HasMaxLength(3); e.Property(x => x.Version).IsConcurrencyToken(); }); } }
