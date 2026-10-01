using System.Diagnostics;
using System.Globalization;
using AnalysisApplication.Data;
using AnalysisApplication.Models;
using AnalysisApplication.Services;
using Microsoft.AspNetCore.Mvc;
using OpenCvSharp;

namespace AnalysisApplication.Controllers
{
    public class ArtifactsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ArtifactJobs _jobs;

        private static readonly string[] Modes = { "gray", "signed", "heat" };

        public ArtifactsController(AppDbContext db, IWebHostEnvironment env, ArtifactJobs jobs)
        {
            _db = db;
            _env = env;
            _jobs = jobs;
        }

        [HttpPost]
        public async Task<IActionResult> Start(int? id, double gain = 10.0, string mode = "gray")
        {
            if (id == null || id <= 0) return Json(new { ok = false, error = "Id is wrong. It have to be not null and more than zero." });
            if (!double.IsFinite(gain) || gain < 0.5 || gain > 64.0) return Json(new { ok = false, error = "Gain is wrong. It have to be between 0.5 and 64." });
            if (!Modes.Contains(mode)) return Json(new { ok = false, error = $"Mode is wrong. Allowed: {string.Join(", ", Modes)}." });
            VideoItem? compressed = await _db.VideoItems.FindAsync(id);
            if (compressed == null) return Json(new { ok = false, error = "Compressed video not found" });
            if (compressed.SourceVideoId == null || compressed.SourceVideoId <= 0) return Json(new { ok = false, error = "This video is not a compressed copy. Compress a source video first." });
            VideoItem? source = await _db.VideoItems.FindAsync(compressed.SourceVideoId);
            if (source == null) return Json(new { ok = false, error = "Source video not found" });
            string videosDir = Path.Combine(_env.WebRootPath, "videos");
            string sourcePath = Path.Combine(videosDir, source.FileName);
            string compressPath = Path.Combine(videosDir, compressed.FileName);
            if (!System.IO.File.Exists(sourcePath)) return Json(new { ok = false, error = $"Source video file not found: {source.FileName}" });
            if (!System.IO.File.Exists(compressPath)) return Json(new { ok = false, error = $"Compressed video file not found: {compressed.FileName}" });

            string gainTag = gain.ToString("0.#", CultureInfo.InvariantCulture).Replace('.', '-');
            string outputName = $"{Path.GetFileNameWithoutExtension(compressed.FileName)}_art_{mode}_g{gainTag}.mp4";
            string outputPath = Path.Combine(videosDir, outputName);

            int total = FrameCount(sourcePath);
            ArtifactJob job = _jobs.Create(source.Id, compressed.Id, mode, gain, total);
            if (System.IO.File.Exists(outputPath))
            {
                _jobs.Complete(job.Id, outputName);
                return Json(new { ok = true, jobId = job.Id, cached = true, url = $"/videos/{outputName}", name = outputName });
            }
            _ = Task.Run(() => Run(job.Id, sourcePath, compressPath, outputPath, outputName, BuildFilter(mode, gain)));
            return Json(new { ok = true, jobId = job.Id, cached = false, gain, mode, total });
        }

        [HttpGet]
        public IActionResult Status(string? jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId)) return Json(new { ok = false, error = "JobId is wrong. It have to be not empty." });
            ArtifactJob? job = _jobs.Get(jobId);
            if (job == null) return Json(new { ok = false, error = "Job not found" });
            if (job.State == "failed") return Json(new { ok = false, state = job.State, error = job.Error });
            if (job.State != "done") return Json(new { ok = true, state = job.State, done = job.Done, total = job.Total });
            return Json(new
            {
                ok = true,
                state = job.State,
                done = job.Done,
                total = job.Total,
                gain = job.Gain,
                mode = job.Mode,
                name = job.FileName,
                url = $"/videos/{job.FileName}"
            });
        }

        private static string BuildFilter(string mode, double gain)
        {
            string g = gain.ToString("0.###", CultureInfo.InvariantCulture);
            return mode switch
            {
                "signed" => $"[0:v][1:v]blend=all_expr=clip((A-B)*{g}+128\\,0\\,255),format=gray",
                "heat" => $"[0:v][1:v]blend=all_expr=clip(abs(A-B)*{g}\\,0\\,255),format=gray,format=yuv444p,pseudocolor=preset=turbo",
                _ => $"[0:v][1:v]blend=all_expr=clip(abs(A-B)*{g}\\,0\\,255),format=gray"
            };
        }

        private static int FrameCount(string path)
        {
            try
            {
                using VideoCapture capture = new VideoCapture(path);
                return capture.IsOpened() && capture.FrameCount > 0 ? capture.FrameCount : 0;
            }
            catch
            {
                return 0;
            }
        }

        private async Task Run(string jobId, string sourcePath, string compressPath, string outputPath, string outputName, string filter)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                string[] arguments =
                {
                    "-y", "-loglevel", "error", "-nostats", "-progress", "pipe:1",
                    "-i", sourcePath,
                    "-i", compressPath,
                    "-filter_complex", filter,
                    "-c:v", "libx264", "-preset", "medium", "-crf", "20",
                    "-pix_fmt", "yuv420p", "-an", "-movflags", "+faststart",
                    outputPath
                };
                foreach (string argument in arguments) psi.ArgumentList.Add(argument);

                using Process? proc = Process.Start(psi);
                if (proc == null) throw new InvalidOperationException("FFmpeg process could not be started.");
                Task<string> errorTask = proc.StandardError.ReadToEndAsync();
                string? line;
                while ((line = await proc.StandardOutput.ReadLineAsync()) != null)
                {
                    if (!line.StartsWith("frame=", StringComparison.Ordinal)) continue;
                    if (int.TryParse(line.AsSpan(6).Trim(), out int frame)) _jobs.Progress(jobId, frame);
                }
                await proc.WaitForExitAsync();
                string error = await errorTask;
                if (proc.ExitCode != 0) throw new InvalidOperationException($"FFmpeg exited with code {proc.ExitCode}: {Tail(error)}");
                if (!System.IO.File.Exists(outputPath)) throw new InvalidOperationException("Artifacts video file was not created.");
                _jobs.Complete(jobId, outputName);
            }
            catch (Exception ex)
            {
                _jobs.Fail(jobId, ex.Message);
            }
        }

        private static string Tail(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "no output";
            string trimmed = text.Trim();
            return trimmed.Length <= 400 ? trimmed : trimmed[^400..];
        }
    }
}
