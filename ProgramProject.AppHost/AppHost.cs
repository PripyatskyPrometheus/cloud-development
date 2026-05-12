var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache").WithRedisCommander();

// Minio (объектное хранилище)
var minio = builder.AddMinioContainer("minio")
    .WithDataVolume("minio-data")
    .WithEnvironment("MINIO_ROOT_USER", "minioadmin")
    .WithEnvironment("MINIO_ROOT_PASSWORD", "minioadmin");

// Эмулятор из готового пакета aspire (ей богу, с AddContainer было проще)
var awsConfig = builder.AddAWSSDKConfig()
    .WithProfile("default")
    .WithRegion(Amazon.RegionEndpoint.EUCentral1);

var localStack = builder.AddLocalStack("aws-local", awsConfig: awsConfig,
    configureContainer: container =>
    {
        container.Lifetime = ContainerLifetime.Session;
        container.Port = 4566;
    });

var sqsResources = builder.AddAWSCloudFormationTemplate("sqs-resources", "projects-template.yaml", 
    "projects")
    .WithReference(awsConfig);

// Создаём 5 генераторов в цикле
var generators = new List<IResourceBuilder<ProjectResource>>();

for (var i = 1; i <= 5; i++)
{
    var generator = builder.AddProject<Projects.ProgramProject_GenerationService>($"generator-{i}")
        .WithExternalHttpEndpoints()
        .WithReference(cache)
        .WaitFor(cache)
        .WaitFor(sqsResources)
        .WithEndpoint("http", endpoint => endpoint.Port = 6200 + i)
        .WithEndpoint("https", endpoint => endpoint.Port = 7200 + i)
        .WithEnvironment("SQS__QueueUrl", sqsResources.GetOutput("SQSQueueUrl"));

    generators.Add(generator);
}

// Шлюз
var gateway = builder.AddProject<Projects.ProgramProject_Gateway>("gateway")
    .WithExternalHttpEndpoints();

foreach (var generator in generators)
{
    gateway.WaitFor(generator);
}

// Файловый сервис
builder.AddProject<Projects.ProgramProject_FileService>("programproject-fileservice")
    .WithExternalHttpEndpoints()
    .WithEnvironment("SQS__QueueUrl", sqsResources.GetOutput("SQSQueueUrl"))
    .WithEnvironment("Minio__Endpoint", minio.GetEndpoint("http"))
    .WithEnvironment("Minio__AccessKey", "minioadmin")
    .WithEnvironment("Minio__SecretKey", "minioadmin")
    .WaitFor(sqsResources)
    .WaitFor(minio);

// Клиент теперь связывается с генератором через шлюз
builder.AddProject<Projects.Client_Wasm>("client-wasm")
    .WithExternalHttpEndpoints()
    .WaitFor(gateway);

builder.UseLocalStack(localStack);

builder.Build().Run();