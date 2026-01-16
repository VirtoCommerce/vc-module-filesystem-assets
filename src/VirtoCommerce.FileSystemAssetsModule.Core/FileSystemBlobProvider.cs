using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using VirtoCommerce.Assets.Abstractions;
using VirtoCommerce.AssetsModule.Core.Assets;
using VirtoCommerce.AssetsModule.Core.Events;
using VirtoCommerce.AssetsModule.Core.Model;
using VirtoCommerce.AssetsModule.Core.Services;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.Platform.Core.Events;
using VirtoCommerce.Platform.Core.Exceptions;

namespace VirtoCommerce.FileSystemAssetsModule.Core
{
    public class FileSystemBlobProvider : IBlobStorageProvider, IBlobUrlResolver, ICommonBlobProvider
    {
        public const string ProviderName = "FileSystem";

        private readonly string _storagePath;
        private readonly string _basePublicUrl;
        private readonly IFileExtensionService _fileExtensionService;
        private readonly IEventPublisher _eventPublisher;
        private readonly ILogger<FileSystemBlobProvider> _logger;
        private readonly ResiliencePipeline _retryPipeline;


        public FileSystemBlobProvider(
            IOptions<FileSystemBlobOptions> options,
            IFileExtensionService fileExtensionService,
            IEventPublisher eventPublisher,
            ILogger<FileSystemBlobProvider> logger)
        {
            // extra replace step to prevent windows path getting into Linux environment
            _storagePath = options.Value.RootPath.TrimEnd(Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            _basePublicUrl = options.Value.PublicUrl;
            _basePublicUrl = _basePublicUrl?.TrimEnd('/');
            _fileExtensionService = fileExtensionService;
            _eventPublisher = eventPublisher;
            _logger = logger;

            // Configure retry pipeline with exponential backoff
            _retryPipeline = new ResiliencePipelineBuilder()
                .AddRetry(new RetryStrategyOptions
                {
                    ShouldHandle = new Polly.PredicateBuilder()
                        .Handle<IOException>(exception =>
                            exception is not FileNotFoundException &&
                            exception is not DirectoryNotFoundException),
                    MaxRetryAttempts = 3,
                    Delay = TimeSpan.FromMilliseconds(50),
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    OnRetry = args =>
                    {
                        _logger.LogWarning(
                            "Retry attempt {AttemptNumber} after {RetryDelay}ms due to {ExceptionType}: {ExceptionMessage}",
                            args.AttemptNumber,
                            args.RetryDelay.TotalMilliseconds,
                            args.Outcome.Exception?.GetType().Name,
                            args.Outcome.Exception?.Message);
                        return default;
                    }
                })
                .Build();
        }

        #region ICommonBlobProvider members

        public bool Exists(string blobUrl)
        {
            return ExistsAsync(blobUrl).GetAwaiter().GetResult();
        }

        public async Task<bool> ExistsAsync(string blobUrl)
        {
            var blobInfo = await GetBlobInfoAsync(blobUrl);
            return blobInfo != null;
        }

        #endregion ICommonBlobProvider members

        #region IBlobStorageProvider members

        /// <summary>
        /// Get blob info by URL
        /// </summary>
        /// <param name="blobUrl"></param>
        /// <returns></returns>
        public virtual Task<BlobInfo> GetBlobInfoAsync(string blobUrl)
        {
            if (string.IsNullOrEmpty(blobUrl))
            {
                throw new ArgumentNullException(nameof(blobUrl));
            }

            BlobInfo result = null;
            var filePath = GetStoragePathFromUrl(blobUrl);

            ValidatePath(filePath);

            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);

                result = AbstractTypeFactory<BlobInfo>.TryCreateInstance();
                result.Url = GetAbsoluteUrlFromPath(fileInfo.DirectoryName, fileInfo.Name);
                result.ContentType = MimeTypeResolver.ResolveContentType(fileInfo.Name);
                result.Size = fileInfo.Length;
                result.Name = fileInfo.Name;
                result.CreatedDate = fileInfo.CreationTimeUtc;
                result.ModifiedDate = fileInfo.LastWriteTimeUtc;
                result.RelativeUrl = GetRelativeUrl(result.Url);
            }

            return Task.FromResult(result);
        }

        /// <summary>
        /// Open blob for read by relative or absolute URL
        /// </summary>
        /// <param name="blobUrl"></param>
        /// <returns>blob stream</returns>
        public virtual Stream OpenRead(string blobUrl)
        {
            var filePath = GetStoragePathFromUrl(blobUrl);

            ValidatePath(filePath);

            return _retryPipeline.Execute(() =>
            {
                return File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            });
        }

        public Task<Stream> OpenReadAsync(string blobUrl)
        {
            return Task.FromResult(OpenRead(blobUrl));
        }

        /// <summary>
        /// Open blob for write by relative or absolute URL
        /// </summary>
        /// <param name="blobUrl"></param>
        /// <returns>blob stream</returns>
        public virtual Stream OpenWrite(string blobUrl)
        {
            return OpenWriteAsync(blobUrl).GetAwaiter().GetResult();
        }

