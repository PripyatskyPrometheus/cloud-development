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
    private readonly string _queueName = configuration["AWS:Resources:SQSQueueName"] ?? "projects";
    private string? _queueUrl;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Message Consumer Service запущен");

        try
        {
            var getQueueUrlResponse = await sqsClient.GetQueueUrlAsync(_queueName, stoppingToken);
            _queueUrl = getQueueUrlResponse.QueueUrl;
            logger.LogInformation("QueueUrl получен: {QueueUrl}", _queueUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось получить URL очереди по имени {QueueName}", _queueName);
            return;
        }

        try
        {
            var queueAttr = await sqsClient.GetQueueAttributesAsync(_queueUrl, new List<string> { "All" }, stoppingToken);
            logger.LogInformation("Очередь существует, атрибуты получены");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось получить атрибуты очереди");
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

                var response = await sqsClient.ReceiveMessageAsync(request, stoppingToken);

                if (response?.Messages == null || !response.Messages.Any())
                {
                    continue;  
                }

                foreach (var message in response.Messages)
                {
                    using var document = JsonDocument.Parse(message.Body);
                    var root = document.RootElement;
                    var id = root.GetProperty("Id").GetInt32();
                    logger.LogInformation("Получен проект с ID {ProjectId}", id);

                    await projectSaver.SaveAsync(message.Body, stoppingToken);
                    await sqsClient.DeleteMessageAsync(_queueUrl, message.ReceiptHandle, stoppingToken);
                    logger.LogInformation("Проект {ProjectId} сохранён и сообщение удалено", id);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка при обработке сообщения из SQS");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}