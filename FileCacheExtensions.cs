using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace CoreFileCache
{
    public static class FileCacheServiceCollectionExtensions
    {
        public static IServiceCollection AddCoreFileCache(this IServiceCollection services,
            Action<FileCacheOptions> setupAction)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (setupAction == null)
            {
                throw new ArgumentNullException(nameof(setupAction));
            }

            services.AddOptions();
            services.AddSingleton<FileCache>();
            services.AddSingleton<IDistributedCache, FileCache>(services => services.GetRequiredService<FileCache>());
            services.Configure(setupAction);
            return services;
        }

        public static IServiceCollection AddCoreFileCache(this IServiceCollection services, string path, int cleanup_minute)
        {
            return AddCoreFileCache(services, options => {
                options.CachePath = path;
                options.CleanupInterval = TimeSpan.FromMinutes(cleanup_minute);
                });
        }
    }
}
