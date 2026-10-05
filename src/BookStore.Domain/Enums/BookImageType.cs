namespace BookStore.Domain.Enums;

/// <summary>
/// What a book photograph shows. A <see cref="Cover"/> image is mandatory before a
/// book can be submitted for review.
/// </summary>
public enum BookImageType
{
    Cover = 0,
    BackCover = 1,
    InsidePage = 2,
    Isbn = 3,
    Damage = 4,
    Other = 5,
}
