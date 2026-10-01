namespace AnalysisApplication.Models
{
    public sealed class VideoItem
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public bool IsCompressed { get; set; } = false;
        public int? SourceVideoId { get; set; } = null;
        public int BitrateKbps { get; set; } = 0;
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }
}
