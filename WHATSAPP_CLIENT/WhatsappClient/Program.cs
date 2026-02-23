using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Authorization;
using System.Net.Http.Headers;
using WhatsappClient.Services;
using WhatsappClient.Middleware;
using WhatsappClient.Helpers;

var builder = WebApplication.CreateBuilder(args);

// Lee SIEMPRE de appsettings: Api:BaseUrl
var apiBase = (builder.Configuration["Api:BaseUrl"] ?? "https://nondeclaratory-brecken-unperpendicularly.ngrok-free.dev/").TrimEnd('/') + "/";
var companyIdFallback = builder.Configuration["Api:CompanyId"] ?? "1";

builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new AuthorizeFilter());
});

// Cookie Auth (para la web del cliente)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt =>
    {
        opt.LoginPath = "/Account/Login";
        opt.LogoutPath = "/Account/Logout";
        opt.AccessDeniedPath = "/Account/Denied";
        opt.ExpireTimeSpan = TimeSpan.FromHours(2);
        opt.SlidingExpiration = true;
        opt.Cookie.Name = "whatsappclient.auth";
        opt.Cookie.HttpOnly = true;
        opt.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        opt.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddAuthorization();

// Session para guardar JWT y empresa_id
builder.Services.AddHttpContextAccessor();
builder.Services.AddSession(o =>
{
    o.Cookie.Name = "whatsappclient.session";
    o.IdleTimeout = TimeSpan.FromHours(2);
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.Cookie.SameSite = SameSiteMode.Lax;
});

// Handler para log de requests salientes
builder.Services.AddTransient<OutgoingDebugHandler>();

// HttpClient para ApiService
builder.Services
    .AddHttpClient<ApiService>(client =>
    {
        client.BaseAddress = new Uri(apiBase);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    })
    .AddHttpMessageHandler<OutgoingDebugHandler>()
    .AddTypedClient<ApiService>((http, sp) =>
    {
        var acc = sp.GetRequiredService<IHttpContextAccessor>();
        return new ApiService(http, companyIdFallback, acc);
    });

// HttpClient named para AccountController (login)   usa el MISMO BaseUrl
builder.Services.AddHttpClient(nameof(WhatsappClient.Controllers.AccountController), client =>
{
    client.BaseAddress = new Uri(apiBase);
    client.DefaultRequestHeaders.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/json"));
});

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

// Session ANTES de Auth
app.UseSession();

// Middleware de verificación de Auth (JWT en sesión)
app.UseMiddleware<AuthCheckMiddleware>();

app.UseAuthentication();

// redirección raíz
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/")
    {
        var isAuth = context.User?.Identity?.IsAuthenticated == true;
        context.Response.Redirect(isAuth ? "/dashboard" : "/account/login");
        return;
    }
    await next();
});


app.UseAuthorization();

// Rutas
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
