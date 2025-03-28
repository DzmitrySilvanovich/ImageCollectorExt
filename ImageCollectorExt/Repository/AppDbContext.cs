using ImageCollectorExt.Models;
using Microsoft.EntityFrameworkCore;

namespace ImageCollectorExt.Repository
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
            // Database.EnsureDeleted();
         //   Database.EnsureCreated();
        }
        public DbSet<FileRecord> FileRecords { get; set; }
    }
}
