using System;
using System.Collections.Generic;

namespace SwiftList.PluginSdk
{
    /// <summary>
    /// Registry for active path collectors loaded from plugins.
    /// </summary>
    public static class ActivePathCollectorRegistry
    {
        private static readonly List<IActivePathCollector> Collectors = new();

        /// <summary>
        /// Registers a new active path collector.
        /// </summary>
        public static void Register(IActivePathCollector collector)
        {
            if (collector == null) return;
            lock (Collectors)
            {
                if (!Collectors.Contains(collector))
                {
                    Collectors.Add(collector);
                }
            }
        }

        /// <summary>
        /// Retrieves all registered active path collectors.
        /// </summary>
        public static IReadOnlyList<IActivePathCollector> GetCollectors()
        {
            lock (Collectors)
            {
                return Collectors.ToArray();
            }
        }
    }
}
