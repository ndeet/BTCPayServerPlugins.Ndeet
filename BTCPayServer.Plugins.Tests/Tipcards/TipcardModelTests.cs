using BTCPayServer.Ndeet.Plugins.Tipcards;
using NBitcoin.DataEncoders;
using Xunit;
using PayoutData = BTCPayServer.Data.PayoutData;
using PayoutState = BTCPayServer.Client.Models.PayoutState;
using PullPaymentData = BTCPayServer.Data.PullPaymentData;

namespace BTCPayServer.Plugins.Tests.Tipcards;

public class TipcardModelTests
{
    [Fact]
    public void CreateCards_CreatesOrderedUniqueUnactivatedCards()
    {
        var cards = TipcardService.CreateCards(500, 7);

        Assert.Equal(500, cards.Count);
        Assert.Equal(Enumerable.Range(7, 500), cards.Select(card => card.CardNumber));
        Assert.Equal(500, cards.Select(card => card.ClaimId).Distinct().Count());
        Assert.All(cards, card =>
        {
            Assert.Equal(16, Encoders.Base58.DecodeData(card.ClaimId).Length);
            Assert.Null(card.PullPaymentId);
        });
    }

    [Fact]
    public void CreateCards_RejectsNegativeCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TipcardService.CreateCards(-1));
    }

    [Theory]
    [InlineData(PayoutState.AwaitingApproval, true)]
    [InlineData(PayoutState.AwaitingPayment, true)]
    [InlineData(PayoutState.InProgress, true)]
    [InlineData(PayoutState.Completed, true)]
    [InlineData(PayoutState.Cancelled, false)]
    public void IsPullPaymentClaimed_UsesClaimReservingStates(PayoutState state, bool expected)
    {
        var pullPayment = new PullPaymentData
        {
            Payouts =
            [
                new PayoutData
                {
                    State = state,
                    OriginalAmount = 0.00001m
                }
            ]
        };

        Assert.Equal(expected, TipcardsController.IsPullPaymentClaimed(pullPayment));
    }

    [Fact]
    public void IsPullPaymentClaimed_IgnoresZeroAmountAndMissingPayouts()
    {
        Assert.False(TipcardsController.IsPullPaymentClaimed(new PullPaymentData()));
        Assert.False(TipcardsController.IsPullPaymentClaimed(new PullPaymentData
        {
            Payouts =
            [
                new PayoutData
                {
                    State = PayoutState.Completed,
                    OriginalAmount = 0m
                }
            ]
        }));
    }
}
