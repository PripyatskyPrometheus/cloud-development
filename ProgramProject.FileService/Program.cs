using Amazon.SQS;
using Minio;
using ProgramProject.FileService.Services;
using ProgramProject.ServiceDefaults;
using LocalStack.Client.Extensions;

var builder = WebApplication.CreateBuilder(args);

Console.OutputEncoding = System.Text.Encoding.UTF8;

builder.AddServiceDefaults();

builder.Services.AddLocalStack(builder.Configuration);
builder.Services.AddAwsService<IAmazonSQS>();

var minioEndpoint = builder.Configuration["Minio:Endpoint"] ?? "http://localhost:9000";
var minioAccessKey = builder.Configuration["Minio:AccessKey"] ?? "minioadmin";
var minioSecretKey = builder.Configuration["Minio:SecretKey"] ?? "minioadmin";

builder.Services.AddSingleton<Minio.IMinioClient>(sp =>
{
    return new Minio.MinioClient()
        .WithEndpoint(minioEndpoint.Replace("http://", "").Replace("https://", ""))
        .WithCredentials(minioAccessKey, minioSecretKey)
        .WithSSL(false)
        .Build();
});

builder.Services.AddHostedService<InfrastructureInitializer>();
builder.Services.AddSingleton<IProjectSaver, ProjectSaver>();
builder.Services.AddHostedService<MessageConsumerService>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.Run();