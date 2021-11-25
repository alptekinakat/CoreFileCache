using Microsoft.Extensions.Options;
using System;


namespace CoreFileCache
{
    public class FileCacheOptions : IOptions<FileCacheOptions>
    {
        FileCacheOptions IOptions<FileCacheOptions>.Value => this;
        public string CachePath { get; set; } = "";
        public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(30);
    }
}
