This article was originally posted on my blog at https://damieng.com/blog/2025/09/22/mongodb-queryable-encryption/

---

# Queryable Encryption with the MongoDB EF Core Provider

[MongoDB's Queryable Encryption](https://www.mongodb.com/docs/manual/core/queryable-encryption/) lets you encrypt sensitive database fields while keeping them searchable. Unlike traditional encryption-at-rest that renders data unreadable to the database, queryable encryption supports equality and range queries on encrypted fields without requiring decryption first.

For example, you can encrypt an employees email address and still query `users.Where(u => u.Email == "john@example.com")` or encrypt salaries and query `employees.Where(e => e.Salary > 50000m)` without any changes to your application code.

This tutorial assumes basic familiarity with [Entity Framework Core](https://docs.microsoft.com/en-us/ef/core/) and [MongoDB](https://docs.mongodb.com/manual/tutorial/getting-started/).

## Prerequisites

Before we dive into queryable encryption, let's make sure you have everything you need to follow along with this tutorial.

You'll need **MongoDB Enterprise 8.0 or Atlas** (Community Edition does not support automatic encryption). You can either use:

### Docker

Install [Docker](https://www.docker.com/) if you don't already have it and then fire up the [MongoDB Atlas Local Docker image](https://hub.docker.com/r/mongodb/mongodb-atlas-local) by simply running:

```bash
docker run --name mongodb-atlas-local -p 27017:27017 -d mongodb/mongodb-atlas-local:latest
```

### Atlas Cloud

Alternatively you can create a MongoDB Atlas instance directly on [MongoDB Cloud](https://www.mongodb.com/products/platform/cloud) or on your favorite cloud provider and ensure you can remotely access it.

### Enterprise Self-Hosted

If you already have access to a MongoDB Enterprise 8.0 or later installation you are good-to-go and just need a connection string and necessary access.

## Creating your project

Create a .NET Core Console project and add two NuGet packages to your project:

- **[MongoDB.EntityFrameworkCore](https://www.nuget.org/packages/MongoDB.EntityFrameworkCore)** (version 9.0.1 or above)
- **[MongoDB.Driver.Encryption](https://www.nuget.org/packages/MongoDB.Driver.Encryption)** (latest version)

As well as directly bringing in our EF Provider and encryption support this indirectly brings the necessary encryption algorithm library [libmongocrypt](https://github.com/mongodb/libmongocrypt) and our C# Driver to handle the lower-level support for Queryable Encryption.

## Keys

> Encryption key management is a complex topic that involves serious consideration of secure storage of your Customer Master Key in a secure management service as well as considerations for your key rotation process. These are beyond the scope of this tutorial but touched on in [Encryption Keys and Key Vaults](https://www.mongodb.com/docs/manual/core/queryable-encryption/fundamentals/keys-key-vaults/) which should be followed for production scenarios.

Let's look at the components needed to get encryption running specifically from the local development angle.

### Customer Master Key (CMK)

The Customer Master Key is the sensitive secret used to encrypt and decrypt all the individual field-level Data Encryption Keys.

For our local testing we'll generate a [Customer Master Key (CMK)](https://www.mongodb.com/docs/manual/core/queryable-encryption/qe-create-cmk/) with the following code. This will output a 768-bit key as a base-64 encoded string which we will - for testing purposes - embed in our application. In production this key would have been externally generated and stored in a remote Key Management System.

```csharp
Console.WriteLine(Convert.ToBase64String(RandomNumberGenerator.GetBytes(96)));
```

### Key Management Systems (KMS)

MongoDB drivers support a number of [secure key management systems](https://www.mongodb.com/docs/manual/core/queryable-encryption/fundamentals/kms-providers/) including Amazon AWS KMS, Azure Key Vault and Google Cloud KMS.

For our local testing we'll create the required dictionary of KMS providers with just one provider called `local` that will contain our Customer Master Key.

```csharp
var customerMasterKey = Convert.FromBase64String("gT6tpjPkeEmKYra2...b0Vfga");

var kmsProviders = new Dictionary<string, IReadOnlyDictionary<string, object>>
{
    { "local", new Dictionary<string, object> { { "key", customerMasterKey } } }
};
```

### Data Encryption Keys (DEK)

Data Encryption Keys are encryption keys used to encrypt individual fields. They are stored in a MongoDB collection known as the Key Vault (typically named `__keyVault` within your database) and are themselves encrypted with the Customer Master Key.

We'll generate the Data Encryption Keys in a moment but let's specify the database and key vault now in our code:

```csharp
const string databaseName = "queryable-encryption-fun";
var keyVaultNamespace = new CollectionNamespace(databaseName, "__keyVault");
```

### Automatic Encryption

In order to make encryption transparent to the application a component is required to automatically encrypt and decrypt the fields. There are two options available as part of either a MongoDB Enterprise or Atlas subscription (Community Edition is not supported).

#### Automatic Encryption Shared Library (crypt_shared)

The Automatic Encryption Shared Library is a dynamic library that performs the automatic encryption for you in-process as part of your application. It is the recommended approach for new applications and can be downloaded from the [MongoDB Enterprise Server Page](https://www.mongodb.com/try/download/enterprise) - just switch the package to **crypt_shared** once you have selected your version and platform and the place the extracted library somewhere your application can reference it.

#### mongocryptd

You can also use the [mongocryptd](https://www.mongodb.com/docs/manual/core/csfle/reference/install-library/) local background process that is supplied as part of MongoDB Enterprise Server which uses a different port number to connect. You'll also need to tell our EF Core Provider you want to use it and where the binary is so it can auto-start it if required.

```csharp
.WithCryptProvider(CryptProvider.Mongocryptd, "C:\\MongoDB"); // Adjust for your path
```

## Setting up your application

Now we're ready to configure our EF Core DbContext with the decisions we've made about our connection, keys and auto encryption provider!

```csharp
MongoClientSettings.Extensions.AddAutoEncryption();
var clientSettings = MongoClientSettings.
    FromConnectionString("mongodb://localhost:27017?directConnection=true");

var options = new DbContextOptionsBuilder()
    .UseMongoDB(new MongoOptionsExtension()
        .WithClientSettings(clientSettings)
        .WithDatabaseName(databaseName)
        .WithKeyVaultNamespace(keyVaultNamespace)
        .WithCryptProvider(CryptProvider.AutoEncryptSharedLibrary, "C:\\MongoDB\\mongo_crypt_v1.dll")
        .WithKmsProviders(kmsProviders));
```

If you are using Linux or Mac OS ensure that your `WithCryptProvider` path points to the necessary `.so` or `.dylib` binary for your platform.

## Configuring field encryption

Let's start with a simple `Employee` entity that contains some sensitive information we want to encrypt:

```csharp
public class Employee
{
    public ObjectId Id { get; set; }
    public string Name { get; set; }
    public string TaxPayerId { get; set; }
    public decimal Salary { get; set; }
    public string Notes { get; set; }
}
```

We want to encrypt two properties here so we'll need to generate two Data Encryption Keys we can use. Like when we generated the Customer Master Key we will write some one-time code to generate them in our key vault and we'll output the necessary GUIDs to reference them from our app.

```csharp
using var clientEncryption = new ClientEncryption(new ClientEncryptionOptions(new MongoClient(clientSettings), keyVaultNamespace, kmsProviders));

Guid CreateDataKey() => clientEncryption.CreateDataKey("local", new DataKeyOptions(), CancellationToken.None);
Console.WriteLine(String.Join("\n", Enumerable.Range(1, 3).Select(_ => CreateDataKey())));
```

These two GUIDs can now be referenced in our fluent EF API to enable the encryption on the properties. (We hope to automate key generation in a future EF Core Provider update). If you delete the database or the key vault collection then you will lose the ability to read your encrypted data!

We can now configure encryption using EF Core's fluent API in our `OnModelCreating` method. For properties you want to query with equality operations, use `IsEncryptedForEquality`. For numeric properties where you need range queries, use `IsEncryptedForRange` and specify the min/max bounds and precision. Alternatively you can encrypt with `IsEncrypted` if you just want non-queryable encryption for things like sub-documents and objects.

So let's go ahead and add create a data context with the fluent configuration for these properties:

```csharp
public class MyContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Employee> Employees { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.Property(e => e.TaxPayerId)
                // Might want to look-up by Tax Payer ID
                .IsEncryptedForEquality(Guid.Parse("adfcb376-16b2-4325-b0bd-1e7811d0ce6b"));

            entity.Property(e => e.Salary)
                .HasBsonRepresentation(BsonType.Decimal128)
                // Salaries from 0 to 10 million, no decimal place precision
                .IsEncryptedForRange(0m, 10000000m, 0, Guid.Parse("e1c70f05-d0f9-41c5-938e-059632b33d09"));

            entity.Property(e => e.Notes)
                // This is encrypted and readable but you can't query on it
                .IsEncrypted(Guid.Parse("f10e6bb1-833f-46ee-adc7-62946c7906a7"));
        });
    }
}
```

## Adding some encrypted test data

Now that you have configured your queryable encrypted fields feel free to use EF to add some data to the database using the regular `.Add` and `.SaveChanges` methods:

```csharp
if (!await db.Employees.AnyAsync())
{
    // Setup initial test data
    db.Employees.AddRange(
        new Employee { Name = "Tom", TaxPayerId = "12345", Salary = 50000 },
        new Employee { Name = "Dick", TaxPayerId = "23456", Salary = 100000 },
        new Employee { Name = "Sally", TaxPayerId = "34567", Salary = 199000 },
        new Employee { Name = "Harry", TaxPayerId = "45678", Salary = 200000 });
    await db.SaveChangesAsync();
}
```

## Querying encrypted data

Now we can query some data based on the encrypted fields!

```csharp
var found = db.Employees.First(e => e.TaxPayerId == "12345");
Console.WriteLine(found.Name);

var band2Earners = db.Employees.Where(e => e.Salary >= 100000m && e.Salary < 200000m);
foreach (var employee in band2Earners)
    Console.WriteLine(employee.Name);
```

That's all there is to it, but if you want to dig a little deeper...

## Encryption schemas

If you have some familiarity with other drivers or have read the docs you might be wondering where the Queryable Encryption field schema is. Well, our EF Core provider is generating that for you with all the necessary element names, BSON types, query specifications and attributes based on the fluent `.IsEncrypted` methods you call against the EF ModelBuilder.

This schema is, for now, client side only which allows you to rapidly iterate and change your mind about which fields are Queryable Encrypted and how.

> Before you go to production it is recommended that the schema be applied to the collection creation so that it can be enforced for all clients and no elements can be inadvertently left unencrypted by a misconfigured client. The trade-off is that in order to change any of the encryption parameters you would need to migrate the data to a new collection with the new encryption configuration which is beyond the scope of this quickstart.

If you want to apply the generated schemas to the server yourself, you can use the `QueryableEncryptionSchemaGenerator` class that takes your EF Model and loop through each of the dictionary results to obtain the field schema necessary to provide to CreateCollection. We're looking at allowing this to be automated in a future EF Provider update.

## Limitations & considerations

There are some important limitations and considerations to consider when encrypting data.

### Supported data types

| CLR Type       | BSON Type  | Equality | Range  |
| -------------- | ---------- | -------- | ------ |
| `string`       | string     | ✅ Yes   | ❌ No  |
| `int`          | int        | ✅ Yes   | ✅ Yes |
| `long`         | long       | ✅ Yes   | ✅ Yes |
| `double`       | double     | ❌ No    | ✅ Yes |
| `decimal`      | decimal128 | ❌ No    | ✅ Yes |
| `DateTime`     | date       | ✅ Yes   | ✅ Yes |
| `bool`         | bool       | ✅ Yes   | ❌ No  |
| `ObjectId`     | objectId   | ✅ Yes   | ❌ No  |
| `byte[]`       | binData    | ✅ Yes   | ❌ No  |
| `Guid`         | binData    | ✅ Yes   | ❌ No  |
| Array & List   | array      | ❌ No    | ❌ No  |
| Class & Struct | object     | ❌ No    | ❌ No  |

### General limitations

There are a few limitations to know about when using Queryable Encryption:

- Encrypted elements can be either equality or range queries, but not both!
- Arrays or elements on an object within an array (e.g. on an `OwnsMany`) cannot be encrypted
- Sorting on encrypted elements even if configured for range is not supported
- Nulls can not be stored or compared on encrypted elements

## Moving to production

While we've been able to get things up and running simply here there are a number of additional steps to take when considering how to move your application to production. 

Here's a short check-list to point you in the right direction but each step requires some planning and effort.

- Generate a new Customer Master Key
- Setup a secure key management system
- Generate your Data Encryption Keys
- Deploy the crypt_shared library with your package
- Adjust your application to use the new keys and service
- Include the Queryable Encryption Schema as you create each collection
- Have a plan and develop a tool for key rotation

## That's a wrap

I hope that you found this quickstart on Queryable Encryption with the MongoDB EF Core Provider helpful. 

Enjoy!

Damien
