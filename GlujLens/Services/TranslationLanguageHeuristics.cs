using GlujLens.Models;

namespace GlujLens.Services;

public static class TranslationLanguageHeuristics
{
    public static bool ShouldTranslate(TextRegion region, string sourceLanguage, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(region.Text))
        {
            return false;
        }

        var normalizedSource = NormalizeLanguage(sourceLanguage);
        var normalizedTarget = NormalizeLanguage(targetLanguage);
        var detected = NormalizeLanguage(region.Language);

        if (!string.IsNullOrWhiteSpace(detected))
        {
            if (detected == normalizedTarget)
            {
                return false;
            }

            if (detected == normalizedSource)
            {
                return true;
            }
        }

        return LooksLikeLanguage(region.Text, normalizedSource);
    }

    public static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return string.Empty;
        }

        var normalized = language.Trim().ToLowerInvariant().Replace('_', '-');
        var dashIndex = normalized.IndexOf('-');
        return dashIndex > 0 ? normalized[..dashIndex] : normalized;
    }

    private static bool LooksLikeLanguage(string text, string language)
    {
        return language switch
        {
            "ja" => ContainsJapanese(text),
            "zh" => ContainsCjk(text),
            "ko" => ContainsHangul(text),
            "ru" => ContainsCyrillic(text),
            "uk" => ContainsCyrillic(text),
            "bg" => ContainsCyrillic(text),
            "en" => ContainsLatin(text),
            "de" => ContainsLatin(text),
            "fr" => ContainsLatin(text),
            "es" => ContainsLatin(text),
            "pl" => ContainsLatin(text),
            _ => true
        };
    }

    private static bool ContainsJapanese(string text)
    {
        return text.Any(character =>
            IsInRange(character, 0x3040, 0x30ff) ||
            IsInRange(character, 0x3400, 0x9fff));
    }

    private static bool ContainsCjk(string text)
    {
        return text.Any(character => IsInRange(character, 0x3400, 0x9fff));
    }

    private static bool ContainsHangul(string text)
    {
        return text.Any(character => IsInRange(character, 0xac00, 0xd7af));
    }

    private static bool ContainsCyrillic(string text)
    {
        return text.Any(character => IsInRange(character, 0x0400, 0x052f));
    }

    private static bool ContainsLatin(string text)
    {
        return text.Any(character =>
            IsInRange(character, 'A', 'Z') ||
            IsInRange(character, 'a', 'z'));
    }

    private static bool IsInRange(char character, int start, int end)
    {
        return character >= start && character <= end;
    }
}
