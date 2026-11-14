namespace RaceDay.Api.Services;

public static class ImageValidator
{
    public const long MaxFileSize = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    public static bool IsValid(IFormFile? file, out string error)
    {
        if (file is null || file.Length == 0)
        {
            error = "Choose an image to upload.";
            return false;
        }
        if (file.Length > MaxFileSize)
        {
            error = "Images must be 5 MB or smaller.";
            return false;
        }
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension))
        {
            error = "Only JPG, PNG, WEBP, and GIF images are allowed.";
            return false;
        }
        var expectedContentType = extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => ""
        };
        if (!string.Equals(file.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            error = "The image content type does not match its file extension.";
            return false;
        }
        Span<byte> signature = stackalloc byte[12];
        using (var stream = file.OpenReadStream())
        {
            var read = stream.Read(signature);
            if (!HasValidSignature(signature[..read], extension))
            {
                error = "The uploaded file is not a supported image.";
                return false;
            }
        }
        error = "";
        return true;
    }

    private static bool HasValidSignature(ReadOnlySpan<byte> bytes, string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            ".png" => bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            ".gif" => bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8),
            ".webp" => bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8),
            _ => false
        };
    }
}
