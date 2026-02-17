using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text; // para Encoding.UTF8.GetBytes
using System.Text.Json.Serialization;
using Whatsapp_API.BotFlows.Core;
using Whatsapp_API.Business.General;
using Whatsapp_API.Business.Integrations;
using Whatsapp_API.Business.Security;
using Whatsapp_API.Business.VAMMP;
using Whatsapp_API.Business.Whatsapp; 
using Whatsapp_API.Data;
using Whatsapp_API.Helpers;
using Whatsapp_API.Infrastructure.MultiTenancy;

var builder = WebApplication.CreateBuilder(args);

// Controllers + JSON
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.Preserve;
    });

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Whatsapp API",
        Version = "v1",
        Description = "Whatsapp API",
        Contact = new OpenApiContact { Name = "Kenneth Martinez", Email = "kennethmartinezvargas@gmail.com" }
    });

    options.AddServer(new OpenApiServer { Url = "/" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Bearer. Ej: \"Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    // Header auxiliar (solo bootstrap si no hay JWT)
    options.AddSecurityDefinition("Empresa", new OpenApiSecurityScheme
    {
        Description = "Id de Empresa para bootstrap (solo si aún no tienes JWT). Header: X-Empresa-Id",
        Name = "X-Empresa-Id",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantContext>();

// DbContext
builder.Services.AddDbContext<MyDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// HttpClient / Buses / Helpers
builder.Services.AddHttpClient();
builder.Services.AddHttpClient(nameof(UserVAMMPBus));
builder.Services.AddHttpClient(nameof(Auth2Bus));
builder.Services.AddHttpClient(nameof(Whatsapp_API.Business.Integrations.WhatsappSender));
builder.Services.AddScoped<Whatsapp_API.Business.Integrations.WhatsappSender>();

builder.Services.AddSingleton<EmailHelper>();
builder.Services.AddScoped<ProfileBus>();
builder.Services.AddScoped<ContactBus>();
builder.Services.AddScoped<ConversationBus>();
builder.Services.AddScoped<MessageBus>();
builder.Services.AddScoped<AttachmentBus>();
builder.Services.AddScoped<IntegrationBus>();
builder.Services.AddScoped<UserVAMMPBus>();
builder.Services.AddScoped<Auth2Bus>();
builder.Services.AddScoped<WhatsappTemplateBus>();
builder.Services.AddScoped<UserBus>();

// 👇 Servicio para descarga on-demand de media WhatsApp
builder.Services.AddScoped<WhatsappMediaOnDemandService>();

// CORS (dev)
builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Flows
builder.Services.AddScoped<IChatFlow, Whatsapp_API.BotFlows.Cobano.CobanoFlow>();
builder.Services.AddScoped<IChatFlow, Whatsapp_API.BotFlows.Zaifu.ZaifuFlow>();
builder.Services.AddScoped<IFlowRouter, Whatsapp_API.BotFlows.Core.FlowRouter>();

builder.Services.Configure<WhatsAppOptions>(builder.Configuration.GetSection("WhatsApp"));

// DataProtection
builder.Services.AddDataProtection()
    .SetApplicationName("Whatsapp_API")
    .PersistKeysToFileSystem(new DirectoryInfo(
        Path.Combine(builder.Environment.ContentRootPath, "keys")));

// Scrutor (opcional)
builder.Services.Scan(scan => scan
    .FromAssemblyOf<Program>()
    .AddClasses(c => c.InNamespaces(
        "Whatsapp_API.Business.General",
        "Whatsapp_API.Business.Security",
        "Whatsapp_API.Business.Integrations",
        "Whatsapp_API.Helpers"
    ))
    .AsSelfWithInterfaces()
    .WithScopedLifetime());

// Excluir value-record que no debe resolverse vía DI (evita fallo por constructor con valores int, double, string...)
builder.Services.RemoveAll<TemplateHeaderLocation>();

// CORS (dev) – (queda duplicada pero con la misma policy "Default")
builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

// ===== JWT: usa SIEMPRE la clave del appsettings (ignora 'kid') =====
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection.GetValue<string>("Key") ?? throw new InvalidOperationException("Config Jwt:Key requerido");
var jwtIssuer = jwtSection.GetValue<string>("Issuer") ?? "WhatsappApi";
var jwtAudience = jwtSection.GetValue<string>("Audience") ?? "WhatsappClient";
var symKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,

            ValidateAudience = true,
            ValidAudience = jwtAudience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = symKey,

            // Aunque el token traiga 'kid', valida con esta misma key
            IssuerSigningKeyResolver = (token, st, kid, p) => new[] { symKey },

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        // Logea motivo exacto si falla
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                Console.WriteLine("[JWT][FAIL] " + ctx.Exception.GetType().Name + " - " + ctx.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

var env = app.Services.GetRequiredService<IWebHostEnvironment>();
SimpleFileLogger.ConfigureRoot(env.ContentRootPath);

// Exception pages
if (env.IsDevelopment()) app.UseDeveloperExceptionPage();
else app.UseExceptionHandler("/error");

// Forwarded headers (ngrok/proxy)
var fwd = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
fwd.KnownNetworks.Clear();
fwd.KnownProxies.Clear();
app.UseForwardedHeaders(fwd);

// Swagger
var swaggerPrefix = builder.Configuration["Swagger:RoutePrefix"] ?? "swagger";
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.RoutePrefix = swaggerPrefix;
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Whatsapp API v1");
});

// Security headers
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["X-XSS-Protection"] = "0";
    ctx.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
    await next();
});

// CORS
app.UseCors("Default");

// Auth + Tenant
app.UseAuthentication();
app.UseMiddleware<TenantResolverMiddleware>();
app.UseAuthorization();

// Controllers
app.MapControllers();

// Atajo raíz → swagger
app.MapGet("/", () => Results.Redirect($"/{swaggerPrefix}"));

app.Run();
