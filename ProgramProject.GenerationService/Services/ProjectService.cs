using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Caching.Distributed;
using ProgramProject.GenerationService.Generator;
using ProgramProject.GenerationService.Models;
using System.Text.Json;

namespace ProgramProject.GenerationService.Services;

/// <summary>
/// Сервис работы с кэшем и с объектами
/// </summary>
public class ProjectService : IProjectService
{
    private readonly IDistributedCache _cache;
    private readonly IProgramProjectFaker _faker;
    private readonly ILogger<ProjectService> _logger;
    private readonly DistributedCacheEntryOptions _cacheOptions;
    private readonly IAmazonSQS _sqsClient;
    private readonly string _queueName;
    private string? _queueUrl;

    public ProjectService(
        IDistributedCache cache,
        IProgramProjectFaker faker,
        ILogger<ProjectService> logger,
        IConfiguration configuration,
        IAmazonSQS sqsClient)
    {
        _cache = cache;
        _faker = faker;
        _logger = logger;
        _sqsClient = sqsClient;

        var cacheMinutes = configuration.GetValue("Cache:ExpirationMinutes", 5);
        _cacheOptions = new DistributedCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(cacheMinutes));

        _queueName = configuration["AWS:Resources:SQSQueueName"] ?? "projects";
    }

    public async Task<ProgramProjectModel> GetProjectByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"project:{id}";

        try
        {
            // Получаем из кэша
            var cachedBytes = await _cache.GetAsync(cacheKey, cancellationToken);

            if (cachedBytes != null)
            {
                var cachedProject = JsonSerializer.Deserialize<ProgramProjectModel>(cachedBytes);

                if (cachedProject != null)
                {
                    _logger.LogInformation("Проект с ID {ProjectId} найден в кэше", id);
                    return cachedProject;
                }

                _logger.LogWarning("Проект с ID {ProjectId} найден в кэше, но повреждён. Удаляем.", id);
                await _cache.RemoveAsync(cacheKey, cancellationToken);
            }

            _logger.LogInformation("Проект с ID {ProjectId} не найден в кэше. Генерируем новый", id);

            // Генерируем новый проект
            var newProject = _faker.Generate();
            newProject.Id = id;

            // Сохраняем в кэш
            var serializedProject = JsonSerializer.SerializeToUtf8Bytes(newProject);
            await _cache.SetAsync(cacheKey, serializedProject, _cacheOptions, cancellationToken);

            _logger.LogInformation("Проект с ID {ProjectId} сгенерирован и сохранён в кэш", id);

            // Отправляем проект в SQS 
            await SendToSqsAsync(newProject, cancellationToken);

            return newProject;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении проекта с ID {ProjectId}", id);
            throw;
        }
    }

    /// <summary>
    /// Получение URL очереди по имени
    /// </summary>
    private async Task<string> GetQueueUrlAsync(CancellationToken cancellationToken)
    {
        if (_queueUrl == null)
        {
            var response = await _sqsClient.GetQueueUrlAsync(_queueName, cancellationToken);
            _queueUrl = response.QueueUrl;
            _logger.LogInformation("URL очереди получен: {QueueUrl}", _queueUrl);
        }
        return _queueUrl;
    }

    /// <summary>
    /// Отправка проекта в очередь SQS для последующего сохранения в Minio
    /// </summary>
    private async Task SendToSqsAsync(ProgramProjectModel project, CancellationToken cancellationToken)
    {
        try
        {
            var queueUrl = await GetQueueUrlAsync(cancellationToken);
            var json = JsonSerializer.Serialize(project);
            var sendRequest = new SendMessageRequest
            {
                QueueUrl = queueUrl,
                MessageBody = json
            };

            await _sqsClient.SendMessageAsync(sendRequest, cancellationToken);
            _logger.LogInformation("Проект с ID {ProjectId} отправлен в SQS", project.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при отправке проекта {ProjectId} в SQS", project.Id);
        }
    }
}