using System.Text.Json;
using BookStore.Api.Common;
using BookStore.Api.Filters;
using BookStore.Application.Common.Abstractions;
using BookStore.Application.Common.Exceptions;
using BookStore.Application.Common.Models;
using BookStore.Application.Features.Selling;
using BookStore.Domain.Enums;
using BookStore.Domain.Identity;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BookStore.Api.Controllers.Selling;

/// <summary>
/// A seller managing their own listings. Every action here is scoped to the caller's
/// seller profile by the application layer, so a book code belonging to someone else
/// reads as missing rather than as forbidden.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireMember)]
[Route("api/seller/books")]
public sealed class SellerBooksController : ApiControllerBase
{
    private readonly SellerBookService _books;

    public SellerBooksController(SellerBookService books) => _books = books;

    /// <summary>Lists the caller's own listings.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SellerBookListItem>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<SellerBookListItem>>>> List(
        [FromQuery] SellerBookParameters parameters,
        CancellationToken cancellationToken) =>
        Success(await _books.ListAsync(parameters.ToQuery(), cancellationToken));

    /// <summary>Returns one listing with its photographs and its history.</summary>
    [HttpGet("{publicId}")]
    [ProducesResponseType(typeof(ApiResponse<SellerBookDetails>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<SellerBookDetails>>> Get(
        string publicId,
        CancellationToken cancellationToken) =>
        Success(await _books.GetAsync(publicId, cancellationToken));

    /// <summary>Lists a copy with its photographs and sends it for review.</summary>
    /// <remarks>
    /// A multipart form: <c>data</c> holds the listing as JSON, <c>cover</c> the cover
    /// photograph, and <c>photos</c> up to three more. There is no draft step. The copy
    /// is not public and not for sale until the platform has approved it.
    /// </remarks>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<SellerBookDetails>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<SellerBookDetails>>> Create(
        [FromForm] CreateSellerBookForm form,
        [FromServices] IValidator<SaveSellerBookRequest> validator,
        [FromServices] IOptions<JsonOptions> json,
        CancellationToken cancellationToken)
    {
        var request = ReadListing(form.Data, json.Value.JsonSerializerOptions);

        var result = await validator.ValidateAsync(request, cancellationToken);
        if (!result.IsValid)
        {
            throw new AppValidationException(result.Errors
                .Select(failure => new ValidationError(
                    ValidationFilter.ToCamelCase(failure.PropertyName),
                    failure.ErrorMessage))
                .ToList());
        }

        EnsureFilePresent(form.Cover, "cover", "Add a cover photograph.");

        var photos = form.Photos ?? [];
        var uploads = new List<BookImageUpload>();

        try
        {
            uploads.Add(Open(form.Cover!, BookImageType.Cover));
            uploads.AddRange(photos
                .Where(photo => photo.Length > 0)
                .Select(photo => Open(photo, BookImageType.Other)));

            var book = await _books.CreateAsync(request, uploads, cancellationToken);

            return CreatedResource(
                $"/api/seller/books/{book.PublicId}",
                book,
                "Sent for review.");
        }
        finally
        {
            foreach (var upload in uploads)
            {
                await upload.Content.DisposeAsync();
            }
        }
    }

    /// <summary>Applies edits. Allowed while the listing is a draft or was rejected.</summary>
    [HttpPut("{publicId}")]
    [ProducesResponseType(typeof(ApiResponse<SellerBookDetails>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<SellerBookDetails>>> Update(
        string publicId,
        [FromBody] SaveSellerBookRequest request,
        CancellationToken cancellationToken) =>
        Success(await _books.UpdateAsync(publicId, request, cancellationToken), "Listing saved.");

    /// <summary>Deletes a draft that was never submitted.</summary>
    /// <remarks>
    /// Anything the platform has already reviewed is withdrawn instead, so the record
    /// of what was decided survives.
    /// </remarks>
    [HttpDelete("{publicId}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse>> Delete(
        string publicId,
        CancellationToken cancellationToken)
    {
        await _books.DeleteAsync(publicId, cancellationToken);
        return Success("Draft deleted.");
    }

    /// <summary>Uploads one photograph of the copy.</summary>
    /// <remarks>
    /// The file is decoded and re-encoded before it is stored, so what lands on disk
    /// is always a real image. Uploading a second cover demotes the first.
    /// </remarks>
    [HttpPost("{publicId}/images")]
    [ProducesResponseType(typeof(ApiResponse<SellerBookImage>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<SellerBookImage>>> AddImage(
        string publicId,
        IFormFile file,
        [FromForm] UploadBookImageRequest request,
        CancellationToken cancellationToken)
    {
        EnsureFilePresent(file);

        await using var content = file.OpenReadStream();

        var image = await _books.AddImageAsync(
            publicId,
            content,
            file.FileName,
            file.ContentType,
            request,
            cancellationToken);

        return Success(image, "Photograph uploaded.");
    }

    /// <summary>Removes one photograph and the file behind it.</summary>
    [HttpDelete("{publicId}/images/{imageId:guid}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> RemoveImage(
        string publicId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        await _books.RemoveImageAsync(publicId, imageId, cancellationToken);
        return Success("Photograph removed.");
    }

    /// <summary>Sends the listing to the platform for review.</summary>
    /// <remarks>
    /// A cover photograph is required: the reviewer cannot judge a copy they cannot
    /// see. A rejected listing is taken back to draft first, so the seller can fix
    /// what was wrong and resubmit the same listing.
    /// </remarks>
    [HttpPost("{publicId}/submit")]
    [ProducesResponseType(typeof(ApiResponse<SellerBookDetails>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<SellerBookDetails>>> Submit(
        string publicId,
        CancellationToken cancellationToken) =>
        Success(await _books.SubmitAsync(publicId, cancellationToken), "Sent for review.");

    /// <summary>Withdraws the copy from the platform.</summary>
    [HttpPost("{publicId}/archive")]
    [ProducesResponseType(typeof(ApiResponse<SellerBookDetails>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<SellerBookDetails>>> Archive(
        string publicId,
        [FromBody] ArchiveBookRequest request,
        CancellationToken cancellationToken) =>
        Success(await _books.ArchiveAsync(publicId, request, cancellationToken), "Listing withdrawn.");

    /// <summary>
    /// Model binding leaves a missing file as null rather than as a validation error,
    /// so the check belongs here where the reason can be stated plainly.
    /// </summary>
    private static void EnsureFilePresent(
        IFormFile? file,
        string field = "file",
        string message = "Choose an image to upload.")
    {
        if (file is null || file.Length == 0)
        {
            throw new AppValidationException(field, message);
        }
    }

    private static BookImageUpload Open(IFormFile file, BookImageType type) =>
        new(file.OpenReadStream(), file.FileName, file.ContentType, type);

    /// <summary>
    /// The listing travels as JSON inside the form, so its nested condition block and
    /// enum values read exactly as they would from a JSON body.
    /// </summary>
    private static SaveSellerBookRequest ReadListing(string? data, JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            throw new AppValidationException("data", "The book details are missing.");
        }

        try
        {
            return JsonSerializer.Deserialize<SaveSellerBookRequest>(data, options)
                   ?? throw new AppValidationException("data", "The book details are missing.");
        }
        catch (JsonException)
        {
            throw new AppValidationException("data", "The book details could not be read.");
        }
    }
}

/// <summary>
/// The multipart form that creates a listing. Settable properties, because MVC binds
/// a form through a parameterless constructor.
/// </summary>
public sealed class CreateSellerBookForm
{
    /// <summary>The listing, as the same JSON the update endpoint takes.</summary>
    public string? Data { get; init; }

    /// <summary>The cover photograph. Required.</summary>
    public IFormFile? Cover { get; init; }

    /// <summary>Up to three more photographs.</summary>
    public List<IFormFile>? Photos { get; init; }
}

/// <summary>
/// Query string binding for the seller's own list. Kept apart from the application
/// query type so the wire format stays flat and short.
/// </summary>
public sealed record SellerBookParameters
{
    /// <summary>Only listings in this status.</summary>
    public BookStatus? Status { get; init; }

    /// <summary>Free text, matched against the title and the book code.</summary>
    [FromQuery(Name = "q")]
    public string? Term { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = PageRequest.DefaultPageSize;

    /// <summary>Converts to the application query, where the values are clamped.</summary>
    public SellerBookQuery ToQuery() => new()
    {
        Status = Status,
        Term = Term,
        Page = Page,
        PageSize = PageSize,
    };
}
