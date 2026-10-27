using System.Text.Json.Serialization;

namespace NexusOptimizer.Models;

public sealed class GameScreenshot
{
    public string ThumbnailUrl { get; set; } = "";
    public string FullUrl { get; set; } = "";
}

/// <summary>Данные со страницы игры в магазине Steam.</summary>
public sealed class GameDetails
{
    public string? StoreAppId { get; set; }
    public string? StoreUrl { get; set; }
    public string? HeaderImageUrl { get; set; }
    public string ShortDescription { get; set; } = "";
    public string Description { get; set; } = "";
    public string? ReleaseDate { get; set; }
    public List<string> Genres { get; set; } = [];
    public List<string> Developers { get; set; } = [];
    public List<string> Publishers { get; set; } = [];
    public List<GameScreenshot> Screenshots { get; set; } = [];
    public DateTime FetchedUtc { get; set; }

    /// <summary>Пояснение для пользователя (ошибка, нет страницы в Steam). В кэш не пишется.</summary>
    [JsonIgnore]
    public string? Notice { get; set; }
}
