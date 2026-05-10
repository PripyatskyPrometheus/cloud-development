var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache").WithRedisCommander();

// Minio (объектное хранилище)
var minio = builder.AddContainer("minio", "minio/minio")
    .WithArgs("server", "/data", "--console-address", ":9001")
    .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "api")
    .WithHttpEndpoint(port: 9001, targetPort: 9001, name: "console")
    .WithVolume("minio-data", "/data");

// Эмулятор Amazon SQS, добавлен для корректной отработки тестов
// (до этого были постоянные ошибки тестов из-за рандомизации портов)
var sqs = builder.AddContainer("elasticmq", "softwaremill/elasticmq")
    .WithHttpEndpoint(port: 9324, targetPort: 9324, name: "http");

// Создаём 5 генераторов в цикле
var generators = new List<IResourceBuilder<ProjectResource>>();

for (var i = 1; i <= 5; i++)
{
    var generator = builder.AddProject<Projects.ProgramProject_GenerationService>($"generator-{i}")
        .WithExternalHttpEndpoints()
        .WithReference(cache)
        .WaitFor(cache)
        .WithEndpoint("http", endpoint => endpoint.Port = 6200 + i)
        .WithEndpoint("https", endpoint => endpoint.Port = 7200 + i)
        .WithEnvironment("SQS__ServiceURL", sqs.GetEndpoint("http"));

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
    .WithEnvironment("SQS__ServiceURL", sqs.GetEndpoint("http"))
    .WithEnvironment("Minio__Endpoint", minio.GetEndpoint("api"))
    .WithEnvironment("Minio__AccessKey", "minioadmin")
    .WithEnvironment("Minio__SecretKey", "minioadmin")
    .WaitFor(sqs)
    .WaitFor(minio);

// Клиент теперь связывается с генератором через шлюз
builder.AddProject<Projects.Client_Wasm>("client-wasm")
    .WithExternalHttpEndpoints()
    .WaitFor(gateway);

builder.Build().Run();