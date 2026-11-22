using System.Text.Json.Serialization;
using ThixMail.Core.Model;
using ThixMail.Core.Sync;

namespace ThixMail.Core.Store;

/// <summary>The JSON of ThixMail's store (source-generated: no reflection in the trimmed program).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MailAccount))]
[JsonSerializable(typeof(MailSignature))]
[JsonSerializable(typeof(MailCategory))]
[JsonSerializable(typeof(OperationData))]
[JsonSerializable(typeof(List<MailAddress>))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class MailJson : JsonSerializerContext
{
}
