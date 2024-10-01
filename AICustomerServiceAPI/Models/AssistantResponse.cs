using System.Text.Json.Serialization;

namespace AICustomerServiceAPI.Models;

public class AssistantResponse
{
    [JsonPropertyName("responseMessage")]
    public string ResponseMessage { get; set; } = string.Empty;

    [JsonPropertyName("foundResponse")]
    public bool FoundResponse { get; set; }

    [JsonPropertyName("customerAnxiety")]
    public int CustomerAnxiety { get; set; }
}
