using BookStore.Api.Common;
using BookStore.Application.Features.Buying;
using BookStore.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookStore.Api.Controllers.Buying;

/// <summary>
/// A buyer's delivery addresses. Only the owner and the warehouse staff who dispatch
/// a parcel ever see one; a seller never does.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.RequireMember)]
[Route("api/addresses")]
public sealed class AddressesController : ApiControllerBase
{
    private readonly AddressService _addresses;

    public AddressesController(AddressService addresses) => _addresses = addresses;

    /// <summary>Every saved address, the default one first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AddressView>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AddressView>>>> List(
        CancellationToken cancellationToken) =>
        Success(await _addresses.ListAsync(cancellationToken));

    /// <summary>One saved address.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AddressView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AddressView>>> Get(
        Guid id,
        CancellationToken cancellationToken) =>
        Success(await _addresses.GetAsync(id, cancellationToken));

    /// <summary>Saves a new address. The first one added becomes the default.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AddressView>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AddressView>>> Create(
        [FromBody] SaveAddressRequest request,
        CancellationToken cancellationToken)
    {
        var address = await _addresses.CreateAsync(request, cancellationToken);

        return CreatedResource($"/api/addresses/{address.Id}", address, "Address saved.");
    }

    /// <summary>Edits an address. Past orders keep the copy they were shipped to.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AddressView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AddressView>>> Update(
        Guid id,
        [FromBody] SaveAddressRequest request,
        CancellationToken cancellationToken) =>
        Success(await _addresses.UpdateAsync(id, request, cancellationToken), "Address saved.");

    /// <summary>Makes this the address checkout pre-selects.</summary>
    [HttpPost("{id:guid}/default")]
    [ProducesResponseType(typeof(ApiResponse<AddressView>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AddressView>>> MakeDefault(
        Guid id,
        CancellationToken cancellationToken) =>
        Success(await _addresses.SetDefaultAsync(id, cancellationToken), "Default address updated.");

    /// <summary>
    /// Removes an address from the list. It is hidden rather than deleted, because
    /// past orders carry a copy of it that has to stay traceable.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse>> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        await _addresses.DeleteAsync(id, cancellationToken);
        return Success("Address removed.");
    }
}
