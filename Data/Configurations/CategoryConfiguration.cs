using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MonthExpenses.Api.Models;

namespace MonthExpenses.Api.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(c => c.Type)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(c => c.IconName)
            .HasMaxLength(50);

        builder.Property(c => c.Color)
            .HasMaxLength(20);

        builder.HasIndex(c => new { c.Name, c.Type, c.CreatedBy });
        builder.HasIndex(c => c.IsDeleted);

        builder.HasMany(c => c.Subcategories)
            .WithOne(s => s.Category)
            .HasForeignKey(s => s.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
