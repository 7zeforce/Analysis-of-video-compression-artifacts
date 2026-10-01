using System.Diagnostics;
using AnalysisApplication.Data;
using AnalysisApplication.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AnalysisApplication.Controllers
{
    public class VideoController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly AppDbContext _db;

        private static readonly string[] AllowedExtensions = { ".mp4", ".webm", ".ogg", ".mov", ".mkv", ".avi", ".wmv", ".flv", ".m4v"};

        public VideoController(IWebHostEnvironment env, AppDbContext db)
        {
            _env = env;
            _db = db;
        }

        [HttpPost]
        [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024)]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0) return Json(new { ok = false, error = "Файл пуст" });
            string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if(!AllowedExtensions.Contains(ext)) return Json(new { ok = false, error = "Недопустимый формат файла" });
            string videosDir = Path.Combine(_env.WebRootPath, "videos");
            Directory.CreateDirectory(videosDir);
            string tempName = $"{Guid.NewGuid():N}{ext}";
            string fullPath = Path.Combine(videosDir, tempName);
            using (FileStream stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
            string mp4Name = tempName;
            if (ext != ".mp4" && ext != ".webm")
            {
                mp4Name = $"{Path.GetFileNameWithoutExtension(tempName)}.mp4";
                string mp4Path = Path.Combine(videosDir, mp4Name);
                try
                {
                    await ConvertToMp4Async(fullPath, mp4Path);
                    System.IO.File.Delete(fullPath);
                }
                catch(Exception ex){
                    return Json(new { ok = false, error = $"Error of convert file {tempName}: {ex.Message}"});
                }
            }
            VideoItem itm = new VideoItem
            {
                FileName = mp4Name,
                DisplayName = Path.GetFileNameWithoutExtension(file.FileName),
                FileSize = new FileInfo(Path.Combine(videosDir, mp4Name)).Length,
                IsCompressed = false,
                SourceVideoId = null,
                BitrateKbps = 0,
                UploadedAt = DateTime.UtcNow
            };
            List<VideoItem> previous = await _db.VideoItems.ToListAsync();
            foreach (VideoItem item in previous)
            {
                string path = Path.Combine(videosDir, item.FileName);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                _db.VideoItems.Remove(item);
            }
            _db.VideoItems.Add(itm);
            await _db.SaveChangesAsync();
            return Json(new { ok = true, id = itm.Id, url = $"/videos/{itm.FileName}", name = itm.DisplayName, isCompressed = itm.IsCompressed, sourceId = itm.SourceVideoId, bitrate = itm.BitrateKbps, size = itm.FileSize });
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var videos = await _db.VideoItems.OrderByDescending(v => v.UploadedAt).Select(v => new { Id = v.Id, Name = v.DisplayName, url = $"/videos/{v.FileName}", isCompressed = v.IsCompressed, sourceId = v.SourceVideoId, bitrate = v.BitrateKbps, size = v.FileSize }).ToListAsync();
            return Json(videos);
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            VideoItem? videoItem = await _db.VideoItems.FindAsync(id);
            if (videoItem == null) return Json(new { ok = false, error = "Video not found" });
            List<VideoItem> derived = await _db.VideoItems.Where(v => v.SourceVideoId == id).ToListAsync();
            foreach (VideoItem item in derived.Append(videoItem))
            {
                string path = Path.Combine(_env.WebRootPath, "videos", item.FileName);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                _db.VideoItems.Remove(item);
            }
            await _db.SaveChangesAsync();
            return Json(new { ok = true, removed = derived.Count + 1 });
        }

        [HttpPost]
        public async Task<IActionResult> Compress(int id, int bitrate = 2000)
        {
            if(bitrate <= 0 || bitrate > 100000) return Json(new { ok = false, error = "Bitrate is wrong. It have to be between 1 and 100000 kbps." });
            VideoItem? video = await _db.VideoItems.FindAsync(id);
            if(video == null) return Json(new { ok = false, error = "Video not found" });
            if(video.IsCompressed) return Json(new { ok = false, error = "This video is already a compressed copy. Choose a source video." });
            string pathVideo = Path.Combine(_env.WebRootPath, "videos", video.FileName);
            if(!System.IO.File.Exists(pathVideo)) return Json(new { ok = false, error = "Video file not found" });
            string outputName = $"{Path.GetFileNameWithoutExtension(video.FileName)}_{bitrate}k.mp4";
            string outputPath = Path.Combine(_env.WebRootPath, "videos", outputName);
            VideoItem? existing = await _db.VideoItems.FirstOrDefaultAsync(v => v.FileName == outputName);
            if (existing != null && System.IO.File.Exists(outputPath))
            {
                return Json(new { ok = true, id = existing.Id, url = $"/videos/{existing.FileName}", name = existing.DisplayName, isCompressed = true, sourceId = existing.SourceVideoId, bitrate = existing.BitrateKbps, size = existing.FileSize, cached = true });
            }
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments =
                    $"-i \"{pathVideo}\" " +
                    $"-b:v {bitrate}k -maxrate {bitrate}k -bufsize {bitrate * 2}k " +
                    $"-c:v libx264 -preset medium " +
                    $"-c:a aac -b:a 128k " +
                    $"-movflags +faststart " +
                    $"\"{outputPath}\" -y",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process? proc = Process.Start(psi);
            Task<string> errorTask = proc!.StandardError.ReadToEndAsync();
            Task<string> outputTask = proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            string error = await errorTask;
            await outputTask;
            if(proc.ExitCode != 0)
            {
                return Json(new { ok = false, error = $"FFmpeg exited with code {proc.ExitCode}: {Tail(error)}" });
            }
            if(!System.IO.File.Exists(outputPath)) return Json(new { ok = false, error = "Compressed video file not found" });
            VideoItem compareVideo = existing ?? new VideoItem();
            compareVideo.FileName = outputName;
            compareVideo.DisplayName = $"{video.DisplayName} @ {bitrate} kbps";
            compareVideo.FileSize = new FileInfo(outputPath).Length;
            compareVideo.IsCompressed = true;
            compareVideo.SourceVideoId = video.Id;
            compareVideo.BitrateKbps = bitrate;
            compareVideo.UploadedAt = DateTime.UtcNow;
            if (existing == null) _db.VideoItems.Add(compareVideo);
            await _db.SaveChangesAsync();
            return Json(new { ok = true, id = compareVideo.Id, url = $"/videos/{compareVideo.FileName}", name = compareVideo.DisplayName, isCompressed = true, sourceId = compareVideo.SourceVideoId, bitrate = compareVideo.BitrateKbps, size = compareVideo.FileSize });
        }

        internal static async Task ConvertToMp4Async(string inputPath, string outputPath)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{inputPath}\" -c:v libx264 -preset fast -crf 22 -c:a aac -b:a 128k \"{outputPath}\" -y",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process? proc = Process.Start(psi);
            Task<string> errorTask = proc!.StandardError.ReadToEndAsync();
            Task<string> outputTask = proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            string error = await errorTask;
            await outputTask;
            if (proc.ExitCode != 0)
            {
                throw new Exception($"FFmpeg exited with code {proc.ExitCode}: {Tail(error)}");
            }
        }

        private static string Tail(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" | ", lines.TakeLast(5).Select(l => l.Trim()));
        }
    }
}
