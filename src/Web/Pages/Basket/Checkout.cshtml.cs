using Ardalis.GuardClauses;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Web.Configuration;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Microsoft.eShopWeb.Web.Pages.Basket;

[Authorize]
public class CheckoutModel : PageModel
{
    private readonly IBasketService _basketService;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IOrderService _orderService;
    private string? _username = null;
    private readonly IBasketViewModelService _basketViewModelService;
    private readonly IAppLogger<CheckoutModel> _logger;
    private readonly IOptionsSnapshot<Functions> _functionOpts;
    private readonly HttpClient client = new HttpClient();

    public CheckoutModel(IBasketService basketService,
        IBasketViewModelService basketViewModelService,
        SignInManager<ApplicationUser> signInManager,
        IOrderService orderService,
        IAppLogger<CheckoutModel> logger, IOptionsSnapshot<Functions> functionOpts)
    {
        _basketService = basketService;
        _signInManager = signInManager;
        _orderService = orderService;
        _basketViewModelService = basketViewModelService;
        _logger = logger;
        _functionOpts = functionOpts;
    }

    public BasketViewModel BasketModel { get; set; } = new BasketViewModel();

    public async Task OnGet()
    {
        await SetBasketModelAsync();
    }

    public async Task<IActionResult> OnPost(IEnumerable<BasketItemViewModel> items)
    {
        try
        {
            await SetBasketModelAsync();

            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var updateModel = items.ToDictionary(b => b.Id.ToString(), b => b.Quantity);
            await _basketService.SetQuantities(BasketModel.Id, updateModel);
            await _orderService.CreateOrderAsync(BasketModel.Id, new Address("123 Main St.", "Kent", "OH", "United States", "44240"));
            await _basketService.DeleteBasketAsync(BasketModel.Id);
            
            if (!string.IsNullOrEmpty(_functionOpts.Value.ItemsReserverUri))
            {
                await client.PostAsync(_functionOpts.Value.ItemsReserverUri,
                    JsonContent.Create(items.Select(x => new Order(x.Id, x.Quantity))));
            }

            if (!string.IsNullOrEmpty(_functionOpts.Value.ServiceBusConnectionString))
            {
                var client = new ServiceBusClient(_functionOpts.Value.ServiceBusConnectionString);
                var sender = client.CreateSender("orders");
                var msg = new ServiceBusMessage(
                    JsonConvert.SerializeObject(items.Select(x =>  new Order(x.Id, x.Quantity)))
                    );
                await sender.SendMessageAsync(msg);

                await sender.DisposeAsync();
                await client.DisposeAsync();
            }            //Redirect to Empty Basket page


            if (!string.IsNullOrEmpty(_functionOpts.Value.DeliveryNotificationUri) )
            {
                var deliveryInfo = new DeliveryIngormation(
                    Id: Guid.NewGuid(),
                    ShippingAddress: new Address("123 Main St.", "Kent", "OH", "United States", "44240"),
                    FinalPrice: BasketModel.Items.Select(x => x.Quantity * x.UnitPrice).Sum(),
                    Items: BasketModel.Items.Select(x => new Item(x.Id, x.Quantity, x.ProductName)).ToArray());
               await  client.PostAsync(_functionOpts.Value.DeliveryNotificationUri, JsonContent.Create(deliveryInfo));
            }
            
        }
        catch (EmptyBasketOnCheckoutException emptyBasketOnCheckoutException)
        {
            _logger.LogWarning(emptyBasketOnCheckoutException.Message);
            return RedirectToPage("/Basket/Index");
        }

        return RedirectToPage("Success");
    }

    private async Task SetBasketModelAsync()
    {
        Guard.Against.Null(User?.Identity?.Name, nameof(User.Identity.Name));
        if (_signInManager.IsSignedIn(HttpContext.User))
        {
            BasketModel = await _basketViewModelService.GetOrCreateBasketForUser(User.Identity.Name);
        }
        else
        {
            GetOrSetBasketCookieAndUserName();
            BasketModel = await _basketViewModelService.GetOrCreateBasketForUser(_username!);
        }
    }

    private void GetOrSetBasketCookieAndUserName()
    {
        if (Request.Cookies.ContainsKey(Constants.BASKET_COOKIENAME))
        {
            _username = Request.Cookies[Constants.BASKET_COOKIENAME];
        }
        if (_username != null) return;

        _username = Guid.NewGuid().ToString();
        var cookieOptions = new CookieOptions();
        cookieOptions.Expires = DateTime.Today.AddYears(10);
        Response.Cookies.Append(Constants.BASKET_COOKIENAME, _username, cookieOptions);
    }
    public record class Order(int Id, int Quantity);
    public record DeliveryIngormation(Guid Id, Address ShippingAddress, decimal FinalPrice, Item[] Items);
    public record Item(int Id, int Quantity, string Name);

}
