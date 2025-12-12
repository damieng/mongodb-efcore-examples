using ASPNetCarGarage.Models;
using Microsoft.EntityFrameworkCore;

namespace ASPNetCarGarage.Data
{
    public class CarInventoryDbContext(DbContextOptions options)
        : DbContext(options)
    {
        public DbSet<Car> Cars { get; init; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Car>();
        }
    }
}