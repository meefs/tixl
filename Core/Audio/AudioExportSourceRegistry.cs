using System.Collections.Generic;

namespace T3.Core.Audio
{
    /// <summary>
    /// Registry for audio export sources (e.g., operator instances that provide audio for rendering).
    /// </summary>
    public static class AudioExportSourceRegistry
    {
        private static readonly List<IAudioExportSource> _sources = new();
        public static IReadOnlyList<IAudioExportSource> Sources => _sources;

        public static void Register(IAudioExportSource source)
        {
            if (!_sources.Contains(source))
                _sources.Add(source);
        }

        public static void Unregister(IAudioExportSource source)
        {
            _sources.Remove(source);
        }

        public static void Clear()
        {
            _sources.Clear();
        }
    }
}
