using AnalysisApplication.Components;
using AnalysisApplication.Data;
using AnalysisApplication.Models;
using AnalysisApplication.Services;
using Microsoft.AspNetCore.Mvc;
using OpenCvSharp;

namespace AnalysisApplication.Controllers
{
    public class EstimationController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly EstimationJobs _jobs;

        private readonly string[] Components = { "Y", "Cr", "Cb" };

        private static readonly string[] MetricNames = { "MAE", "MSE", "PSNR", "DiffMax" };

        public EstimationController(AppDbContext db, IWebHostEnvironment env, EstimationJobs jobs)
        {
            _db = db;
            _env = env;
            _jobs = jobs;
        }

        [HttpPost]
        public async Task<IActionResult> Start(int? id, double max = 255.0)
        {
            if (id == null || id <= 0) return Json(new { ok = false, error = "Id is wrong. It have to be not null and more than zero." });
            if (max <= 0 || double.IsNaN(max) || double.IsInfinity(max)) return Json(new { ok = false, error = "Max is wrong. It have to be a finite number more than zero." });
            VideoItem? compressed = await _db.VideoItems.FindAsync(id);
            if (compressed == null) return Json(new { ok = false, error = "Compressed video not found" });
            if (compressed.SourceVideoId == null || compressed.SourceVideoId <= 0) return Json(new { ok = false, error = "This video is not a compressed copy. Compress a source video first." });
            VideoItem? source = await _db.VideoItems.FindAsync(compressed.SourceVideoId);
            if (source == null) return Json(new { ok = false, error = "Source video not found" });
            string sourcePath = Path.Combine(_env.WebRootPath, "videos", source.FileName);
            string compressPath = Path.Combine(_env.WebRootPath, "videos", compressed.FileName);
            if (!System.IO.File.Exists(sourcePath)) return Json(new { ok = false, error = $"Source video file not found: {source.FileName}" });
            if (!System.IO.File.Exists(compressPath)) return Json(new { ok = false, error = $"Compressed video file not found: {compressed.FileName}" });
            EstimationJob job = _jobs.Create(source.Id, source.DisplayName, compressed.Id, compressed.DisplayName, compressed.BitrateKbps);
            _ = Task.Run(() => Run(job.Id, sourcePath, compressPath, max));
            return Json(new { ok = true, jobId = job.Id, bitrate = compressed.BitrateKbps, source = source.DisplayName, name = compressed.DisplayName });
        }

        [HttpGet]
        public IActionResult Status(string? jobId, int points = 2000)
        {
            if (string.IsNullOrWhiteSpace(jobId)) return Json(new { ok = false, error = "JobId is wrong. It have to be not empty." });
            EstimationJob? job = _jobs.Get(jobId);
            if (job == null) return Json(new { ok = false, error = "Job not found" });
            if (job.State == "failed") return Json(new { ok = false, state = job.State, error = job.Error });
            if (job.State != "done") return Json(new { ok = true, state = job.State, done = job.Done, total = job.Total, warning = job.Warning });
            if (points <= 0) points = 2000;
            int stride = job.Frames.Count > points ? (int)Math.Ceiling(job.Frames.Count / (double)points) : 1;
            List<object> series = new List<object>();
            for (int i = 0; i < job.Frames.Count; i += stride)
            {
                FrameMetrics frame = job.Frames[i];
                if (!frame.Components.TryGetValue("Y", out FrameComponentMetrics y)) continue;
                series.Add(new { frame = frame.FrameIndex, mae = Finite(y.MAE), mse = Finite(y.MSE), psnr = Finite(y.PSNR), diffMax = Finite(y.DiffMax) });
            }
            Dictionary<string, Dictionary<string, object>> aggregated = new Dictionary<string, Dictionary<string, object>>();
            foreach (string comp in Components)
            {
                if (job.Aggregated == null || !job.Aggregated.ByComponent.TryGetValue(comp, out Dictionary<string, ComponentStats>? byMetric)) continue;
                Dictionary<string, object> row = new Dictionary<string, object>();
                foreach (KeyValuePair<string, ComponentStats> pair in byMetric)
                {
                    row[pair.Key] = new { mean = Finite(pair.Value.Mean), std = Finite(pair.Value.Std), min = Finite(pair.Value.Min), max = Finite(pair.Value.Max) };
                }
                aggregated[comp] = row;
            }
            return Json(new
            {
                ok = true,
                state = job.State,
                done = job.Done,
                total = job.Total,
                warning = job.Warning,
                bitrate = job.BitrateKbps,
                sourceId = job.SourceId,
                compressedId = job.CompressedId,
                source = job.SourceName,
                name = job.CompressedName,
                components = Components,
                fps = job.Fps > 0 ? job.Fps : (double?)null,
                worst = job.WorstFrameIndex >= 0 ? new { frame = job.WorstFrameIndex, time = job.Fps > 0 ? job.WorstFrameIndex / job.Fps : (double?)null, mae = Finite(job.WorstFrameMae) } : null,
                metrics = MetricNames,
                aggregated,
                series
            });
        }

        private static double? Finite(double value) => double.IsFinite(value) ? value : null;

