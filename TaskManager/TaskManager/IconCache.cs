using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Media;

namespace TaskManager
{
    public static class IconCache
    {
        // afile path as the key and the ImageSource as the value
        private static readonly Dictionary<string, ImageSource> _iconCache = new();

        public static bool TryGetIcon(string path, out ImageSource? icon)
        {
            return _iconCache.TryGetValue(path, out icon);
        }

        public static void AddIcon(string path, ImageSource icon)
        {
            if (!_iconCache.ContainsKey(path))
            {
                _iconCache.Add(path, icon);
            }
        }
    }
}