using Minio;
using Minio.DataModel.Args;
using System.Text;
using System.Text.Json;

namespace ProgramProject.FileService.Services;

/// <summary>
/// Сохранение проектов в Minio
/// </summary>
public class ProjectSaver(
    IMinioClient minioClient,
    IConfiguration configuration,
    ILogger<ProjectSaver> logger) : IProjectSaver
{
    private readonly string _bucketName = configuration["Minio:BucketName"] ?? "projects";

    public async Task SaveAsync(string jsonContent, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(jsonContent);
        var id = document.RootElement.GetProperty("Id").GetInt32();
        var fileName = $"project_{id}.json";

        var bytes = Encoding.UTF8.GetBytes(jsonContent);
        using var memoryStream = new MemoryStream(bytes);

        var putObjectArgs = new PutObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(fileName)
            .WithStreamData(memoryStream)
            .WithObjectSize(memoryStream.Length)
            .WithContentType("application/json");

        await minioClient.PutObjectAsync(putObjectArgs, cancellationToken);
        logger.LogInformation("Проект {ProjectId} сохранён в Minio: {FileName}", id, fileName);
    }
}