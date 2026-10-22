using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MonthExpenses.Api.Models;

namespace MonthExpenses.Api.Data.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Type)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(t => t.Amount)
            .IsRequired()
            .HasColumnType("decimal(18,2)");

        builder.Property(t => t.Date)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(t => t.Note)
            .HasMaxLength(200);

        builder.Property(t => t.PaymentMethod)
            .IsRequired()
            .HasMaxLength(30);

        builder.Property(t => t.Subcategory)
            .HasMaxLength(50);

        builder.HasIndex(t => t.Date);
        builder.HasIndex(t => t.Type);
        builder.HasIndex(t => t.CategoryId);
        builder.HasIndex(t => t.SubcategoryId);
        builder.HasIndex(t => t.CreatedBy);
        builder.HasIndex(t => t.IsDeleted);

        builder.HasOne(t => t.Category)
            .WithMany()
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.SubcategoryEntity)
            .WithMany()
            .HasForeignKey(t => t.SubcategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
