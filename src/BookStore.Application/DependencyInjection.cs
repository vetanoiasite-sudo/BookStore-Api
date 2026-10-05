using System.Reflection;
using BookStore.Application.Features.Administration;
using BookStore.Application.Features.Authentication;
using BookStore.Application.Features.Buying;
using BookStore.Application.Features.Catalog;
using BookStore.Application.Features.Categories;
using BookStore.Application.Features.Content;
using BookStore.Application.Features.Selling;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BookStore.Application;

/// <summary>Composition root for the application layer.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly(), includeInternalTypes: true);

        services.Configure<FrontendOptions>(configuration.GetSection(FrontendOptions.SectionName));

        services.AddScoped<AccountService>();
        services.AddScoped<AuthenticationService>();
        services.AddScoped<BookCatalogService>();
        services.AddScoped<CartService>();
        services.AddScoped<FavoriteService>();
        services.AddScoped<AddressService>();
        services.AddScoped<OrderService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<HowItWorksService>();
        services.AddScoped<SellerBookService>();
        services.AddScoped<BookReviewService>();
        services.AddScoped<AdminDashboardService>();
        services.AddScoped<AdminOrderService>();
        services.AddScoped<AdminPeopleService>();
        services.AddScoped<AdminInventoryService>();
        services.AddScoped<AdminOperationsService>();

        return services;
    }
}
