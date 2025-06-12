using Logger.Entities;
using Microsoft.EntityFrameworkCore;

namespace Logger.Data
{


    public class AppDbContext : DbContext
    {
        public DbSet<SensorReading> SensorReadings { get; set; }
        private readonly string _dbPath = "sensor_data.db";

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SensorReading>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.Property(e => e.Timestamp).HasDefaultValueSql("CURRENT_TIMESTAMP");
            });
        }
    }
}
