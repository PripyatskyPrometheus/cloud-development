using Amazon.SQS;
using Amazon.SQS.Model;
using Minio;
using Minio.DataModel.Args;

namespace ProgramProject.FileService.Services;

/// <summary>
/// Инициализация инфраструктуры (очередь SQS, бакет Minio)
/// </summary>
public class InfrastructureInitializer(
    IAmazonSQS sqsClient,
    IMinioClient minioClient,
    IConfiguration configuration,
    ILogger<InfrastructureInitializer> logger) : IHostedService
{
    private readonly string _bucketName = configuration["Minio:BucketName"] ?? "projects";
    private readonly string _queueName = configuration["AWS:Resources:SQSQueueName"] ?? "projects";

    public static string? QueueUrl { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await EnsureQueueExistsAsync(cancellationToken);
        await EnsureBucketExistsAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureQueueExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var createQueueRequest = new CreateQueueRequest
            {
                QueueName = _queueName,
                Attributes = new Dictionary<string, string> { { "VisibilityTimeout", "30" } }
            };
            var response = await sqsClient.CreateQueueAsync(createQueueRequest, cancellationToken);
            QueueUrl = response.QueueUrl;
            logger.LogInformation("Очередь создана или уже существует: {QueueUrl}", QueueUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при создании очереди");
        }
    }

    private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var bucketExists = await minioClient.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(_bucketName), cancellationToken);

            if (!bucketExists)
            {
                await minioClient.MakeBucketAsync(
                    new MakeBucketArgs().WithBucket(_bucketName), cancellationToken);
                logger.LogInformation("Бакет {BucketName} создан", _bucketName);
            }
            else
            {
                logger.LogInformation("Бакет {BucketName} уже существует", _bucketName);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при создании бакета");
        }
    }
}
