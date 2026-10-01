namespace AnalysisApplication.Components
{
    public struct FrameMetrics
    {
        public int FrameIndex { get; set; }
        public Dictionary<string, FrameComponentMetrics> Components { get; set; } = new();

        public FrameMetrics(Dictionary<string, FrameComponentMetrics> metrics, int index)
        {
            FrameIndex = index;
            Components = metrics;
        }
    }
}
