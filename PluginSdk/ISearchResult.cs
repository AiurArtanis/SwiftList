namespace SwiftList.App.Services
{
    /// <summary>
    /// Read-only search result data structure exposed to plugins.
    /// </summary>
    public interface ISearchResult
    {
        /// <summary>Name of the file or folder.</summary>
        string Name { get; }

        /// <summary>Full absolute path of the file or folder.</summary>
        string FullPath { get; }

        /// <summary>Directory context where the result action is invoked.</summary>
        string ContextDirectory { get; }

        /// <summary>True if this is a directory, false if a file.</summary>
        bool IsDir { get; }

        /// <summary>True if this search result represents an application.</summary>
        bool IsApplication { get; }

        /// <summary>Last modified date of the file or folder.</summary>
        System.DateTime DateModified { get; }
    }
}
