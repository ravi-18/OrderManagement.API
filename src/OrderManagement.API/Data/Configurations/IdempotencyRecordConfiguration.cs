using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderManagement.API.Domain;

namespace OrderManagement.API.Data.Configurations;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");
        builder.HasKey(r => r.Key);

        builder.Property(r => r.Key)
            .HasMaxLength(256);

        builder.Property(r => r.RequestHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(r => r.ResponseBody)
            .IsRequired()
            .HasMaxLength(8000);

        builder.Property(r => r.ResponseStatus).IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();
    }
}
