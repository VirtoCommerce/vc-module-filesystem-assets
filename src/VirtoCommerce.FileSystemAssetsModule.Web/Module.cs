using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtoCommerce.FileSystemAssetsModule.Core;
using VirtoCommerce.FileSystemAssetsModule.Core.Extensions;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Modularity;
using VirtoCommerce.FileSystemAssetsModule.Web.Extensions;

namespace VirtoCommerce.FileSystemAssetsModule.Web
{
    public class Module : IModule, IHasConfiguration
    {
        public IConfiguration Configuration { get; set; }
        public ManifestModuleInfo ModuleInfo { get; set; }

        public void Initialize(IServiceCollection serviceCollection)
        {
            var assetsProvider = Configuration.GetSection("Assets:Provider").Value;
            if (assetsProvider.EqualsInvariant(FileSystemBlobProvider.ProviderName))
            {
                serviceCollection.AddOptions<FileSystemBlobOptions>().Bind(Configuration.GetSection("Assets:FileSystem"))
                    .PostConfigure<IWebHostEnvironment>((opts, env) =>
                    {
                        opts.RootPath = env.MapPath(opts.RootPath);
                    }).ValidateDataAnnotations();
                serviceCollection.AddFileSystemBlobProvider();

            }
        }

        public void PostInitialize(IApplicationBuilder appBuilder)
        {
            // Method intentionally left empty
        }

        public void Uninstall()
        {
            // Method intentionally left empty
        }
    }
}
