namespace RaceDay.Api.Services;

public interface IBlobStorageService
{
    bool IsConfigured { get; }
    Task<string> UploadAsync(Stream content, string fileName, string contentType, CancellationToken cancellationToken = default);
}
