using System.Collections.Concurrent;
using Microsoft.UI.Xaml.Media;

namespace TaskManager.Helper
{
    /// <summary>
    /// Class IconCache is responsible for caching icons to improve performance and reduce redundant loading of icons from disk.
    /// </summary>
    public static class IconCache
    {
        // ConcurrentDictionary ist sicher für Multi-Threading
        private static readonly ConcurrentDictionary<string, ImageSource> _iconCache = new();

        /// <summary>
        /// Method to try to get an icon from the cache based on the provided path. 
        /// It returns true if the icon is found in the cache, otherwise false.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="icon"></param>
        /// <returns></returns>
        public static bool TryGetIcon(string path, out ImageSource icon)
        {
            return _iconCache.TryGetValue(path, out icon);
        }

        /// <summary>
        /// Method to add an icon.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="icon"></param>
        public static void AddIcon(string path, ImageSource icon)
        {
            // TryAdd fügt nur hinzu, wenn der Key noch nicht existiert
            _iconCache.TryAdd(path, icon);
        }
    }
}