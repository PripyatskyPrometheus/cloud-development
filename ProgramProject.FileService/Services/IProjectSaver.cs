namespace ProgramProject.FileService.Services;

public interface IProjectSaver
{
    public Task SaveAsync(string jsonContent, CancellationToken cancellationToken);
}