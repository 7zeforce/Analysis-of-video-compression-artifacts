using AnalysisApplication.Models;

namespace AnalysisApplication.Services
{
    public sealed class VideoLibary
    {
        private readonly List<VideoItem> _videosItems = new();
        private int _nextId;

        public IReadOnlyList<VideoItem> All => _videosItems;

        public VideoItem Add(string fileName, string displayName, long fileSize)
        {
            VideoItem videoItem = new VideoItem
            {
                Id = _nextId++,
                FilleName = fileName,
                DisplayName = displayName,
                FileSize = fileSize
            };
            _videosItems.Add(videoItem);
            return videoItem;
        }

        public VideoItem? Get(int id) => _videosItems.FirstOrDefault(v => v.Id == id);
    }
}
