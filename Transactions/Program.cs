using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;

var optionsBuilder = new DbContextOptionsBuilder<BankContext>()
    .UseMongoDB("mongodb://localhost:27017/?directConnection=true", "banking");

SetupData();

ImplicitTransaction();
await ExplicitTransaction();
await ExplicitTransactionWithMultipleCollections();

void SetupData()
{
    using var db = new BankContext(optionsBuilder.Options);
    db.Database.EnsureCreated();
    db.Accounts.Add(new Account { Number = "12345", Balance = 100m });
    db.Accounts.Add(new Account { Number = "67890", Balance = 350m });
    db.SaveChanges();   
}

void ImplicitTransaction()
{
    using var db = new BankContext(optionsBuilder.Options);
    db.Database.EnsureCreated();

    // Find accounts
    var from = db.Accounts.First(a => a.Number == "12345");
    var to = db.Accounts.First(a => a.Number == "67890");

    // Transfer money
    from.Balance -= 100m;
    to.Balance += 100m;

    // Automatically uses an implicit transaction because we're affecting multiple documents
    db.SaveChanges();
}

async Task ExplicitTransaction()
{
    await using var db = new BankContext(optionsBuilder.Options);
    await using var transaction = await db.Database.BeginTransactionAsync();

    try
    {
        var source = await db.Accounts.FirstAsync(a => a.Number == "12345");
        var target = await db.Accounts.FirstAsync(a => a.Number == "67890");

        // Check if sufficient funds
        if (source.Balance < 100m)
            throw new InvalidOperationException("Insufficient funds");

        source.Balance -= 100m;
        await db.SaveChangesAsync();
        Console.WriteLine($"Withdrawing from {source.Number}");

        target.Balance += 100m;
        await db.SaveChangesAsync();
        Console.WriteLine($"Depositing to {target.Number}");

        await transaction.CommitAsync();
        Console.WriteLine("Transfer completed successfully");
    }
    catch (Exception ex)
    {
        // Something went wrong, rollback
        await transaction.RollbackAsync();
        Console.WriteLine($"Transfer failed: {ex.Message}");
        throw;
    }
}


async Task ExplicitTransactionWithMultipleCollections()
{
    await using var db = new BankContext(optionsBuilder.Options);
    await using var transaction = db.Database.BeginTransaction();

    try
    {
        var account = db.Accounts.First(a => a.Number == "12345");
        account.Balance += 500m;
        db.SaveChanges();

        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = DateTime.UtcNow,
            Action = "Deposit",
            Details = $"Added 500 to account {account.Number}"
        });
        db.SaveChanges();

        transaction.Commit();
    }
    catch
    {
        transaction.Rollback();
        throw;
    }
}

public class Account
{
    public ObjectId Id { get; set; }
    public string Number { get; set; }
    public decimal Balance { get; set; }
}

public class AuditLog
{
    public ObjectId Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Action { get; set; }
    public string Details { get; set; }
}

public class BankContext : DbContext
{
    public DbSet<Account> Accounts { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }

    public BankContext(DbContextOptions<BankContext> options) : base(options)
    {
    }
}