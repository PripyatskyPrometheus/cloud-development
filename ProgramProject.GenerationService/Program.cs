using Amazon.Runtime;
using Amazon.SQS;
using ProgramProject.GenerationService.Generator;
using ProgramProject.GenerationService.Services;
using ProgramProject.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

Console.OutputEncoding = System.Text.Encoding.UTF8;

builder.AddServiceDefaults();
builder.AddRedisDistributedCache("cache");

var sqsServiceUrl = builder.Configuration["SQS:QueueUrl"] ?? builder.Configuration["SQS:ServiceURL"] 
    ?? "http://localhost:9324";
var sqsConfig = new AmazonSQSConfig
{
    ServiceURL = sqsServiceUrl,
    UseHttp = true,
    AuthenticationRegion = "eu-central-1"
};
builder.Services.AddSingleton<IAmazonSQS>(sp => new AmazonSQSClient(new AnonymousAWSCredentials(), sqsConfig));

builder.Services.AddSingleton<IProgramProjectFaker, ProgramProjectFaker>();
builder.Services.AddScoped<IProjectService, ProjectService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseCors("AllowClient");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

app.Run();