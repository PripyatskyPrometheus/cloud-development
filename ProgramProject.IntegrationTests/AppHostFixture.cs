using Amazon.Runtime;
using Amazon.S3;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ProgramProject.IntegrationTests;


public class AppHostFixture : IAsyncLifetime
{
    public DistributedApplication App { get; private set; } = null!;
    public IAmazonS3 S3Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.ProgramProject_AppHost>(args: ["DcpPublisher:RandomizePorts=false"]);

        appHost.Services.ConfigureHttpClientDefaults(http =>
            http.AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(60);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
            }));

        App = await appHost.BuildAsync();
        await App.StartAsync();

        await App.ResourceNotifications.WaitForResourceHealthyAsync("cache").WaitAsync(TimeSpan.FromSeconds(60));
        await App.ResourceNotifications.WaitForResourceHealthyAsync("generator-1").WaitAsync(TimeSpan.FromSeconds(60));
        await App.ResourceNotifications.WaitForResourceHealthyAsync("gateway").WaitAsync(TimeSpan.FromSeconds(60));

        await Task.Delay(TimeSpan.FromSeconds(5));

        using var minioClient = App.CreateHttpClient("minio", "http");
        var minioUrl = minioClient.BaseAddress!.ToString().TrimEnd('/');

        S3Client = new AmazonS3Client(
            new BasicAWSCredentials("minioadmin", "minioadmin"),
            new AmazonS3Config
            {
                ServiceURL = minioUrl,
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1"
            });
    }

    /// <summary>
    /// Ожидаем появления файла в Minio по указанному префиксу
    /// </summary>
    public async Task<List<Amazon.S3.Model.S3Object>> WaitForS3ObjectAsync(string prefix, int maxAttempts = 15)
    {
        for (var i = 0; i < maxAttempts; i++)
        {
            try
            {
                var listResponse = await S3Client.ListObjectsV2Async(new Amazon.S3.Model.ListObjectsV2Request
                {
                    BucketName = "projects",
                    Prefix = prefix
                });

                if (listResponse.S3Objects.Count > 0)
                    return listResponse.S3Objects;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при проверке Minio (попытка {i + 1}): {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        return new List<Amazon.S3.Model.S3Object>();
    }

    public async Task DisposeAsync()
    {
        S3Client?.Dispose();
        try
        {
            await App.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException) { }
        catch (OperationCanceledException) { }
    }
}