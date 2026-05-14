namespace GlujLens.Services;

public sealed class TranslationBatchResult
{
    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public List<TranslatedTextItem> Items { get; } = new();
}

public sealed class TranslatedTextItem
{
    public int RegionIndex { get; set; }

    public string SourceText { get; set; } = string.Empty;

    public string TranslatedText { get; set; } = string.Empty;
}
