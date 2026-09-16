using AnalysisApplication.Models;
using Microsoft.EntityFrameworkCore;

namespace AnalysisApplication.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<VideoItem> VideoItems => Set<Models.VideoItem>();
    }
}
