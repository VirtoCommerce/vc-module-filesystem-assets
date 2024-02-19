using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using System;
using System.IO;
using System.Linq;
using VirtoCommerce.FileSystemAssetsModule.Core;
using VirtoCommerce.Platform.Core;
using Xunit;

namespace VirtoCommerce.Platform.Tests.Assets
{
    [Trait("Category", "Unit")]
    public class FileSystemBlobStorageProviderTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly IOptions<FileSystemBlobOptions> _options;

        public FileSystemBlobStorageProviderTests()
        {
            var tempPath = Path.GetTempPath();
            _tempDirectory = Path.Combine(tempPath, "FileSystemBlobProviderTests");
            Directory.CreateDirectory(_tempDirectory);

            _options = BuildOptions(_tempDirectory);
        }

        private static IOptions<FileSystemBlobOptions> BuildOptions(string tempDirectory)
        {
            var blobContentOptions = new FileSystemBlobOptions
            {
                PublicUrl = "some-public-path",
                RootPath = tempDirectory
            };
            return new OptionsWrapper<FileSystemBlobOptions>(blobContentOptions);
        }

        private static Mock<IHttpContextAccessor> BuildHttpContextAccessor()
        {
            var request = new Mock<HttpRequest>();
            request.Setup(x => x.Scheme).Returns("http");
            request.Setup(x => x.Host).Returns(new HostString("some-test-host.testcompany.com"));

            var httpContext = new Mock<HttpContext>();
            httpContext.Setup(x => x.Request).Returns(request.Object);

            var result = new Mock<IHttpContextAccessor>();
            result.Setup(x => x.HttpContext).Returns(httpContext.Object);

            return result;
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }

        /// <summary>
        /// `OpenWrite` method should return write-only stream.
        /// </summary>
        [Fact]
        public void FileSystemBlobProviderStreamWritePermissionsTest()
        {
            var fsbProvider = new FileSystemBlobProvider(_options, new OptionsWrapper<PlatformOptions>(new PlatformOptions()), null, null);

            using (var actualStream = fsbProvider.OpenWrite("file-write.tmp"))
            {
                Assert.True(actualStream.CanWrite, "'OpenWrite' stream should be writable.");
                Assert.True(actualStream.CanRead, "'OpenWrite' stream should be write-only.");
            }
        }

        /// <summary>
        /// `OpenRead` method should return read-only stream.
        /// </summary>
        [Fact]
        public void FileSystemBlobProviderStreamReadPermissionsTest()
        {
            var fsbProvider = new FileSystemBlobProvider(_options, new OptionsWrapper<PlatformOptions>(new PlatformOptions()), null, null);
            const string fileForRead = "file-read.tmp";

            // Creating empty file.
            File.WriteAllText(Path.Combine(_tempDirectory, fileForRead), string.Empty);

            using (var actualStream = fsbProvider.OpenRead(fileForRead))
            {
                Assert.True(actualStream.CanRead, "'OpenRead' stream should be readable.");
                Assert.False(actualStream.CanWrite, "'OpenRead' stream should be read-only.");
            }
        }


        [Fact]
        public void FileSystemBlobOptions_CanValidateDataAnnotations()
        {
            //Arrange
            var services = new ServiceCollection();
            services.AddOptions<FileSystemBlobOptions>()
                .Configure(o =>
                {
                    o.RootPath = null;
                    o.PublicUrl = "wrong url";
                })
                .ValidateDataAnnotations();

            //Act
            var sp = services.BuildServiceProvider();

            //Assert
            var error = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<FileSystemBlobOptions>>().Value);
            ValidateFailure<FileSystemBlobOptions>(error, Options.DefaultName, 2,
                $"DataAnnotation validation failed for '{nameof(FileSystemBlobOptions)}' members: '{nameof(FileSystemBlobOptions.RootPath)}' with the error: 'The {nameof(FileSystemBlobOptions.RootPath)} field is required.'.",
                $"DataAnnotation validation failed for '{nameof(FileSystemBlobOptions)}' members: '{nameof(FileSystemBlobOptions.PublicUrl)}' with the error: 'The {nameof(FileSystemBlobOptions.PublicUrl)} field is not a valid fully-qualified http, https, or ftp URL.");
        }

        private void ValidateFailure<TOptions>(OptionsValidationException ex, string name = "", int count = 1, params string[] errorsToMatch)
        {
            Assert.Equal(typeof(TOptions), ex.OptionsType);
            Assert.Equal(name, ex.OptionsName);
            if (errorsToMatch.Length == 0)
            {
                errorsToMatch = new string[] { "A validation error has occured." };
            }
            Assert.Equal(count, ex.Failures.Count());
            // Check for the error in any of the failures
            foreach (var error in errorsToMatch)
            {
                Assert.True(ex.Failures.FirstOrDefault(f => f.Contains(error)) != null, "Did not find: " + error);
            }
        }
    }
}
