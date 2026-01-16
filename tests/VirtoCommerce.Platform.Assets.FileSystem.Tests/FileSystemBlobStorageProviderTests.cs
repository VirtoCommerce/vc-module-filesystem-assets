using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using VirtoCommerce.AssetsModule.Core.Assets;
using VirtoCommerce.AssetsModule.Core.Services;
using VirtoCommerce.FileSystemAssetsModule.Core;
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
                PublicUrl = "https://localhost:5001/assets",
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
            var mockFileExtensionService = new Mock<IFileExtensionService>();
            mockFileExtensionService.Setup(service => service.IsExtensionAllowedAsync(It.IsAny<string>())).ReturnsAsync(true);

            var mockLogger = new Mock<ILogger<FileSystemBlobProvider>>();

            var fsbProvider = new FileSystemBlobProvider(_options, mockFileExtensionService.Object, null, mockLogger.Object);

            using (var actualStream = fsbProvider.OpenWrite("file-write.tmp"))
            {
                Assert.True(actualStream.CanWrite, "'OpenWrite' stream should be writable.");
                Assert.False(actualStream.CanRead, "'OpenWrite' stream should be write-only.");
            }
        }

        /// <summary>
        /// `OpenRead` method should return read-only stream.
        /// </summary>
        [Fact]
        public void FileSystemBlobProviderStreamReadPermissionsTest()
        {
            var mockFileExtensionService = new Mock<IFileExtensionService>();
            mockFileExtensionService.Setup(service => service.IsExtensionAllowedAsync(It.IsAny<string>())).ReturnsAsync(true);

            var mockLogger = new Mock<ILogger<FileSystemBlobProvider>>();

            var fsbProvider = new FileSystemBlobProvider(_options, mockFileExtensionService.Object, null, mockLogger.Object);
            const string fileForRead = "file-read.tmp";

            // Creating empty file.
            File.WriteAllText(Path.Combine(_tempDirectory, fileForRead), string.Empty);

            using (var actualStream = fsbProvider.OpenRead(fileForRead))
            {
                Assert.True(actualStream.CanRead, "'OpenRead' stream should be readable.");
                Assert.False(actualStream.CanWrite, "'OpenRead' stream should be read-only.");
            }
        }

        /// <summary>
        /// Test that retry policy resolves IOException when file is locked by another process.
        /// </summary>
        [Fact]
        public async Task OpenReadAsync_ShouldRetryOnIOException_WhenFileIsLocked()
        {
            // Arrange
            var mockFileExtensionService = new Mock<IFileExtensionService>();
            mockFileExtensionService.Setup(service => service.IsExtensionAllowedAsync(It.IsAny<string>())).ReturnsAsync(true);

            var mockLogger = new Mock<ILogger<FileSystemBlobProvider>>();

            var fsbProvider = new FileSystemBlobProvider(_options, mockFileExtensionService.Object, null, mockLogger.Object);
            const string fileName = "locked-file.tmp";
            var filePath = Path.Combine(_tempDirectory, fileName);

            // Create a file with some content
            await File.WriteAllTextAsync(filePath, "test content");

            FileStream lockingStream = null;
            Stream resultStream = null;

            try
            {
                // Lock the file exclusively (no sharing)
                lockingStream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

                // Start a task that will release the lock after a short delay
                var unlockTask = Task.Run(async () =>
                {
                    await Task.Delay(100); // Release after 100ms (should trigger 1-2 retries)
                    lockingStream?.Dispose();
                    lockingStream = null;
                });

                // Act - This should retry and eventually succeed after the lock is released
                resultStream = await fsbProvider.OpenReadAsync(fileName);

                // Assert
                Assert.NotNull(resultStream);
                Assert.True(resultStream.CanRead);

                // Verify that retry logging was called (at least one retry should have occurred)
                mockLogger.Verify(
                    x => x.Log(
                        LogLevel.Warning,
                        It.IsAny<EventId>(),
                        It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Retry attempt")),
                        It.IsAny<Exception>(),
                        It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                    Times.AtLeastOnce);

                await unlockTask; // Ensure unlock task completes
            }
            finally
            {
                resultStream?.Dispose();
                lockingStream?.Dispose();
            }
        }

        /// <summary>
        /// Test that retry policy eventually fails after max retries when file remains locked.
        /// </summary>
        [Fact]
        public async Task OpenReadAsync_ShouldFailAfterMaxRetries_WhenFilePermanentlyLocked()
        {
            // Arrange
            var mockFileExtensionService = new Mock<IFileExtensionService>();
            mockFileExtensionService.Setup(service => service.IsExtensionAllowedAsync(It.IsAny<string>())).ReturnsAsync(true);

            var mockLogger = new Mock<ILogger<FileSystemBlobProvider>>();

            var fsbProvider = new FileSystemBlobProvider(_options, mockFileExtensionService.Object, null, mockLogger.Object);
            const string fileName = "permanently-locked-file.tmp";
            var filePath = Path.Combine(_tempDirectory, fileName);

            // Create a file with some content
            await File.WriteAllTextAsync(filePath, "test content");

            FileStream lockingStream = null;

            try
            {
                // Lock the file exclusively and keep it locked
                lockingStream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

                // Act & Assert - Should throw IOException after exhausting retries
                await Assert.ThrowsAsync<IOException>(async () =>
                {
                    await fsbProvider.OpenReadAsync(fileName);
                });

                // Verify that multiple retry attempts were logged (should be 3 retries based on our config)
                mockLogger.Verify(
                    x => x.Log(
                        LogLevel.Warning,
                        It.IsAny<EventId>(),
                        It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Retry attempt")),
                        It.IsAny<Exception>(),
                        It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                    Times.Exactly(3)); // Should retry exactly 3 times before failing
            }
            finally
            {
                lockingStream?.Dispose();
            }
        }

        /// <summary>
        /// Test that retry policy is not executed for FileNotFoundException.
        /// </summary>
        [Fact]
        public async Task OpenReadAsync_ShouldNotRetry_WhenFileIsMissing()
        {
            // Arrange
            var mockFileExtensionService = new Mock<IFileExtensionService>();
            mockFileExtensionService.Setup(service => service.IsExtensionAllowedAsync(It.IsAny<string>())).ReturnsAsync(true);

            var mockLogger = new Mock<ILogger<FileSystemBlobProvider>>();

            var fsbProvider = new FileSystemBlobProvider(_options, mockFileExtensionService.Object, null, mockLogger.Object);
            const string missingFile = "missing-file.tmp";

            // Act & Assert - FileNotFoundException should be thrown immediately without retries
            await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            {
                await fsbProvider.OpenReadAsync(missingFile);
            });

            // Verify that retry logging never occurred
            mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Retry attempt")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Never);
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

        [Theory]
        [InlineData("https://localhost:5001/assets/catalog/151349/epson printer.txt?test=Name With Space", "https://localhost:5001/assets/catalog/151349/epson%20printer.txt?test=Name%20With%20Space")]
        [InlineData("catalog/151349/epsonprinter.txt", "https://localhost:5001/assets/catalog/151349/epsonprinter.txt")]
        [InlineData("catalog/151349/epson printer.txt", "https://localhost:5001/assets/catalog/151349/epson%20printer.txt")]
        [InlineData("catalog/151349/epson%20printer.txt?test=Name%20With%20Space", "https://localhost:5001/assets/catalog/151349/epson%20printer.txt?test=Name%20With%20Space")]
        [InlineData("catalog/151349/epson printer.txt?test=Name With Space", "https://localhost:5001/assets/catalog/151349/epson%20printer.txt?test=Name%20With%20Space")]
        [InlineData("/catalog/151349/epsonprinter.txt", "https://localhost:5001/assets/catalog/151349/epsonprinter.txt")]
        [InlineData("/catalog/151349/epson printer.txt", "https://localhost:5001/assets/catalog/151349/epson%20printer.txt")]
        [InlineData("/catalog/151349/epson%20printer.txt?test=Name%20With%20Space", "https://localhost:5001/assets/catalog/151349/epson%20printer.txt?test=Name%20With%20Space")]
        [InlineData("/catalog/151349/epson printer.txt?test=Name With Space", "https://localhost:5001/assets/catalog/151349/epson%20printer.txt?test=Name%20With%20Space")]
        [InlineData("epsonprinter.txt", "https://localhost:5001/assets/epsonprinter.txt")]
        [InlineData("/epson printer.txt", "https://localhost:5001/assets/epson%20printer.txt")]
        [InlineData("epson%20printer.txt?test=Name%20With%20Space", "https://localhost:5001/assets/epson%20printer.txt?test=Name%20With%20Space")]
        [InlineData("/epson printer.txt?test=Name With Space", "https://localhost:5001/assets/epson%20printer.txt?test=Name%20With%20Space")]
        [InlineData("/epson%20printer.txt?test=Name+With+Space", "https://localhost:5001/assets/epson%20printer.txt?test=Name+With+Space")]
        [InlineData("https://localhost:5001/assets/catalog/151349/epsonprinter.txt", "https://localhost:5001/assets/catalog/151349/epsonprinter.txt")]
        [InlineData("https://localhost:5001/assets/catalog/151349/epson printer.txt", "https://localhost:5001/assets/catalog/151349/epson%20printer.txt")]
        [InlineData("https://localhost:5001/assets/catalog/151349/epson%20printer.txt?test=Name%20With%20Space", "https://localhost:5001/assets/catalog/151349/epson%20printer.txt?test=Name%20With%20Space")]
        public void GetAbsoluteUrlTest(string blobKey, string absoluteUrl)
        {
            var mockFileExtensionService = new Mock<IFileExtensionService>();
            mockFileExtensionService.Setup(service => service.IsExtensionAllowedAsync(It.IsAny<string>())).ReturnsAsync(true);

            var mockLogger = new Mock<ILogger<FileSystemBlobProvider>>();

            var fsbProvider = new FileSystemBlobProvider(_options, mockFileExtensionService.Object, null, mockLogger.Object);
            var blobUrlResolver = (IBlobUrlResolver)fsbProvider;

            Assert.Equal(absoluteUrl, blobUrlResolver.GetAbsoluteUrl(blobKey));
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
