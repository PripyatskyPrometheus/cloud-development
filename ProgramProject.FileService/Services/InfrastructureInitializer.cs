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
    private readonly IAmazonSQS _sqsClient = sqsClient;
    private readonly IMinioClient _minioClient = minioClient;
    private readonly ILogger<InfrastructureInitializer> _logger = logger;
    private readonly string _bucketName = configuration["Minio:BucketName"] ?? "projects";
    private readonly string _queueName = "projects";

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
            var response = await _sqsClient.CreateQueueAsync(createQueueRequest, cancellationToken);
            _logger.LogInformation("Очередь создана или уже существует: {QueueUrl}", response.QueueUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании очереди");
        }
    }

    private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var bucketExists = await _minioClient.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(_bucketName), cancellationToken);

            if (!bucketExists)
            {
                await _minioClient.MakeBucketAsync(
                    new MakeBucketArgs().WithBucket(_bucketName), cancellationToken);
                _logger.LogInformation("Бакет {BucketName} создан", _bucketName);
            }
            else
            {
                _logger.LogInformation("Бакет {BucketName} уже существует", _bucketName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании бакета");
        }
    }
}
