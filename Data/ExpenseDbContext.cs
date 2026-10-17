using Microsoft.EntityFrameworkCore;
using MonthExpenses.Api.Models;
using MonthExpenses.Api.Services;

namespace MonthExpenses.Api.Data;

public class ExpenseDbContext : DbContext
{
    public ExpenseDbContext(DbContextOptions<ExpenseDbContext> options) : base(options)
    {
    }

    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<MonthlyBudget> Budgets => Set<MonthlyBudget>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Transaction indexes & query filters
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasIndex(t => t.Date);
            entity.HasIndex(t => t.Type);
            entity.HasIndex(t => t.CategoryId);
            entity.HasIndex(t => t.CreatedBy);
            entity.HasIndex(t => t.IsDeleted);

            entity.HasOne(t => t.Category)
                  .WithMany()
                  .HasForeignKey(t => t.CategoryId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // Budget indexes
        modelBuilder.Entity<MonthlyBudget>(entity =>
        {
            entity.HasIndex(b => new { b.MonthYear, b.CreatedBy });
            entity.HasIndex(b => b.IsDeleted);
        });

        // User indexes
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.MobileNumber).IsUnique();
            entity.HasIndex(u => u.IsDeleted);
        });

        // RefreshToken indexes
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasIndex(r => r.Token).IsUnique();
            entity.HasIndex(r => r.UserId);
            entity.HasIndex(r => r.IsDeleted);
        });

        // Category indexes
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasIndex(c => new { c.Name, c.Type, c.CreatedBy });
            entity.HasIndex(c => c.IsDeleted);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<BaseBo>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedDate == default)
                {
                    entry.Entity.CreatedDate = DateTime.UtcNow;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedDate = DateTime.UtcNow;
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }

    public async Task SeedSampleDataAsync()
    {
        // 1. Ensure Default Admin User exists (Username: Admin, Mobile: 9999999999, Password: 123456)
        var adminUser = await Users.FirstOrDefaultAsync(u => u.FullName.ToLower() == "admin" || u.MobileNumber == "9999999999" || u.MobileNumber == "admin");
        if (adminUser == null)
        {
            adminUser = new User
            {
                FullName = "Admin",
                MobileNumber = "9999999999",
                PasswordHash = PasswordHelper.HashPassword("123456"),
                CreatedBy = 1,
                CreatedDate = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow,
                IsDeleted = 0
            };
            await Users.AddAsync(adminUser);
            await SaveChangesAsync();

            if (adminUser.CreatedBy == 0)
            {
                adminUser.CreatedBy = adminUser.Id;
                await SaveChangesAsync();
            }
        }
        else
        {
            adminUser.PasswordHash = PasswordHelper.HashPassword("123456");
            await SaveChangesAsync();
        }

        // 2. Ensure default Categories exist
        if (!await Categories.AnyAsync())
        {
            var defaultCategories = new List<Category>
            {
                // Expense Categories
                new() { Name = "Food", Type = "expense", IconName = "Utensils", Color = "#f97316", IsDefault = true, CreatedBy = 1, CreatedDate = DateTime.UtcNow, IsDeleted = 0 },
                new() { Name = "Transport", Type = "expense", IconName = "Car", Color = "#3b82f6", IsDefault = true, CreatedBy = 1, CreatedDate = DateTime.UtcNow, IsDeleted = 0 },
                new() { Name = "Rent", Type = "expense", IconName = "Home", Color = "#8b5cf6", IsDefault = true, CreatedBy = 1, CreatedDate = DateTime.UtcNow, IsDeleted = 0 },
                new() { Name = "Bills", Type = "expense", IconName = "Receipt", Color = "#eab308", IsDefault = true, CreatedBy = 1, CreatedDate = DateTime.UtcNow, IsDeleted = 0 },
                new() { Name = "Shopping", Type = "expense", IconName = "ShoppingBag", Color = "#ec4899", IsDefault = true, CreatedBy = 1, CreatedDate = DateTime.UtcNow, IsDeleted = 0 },
                new() { Name = "Health", Type = "expense", IconName = "HeartPulse", Color = "#ef4444", IsDefault = true, CreatedBy = 1, CreatedDate = DateTime.UtcNow, IsDeleted = 0 },
                new() { Name = "Entertainment", Type = "expense", IconName = "Film", Color = "#06b6d4", IsDefault = true, CreatedBy = 1, CreatedDate = DateTime.UtcNow, IsDeleted = 0 },
                new() { Name = "Other", Type = "expense", IconName = "Sparkles", Color = "#64748b", IsDefault = true, CreatedBy = 1, CreatedDate = DateTime.UtcNow, IsDeleted = 0 },

                // Income Categories
                new() { Name = "Salary", Type = "income", IconName = "Banknote", Color = "#10b981", IsDefault = true, CreatedBy = 1, CreatedDate = DateTime.UtcNow, IsDeleted = 0 },
                new() { Name = "Freelance", Type = "income", IconName = "Laptop", Color = "#0ea5e9", IsDefault = true, CreatedBy = 1, CreatedDate = DateTime.UtcNow, IsDeleted = 0 },
                new() { Name = "Investment", Type = "income", IconName = "TrendingUp", Color = "#6366f1", IsDefault = true, CreatedBy = 1, CreatedDate = DateTime.UtcNow, IsDeleted = 0 },
                new() { Name = "Other", Type = "income", IconName = "Wallet", Color = "#14b8a6", IsDefault = true, CreatedBy = 1, CreatedDate = DateTime.UtcNow, IsDeleted = 0 }
            };

            await Categories.AddRangeAsync(defaultCategories);
            await SaveChangesAsync();
        }
    }
}
