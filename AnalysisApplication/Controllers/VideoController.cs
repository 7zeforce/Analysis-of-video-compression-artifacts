using System.Diagnostics;
using AnalysisApplication.Data;
using AnalysisApplication.Models;
using AnalysisApplication.Services;
using Microsoft.AspNetCore.Components.Forms;
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
                FilleName = mp4Name,
                DisplayName = Path.GetFileNameWithoutExtension(file.FileName),
                FileSize = new FileInfo(Path.Combine(videosDir, mp4Name)).Length,
                UploadedAt = DateTime.UtcNow
            };
            _db.VideoItems.Add(itm);
            await _db.SaveChangesAsync();
            return Json(new { ok = true, id = itm.Id, url = $"/videos/{itm.FilleName}", name = itm.DisplayName });
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var videos = await _db.VideoItems.OrderByDescending(v => v.UploadedAt).Select(v => new { Id = v.Id, Name = v.DisplayName, url = $"/videos/{v.FilleName}"}).ToListAsync();
            return Json(videos); 
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            VideoItem videoItem = await _db.VideoItems.FindAsync(id);
            if (videoItem == null) return Json(new { ok = false });
            string path = Path.Combine(_env.WebRootPath, "videos", videoItem.FilleName);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            _db.VideoItems.Remove(videoItem);
            await _db.SaveChangesAsync();
            return Json(new { ok = true });
        }

        [HttpPost]
        public async Task<IActionResult> Compress(int id)
        {
            VideoItem video = await _db.VideoItems.FindAsync(id);
            if(video == null) return Json(new { ok = false, error = "Video not found" });
            string pathVideo = Path.Combine(_env.WebRootPath, "videos", video.FilleName);
            if(!System.IO.File.Exists(pathVideo)) return Json(new { ok = false, error = "Video file not found" });
            string outputName = $"{Path.GetFileNameWithoutExtension(video.FilleName)}_compressed.mp4";
            string outputPath = Path.Combine(_env.WebRootPath, "videos", outputName);
            if (System.IO.File.Exists(outputPath))
            {
                VideoItem? existing = _db.VideoItems.FirstOrDefault(v => v.FilleName == outputName);
                if(existing != null) return Json(new { ok = true, id = existing.Id, url = $"/videos/{existing.FilleName}", name = existing.DisplayName, cached = true });
            }
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments =
                    $"-i \"{pathVideo}\" " +
                    $"-b:v 2M -maxrate 2M -bufsize 4M " +
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
            await proc!.WaitForExitAsync();
            if(proc.ExitCode != 0)
            {
                string error = await proc.StandardError.ReadToEndAsync();
                return Json(new { ok = false, error = $"FFmpeg exited with code {proc.ExitCode}: {error}" });
            }
            if(!System.IO.File.Exists(outputPath)) return Json(new { ok = false, error = "Compressed video file not found" });
            VideoItem compareVideo = new VideoItem
            {
                FilleName = outputName,
                DisplayName = Path.GetFileNameWithoutExtension(outputPath),
                FileSize = new FileInfo(outputPath).Length,
                UploadedAt = DateTime.UtcNow
            };
            _db.Add(compareVideo);
            await _db.SaveChangesAsync();
            return Json(new { ok = true, id = compareVideo.Id, url = $"/videos/{compareVideo.FilleName}", name = compareVideo.DisplayName });
        }

        private async Task ConvertToMp4Async(string inputPath, string outputPath)
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{inputPath}\" -c:v libx264 -preset fast -crf 22 -c:a aac -b:a 128k \"{outputPath}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process? proc = Process.Start(psi);
            await proc!.WaitForExitAsync();
            if (proc.ExitCode != 0)
            {
                string error = await proc.StandardError.ReadToEndAsync();
                throw new Exception($"FFmpeg exited with code {proc.ExitCode}: {error}");
            }
        }
    }
}
