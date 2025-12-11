using Microsoft.EntityFrameworkCore;
using CarInventorySystem.Models;

namespace CarInventorySystem.Services
{
    public class CarInventoryDbContext : DbContext
    {
        public DbSet<Car> Cars { get; init; }

        public CarInventoryDbContext(DbContextOptions options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Car>();
        }
    }
}