        private void Run(string jobId, string sourcePath, string compressPath, double max)
        {
            try
            {
                using VideoCapture source = new VideoCapture(sourcePath);
                if (!source.IsOpened()) throw new InvalidOperationException($"There is a problem with opening file {Path.GetFileName(sourcePath)}");
                using VideoCapture compress = new VideoCapture(compressPath);
                if (!compress.IsOpened()) throw new InvalidOperationException($"There is a problem with opening file {Path.GetFileName(compressPath)}");
                if (source.Fps > 0 && compress.Fps > 0 && Math.Abs(source.Fps - compress.Fps) > 0.01)
                {
                    _jobs.Warn(jobId, $"Frame rates differ ({source.Fps:F3} vs {compress.Fps:F3}), frames may be compared out of sync.");
                }
                int total = source.FrameCount > 0 ? source.FrameCount : 0;
                List<FrameMetrics> metrics = new List<FrameMetrics>();
                using Mat frameS = new Mat();
                using Mat frameC = new Mat();
                int i = 0;
                while (true)
                {
                    if (!source.Read(frameS) || !compress.Read(frameC)) break;
                    if (frameS.Empty() || frameC.Empty()) break;
                    if (frameS.Size() != frameC.Size()) Cv2.Resize(frameC, frameC, frameS.Size(), 0, 0, InterpolationFlags.Linear);
                    using Mat ycrcbS = new Mat();
                    using Mat ycrcbC = new Mat();
                    Cv2.CvtColor(frameS, ycrcbS, ColorConversionCodes.BGR2YCrCb);
                    Cv2.CvtColor(frameC, ycrcbC, ColorConversionCodes.BGR2YCrCb);
                    Mat[] chS = Cv2.Split(ycrcbS);
                    Mat[] chC = Cv2.Split(ycrcbC);
                    try
                    {
                        FrameMetrics frameMet = new FrameMetrics(new Dictionary<string, FrameComponentMetrics>(), i);
                        for (int j = 0; j < Components.Length; j++)
                        {
                            using Mat s = new Mat();
                            using Mat c = new Mat();
                            chS[j].ConvertTo(s, MatType.CV_32F);
                            chC[j].ConvertTo(c, MatType.CV_32F);
                            FrameComponentMetrics metric = ComputeComponentMetrics(s, c, max);
                            frameMet.Components[Components[j]] = metric;
                        }
                        metrics.Add(frameMet);
                    }
                    finally
                    {
                        foreach (Mat ch in chS) ch.Dispose();
                        foreach (Mat ch in chC) ch.Dispose();
                    }
                    i++;
                    _jobs.Progress(jobId, i, total > 0 ? total : i);
                }
                if (metrics.Count == 0) throw new InvalidOperationException("There are no comparable frames in these videos.");
                int worstIndex = -1;
                double worstMae = double.NegativeInfinity;
                for(int k = 0; k < metrics.Count; k++)
                {
                    if (!metrics[k].Components.TryGetValue("Y", out FrameComponentMetrics y)) continue;
                    if (!double.IsFinite(y.MAE) || y.MAE <= worstMae) continue;
                    worstMae = y.MAE;
                    worstIndex = metrics[k].FrameIndex;
                }
                AggregatedMetrics aggregated = Aggregate(metrics);
                _jobs.Complete(jobId, metrics, aggregated, source.Fps, worstIndex, worstMae);
            }
            catch (Exception ex)
            {
                _jobs.Fail(jobId, ex.Message);
            }
        }

        private FrameComponentMetrics ComputeComponentMetrics(Mat s, Mat c, double max)
        {
            using Mat absDiff = new Mat();
            Cv2.Absdiff(s, c, absDiff);
            double mae = Cv2.Mean(absDiff).Val0;
            using Mat diffSq = new Mat();
            Cv2.Multiply(absDiff, absDiff, diffSq);
            double mse = Cv2.Mean(diffSq).Val0;
            double psnr = mse > 0 ? 10.0 * Math.Log10((max * max) / mse) : double.PositiveInfinity;
            Cv2.MinMaxLoc(absDiff, out _, out double maxAbsDiff);
            return new FrameComponentMetrics
            {
                MAE = mae,
                MSE = mse,
                PSNR = psnr,
                DiffMax = maxAbsDiff,
            };
        }

        private AggregatedMetrics Aggregate(List<FrameMetrics> frames)
        {
            var result = new AggregatedMetrics();

            foreach (var comp in Components)
            {
                result.ByComponent[comp] = new Dictionary<string, ComponentStats>();

                foreach (var metric in MetricNames)
                {
                    var values = frames
                        .Where(f => f.Components != null && f.Components.ContainsKey(comp))
                        .Select(f => GetMetric(f.Components[comp], metric))
                        .Where(v => !double.IsInfinity(v) && !double.IsNaN(v))
                        .ToList();

                    if (values.Count == 0) continue;

                    result.ByComponent[comp][metric] = new ComponentStats
                    {
                        Mean = values.Average(),
                        Std = StdDev(values),
                        Min = values.Min(),
                        Max = values.Max(),
                    };
                }
            }

            return result;
        }

        private double GetMetric(FrameComponentMetrics m, string name) => name switch
        {
            "MAE" => m.MAE,
            "MSE" => m.MSE,
            "PSNR" => m.PSNR,
            "DiffMax" => m.DiffMax,
            _ => 0
        };

        private double StdDev(List<double> values)
        {
            double mean = values.Average();
            return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
        }
    }
}