        public async Task<Stream> OpenWriteAsync(string blobUrl)
        {
            var filePath = GetStoragePathFromUrl(blobUrl);
            var folderPath = Path.GetDirectoryName(filePath);

            if (!await _fileExtensionService.IsExtensionAllowedAsync(filePath))
            {
                throw new PlatformException($"File extension {Path.GetExtension(filePath)} is not allowed. Please contact administrator.");
            }

            ValidatePath(filePath);

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            return new BlobUploadStream(File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.None), blobUrl, ProviderName, _eventPublisher);

        }

        /// <summary>
        /// SearchAsync folders and blobs in folder
        /// </summary>
        /// <param name="folderUrl">absolute or relative path</param>
        /// <param name="keyword"></param>
        /// <returns></returns>
        public virtual Task<BlobEntrySearchResult> SearchAsync(string folderUrl, string keyword)
        {
            var result = AbstractTypeFactory<BlobEntrySearchResult>.TryCreateInstance();
            folderUrl ??= _basePublicUrl;

            var storageFolderPath = GetStoragePathFromUrl(folderUrl);

            ValidatePath(storageFolderPath);

            if (!Directory.Exists(storageFolderPath))
            {
                return Task.FromResult(result);
            }
            var directories = string.IsNullOrEmpty(keyword) ? Directory.GetDirectories(storageFolderPath) : Directory.GetDirectories(storageFolderPath, "*" + keyword + "*", SearchOption.AllDirectories);
            foreach (var directory in directories)
            {
                var directoryInfo = new DirectoryInfo(directory);

                var folder = AbstractTypeFactory<BlobFolder>.TryCreateInstance();
                folder.Name = Path.GetFileName(directory);
                folder.Url = GetAbsoluteUrlFromPath(directoryPath: directory);
                folder.ParentUrl = GetAbsoluteUrlFromPath(directoryPath: directoryInfo.Parent?.FullName);
                folder.RelativeUrl = GetRelativeUrl(folder.Url);
                folder.CreatedDate = directoryInfo.CreationTimeUtc;
                folder.ModifiedDate = directoryInfo.LastWriteTimeUtc;
                result.Results.Add(folder);
            }

            var files = string.IsNullOrEmpty(keyword) ? Directory.GetFiles(storageFolderPath) : Directory.GetFiles(storageFolderPath, "*" + keyword + "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);

                var blobInfo = AbstractTypeFactory<BlobInfo>.TryCreateInstance();
                blobInfo.Url = GetAbsoluteUrlFromPath(fileInfo.DirectoryName, fileInfo.Name);
                blobInfo.ContentType = MimeTypeResolver.ResolveContentType(fileInfo.Name);
                blobInfo.Size = fileInfo.Length;
                blobInfo.Name = fileInfo.Name;
                blobInfo.CreatedDate = fileInfo.CreationTimeUtc;
                blobInfo.ModifiedDate = fileInfo.LastWriteTimeUtc;
                blobInfo.RelativeUrl = GetRelativeUrl(blobInfo.Url);
                result.Results.Add(blobInfo);
            }

            result.TotalCount = result.Results.Count;
            return Task.FromResult(result);
        }

        /// <summary>
        /// Create folder in file system within to base directory
        /// </summary>
        /// <param name="folder"></param>
        public virtual Task CreateFolderAsync(BlobFolder folder)
        {
            if (folder == null)
            {
                throw new ArgumentNullException(nameof(folder));
            }
            var path = _storagePath;
            if (folder.ParentUrl != null)
            {
                path = GetStoragePathFromUrl(folder.ParentUrl);
            }
            path = Path.Combine(path, folder.Name);

            ValidatePath(path);

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Remove folders and blobs by absolute or relative URLs
        /// </summary>
        /// <param name="urls"></param>
        public virtual Task RemoveAsync(string[] urls)
        {
            ArgumentNullException.ThrowIfNull(urls);

            var urlsToDelete = urls.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

            if (urlsToDelete.Length == 0)
            {
                return Task.CompletedTask;
            }

            foreach (var url in urlsToDelete)
            {
                var path = GetStoragePathFromUrl(url);

                ValidatePath(path);

                // get the file attributes for file or directory
                var attr = File.GetAttributes(path);

                //detect whether its a directory or file
                if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    Directory.Delete(path, true);
                }
                else
                {
                    File.Delete(path);
                }
            }

            return RaiseBlobDeletedEvent(urlsToDelete);
        }

        public virtual void Move(string srcUrl, string destUrl)
        {
            MoveAsyncPublic(srcUrl, destUrl).GetAwaiter().GetResult();
        }

        public async Task MoveAsyncPublic(string srcUrl, string destUrl)
        {
            var srcPath = GetStoragePathFromUrl(srcUrl);
            var dstPath = GetStoragePathFromUrl(destUrl);

            if (srcPath != dstPath)
            {
                if (Directory.Exists(srcPath) && !Directory.Exists(dstPath))
                {
                    Directory.Move(srcPath, dstPath);
                }
                else if (File.Exists(srcPath) && !File.Exists(dstPath))
                {
                    if (!await _fileExtensionService.IsExtensionAllowedAsync(dstPath))
                    {
                        throw new PlatformException($"File extension {Path.GetExtension(dstPath)} is not allowed. Please contact administrator.");
                    }

                    File.Move(srcPath, dstPath);
                }
            }
        }

        public virtual void Copy(string srcUrl, string destUrl)
        {
            var srcPath = GetStoragePathFromUrl(srcUrl);
            var destPath = GetStoragePathFromUrl(destUrl);

            CopyDirectoryRecursive(srcPath, destPath);
        }

        public Task CopyAsync(string srcUrl, string destUrl)
        {
            Copy(srcUrl, destUrl);

            return Task.CompletedTask;
        }


        protected virtual Task RaiseBlobDeletedEvent(string[] urls)
        {
            if (_eventPublisher != null)
            {
                var entries = urls.Select(url =>
                    new GenericChangedEntry<BlobEventInfo>(new BlobEventInfo
                    {
                        Id = url,
                        Uri = url,
                        Provider = ProviderName,
                    }, EntryState.Deleted)).ToArray();

                return _eventPublisher.Publish(new BlobDeletedEvent(entries));
            }

            return Task.CompletedTask;
        }

        private static void CopyDirectoryRecursive(string sourcePath, string destPath)
        {
            if (!Directory.Exists(destPath))
            {
                Directory.CreateDirectory(destPath);
            }

            foreach (var file in Directory.GetFiles(sourcePath))
            {
                var dest = Path.Combine(destPath, Path.GetFileName(file));
                File.Copy(file, dest);
            }

            foreach (var folder in Directory.GetDirectories(sourcePath))
            {
                var dest = Path.Combine(destPath, Path.GetFileName(folder));
                CopyDirectoryRecursive(folder, dest);
            }
        }

        #endregion IBlobStorageProvider members

        #region IBlobUrlResolver Members

        public virtual string GetAbsoluteUrl(string inputUrl)
        {
            ArgumentNullException.ThrowIfNull(nameof(inputUrl));

            // do trim lead slash to prevent transform it to absolute file path on linux.
            if (Uri.TryCreate(inputUrl.TrimStart('/'), UriKind.Absolute, out var resultUri))
            {
                // If the input URL is already absolute, return it as is (with correct encoding)
                return resultUri.AbsoluteUri;
            }

            if (inputUrl.StartsWith('/'))
            {
                inputUrl = "." + inputUrl;
            }
            else if (!inputUrl.StartsWith('.'))
            {
                inputUrl = "./" + inputUrl;
            }

            var baseUri = new Uri(_basePublicUrl + '/', UriKind.Absolute);
            if (Uri.TryCreate(baseUri, inputUrl, out resultUri))
            {
                // If the input URL is relative, combine it with the base URI
                return resultUri.AbsoluteUri;
            }

            return inputUrl;
        }

        #endregion IBlobUrlResolver Members

        protected string GetRelativeUrl(string url)
        {
            var result = url;
            if (!string.IsNullOrEmpty(_basePublicUrl))
            {
                result = url.Replace(_basePublicUrl, string.Empty);
            }
            return result;
        }

        protected string GetAbsoluteUrlFromPath(string directoryPath, string fileName = null)
        {
            var basePath = _basePublicUrl + "/" + directoryPath.Replace(_storagePath, string.Empty)
                             .TrimStart(Path.DirectorySeparatorChar)
                             .Replace(Path.DirectorySeparatorChar, '/');

            if (string.IsNullOrEmpty(fileName))
            {
                return basePath;
            }

            // escape filename separately
            // 'new Uri(path).ToString()' has the same corruption issues that 'Uri.EscapeUriString()' has
            var escapedFileName = Uri.EscapeDataString(fileName);

            if (!basePath.EndsWith('/'))
            {
                basePath = $"{basePath}/";
            }

            return $"{basePath}{escapedFileName}";
        }

        protected string GetStoragePathFromUrl(string url)
        {
            var result = _storagePath;
            if (url != null)
            {
                result = _storagePath + Path.DirectorySeparatorChar;
                result += GetRelativeUrl(url);
                result = result.Replace('/', Path.DirectorySeparatorChar)
                               .Replace($"{Path.DirectorySeparatorChar}{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}");
            }
            return Uri.UnescapeDataString(result);
        }

        protected void ValidatePath(string path)
        {
            path = Path.GetFullPath(path);
            //Do not allow the use paths located above of  the defined storagePath folder
            //for security reason (avoid the file structure manipulation through using relative paths)
            if (!path.StartsWith(_storagePath))
            {
                throw new PlatformException($"Invalid path {path}");
            }
        }
    }
}
