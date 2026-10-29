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
    public DbSet<Subcategory> Subcategories => Set<Subcategory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ExpenseDbContext).Assembly);
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

        // 3. Ensure default Subcategories exist
        if (!await Subcategories.AnyAsync())
        {
            var allCategories = await Categories.Where(c => c.IsDefault && c.IsDeleted == 0).ToListAsync();
            var subcatMap = new Dictionary<string, string[]>
            {
                ["Food"] = ["Groceries", "Restaurant", "Coffee", "Snacks", "Delivery", "Fruits & Veggies"],
                ["Transport"] = ["Fuel", "Car", "Bus", "Train", "Taxi", "Metro", "Bike", "Flight"],
                ["Rent"] = ["Apartment", "House", "Office", "Maintenance"],
                ["Bills"] = ["Electricity", "Water", "Internet", "Mobile", "Gas"],
                ["Shopping"] = ["Clothes", "Electronics", "Footwear", "Home", "Gifts"],
                ["Health"] = ["Medicine", "Doctor", "Gym", "Lab Tests", "Insurance"],
                ["Entertainment"] = ["Movies", "Streaming", "Gaming", "Events", "Outing"],
                ["Other"] = ["General", "Miscellaneous"],
                ["Salary"] = ["Monthly Salary", "Bonus", "Overtime"],
                ["Freelance"] = ["Project", "Client", "Consulting"],
                ["Investment"] = ["Stocks", "Mutual Funds", "Crypto", "Real Estate", "Dividends"]
            };

            var subcategoriesToSeed = new List<Subcategory>();
            foreach (var cat in allCategories)
            {
                if (subcatMap.TryGetValue(cat.Name, out var names))
                {
                    foreach (var subName in names)
                    {
                        subcategoriesToSeed.Add(new Subcategory
                        {
                            Name = subName,
                            CategoryId = cat.Id,
                            IsDefault = true,
                            CreatedBy = 1,
                            CreatedDate = DateTime.UtcNow,
                            IsDeleted = 0
                        });
                    }
                }
            }

            if (subcategoriesToSeed.Count > 0)
            {
                await Subcategories.AddRangeAsync(subcategoriesToSeed);
                await SaveChangesAsync();
            }
        }

    }
}
