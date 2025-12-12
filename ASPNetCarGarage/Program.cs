using ASPNetCarGarage.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<CarInventoryDbContext>(o =>
{
    var mongoDbUri = builder.Configuration["MongoDBSettings:MongoDBUri"]
                     ?? throw new InvalidOperationException("MongoDBSettings.MongoDbUri required in appsettings.json.");
    var databaseName = builder.Configuration["MongoDBSettings:DatabaseName"] 
                       ?? throw new InvalidOperationException("MongoDBSettings.DatabaseName required in appsettings.json.");
    o.UseMongoDB(mongoDbUri, databaseName);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=CarInventory}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();