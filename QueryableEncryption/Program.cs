using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Encryption;
using MongoDB.EntityFrameworkCore;
using MongoDB.EntityFrameworkCore.Infrastructure;

// Uncomment the line below to generate a new Customer Master Key and paste into the subsequent line
// Console.WriteLine(Convert.ToBase64String(RandomNumberGenerator.GetBytes(96)));
var customerMasterKey =
    Convert.FromBase64String(
        "gT6tpjPkeEmKYra2qggwVE5YNnKMg+NjAEFeFlycZRfUB1tQr3U+JrYOHuRQi2hhO1mVMgPdMT2uQpsAmEwt7TjXqy0uZ4Kj37rXa+PCUb7DBTT/TVAITDf6IYb0Vfga");

// Storing our CMK master key in a local provider for local testing only
// In production use a Secure Key System like AWS KMS or Azure Key Vault
var kmsProviders = new Dictionary<string, IReadOnlyDictionary<string, object>>
{
    { "local", new Dictionary<string, object> { { "key", customerMasterKey } } }
};

// Setup our database connection, database name, key vault name and auto encryption
MongoClientSettings.Extensions.AddAutoEncryption();
var clientSettings = MongoClientSettings.FromConnectionString("mongodb://localhost:27017?directConnection=true");
const string databaseName = "queryable-encryption-fun";
var keyVaultNamespace = new CollectionNamespace(databaseName, "__keyVault");

// Uncomment the following block to generate data keys in the vault
// and paste the outputted GUIDs into the IsEncrypted calls below
using var clientEncryption =
    new ClientEncryption(new ClientEncryptionOptions(new MongoClient(clientSettings), keyVaultNamespace, kmsProviders));
Guid CreateDataKey() => clientEncryption.CreateDataKey("local", new DataKeyOptions(), CancellationToken.None);
Console.WriteLine(String.Join("\n", Enumerable.Range(1, 3).Select(_ => CreateDataKey())));

// Configure the MongoDB EF Core Provider with necessary encryption options
var options = new DbContextOptionsBuilder()
    .UseMongoDB(new MongoOptionsExtension()
        .WithClientSettings(clientSettings)
        .WithDatabaseName(databaseName)
        .WithKeyVaultNamespace(keyVaultNamespace)
        .WithCryptProvider(CryptProvider.Mongocryptd, "C:\\MongoDB\\mongo_crypt_v1.dll")
        .WithKmsProviders(kmsProviders));

var db = new PayrollContext(options.Options);

if (!await db.Employees.AnyAsync())
{
    // Setup initial test data
    db.Employees.AddRange(
        new Employee { Name = "Tom", TaxPayerId = "12345", Salary = 50000, Notes = "" },
        new Employee { Name = "Dick", TaxPayerId = "23456", Salary = 100000, Notes = "Very busy" },
        new Employee { Name = "Sally", TaxPayerId = "34567", Salary = 199000, Notes = "Afternoons" },
        new Employee { Name = "Harry", TaxPayerId = "45678", Salary = 200000, Notes = "Weekends" });
    await db.SaveChangesAsync();
}

// Query example with equality
var found = db.Employees.First(e => e.TaxPayerId == "12345");
Console.WriteLine(found.Name);

// Query example by range
var band2Earners = db.Employees.Where(e => e.Salary >= 100000m && e.Salary < 200000m);
foreach (var employee in band2Earners)
    Console.WriteLine(employee.Name);

// Show the generated encryption schemas
var schema = QueryableEncryptionSchemaGenerator.GenerateSchemas(db.Model);
foreach (var collection in schema)
    Console.WriteLine(collection.Value.ToJson());

// Our EF db context
public class PayrollContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Employee> Employees { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Our fluent entity configuration
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.Property(e => e.TaxPayerId)
                // Might want to look-up by Tax Payer ID
                .IsEncryptedForEquality(Guid.Parse("48bdf151-7cf2-41a6-b669-763967afcd1f"));

            entity.Property(e => e.Salary)
                .HasBsonRepresentation(BsonType.Decimal128)
                // Salaries from 0 to 1 million, no decimal place precision
                .IsEncryptedForRange(0m, 10000000m, 0, Guid.Parse("d147334e-0686-4cca-aa1b-8a768019f92b"));

            entity.Property(e => e.Notes)
                // This is encrypted and readable but you can't query on it
                .IsEncrypted(Guid.Parse("f10e6bb1-833f-46ee-adc7-62946c7906a7"));
        });
    }
}

// Our employee entity
public class Employee
{
    public ObjectId Id { get; set; }
    public string Name { get; set; }
    public string TaxPayerId { get; set; }
    public decimal Salary { get; set; }
    public string Notes { get; set; }
}
