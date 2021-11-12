using Microsoft.Extensions.DependencyInjection;
using System;
using VirtoCommerce.Assets.Abstractions;
using VirtoCommerce.AssetsModule.Core.Assets;

namespace VirtoCommerce.FileSystemAssetsModule.Core.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static void AddFileSystemBlobProvider(this IServiceCollection services, Action<FileSystemBlobOptions> setupAction = null)
        {
            services.AddSingleton<ICommonBlobProvider, FileSystemBlobProvider>();
            services.AddSingleton<IBlobStorageProvider, FileSystemBlobProvider>();
            services.AddSingleton<IBlobUrlResolver, FileSystemBlobProvider>();
            if (setupAction != null)
            {
                services.Configure(setupAction);
            }
        }
    }
}
