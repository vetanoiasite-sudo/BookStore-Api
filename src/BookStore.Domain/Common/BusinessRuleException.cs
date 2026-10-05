namespace BookStore.Domain.Common;

/// <summary>
/// A domain invariant was violated (for example: reviewing a book that was never
/// bought, or withdrawing more than the available wallet balance).
/// </summary>
public sealed class BusinessRuleException : DomainException
{
    public BusinessRuleException(string message, string code = "business_rule_violated")
        : base(message)
    {
        Code = code;
    }

    public override string Code { get; }
}
