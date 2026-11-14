using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace RaceDay.Api.Services;

public sealed class BlobStorageService(IConfiguration configuration) : IBlobStorageService
{
    private readonly string? _connectionString = configuration["Azure:ConnectionString"];
    private readonly string _containerName = configuration["Azure:ContainerName"] ?? "raceday-images";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connectionString);

    public async Task<string> UploadAsync(Stream content, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("Azure Blob Storage is not configured.");
        var container = new BlobContainerClient(_connectionString, _containerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);
        var blobName = $"{Guid.NewGuid():N}{Path.GetExtension(fileName).ToLowerInvariant()}";
        var blob = container.GetBlobClient(blobName);
        await blob.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: cancellationToken);
        return blob.Uri.ToString();
    }
}
