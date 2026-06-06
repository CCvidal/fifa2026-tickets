using System.Text.Json;
using Fifa2026.V2.Functions.Data;
using Fifa2026.V2.Functions.Functions;
using Fifa2026.V2.Functions.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Fifa2026.V2.Functions.Tests;

/// <summary>
/// AC-4/AC-6/AC-7 — comportamento do consumer: happy path, idempotência (duplicata
/// não falha), e falha permanente (categoria inexistente → re-throw → DLQ).
/// </summary>
public sealed class PurchaseConsumerFunctionTests
{
    private static string Serialize(PurchaseMessage message) => JsonSerializer.Serialize(message);

    private static PurchaseMessage NewMessage() => new()
    {
        CorrelationId = Guid.NewGuid(),
        MatchId = 1,
        Category = "VIP",
        UserId = 7,
        Quantity = 2
    };

    [Fact]
    public async Task Happy_path_inserts_and_completes()
    {
        var repo = new Mock<IPurchaseRepository>();
        repo.Setup(r => r.InsertPurchaseAsync(It.IsAny<PurchaseMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsertOutcome.Inserted);

        var sut = new PurchaseConsumerFunction(repo.Object, NullLogger<PurchaseConsumerFunction>.Instance);

        await sut.RunAsync(Serialize(NewMessage()), CancellationToken.None);

        repo.Verify(r => r.InsertPurchaseAsync(It.IsAny<PurchaseMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Duplicate_is_swallowed_silently_no_throw()
    {
        // AC-6: enviar a mesma mensagem 2x → consumer NÃO lança (não vai para DLQ).
        var repo = new Mock<IPurchaseRepository>();
        repo.Setup(r => r.InsertPurchaseAsync(It.IsAny<PurchaseMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsertOutcome.Duplicate);

        var sut = new PurchaseConsumerFunction(repo.Object, NullLogger<PurchaseConsumerFunction>.Instance);

        var exception = await Record.ExceptionAsync(() => sut.RunAsync(Serialize(NewMessage()), CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task CategoryNotFound_throws_to_route_to_dlq()
    {
        // AC-7: matchId/category inválidos → falha permanente → re-throw → DLQ.
        var repo = new Mock<IPurchaseRepository>();
        repo.Setup(r => r.InsertPurchaseAsync(It.IsAny<PurchaseMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(InsertOutcome.CategoryNotFound);

        var sut = new PurchaseConsumerFunction(repo.Object, NullLogger<PurchaseConsumerFunction>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.RunAsync(Serialize(NewMessage()), CancellationToken.None));
    }

    [Fact]
    public async Task Malformed_json_throws_to_route_to_dlq()
    {
        var repo = new Mock<IPurchaseRepository>();
        var sut = new PurchaseConsumerFunction(repo.Object, NullLogger<PurchaseConsumerFunction>.Instance);

        await Assert.ThrowsAsync<JsonException>(
            () => sut.RunAsync("{ not valid json", CancellationToken.None));

        repo.Verify(r => r.InsertPurchaseAsync(It.IsAny<PurchaseMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Empty_correlationId_throws()
    {
        var repo = new Mock<IPurchaseRepository>();
        var sut = new PurchaseConsumerFunction(repo.Object, NullLogger<PurchaseConsumerFunction>.Instance);

        var message = NewMessage();
        message.CorrelationId = Guid.Empty;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.RunAsync(Serialize(message), CancellationToken.None));

        repo.Verify(r => r.InsertPurchaseAsync(It.IsAny<PurchaseMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
