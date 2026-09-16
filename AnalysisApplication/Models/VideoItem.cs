namespace AnalysisApplication.Models
{
    public sealed class VideoItem
    {
        public int Id { get; set; }
        public string FilleName { get; set; }
        public string DisplayName { get; set; }
        public long FileSize { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }
}
