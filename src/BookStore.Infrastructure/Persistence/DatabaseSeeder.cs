using BookStore.Application.Common.Abstractions;
using BookStore.Domain.Catalog;
using BookStore.Domain.Enums;
using BookStore.Domain.Identity;
using BookStore.Domain.Inventory;
using BookStore.Domain.Platform;
using BookStore.Domain.Selling;
using BookStore.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookStore.Infrastructure.Persistence;

/// <summary>
/// Fills an empty database with the roles, the four development accounts and a small
/// but realistic catalogue. It is idempotent and only ever adds what is missing, so
/// it is safe to run on every startup.
/// </summary>
/// <remarks>
/// The passwords here are obviously fake and are only ever used in Development. The
/// caller decides whether seeding runs at all.
/// </remarks>
public sealed class DatabaseSeeder
{
    /// <summary>The single password shared by every seeded development account.</summary>
    public const string DevelopmentPassword = "Dev@12345!";

    private const string AdminEmail = "admin@bookstore.local";
    private const string StaffEmail = "staff@bookstore.local";
    private const string SellerEmail = "seller@bookstore.local";
    private const string BuyerEmail = "buyer@bookstore.local";

    private readonly AppDbContext _context;
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<ApplicationRole> _roles;
    private readonly IPublicIdProvider _publicIds;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        AppDbContext context,
        UserManager<ApplicationUser> users,
        RoleManager<ApplicationRole> roles,
        IPublicIdProvider publicIds,
        IDateTimeProvider clock,
        ILogger<DatabaseSeeder> logger)
    {
        _context = context;
        _users = users;
        _roles = roles;
        _publicIds = publicIds;
        _clock = clock;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;

        await SeedRolesAsync();
        await SeedSettingsAsync(now, cancellationToken);

        var admin = await EnsureUserAsync(AdminEmail, "مدير المنصة", Roles.Admin);
        await EnsureUserAsync(StaffEmail, "موظف المنصة", Roles.Staff);
        var sellerUser = await EnsureUserAsync(SellerEmail, "مكتبة القاهرة", Roles.Member);
        var buyerUser = await EnsureUserAsync(BuyerEmail, "مشترٍ تجريبي", Roles.Member);

        var seller = await EnsureSellerAsync(sellerUser, now, cancellationToken);
        await EnsureSellerAsync(buyerUser, now, cancellationToken);
        var locations = await SeedInventoryLocationsAsync(now, cancellationToken);
        var categories = await SeedCategoriesAsync(now, cancellationToken);
        var authors = await SeedAuthorsAsync(now, cancellationToken);
        var publishers = await SeedPublishersAsync(now, cancellationToken);

        await SeedBooksAsync(seller, admin.Id, categories, authors, publishers, locations, now, cancellationToken);

        _logger.LogInformation("Development seed data is in place.");
    }

    private async Task SeedRolesAsync()
    {
        foreach (var role in Roles.All)
        {
            if (!await _roles.RoleExistsAsync(role))
            {
                await _roles.CreateAsync(new ApplicationRole(role)
                {
                    Description = role switch
                    {
                        Roles.Admin => "Full access to the back office.",
                        Roles.Staff => "Reviews books, processes orders and answers support.",
                        _ => "Buys and sells books through the platform.",
                    },
                });
            }
        }
    }

    private async Task<ApplicationUser> EnsureUserAsync(
        string email,
        string displayName,
        params string[] roles)
    {
        var existing = await _users.FindByEmailAsync(email);
        if (existing is not null)
        {
            return existing;
        }

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
            PublicId = _publicIds.NewUserPublicId(),
            CreatedAt = _clock.UtcNow,
            IsActive = true,
        };

        var result = await _users.CreateAsync(user, DevelopmentPassword);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(error => error.Description));
            throw new InvalidOperationException($"Could not seed the user {email}: {errors}");
        }

        await _users.AddToRolesAsync(user, roles);
        _logger.LogInformation("Seeded the {Roles} account {Email}.", string.Join('/', roles), email);
        return user;
    }

    private async Task<Seller> EnsureSellerAsync(
        ApplicationUser user,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await _context.Sellers
            .Include(seller => seller.Wallet)
            .FirstOrDefaultAsync(seller => seller.UserId == user.Id, cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var seller = Seller.Create(user.Id, user.DisplayName, now, _publicIds.NewSellerPublicId());
        seller.MarkVerified(now);

        _context.Sellers.Add(seller);
        await _context.SaveChangesAsync(cancellationToken);
        return seller;
    }

    private async Task SeedSettingsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await _context.PlatformSettings.AnyAsync(cancellationToken))
        {
            return;
        }

        _context.PlatformSettings.AddRange(
            PlatformSetting.Create("platform.currency", "EGP", now, "Currency every price is quoted in."),
            PlatformSetting.Create("platform.feePercent", "10", now, "Commission the platform takes from each sale."),
            PlatformSetting.Create("shipping.flatCost", "30", now, "Flat shipping charge added to an order."),
            PlatformSetting.Create("checkout.reservationMinutes", "30", now, "How long a copy is held during checkout."),
            PlatformSetting.Create("wallet.settlementDays", "7", now, "Days after completion before earnings can be withdrawn."),
            PlatformSetting.Create("orders.autoCompleteDays", "14", now, "Days after delivery before an order completes itself."));

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<InventoryLocation>> SeedInventoryLocationsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await _context.InventoryLocations.AnyAsync(cancellationToken))
        {
            return await _context.InventoryLocations.ToListAsync(cancellationToken);
        }

        var locations = new List<InventoryLocation>();
        foreach (var shelf in new[] { "01", "02", "03" })
        {
            foreach (var box in new[] { "17", "18" })
            {
                locations.Add(InventoryLocation.Create(
                    "Warehouse A", now, zone: "Z1", rack: "04", shelf: shelf, box: box, capacity: 50));
            }
        }

        _context.InventoryLocations.AddRange(locations);
        await _context.SaveChangesAsync(cancellationToken);
        return locations;
    }

    private async Task<Dictionary<string, Category>> SeedCategoriesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await _context.Categories.AnyAsync(cancellationToken))
        {
            return await _context.Categories.ToDictionaryAsync(
                category => category.Slug, cancellationToken);
        }

        var literature = Category.Create("أدب", "Literature", now, sortOrder: 1);
        var history = Category.Create("تاريخ", "History", now, sortOrder: 2);
        var science = Category.Create("علوم", "Science", now, sortOrder: 3);
        var philosophy = Category.Create("فلسفة", "Philosophy", now, sortOrder: 4);
        var children = Category.Create("أطفال", "Children", now, sortOrder: 5);

        _context.Categories.AddRange(literature, history, science, philosophy, children);
        await _context.SaveChangesAsync(cancellationToken);

        var novels = Category.Create("روايات", "Novels", now, literature.Id, sortOrder: 1);
        var poetry = Category.Create("شعر", "Poetry", now, literature.Id, sortOrder: 2);
        var islamicHistory = Category.Create("تاريخ إسلامي", "Islamic History", now, history.Id, sortOrder: 1);
        var physics = Category.Create("فيزياء", "Physics", now, science.Id, sortOrder: 1);

        _context.Categories.AddRange(novels, poetry, islamicHistory, physics);
        await _context.SaveChangesAsync(cancellationToken);

        return await _context.Categories.ToDictionaryAsync(category => category.Slug, cancellationToken);
    }

    private async Task<Dictionary<string, Author>> SeedAuthorsAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await _context.Authors.AnyAsync(cancellationToken))
        {
            return await _context.Authors.ToDictionaryAsync(author => author.Slug, cancellationToken);
        }

        _context.Authors.AddRange(
            Author.Create("نجيب محفوظ", now, "Naguib Mahfouz"),
            Author.Create("طه حسين", now, "Taha Hussein"),
            Author.Create("أحمد خالد توفيق", now, "Ahmed Khaled Tawfik"),
            Author.Create("غسان كنفاني", now, "Ghassan Kanafani"),
            Author.Create("سون تزو", now, "Sun Tzu"),
            Author.Create("ستيفن هوكينج", now, "Stephen Hawking"));

        await _context.SaveChangesAsync(cancellationToken);
        return await _context.Authors.ToDictionaryAsync(author => author.Slug, cancellationToken);
    }

    private async Task<Dictionary<string, Publisher>> SeedPublishersAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await _context.Publishers.AnyAsync(cancellationToken))
        {
            return await _context.Publishers.ToDictionaryAsync(
                publisher => publisher.Slug, cancellationToken);
        }

        _context.Publishers.AddRange(
            Publisher.Create("دار الشروق", now, "Dar El Shorouk"),
            Publisher.Create("دار المعارف", now, "Dar Al Maaref"),
            Publisher.Create("المركز الثقافي العربي", now, "Arab Cultural Centre"));

        await _context.SaveChangesAsync(cancellationToken);
        return await _context.Publishers.ToDictionaryAsync(publisher => publisher.Slug, cancellationToken);
    }

    private async Task SeedBooksAsync(
        Seller seller,
        Guid staffUserId,
        Dictionary<string, Category> categories,
        Dictionary<string, Author> authors,
        Dictionary<string, Publisher> publishers,
        IReadOnlyList<InventoryLocation> locations,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await _context.Books.AnyAsync(cancellationToken))
        {
            return;
        }

        var samples = new[]
        {
            new SampleBook("الثلاثية", "novels", "naguib-mahfouz", "dar-el-shorouk", 250m, ConditionGrade.VeryGood, "9789770914564", 1956, 1500),
            new SampleBook("أولاد حارتنا", "novels", "naguib-mahfouz", "dar-el-shorouk", 180m, ConditionGrade.Good, "9789770915165", 1959, 552),
            new SampleBook("الأيام", "novels", "taha-hussein", "dar-al-maaref", 120m, ConditionGrade.Acceptable, "9789770211564", 1929, 320),
            new SampleBook("يوتوبيا", "novels", "ahmed-khaled-tawfik", "dar-el-shorouk", 95m, ConditionGrade.LikeNew, "9789770926123", 2008, 158),
            new SampleBook("رجال في الشمس", "novels", "ghassan-kanafani", "arab-cultural-centre", 110m, ConditionGrade.VeryGood, "9789953682341", 1963, 96),
            new SampleBook("فن الحرب", "philosophy", "sun-tzu", "arab-cultural-centre", 140m, ConditionGrade.Good, "9789953685012", 1910, 273),
            new SampleBook("تاريخ موجز للزمن", "physics", "stephen-hawking", "dar-el-shorouk", 200m, ConditionGrade.LikeNew, "9789770928899", 1988, 212),
            new SampleBook("الكون في قشرة جوز", "physics", "stephen-hawking", "dar-el-shorouk", 230m, ConditionGrade.VeryGood, "9789770929155", 2001, 224),
        };

        var index = 0;
        foreach (var sample in samples)
        {
            var publicId = await _publicIds.NextBookPublicIdAsync(cancellationToken);

            var book = Book.CreateDraft(
                publicId,
                sample.Title,
                categories[sample.CategorySlug].Id,
                seller.Id,
                sample.Price,
                BookLanguage.Arabic,
                BookCondition.Create(
                    sample.Grade,
                    sample.Grade,
                    sample.Grade,
                    hasYellowing: sample.Grade >= ConditionGrade.Good,
                    notes: "نسخة مستعملة بحالة جيدة، تم فحصها في مخزن المنصة."),
                now,
                description: $"نسخة مستعملة من كتاب {sample.Title}. تم فحص الحالة وتصويرها بواسطة فريق المنصة.",
                isbn: sample.Isbn,
                authorId: authors[sample.AuthorSlug].Id,
                publisherId: publishers[sample.PublisherSlug].Id,
                publicationYear: sample.Year,
                pageCount: sample.Pages);

            // Placeholder artwork so the catalogue renders before real uploads exist.
            book.AddImage(
                $"samples/{sample.CategorySlug}-{index % 4 + 1}.svg",
                BookImageType.Cover,
                "image/svg+xml",
                sizeInBytes: 2048,
                width: 600,
                height: 900,
                now,
                altText: sample.Title);

            // Two of the eight stay in the review queue so the admin screen has work
            // to show; the rest go all the way through to being on sale.
            book.SubmitForReview(seller.UserId, now);

            if (index >= samples.Length - 2)
            {
                _context.Books.Add(book);
                index++;
                continue;
            }

            book.Approve(staffUserId, now);
            book.AwaitDelivery(staffUserId, now);
            book.MarkReceived(staffUserId, now);

            var location = locations[index % locations.Count];
            book.Publish(location.Id, staffUserId, now);

            _context.Books.Add(book);
            _context.InventoryItems.Add(
                InventoryItem.Receive(book.Id, location.Id, staffUserId, now, "Seeded sample copy."));

            index++;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Shape of one seeded catalogue entry.</summary>
    private sealed record SampleBook(
        string Title,
        string CategorySlug,
        string AuthorSlug,
        string PublisherSlug,
        decimal Price,
        ConditionGrade Grade,
        string Isbn,
        int Year,
        int Pages);
}
