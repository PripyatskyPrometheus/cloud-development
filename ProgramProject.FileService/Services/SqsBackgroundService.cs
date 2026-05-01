using Amazon.SQS;
using Amazon.SQS.Model;
using Minio;
using Minio.DataModel.Args;
using System.Text.Json;
using ProgramProject.GenerationService.Models;

namespace ProgramProject.FileService.Services;

/// <summary>
/// Фоновый сервис для чтения сообщений из SQS и сохранения в Minio
/// </summary>
public class SqsBackgroundService : BackgroundService
{
    private readonly IAmazonSQS _sqsClient;
    private readonly IMinioClient _minioClient;
    private readonly ILogger<SqsBackgroundService> _logger;
    private readonly string _queueUrl;
    private readonly string _bucketName;

    public SqsBackgroundService(
        IAmazonSQS sqsClient,
        IMinioClient minioClient,
        IConfiguration configuration,
        ILogger<SqsBackgroundService> logger)
    {
        _sqsClient = sqsClient;
        _minioClient = minioClient;
        _logger = logger;

        _queueUrl = configuration["SQS:QueueUrl"] ?? "http://localhost:9324/queue/projects";
        _bucketName = configuration["Minio:BucketName"] ?? "projects";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SQS Background Service запущен");

        await EnsureQueueExistsAsync(stoppingToken);

        await EnsureBucketExistsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var request = new ReceiveMessageRequest
                {
                    QueueUrl = _queueUrl,
                    MaxNumberOfMessages = 1,
                    WaitTimeSeconds = 10,
                    VisibilityTimeout = 30
                };

                var response = await _sqsClient.ReceiveMessageAsync(request, stoppingToken);

                foreach (var message in response.Messages)
                {
                    _logger.LogInformation("Получено сообщение: {MessageBody}", message.Body);

                    var project = JsonSerializer.Deserialize<ProgramProjectModel>(message.Body);

                    if (project != null)
                    {
                        await SaveToMinioAsync(project, stoppingToken);

                        await _sqsClient.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                        _logger.LogInformation("Проект {ProjectId} сохранён и сообщение удалено", project.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке сообщения из SQS");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task EnsureQueueExistsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var createQueueRequest = new CreateQueueRequest
            {
                QueueName = "projects",
                Attributes = new Dictionary<string, string>
                {
                    { "VisibilityTimeout", "30" }
                }
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
            var bucketExists = await _minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucketName), cancellationToken);

            if (!bucketExists)
            {
                await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucketName), cancellationToken);
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

    private async Task SaveToMinioAsync(ProgramProjectModel project, CancellationToken cancellationToken)
    {
        var jsonContent = JsonSerializer.SerializeToUtf8Bytes(project, new JsonSerializerOptions { WriteIndented = true });
        var fileName = $"project_{project.Id}.json";

        using var memoryStream = new MemoryStream(jsonContent);

        var putObjectArgs = new PutObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(fileName)
            .WithStreamData(memoryStream)
            .WithObjectSize(memoryStream.Length)
            .WithContentType("application/json");

        await _minioClient.PutObjectAsync(putObjectArgs, cancellationToken);
        _logger.LogInformation("Проект {ProjectId} сохранён в Minio: {FileName}", project.Id, fileName);
    }
}