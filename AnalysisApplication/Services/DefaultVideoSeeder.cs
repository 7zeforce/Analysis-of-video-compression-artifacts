using AnalysisApplication.Controllers;
using AnalysisApplication.Data;
using AnalysisApplication.Models;
using Microsoft.EntityFrameworkCore;

namespace AnalysisApplication.Services
{
    public static class DefaultVideoSeeder
    {
        public static async Task SeedAsync(IServiceProvider services, IWebHostEnvironment env, IConfiguration config, ILogger logger)
        {
            string? configured = config["DefaultVideo"];
            if (string.IsNullOrWhiteSpace(configured)) return;
            string samplePath = Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured);
            if (!File.Exists(samplePath))
            {
                logger.LogWarning("Default video not found: {Path}", samplePath);
                return;
            }
            using IServiceScope scope = services.CreateScope();
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            string videosDir = Path.Combine(env.WebRootPath, "videos");
            Directory.CreateDirectory(videosDir);
            List<VideoItem> existing = await db.VideoItems.ToListAsync();
            if (existing.Any(v => !v.IsCompressed && File.Exists(Path.Combine(videosDir, v.FileName)))) return;
            db.VideoItems.RemoveRange(existing);
            string ext = Path.GetExtension(samplePath).ToLowerInvariant();
            string tempName = $"{Guid.NewGuid():N}{ext}";
            string tempPath = Path.Combine(videosDir, tempName);
            File.Copy(samplePath, tempPath, true);
            string mp4Name = tempName;
            if (ext != ".mp4" && ext != ".webm")
            {
                mp4Name = $"{Path.GetFileNameWithoutExtension(tempName)}.mp4";
                logger.LogInformation("Converting default video {Name} to mp4", Path.GetFileName(samplePath));
                await VideoController.ConvertToMp4Async(tempPath, Path.Combine(videosDir, mp4Name));
                File.Delete(tempPath);
            }
            VideoItem itm = new VideoItem
            {
                FileName = mp4Name,
                DisplayName = Path.GetFileNameWithoutExtension(samplePath),
                FileSize = new FileInfo(Path.Combine(videosDir, mp4Name)).Length,
                IsCompressed = false,
                SourceVideoId = null,
                BitrateKbps = 0,
                UploadedAt = DateTime.UtcNow
            };
            db.VideoItems.Add(itm);
            await db.SaveChangesAsync();
        }
    }
}
