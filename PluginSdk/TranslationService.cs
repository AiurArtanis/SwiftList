using System;

namespace SwiftList.PluginSdk
{
    /// <summary>
    /// A decoupled service to provide runtime dynamic translations to plugins.
    /// </summary>
    public static class TranslationService
    {
        /// <summary>
        /// Delegate function set by the main application to perform multi-language lookup.
        /// </summary>
        public static Func<string, string> LookupFunc { get; set; } = key => $"[{key}]";

        /// <summary>
        /// Gets translation by key.
        /// </summary>
        public static string Get(string key) => LookupFunc(key);

        /// <summary>
        /// Gets formatted translation by key.
        /// </summary>
        public static string Format(string key, params object[] args)
        {
            string fmt = LookupFunc(key);
            try
            {
                return string.Format(fmt, args);
            }
            catch
            {
                return fmt;
            }
        }
    }
}
