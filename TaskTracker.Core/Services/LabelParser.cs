namespace TaskTracker.Core.Services
{
    public static class LabelParser
    {
        /// <summary>Parses a comma-separated label string into trimmed, distinct labels.</summary>
        public static List<string> Parse(string? text) =>
            (text ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}
