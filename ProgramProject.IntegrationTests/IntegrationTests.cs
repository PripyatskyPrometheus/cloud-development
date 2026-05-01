using Aspire.Hosting.Testing;
using ProgramProject.GenerationService.Models;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ProgramProject.IntegrationTests;

/// <summary>
/// Интеграционные тесты, проверяющие корректную совместную работу всех сервисов бекенда
/// </summary>
public class IntegrationTests(AppHostFixture fixture) : IClassFixture<AppHostFixture>
{
    /// <summary>
    /// Проверяет, что API отвечает на запрос с валидным ID
    /// </summary>
    [Fact]
    public async Task Generator_ReturnsSuccess()
    {
        using var httpClient = fixture.App.CreateHttpClient("generator-1", "http");
        using var response = await httpClient.GetAsync("/api/projects?id=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Проверяет, что запрос с конкретным ID возвращает корректный объект программного проекта
    /// </summary>
    [Fact]
    public async Task Generator_GetById_ReturnsValidProject()
    {
        using var httpClient = fixture.App.CreateHttpClient("generator-1", "http");
        using var response = await httpClient.GetAsync("/api/projects?id=42");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var project = await response.Content.ReadFromJsonAsync<ProgramProjectModel>();
        Assert.NotNull(project);
        Assert.Equal(42, project.Id);
        Assert.False(string.IsNullOrEmpty(project.Name));
        Assert.False(string.IsNullOrEmpty(project.Customer));
        Assert.False(string.IsNullOrEmpty(project.Manager));
        Assert.True(project.Budget > 0);
        Assert.InRange(project.CompletionPercentage, 0, 100);
    }

    /// <summary>
    /// Проверяет работу Redis-кэширования: Два последовательных запроса с одинаковым ID должны вернуть идентичные данные
    /// </summary>
    [Fact]
    public async Task Redis_CachingWorks_ReturnsCachedData()
    {
        var testId = new Random().Next(100, 1000);
        using var httpClient = fixture.App.CreateHttpClient("generator-1", "http");

        using var firstResponse = await httpClient.GetAsync($"/api/projects?id={testId}");
        var firstContent = await firstResponse.Content.ReadAsStringAsync();

        using var secondResponse = await httpClient.GetAsync($"/api/projects?id={testId}");
        var secondContent = await secondResponse.Content.ReadAsStringAsync();

        Assert.Equal(firstContent, secondContent);
    }

    /// <summary>
    /// Проверяет, что генератор возвращает разные данные для разных ID
    /// </summary>
    [Fact]
    public async Task DifferentIds_ReturnDifferentProjects()
    {
        using var httpClient = fixture.App.CreateHttpClient("generator-1", "http");

        var project1 = await httpClient.GetFromJsonAsync<ProgramProjectModel>("/api/projects?id=1001");
        var project2 = await httpClient.GetFromJsonAsync<ProgramProjectModel>("/api/projects?id=1002");

        Assert.NotNull(project1);
        Assert.NotNull(project2);
        Assert.Equal(1001, project1.Id);
        Assert.Equal(1002, project2.Id);
        Assert.NotEqual(project1.Name, project2.Name);
    }

    /// <summary>
    /// Проверяет, что все поля модели программного проекта заполнены корректно
    /// </summary>
    [Fact]
    public async Task AllFieldsPopulated()
    {
        var testId = new Random().Next(2000, 3000);
        using var httpClient = fixture.App.CreateHttpClient("generator-1", "http");

        var project = await httpClient.GetFromJsonAsync<ProgramProjectModel>($"/api/projects?id={testId}");

        Assert.NotNull(project);
        Assert.Equal(testId, project.Id);
        Assert.False(string.IsNullOrEmpty(project.Name));
        Assert.False(string.IsNullOrEmpty(project.Customer));
        Assert.False(string.IsNullOrEmpty(project.Manager));
        Assert.NotEqual(default, project.StartDate);
        Assert.NotEqual(default, project.PlannedEndDate);
        Assert.True(project.Budget > 0);
        Assert.True(project.ActualCost > 0);
        Assert.InRange(project.CompletionPercentage, 0, 100);

        if (project.CompletionPercentage == 100)
        {
            Assert.NotNull(project.ActualEndDate);
        }
    }

    /// <summary>
    /// Сквозной тест, проверяющий полный путь данных через GenerationService,SQS, FileService и Mini
    /// </summary>
    [Fact]
    public async Task Minio_FileSaved()
    {
        using var client = fixture.App.CreateHttpClient("generator-1", "http");
        var testId = new Random().Next(5000, 6000);

        var response = await client.GetAsync($"/api/projects?id={testId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var objects = await fixture.WaitForS3ObjectAsync($"project_{testId}.json");
        Assert.NotEmpty(objects);
    }

    /// <summary>
    /// Расширенный сквозной тест, проверяет соответствие его содержимого данным, возвращённым API
    /// </summary>
    [Fact]
    public async Task Minio_FileContentMatchesApiResponse()
    {
        var testId = new Random().Next(7000, 8000);
        using var httpClient = fixture.App.CreateHttpClient("generator-1", "http");

        var apiProject = await httpClient.GetFromJsonAsync<ProgramProjectModel>($"/api/projects?id={testId}");
        Assert.NotNull(apiProject);

        var objects = await fixture.WaitForS3ObjectAsync($"project_{testId}.json");
        Assert.NotEmpty(objects);

        var getResponse = await fixture.S3Client.GetObjectAsync("projects", objects.First().Key);
        using var reader = new StreamReader(getResponse.ResponseStream);
        var json = await reader.ReadToEndAsync();
        var savedProject = System.Text.Json.JsonSerializer.Deserialize<ProgramProjectModel>(json);

        Assert.NotNull(savedProject);
        Assert.Equal(apiProject.Id, savedProject.Id);
        Assert.Equal(apiProject.Name, savedProject.Name);
        Assert.Equal(apiProject.Customer, savedProject.Customer);
        Assert.Equal(apiProject.Budget, savedProject.Budget);
    }
}