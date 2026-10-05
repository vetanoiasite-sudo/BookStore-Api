using BookStore.Domain.Common;
using BookStore.Domain.Enums;
using BookStore.Domain.Selling;

namespace BookStore.UnitTests.Selling;

/// <summary>
/// The seller balance is a ledger, not a number that code can overwrite. These tests
/// follow the worked example from the specification: a book sells for 200, the
/// platform takes 20, and the seller nets 180 once the order settles.
/// </summary>
public sealed class WalletLedgerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrderId = Guid.CreateVersion7();
    private static readonly Guid OrderItemId = Guid.CreateVersion7();

    private static Wallet NewWallet() =>
        Seller.Create(Guid.CreateVersion7(), "Seller One", Now).Wallet;

    /// <summary>Posts the sale credit and the commission debit for one sold copy.</summary>
    private static Wallet WithSale(Wallet wallet, decimal price = 200m, decimal fee = 20m)
    {
        wallet.Post(WalletTransaction.Sale(wallet.Id, price, OrderId, OrderItemId, "Sale", Now));
        wallet.Post(WalletTransaction.Fee(wallet.Id, fee, OrderId, OrderItemId, "Platform fee", Now));
        return wallet;
    }

    [Fact]
    public void A_new_wallet_is_empty()
    {
        var wallet = NewWallet();

        wallet.AvailableBalance.ShouldBe(0m);
        wallet.PendingBalance.ShouldBe(0m);
        wallet.TotalBalance.ShouldBe(0m);
    }

    [Fact]
    public void A_sale_credits_the_seller_and_the_fee_debits_them()
    {
        var wallet = WithSale(NewWallet());

        wallet.Transactions.Count.ShouldBe(2);
        wallet.Transactions.Single(t => t.Type == WalletTransactionType.Sale).Amount.ShouldBe(200m);
        wallet.Transactions.Single(t => t.Type == WalletTransactionType.Fee).Amount.ShouldBe(-20m);
    }

    [Fact]
    public void Earnings_are_pending_until_the_order_settles()
    {
        var wallet = WithSale(NewWallet());

        wallet.PendingBalance.ShouldBe(180m);
        wallet.AvailableBalance.ShouldBe(0m);
        wallet.TotalBalance.ShouldBe(180m);
    }

    [Fact]
    public void Pending_money_cannot_be_withdrawn()
    {
        var wallet = WithSale(NewWallet());

        var exception = Should.Throw<BusinessRuleException>(() => wallet.EnsureCanWithdraw(180m));

        exception.Code.ShouldBe("insufficient_balance");
    }

    [Fact]
    public void An_entry_cannot_be_released_before_the_order_completes()
    {
        var wallet = WithSale(NewWallet());
        var sale = wallet.Transactions.Single(t => t.Type == WalletTransactionType.Sale);

        var exception = Should.Throw<BusinessRuleException>(() => sale.Release(Now));

        exception.Code.ShouldBe("release_date_missing");
    }

    [Fact]
    public void An_entry_cannot_be_released_before_its_settlement_window_has_passed()
    {
        var wallet = WithSale(NewWallet());
        var sale = wallet.Transactions.Single(t => t.Type == WalletTransactionType.Sale);
        sale.ScheduleRelease(Now.AddDays(7));

        var exception = Should.Throw<BusinessRuleException>(() => sale.Release(Now.AddDays(3)));

        exception.Code.ShouldBe("settlement_pending");
    }

    [Fact]
    public void Once_the_settlement_window_passes_the_money_becomes_withdrawable()
    {
        var wallet = WithSale(NewWallet());
        foreach (var entry in wallet.Transactions)
        {
            entry.ScheduleRelease(Now.AddDays(7));
        }

        var released = wallet.ReleaseDueTransactions(Now.AddDays(7));

        released.ShouldBe(2);
        wallet.AvailableBalance.ShouldBe(180m);
        wallet.PendingBalance.ShouldBe(0m);
    }

    [Fact]
    public void Releasing_only_touches_entries_whose_window_has_actually_passed()
    {
        var wallet = NewWallet();
        wallet.Post(WalletTransaction.Sale(wallet.Id, 200m, OrderId, OrderItemId, "Old sale", Now))
            .ScheduleRelease(Now.AddDays(1));
        wallet.Post(WalletTransaction.Sale(wallet.Id, 90m, Guid.CreateVersion7(), Guid.CreateVersion7(), "Recent sale", Now))
            .ScheduleRelease(Now.AddDays(30));

        var released = wallet.ReleaseDueTransactions(Now.AddDays(2));

        released.ShouldBe(1);
        wallet.AvailableBalance.ShouldBe(200m);
        wallet.PendingBalance.ShouldBe(90m);
    }

    [Fact]
    public void Settled_money_can_be_withdrawn_up_to_the_available_balance()
    {
        var wallet = WithSale(NewWallet());
        foreach (var entry in wallet.Transactions)
        {
            entry.ScheduleRelease(Now);
        }

        wallet.ReleaseDueTransactions(Now);

        Should.NotThrow(() => wallet.EnsureCanWithdraw(180m));
        Should.Throw<BusinessRuleException>(() => wallet.EnsureCanWithdraw(180.01m))
            .Code.ShouldBe("insufficient_balance");
    }

    [Fact]
    public void A_withdrawal_debits_the_ledger_so_the_same_money_cannot_be_drawn_twice()
    {
        var wallet = WithSale(NewWallet());
        foreach (var entry in wallet.Transactions)
        {
            entry.ScheduleRelease(Now);
        }

        wallet.ReleaseDueTransactions(Now);
        wallet.Post(WalletTransaction.Withdrawal(wallet.Id, 100m, Guid.CreateVersion7(), "Payout", Now));

        wallet.AvailableBalance.ShouldBe(80m);
        Should.Throw<BusinessRuleException>(() => wallet.EnsureCanWithdraw(100m));
    }

    [Fact]
    public void A_refund_reverses_the_credit_immediately()
    {
        var wallet = WithSale(NewWallet());
        foreach (var entry in wallet.Transactions)
        {
            entry.ScheduleRelease(Now);
        }

        wallet.ReleaseDueTransactions(Now);
        wallet.Post(WalletTransaction.Refund(wallet.Id, 180m, OrderId, "Buyer returned the book", Now));

        wallet.AvailableBalance.ShouldBe(0m);
    }

    [Fact]
    public void A_reversed_entry_stops_counting_towards_any_balance()
    {
        var wallet = WithSale(NewWallet());
        var sale = wallet.Transactions.Single(t => t.Type == WalletTransactionType.Sale);

        sale.Reverse(Now);

        wallet.PendingBalance.ShouldBe(-20m);
        wallet.LifetimeSales.ShouldBe(0m);
    }

    [Fact]
    public void Lifetime_figures_report_gross_sales_and_commission_separately()
    {
        var wallet = WithSale(NewWallet());
        WithSale(wallet, price: 300m, fee: 30m);

        wallet.LifetimeSales.ShouldBe(500m);
        wallet.LifetimeFees.ShouldBe(50m);
        wallet.TotalBalance.ShouldBe(450m);
    }

    [Fact]
    public void A_ledger_entry_must_have_a_positive_amount()
    {
        var wallet = NewWallet();

        Should.Throw<BusinessRuleException>(() =>
                WalletTransaction.Sale(wallet.Id, 0m, OrderId, OrderItemId, "Bad sale", Now))
            .Code.ShouldBe("invalid_amount");
    }

    [Fact]
    public void An_entry_belonging_to_another_wallet_is_refused()
    {
        var wallet = NewWallet();
        var foreignEntry = WalletTransaction.Sale(
            Guid.CreateVersion7(), 50m, OrderId, OrderItemId, "Someone else's sale", Now);

        Should.Throw<BusinessRuleException>(() => wallet.Post(foreignEntry))
            .Code.ShouldBe("wallet_mismatch");
    }

    [Fact]
    public void An_adjustment_needs_a_reason_and_a_non_zero_amount()
    {
        var wallet = NewWallet();

        Should.Throw<BusinessRuleException>(() =>
                WalletTransaction.Adjustment(wallet.Id, 0m, "No change", Now))
            .Code.ShouldBe("invalid_adjustment");

        Should.Throw<ArgumentException>(() =>
            WalletTransaction.Adjustment(wallet.Id, 10m, "  ", Now));
    }

    [Fact]
    public void A_negative_adjustment_reduces_the_available_balance()
    {
        var wallet = NewWallet();
        wallet.Post(WalletTransaction.Adjustment(wallet.Id, 100m, "Goodwill credit", Now));
        wallet.Post(WalletTransaction.Adjustment(wallet.Id, -40m, "Correcting an earlier error", Now));

        wallet.AvailableBalance.ShouldBe(60m);
    }

    [Fact]
    public void A_zero_withdrawal_is_refused()
    {
        var wallet = NewWallet();

        Should.Throw<BusinessRuleException>(() => wallet.EnsureCanWithdraw(0m))
            .Code.ShouldBe("invalid_withdrawal_amount");
    }
}
