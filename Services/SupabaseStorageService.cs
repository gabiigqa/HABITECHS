using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;

namespace HabiTechs.Services;

public class SupabaseStorageService
{
    private readonly Supabase.Client _supabaseClient;
    private readonly string _bucketName = "habitechs-bucket"; // Replace with your actual Supabase bucket name

    public SupabaseStorageService(Supabase.Client supabaseClient)
    {
        _supabaseClient = supabaseClient;
    }


    public async Task<string> UploadFileAsync(IFormFile file, string folderName)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("El archivo es nulo o está vacío.", nameof(file));
        }

        // Generate a unique filename
        var extension = Path.GetExtension(file.FileName);
        var uniqueFileName = $"{Guid.NewGuid()}{extension}";
        var filePath = string.IsNullOrEmpty(folderName) ? uniqueFileName : $"{folderName}/{uniqueFileName}";

        // Read the file into a memory stream
        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream);
        var fileBytes = memoryStream.ToArray();

        // Upload the file to Supabase using the byte array
        var options = new Supabase.Storage.FileOptions
        {
            CacheControl = "3600",
            Upsert = false
        };

        await _supabaseClient.Storage
            .From(_bucketName)
            .Upload(fileBytes, filePath, options);

        // Get the public URL for the uploaded file
        var publicUrl = _supabaseClient.Storage
            .From(_bucketName)
            .GetPublicUrl(filePath);

        return publicUrl;
    }
}
