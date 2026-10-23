using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using AtmApp.Api.Application;
using Microsoft.AspNetCore.WebUtilities;

namespace AtmApp.Api.Endpoints;

// Opaque to clients so the paging key can change without breaking them.
internal static class CursorToken
{
    public static string Encode(HistoryCursor cursor) =>
        Base64UrlTextEncoder.Encode(Encoding.UTF8.GetBytes(cursor.Sequence.ToString(CultureInfo.InvariantCulture)));

    public static bool TryDecode(string token, [NotNullWhen(true)] out HistoryCursor? cursor)
    {
        cursor = null;
        try
        {
            var text = Encoding.UTF8.GetString(Base64UrlTextEncoder.Decode(token));
            if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || sequence <= 0)
            {
                return false;
            }

            cursor = new HistoryCursor(sequence);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
