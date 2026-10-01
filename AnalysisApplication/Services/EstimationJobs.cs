using System.Collections.Concurrent;
using AnalysisApplication.Components;

namespace AnalysisApplication.Services
{
    public sealed class EstimationJob
    {
        public string Id { get; set; } = string.Empty;
        public int SourceId { get; set; }
        public int CompressedId { get; set; }
        public string SourceName { get; set; } = string.Empty;
        public string CompressedName { get; set; } = string.Empty;
        public int BitrateKbps { get; set; }
        public string State { get; set; } = "running";
        public int Done { get; set; }
        public int Total { get; set; }
        public double Fps { get; set; }
        public int WorstFrameIndex { get; set; } = -1;
        public double WorstFrameMae { get; set; }
        public string? Error { get; set; }
        public string? Warning { get; set; }
        public List<FrameMetrics> Frames { get; set; } = new();
        public AggregatedMetrics? Aggregated { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public sealed class EstimationJobs
    {
        private readonly ConcurrentDictionary<string, EstimationJob> _jobs = new();

        public EstimationJob Create(int sourceId, string sourceName, int compressedId, string compressedName, int bitrateKbps)
        {
            Cleanup();
            EstimationJob job = new EstimationJob
            {
                Id = Guid.NewGuid().ToString("N"),
                SourceId = sourceId,
                SourceName = sourceName,
                CompressedId = compressedId,
                CompressedName = compressedName,
                BitrateKbps = bitrateKbps
            };
            _jobs[job.Id] = job;
            return job;
        }

        public EstimationJob? Get(string id) => _jobs.TryGetValue(id, out EstimationJob? job) ? job : null;

        public void Progress(string id, int done, int total)
        {
            if (!_jobs.TryGetValue(id, out EstimationJob? job)) return;
            job.Done = done;
            job.Total = total;
        }

        public void Warn(string id, string warning)
        {
            if (!_jobs.TryGetValue(id, out EstimationJob? job)) return;
            job.Warning = warning;
        }

        public void Complete(string id, List<FrameMetrics> frames, AggregatedMetrics aggregated, double fps, int worstFrameIndex, double worstFrameMae)
        {
            if (!_jobs.TryGetValue(id, out EstimationJob? job)) return;
            job.Frames = frames;
            job.Aggregated = aggregated;
            job.Total = frames.Count;
            job.Fps = fps;
            job.WorstFrameIndex = worstFrameIndex;
            job.WorstFrameMae = worstFrameMae;
            job.Done = frames.Count;
            job.State = "done";
        }

        public void Fail(string id, string error)
        {
            if (!_jobs.TryGetValue(id, out EstimationJob? job)) return;
            job.Error = error;
            job.State = "failed";
        }

        private void Cleanup()
        {
            DateTime limit = DateTime.UtcNow.AddHours(-2);
            foreach (KeyValuePair<string, EstimationJob> pair in _jobs)
            {
                if (pair.Value.CreatedAt < limit) _jobs.TryRemove(pair.Key, out _);
            }
        }
    }
}
