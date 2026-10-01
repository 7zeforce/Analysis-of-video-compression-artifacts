namespace AnalysisApplication.Components
{
    public sealed class ComponentStats
    {
        public double Mean { get; set; }
        public double Std { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
    }

    public sealed class AggregatedMetrics
    {
        public Dictionary<string, Dictionary<string, ComponentStats>> ByComponent { get; set; } = new();
    }
}
