using Amazon.Runtime;
using Amazon.SQS;
using Minio;
using ProgramProject.FileService.Services;
using ProgramProject.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

Console.OutputEncoding = System.Text.Encoding.UTF8;

builder.AddServiceDefaults();

var sqsServiceUrl = builder.Configuration["SQS:ServiceURL"] ?? "http://localhost:9324";
var sqsConfig = new AmazonSQSConfig
{
    ServiceURL = sqsServiceUrl,
    UseHttp = true,
    AuthenticationRegion = "us-east-1"
};
builder.Services.AddSingleton<IAmazonSQS>(sp => new AmazonSQSClient(new AnonymousAWSCredentials(), sqsConfig));

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

builder.Services.AddHostedService<SqsBackgroundService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

app.Run();