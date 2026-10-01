using System.Collections.Concurrent;

namespace AnalysisApplication.Services
{
    public sealed class ArtifactJob
    {
        public string Id { get; set; } = string.Empty;
        public int SourceId { get; set; }
        public int CompressedId { get; set; }
        public string Mode { get; set; } = "gray";
        public double Gain { get; set; }
        public string State { get; set; } = "running";
        public int Done { get; set; }
        public int Total { get; set; }
        public string? Error { get; set; }
        public string? FileName { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public sealed class ArtifactJobs
    {
        private readonly ConcurrentDictionary<string, ArtifactJob> _jobs = new();

        public ArtifactJob Create(int sourceId, int compressedId, string mode, double gain, int total)
        {
            Cleanup();
            ArtifactJob job = new ArtifactJob
            {
                Id = Guid.NewGuid().ToString("N"),
                SourceId = sourceId,
                CompressedId = compressedId,
                Mode = mode,
                Gain = gain,
                Total = total
            };
            _jobs[job.Id] = job;
            return job;
        }

        public ArtifactJob? Get(string id) => _jobs.TryGetValue(id, out ArtifactJob? job) ? job : null;

        public void Progress(string id, int done)
        {
            if (!_jobs.TryGetValue(id, out ArtifactJob? job)) return;
            job.Done = done;
            if (done > job.Total) job.Total = done;
        }

        public void Complete(string id, string fileName)
        {
            if (!_jobs.TryGetValue(id, out ArtifactJob? job)) return;
            job.FileName = fileName;
            if (job.Total <= 0) job.Total = job.Done;
            job.Done = job.Total;
            job.State = "done";
        }

        public void Fail(string id, string error)
        {
            if (!_jobs.TryGetValue(id, out ArtifactJob? job)) return;
            job.Error = error;
            job.State = "failed";
        }

        private void Cleanup()
        {
            DateTime limit = DateTime.UtcNow.AddHours(-2);
            foreach (KeyValuePair<string, ArtifactJob> pair in _jobs)
            {
                if (pair.Value.CreatedAt < limit) _jobs.TryRemove(pair.Key, out _);
            }
        }
    }
}
