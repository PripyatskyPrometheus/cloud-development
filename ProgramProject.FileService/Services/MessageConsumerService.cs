using Amazon.SQS;
using Amazon.SQS.Model;
using System.Text.Json;

namespace ProgramProject.FileService.Services;

/// <summary>
/// Чтение сообщений из очереди SQS
/// </summary>
public class MessageConsumerService(
    IAmazonSQS sqsClient,
    IProjectSaver projectSaver,
    IConfiguration configuration,
    ILogger<MessageConsumerService> logger) : BackgroundService
{
    private readonly IAmazonSQS _sqsClient = sqsClient;
    private readonly IProjectSaver _projectSaver = projectSaver;
    private readonly ILogger<MessageConsumerService> _logger = logger;
    private readonly string _queueUrl = configuration["SQS:QueueUrl"] ?? configuration["SQS__QueueUrl"] 
        ?? "http://localhost:9324/queue/projects";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Message Consumer Service запущен");
        _logger.LogInformation("QueueUrl: {QueueUrl}", _queueUrl);

        // Добавь проверку существования очереди
        try
        {
            var queueAttr = await _sqsClient.GetQueueAttributesAsync(_queueUrl, new List<string> { "All" }, stoppingToken);
            _logger.LogInformation("Очередь существует, атрибуты получены");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось получить атрибуты очереди");
        }

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

                if (response?.Messages == null || !response.Messages.Any())
                {
                    continue;  
                }

                foreach (var message in response.Messages)
                {
                    using var document = JsonDocument.Parse(message.Body);
                    var root = document.RootElement;
                    var id = root.GetProperty("Id").GetInt32();
                    _logger.LogInformation("Получен проект с ID {ProjectId}", id);

                    await _projectSaver.SaveAsync(message.Body, stoppingToken);
                    await _sqsClient.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                    _logger.LogInformation("Проект {ProjectId} сохранён и сообщение удалено", id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке сообщения из SQS");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}