using Amazon.Runtime;
using Amazon.SQS;
using Minio;
using ProgramProject.FileService.Services;
using ProgramProject.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

Console.OutputEncoding = System.Text.Encoding.UTF8;

builder.AddServiceDefaults();

var sqsServiceUrl = builder.Configuration["SQS:QueueUrl"] ?? builder.Configuration["SQS:ServiceURL"] 
    ?? "http://localhost:9324";
var sqsConfig = new AmazonSQSConfig
{
    ServiceURL = sqsServiceUrl,
    UseHttp = true,
    AuthenticationRegion = "eu-central-1"
};
builder.Services.AddSingleton<IAmazonSQS>(sp => new AmazonSQSClient(new AnonymousAWSCredentials(), sqsConfig));

var minioEndpoint = builder.Configuration["Minio:Endpoint"] ?? "http://localhost:9000";
Console.WriteLine($"Minio endpoint from config: '{minioEndpoint}'");
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