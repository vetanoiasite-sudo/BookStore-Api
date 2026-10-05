namespace BookStore.Domain.Enums;

/// <summary>Ordering options offered on the catalogue listing.</summary>
public enum BookSortOption
{
    Newest = 0,
    Oldest = 1,
    PriceLowToHigh = 2,
    PriceHighToLow = 3,
    MostPopular = 4,
}
