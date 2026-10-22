using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtmApp.Api.Endpoints;

// Every decimal the API returns is money. C# decimals drop trailing zeros depending on how
// they were computed (1000 vs 1000.00), so write them with exactly two decimals for clients.
public sealed class MoneyJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteRawValue(value.ToString("0.00", CultureInfo.InvariantCulture));
}
