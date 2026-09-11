namespace IncidentReview.EventSync.Contracts;

internal static class EventSyncUri
{
    public const int MaximumBaseUriLength = 2048;

    public static bool TryNormalizeBase(Uri? value, out Uri normalized)
    {
        normalized = null!;
        if (value is null ||
            !value.IsAbsoluteUri ||
            value.AbsoluteUri.Length > MaximumBaseUriLength ||
            (value.Scheme != Uri.UriSchemeHttp && value.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(value.Host) ||
            !string.IsNullOrEmpty(value.UserInfo) ||
            !string.IsNullOrEmpty(value.Query) ||
            !string.IsNullOrEmpty(value.Fragment) ||
            value.Port <= 0)
        {
            return false;
        }

        var absolute = value.AbsoluteUri;
        if (!absolute.EndsWith('/'))
        {
            absolute += "/";
        }

        normalized = new Uri(absolute, UriKind.Absolute);
        return normalized.AbsoluteUri.Length <= MaximumBaseUriLength;
    }

    public static bool TryNormalizeListen(Uri? value, out Uri normalized)
    {
        if (!TryNormalizeBase(value, out normalized))
        {
            return false;
        }

        return normalized.AbsolutePath == "/";
    }
}
