using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace NexusOptimizer.Services;

/// <summary>Сохраняет картинки с сайта на диск, чтобы они открывались без интернета.</summary>
public sealed class ImageCacheService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NexusOptimizer/1.0");
        return client;
    }

    /// <returns>Путь к сохранённой копии или null, если её ещё нет.</returns>
    public string? TryGetCachedPath(string url)
    {
        string path = PathFor(url);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Скачивает картинку в кэш. Ошибки не пробрасываются: кэш нужен только для удобства.</summary>
    public async Task DownloadAsync(string url)
    {
        try
        {
            string path = PathFor(url);

            if (File.Exists(path))
                return;

            Directory.CreateDirectory(AppPaths.ImageCacheDirectory);

            byte[] data = await Http.GetByteArrayAsync(url);

            if (data.Length == 0)
                return;

            string temp = path + ".tmp";
            await File.WriteAllBytesAsync(temp, data);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Не удалось сохранить картинку в кэш: {url}", ex);
        }
    }

    private static string PathFor(string url)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..24];
        return Path.Combine(AppPaths.ImageCacheDirectory, hash + ".img");
    }
}
