using Amazon.SQS;
using LocalStack.Client.Extensions;
using ProgramProject.GenerationService.Generator;
using ProgramProject.GenerationService.Services;
using ProgramProject.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

Console.OutputEncoding = System.Text.Encoding.UTF8;

builder.AddServiceDefaults();
builder.AddRedisDistributedCache("cache");

builder.Services.AddLocalStack(builder.Configuration);
builder.Services.AddAwsService<IAmazonSQS>();

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