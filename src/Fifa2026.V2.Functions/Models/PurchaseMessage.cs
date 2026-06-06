using System.Text.Json.Serialization;

namespace Fifa2026.V2.Functions.Models;

/// <summary>
/// Mensagem publicada em `tickets-purchase` pela PurchaseEntryFunction e consumida
/// pela PurchaseConsumerFunction. Carrega o correlationId gerado na entrada para
/// propagação ponta-a-ponta (ADE-000 Inv 5 — Service Bus hop).
/// </summary>
public sealed class PurchaseMessage
{
    [JsonPropertyName("correlationId")]
    public Guid CorrelationId { get; set; }

    [JsonPropertyName("matchId")]
    public int MatchId { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}
