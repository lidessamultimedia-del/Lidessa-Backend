using System.Security.Claims;
using Lidessa.Api.Dtos.Supplies;
using Lidessa.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Lidessa.Api.Controllers;

[ApiController]
[Route("api/supplies/orders")]
[Authorize]
public class SupplyOrdersController : ControllerBase
{
    private const string NotFoundMessage = "Pedido no encontrado";

    private readonly SupplyOrderService _service;

    public SupplyOrdersController(SupplyOrderService service)
    {
        _service = service;
    }

    private long CurrentUserId => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // Panel admin: todos los pedidos.
    [Authorize(Roles = "admin")]
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _service.GetAllAsync());
    }

    // "Mis pedidos" del cliente.
    [Authorize(Roles = "cliente")]
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine()
    {
        return Ok(await _service.GetMineAsync(CurrentUserId));
    }

    // Detalle: el admin ve cualquiera; el cliente solo el suyo.
    [Authorize(Roles = "admin,cliente")]
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var callerClientId = User.IsInRole("admin") ? null : (long?)CurrentUserId;
        var result = await _service.GetByIdAsync(id, callerClientId);
        return result is null ? NotFound(new { message = NotFoundMessage }) : Ok(result);
    }

    // El cliente arma su pedido en la tienda; el admin también puede crear
    // uno a mano (walk-in) desde el panel, con o sin cuenta ligada.
    [Authorize(Roles = "admin,cliente")]
    [HttpPost]
    public async Task<IActionResult> Create(SupplyOrderRequest request)
    {
        // Mismo criterio que GetById/Complete: si la cuenta es admin manda el
        // rol admin, aunque también tenga el rol cliente vinculado (ver 2.2).
        // Antes se miraba solo "cliente", y un admin+cliente no podía crear
        // pedidos walk-in ni a nombre de otro cliente: todos le quedaban a él.
        // Su propia compra desde ClientStore sigue a su nombre porque la
        // tienda manda clientId = su id.
        var callerClientId = User.IsInRole("admin") ? null : (long?)CurrentUserId;
        var (result, error) = await _service.CreateAsync(request, callerClientId);
        if (error is not null)
        {
            return error == SupplyOrderService.ConcurrencyConflictMessage
                ? Conflict(new { message = error })
                : BadRequest(new { message = error });
        }

        return CreatedAtAction(nameof(GetById), new { id = result!.Id }, result);
    }

    [Authorize(Roles = "admin")]
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, UpdateSupplyOrderRequest request)
    {
        var (result, error) = await _service.UpdateAsync(id, request);
        return ToActionResult(result, error);
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var (found, error) = await _service.DeleteAsync(id);
        if (error is not null)
        {
            return Conflict(new { message = error });
        }

        return found ? NoContent() : NotFound(new { message = NotFoundMessage });
    }

    [Authorize(Roles = "admin")]
    [HttpPatch("{id:long}/confirm")]
    public async Task<IActionResult> Confirm(long id)
    {
        var (result, error) = await _service.ConfirmAsync(id);
        return ToActionResult(result, error);
    }

    [Authorize(Roles = "admin")]
    [HttpPatch("{id:long}/reject")]
    public async Task<IActionResult> Reject(long id, RejectSupplyOrderRequest request)
    {
        var (result, error) = await _service.RejectAsync(id, request);
        return ToActionResult(result, error);
    }

    [Authorize(Roles = "admin")]
    [HttpPatch("{id:long}/ship")]
    public async Task<IActionResult> Ship(long id, ShipSupplyOrderRequest request)
    {
        var (result, error) = await _service.ShipAsync(id, request);
        return ToActionResult(result, error);
    }

    [Authorize(Roles = "admin")]
    [HttpPatch("{id:long}/payment-shortfall")]
    public async Task<IActionResult> PaymentShortfall(long id, PaymentShortfallRequest request)
    {
        var (result, error) = await _service.RequestPaymentShortfallAsync(id, request);
        return ToActionResult(result, error);
    }

    // El cliente confirma que le llegó su pedido; el admin también puede
    // cerrarlo (p. ej. pedidos walk-in sin cuenta).
    [Authorize(Roles = "admin,cliente")]
    [HttpPatch("{id:long}/complete")]
    public async Task<IActionResult> Complete(long id)
    {
        var callerClientId = User.IsInRole("admin") ? null : (long?)CurrentUserId;
        var (result, error) = await _service.CompleteAsync(id, callerClientId);
        return ToActionResult(result, error);
    }

    // Botón "Ya me llegó" dentro del correo de envío: sin login, protegido
    // por el token de un solo uso que se generó al despachar el pedido.
    [AllowAnonymous]
    [HttpGet("confirm-delivery/{token}")]
    public async Task<IActionResult> ConfirmDelivery(string token)
    {
        var (success, error) = await _service.ConfirmDeliveryAsync(token);
        if (!success)
        {
            return error == SupplyOrderService.ConcurrencyConflictMessage
                ? Conflict(new { message = error })
                : BadRequest(new { message = error });
        }

        return Ok(new { message = "Gracias, confirmamos que tu pedido fue entregado." });
    }

    private IActionResult ToActionResult(SupplyOrderResponse? result, string? error)
    {
        if (error is null)
        {
            return Ok(result);
        }

        return error switch
        {
            NotFoundMessage => NotFound(new { message = error }),
            SupplyOrderService.ConcurrencyConflictMessage => Conflict(new { message = error }),
            _ => BadRequest(new { message = error }),
        };
    }
}
