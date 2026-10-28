using System.Globalization;
using System.Net;
using System.Security.Claims;
using DeliverySystem.Api.Components;
using DeliverySystem.Core.Domain;
using DeliverySystem.Core.Intake;
using DeliverySystem.Core.Points;
using DeliverySystem.Core.Routes;
using DeliverySystem.Core.Sync;
using DeliverySystem.Infrastructure.Demo;
using DeliverySystem.Infrastructure.Intake;
using DeliverySystem.Infrastructure.Maps;
using DeliverySystem.Infrastructure.Persistence;
using DeliverySystem.Infrastructure.Points;
using DeliverySystem.Infrastructure.Routes;
using DeliverySystem.Infrastructure.Sync;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=delivery.db";
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped<IBusinessDayCalculator, WeekendBusinessDayCalculator>();
builder.Services.AddScoped<OrderIntakeService>();
builder.Services.AddSingleton<AmoFixtureRepository>();
builder.Services.AddScoped<MockAmoIntake>();
builder.Services.AddScoped<IOrderIntakeSource>(sp => sp.GetRequiredService<MockAmoIntake>());
builder.Services.AddScoped<ISyncService, SyncService>();
builder.Services.AddScoped<PointService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new DepotOptions(
    builder.Configuration["Depot:Name"] ?? "Цех",
    builder.Configuration["Depot:Address"],
    ParseConfigDouble(builder.Configuration["Depot:Latitude"]),
    ParseConfigDouble(builder.Configuration["Depot:Longitude"])));
builder.Services.AddScoped(sp => new RouteService(
    sp.GetRequiredService<AppDbContext>(),
    sp.GetRequiredService<TimeProvider>(),
    sp.GetRequiredService<DepotOptions>(),
    sp.GetRequiredService<IRoadRouter>(),
    sp.GetRequiredService<IRoadDistanceMatrix>()));
builder.Services.AddScoped<DemoOrderService>();
builder.Services.AddHttpClient("twogis", client =>
{
    client.BaseAddress = new Uri("https://catalog.api.2gis.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddScoped(sp => new TwoGisGeocoder(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("twogis"),
    sp.GetRequiredService<IConfiguration>()["TwoGis:ApiKey"] ?? string.Empty));
builder.Services.AddHttpClient("twogis-routing", client =>
{
    client.BaseAddress = new Uri("https://routing.api.2gis.com/");
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddScoped<IRoadRouter>(sp => new TwoGisRouter(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("twogis-routing"),
    sp.GetRequiredService<IConfiguration>()["TwoGis:ApiKey"] ?? string.Empty));
builder.Services.AddHttpClient("twogis-matrix", client =>
{
    client.BaseAddress = new Uri("https://routing.api.2gis.com/");
    client.Timeout = TimeSpan.FromSeconds(20);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddScoped<IRoadDistanceMatrix>(sp => new TwoGisDistanceMatrix(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("twogis-matrix"),
    sp.GetRequiredService<IConfiguration>()["TwoGis:ApiKey"] ?? string.Empty));
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ds.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/forbidden";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Staff", policy => policy.RequireRole(
        nameof(UserRole.Dispatcher),
        nameof(UserRole.Admin),
        nameof(UserRole.Head)));
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(nameof(UserRole.Admin)));
    options.AddPolicy("CourierOnly", policy => policy.RequireRole(nameof(UserRole.Courier)));
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapStaticAssets();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
    db.Database.EnsureCreated();
    DevSeed.EnsureSeeded(db, hasher);
}

app.MapGet("/", () => Results.Ok(new
{
    service = "DeliverySystem.Api",
    health = "/health",
    login = "/login",
    swagger = "/swagger",
    ui = new { orders = "/orders", routes = "/routes", points = "/points", sync = "/sync" },
    scenarios = "/dev/amo/scenarios",
    orders = "/api/orders",
    routes = "/api/routes",
    couriers = "/api/couriers",
    points = "/api/points",
    geocode = new
    {
        forward = "/api/geocode?q=Москва,+Тверская+12",
        reverse = "/api/geocode/reverse?lat=55.7558&lng=37.6173",
        suggest = "/api/suggest?q=Тверская"
    },
    sync = new { push = "/api/sync/push", pull = "/api/sync/pull?since=0&limit=100" },
    courierSync = "/api/courier/sync/push"
}));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/login", (
    HttpContext ctx,
    IAntiforgery antiforgery,
    IWebHostEnvironment env) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true)
    {
        return Results.Redirect(HomeForRole(ctx.User.FindFirst(ClaimTypes.Role)?.Value));
    }

    var returnUrl = ctx.Request.Query["returnUrl"].FirstOrDefault()
        ?? ctx.Request.Query["ReturnUrl"].FirstOrDefault();
    var email = ctx.Request.Query["email"].FirstOrDefault();
    var hasError = ctx.Request.Query.ContainsKey("error");
    var denied = ctx.Request.Query.ContainsKey("denied");

    var tokens = antiforgery.GetAndStoreTokens(ctx);
    var html = BuildLoginHtml(
        tokens.RequestToken ?? string.Empty,
        returnUrl,
        email,
        hasError,
        denied,
        env.IsDevelopment());

    return Results.Content(html, "text/html; charset=utf-8");
}).AllowAnonymous();

app.MapPost("/auth/login", async (
    HttpContext ctx,
    AppDbContext db,
    IPasswordHasher<AppUser> hasher,
    IAntiforgery antiforgery) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(ctx);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.BadRequest("Недействительный токен формы. Обновите страницу входа.");
    }

    var form = await ctx.Request.ReadFormAsync();
    var email = (form["email"].ToString() ?? string.Empty).Trim().ToLowerInvariant();
    var password = form["password"].ToString() ?? string.Empty;
    var requestedReturnUrl = form["returnUrl"].ToString();

    var user = string.IsNullOrWhiteSpace(email)
        ? null
        : await db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);

    if (user is null)
    {
        return LoginError(email, requestedReturnUrl);
    }

    var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
    if (verification == PasswordVerificationResult.Failed)
    {
        return LoginError(email, requestedReturnUrl);
    }

    if (verification == PasswordVerificationResult.SuccessRehashNeeded)
    {
        user.PasswordHash = hasher.HashPassword(user, password);
    }

    user.LastLoginAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name, user.FullName),
        new(ClaimTypes.Email, user.Email),
        new(ClaimTypes.Role, user.Role.ToString()),
        new("tenant_id", user.TenantId.ToString())
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
        });

    var target = IsLocalUrl(requestedReturnUrl)
        ? requestedReturnUrl
        : HomeForRole(user.Role.ToString());

    return Results.Redirect(target);
}).AllowAnonymous();

app.MapGet("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).AllowAnonymous();

app.MapGet("/api/orders", async (AppDbContext db, CancellationToken ct) =>
{
    var orders = await db.Orders
        .AsNoTracking()
        .Include(o => o.Items)
        .Include(o => o.StatusHistory)
        .ToListAsync(ct);

    return Results.Ok(orders.OrderByDescending(o => o.CreatedAt));
}).RequireAuthorization("Staff");

app.MapGet("/api/orders/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
{
    var order = await db.Orders
        .AsNoTracking()
        .Include(o => o.Items)
        .Include(o => o.StatusHistory)
        .FirstOrDefaultAsync(o => o.Id == id, ct);

    return order is null ? Results.NotFound() : Results.Ok(order);
}).RequireAuthorization("Staff");

app.MapGet("/api/webhook-logs", async (AppDbContext db, CancellationToken ct) =>
{
    var logs = await db.WebhookLogs.AsNoTracking().ToListAsync(ct);
    return Results.Ok(logs.OrderByDescending(x => x.CreatedAt));
}).RequireAuthorization("AdminOnly");

app.MapGet("/api/points", async (PointService points, CancellationToken ct) =>
        Results.Ok(await points.ListAsync(DevSeed.TenantId, ct)))
    .RequireAuthorization();

app.MapGet("/api/couriers", async (RouteService routes, CancellationToken ct) =>
    Results.Ok(await routes.ListCouriersAsync(DevSeed.TenantId, ct))).RequireAuthorization("Staff");

app.MapGet("/api/routes", async (
    DateOnly? date,
    RouteService routes,
    CancellationToken ct) =>
{
    var day = date ?? DateOnly.FromDateTime(DateTime.Today);
    return Results.Ok(await routes.ListRoutesAsync(DevSeed.TenantId, day, ct));
}).RequireAuthorization("Staff");

app.MapGet("/api/routes/available-orders", async (
    DateOnly? date,
    RouteService routes,
    CancellationToken ct) =>
{
    var day = date ?? DateOnly.FromDateTime(DateTime.Today);
    return Results.Ok(await routes.ListAvailableOrdersAsync(DevSeed.TenantId, day, ct));
}).RequireAuthorization("Staff");

app.MapGet("/api/routes/{id:guid}", async (
    Guid id,
    RouteService routes,
    CancellationToken ct) =>
{
    var route = await routes.GetAsync(DevSeed.TenantId, id, ct);
    return route is null ? Results.NotFound() : Results.Ok(route);
}).RequireAuthorization("Staff");

app.MapPost("/api/routes", async (
    CreateRouteRequest request,
    RouteService routes,
    CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await routes.EnsureDraftAsync(
            DevSeed.TenantId, request.CourierId, request.Date, ct));
    }
    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
    {
        return Results.BadRequest(new { error = "route_error", message = ex.Message });
    }
}).RequireAuthorization("Staff");

app.MapPut("/api/routes/{id:guid}/stops", async (
    Guid id,
    SetRouteStopsRequest request,
    RouteService routes,
    CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await routes.SetStopsAsync(
            DevSeed.TenantId, id, request.OrderIds ?? [], ct: ct));
    }
    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
    {
        return Results.BadRequest(new { error = "route_error", message = ex.Message });
    }
}).RequireAuthorization("Staff");

app.MapPost("/api/routes/{id:guid}/auto-order", async (
    Guid id,
    RouteService routes,
    CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await routes.AutoOrderAsync(DevSeed.TenantId, id, ct));
    }
    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
    {
        return Results.BadRequest(new { error = "route_error", message = ex.Message });
    }
}).RequireAuthorization("Staff");

app.MapPost("/api/routes/{id:guid}/confirm", async (
    Guid id,
    RouteService routes,
    CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await routes.ConfirmAsync(DevSeed.TenantId, id, ct));
    }
    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
    {
        return Results.BadRequest(new { error = "route_error", message = ex.Message });
    }
}).RequireAuthorization("Staff");

app.MapPost("/api/points", async (
    PointUpsertRequest request,
    PointService points,
    CancellationToken ct) =>
{
    try
    {
        var result = await points.UpsertAsync(DevSeed.TenantId, request, ct);
        return Results.Ok(result);
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = "invalid_point", message = ex.Message });
    }
}).RequireAuthorization("Staff");

app.MapGet("/api/geocode", async (
    string? q,
    TwoGisGeocoder geocoder,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(q))
    {
        return Results.BadRequest(new { error = "query_required" });
    }

    try
    {
        return Results.Ok(await geocoder.ForwardAsync(q.Trim(), ct));
    }
    catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or OperationCanceledException)
    {
        return Results.BadRequest(new { error = "geocode_failed", message = ex.Message });
    }
}).RequireAuthorization();

app.MapGet("/api/geocode/reverse", async (
    double? lat,
    double? lng,
    TwoGisGeocoder geocoder,
    CancellationToken ct) =>
{
    if (lat is null or < -90 or > 90 || lng is null or < -180 or > 180)
    {
        return Results.BadRequest(new { error = "invalid_coordinates" });
    }

    try
    {
        var place = await geocoder.ReverseAsync(lat.Value, lng.Value, ct);
        return place is null ? Results.NotFound() : Results.Ok(place);
    }
    catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or OperationCanceledException)
    {
        return Results.BadRequest(new { error = "geocode_failed", message = ex.Message });
    }
}).RequireAuthorization();

app.MapGet("/api/suggest", async (
    string? q,
    TwoGisGeocoder geocoder,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(q))
    {
        return Results.Ok(Array.Empty<object>());
    }

    try
    {
        return Results.Ok(await geocoder.SuggestAsync(q.Trim(), ct));
    }
    catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or OperationCanceledException)
    {
        return Results.BadRequest(new { error = "suggest_failed", message = ex.Message });
    }
}).RequireAuthorization();

app.MapPost("/api/sync/push", async (
    SyncPushRequest request,
    ISyncService sync,
    CancellationToken ct) =>
{
    if (request.Operations is null || request.Operations.Count == 0)
    {
        return Results.BadRequest(new { error = "operations_required" });
    }

    return Results.Ok(await sync.PushAsync(DevSeed.TenantId, request, ct));
}).RequireAuthorization();

app.MapPost("/api/courier/sync/push", async (
    HttpContext ctx,
    SyncPushRequest request,
    ISyncService sync,
    RouteService routes,
    CancellationToken ct) =>
{
    if (request.Operations is null || request.Operations.Count == 0)
    {
        return Results.BadRequest(new { error = "operations_required" });
    }

    var userIdValue = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!Guid.TryParse(userIdValue, out var userId))
    {
        return Results.Forbid();
    }

    var courierId = await routes.GetCourierIdForUserAsync(DevSeed.TenantId, userId, ct);
    if (courierId is null)
    {
        return Results.Forbid();
    }

    return Results.Ok(await sync.PushCourierAsync(
        DevSeed.TenantId,
        courierId.Value,
        request,
        ct));
}).RequireAuthorization("CourierOnly");

app.MapGet("/api/sync/pull", async (
    long? since,
    int? limit,
    ISyncService sync,
    CancellationToken ct) =>
{
    var result = await sync.PullAsync(DevSeed.TenantId, since ?? 0, limit ?? 100, ct);
    return Results.Ok(result);
}).RequireAuthorization();

if (app.Environment.IsDevelopment())
{
    var dev = app.MapGroup("/dev/amo").RequireAuthorization("Staff");

    dev.MapGet("/scenarios", (AmoFixtureRepository fixtures) => Results.Ok(fixtures.ListScenarios()));

    dev.MapPost("/simulate", async (
        SimulateRequest request,
        MockAmoIntake intake,
        AmoFixtureRepository fixtures,
        AppDbContext db,
        CancellationToken ct) =>
    {
        var scenarios = fixtures.ListScenarios();
        var scenario = scenarios.FirstOrDefault(
            x => string.Equals(x, request.Scenario, StringComparison.OrdinalIgnoreCase));

        if (scenario is null)
        {
            return Results.BadRequest(new
            {
                error = "unknown_scenario",
                message = "Неизвестный сценарий. Список: GET /dev/amo/scenarios.",
                scenario = request.Scenario,
                available = scenarios
            });
        }

        var now = DateTimeOffset.UtcNow;
        if (request.Now is not null)
        {
            if (!DateTimeOffset.TryParse(
                    request.Now,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedNow))
            {
                return Results.BadRequest(new
                {
                    error = "invalid_now",
                    message = "now должен быть датой ISO 8601, например 2026-10-27T07:20:00Z.",
                    value = request.Now
                });
            }

            now = parsedNow;
        }

        var result = await intake.RunScenarioAsync(scenario, DevSeed.TenantId, now, request.LeadId, ct);
        var order = result.OrderId is null
            ? null
            : await db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == result.OrderId, ct);

        return Results.Ok(new { result.Status, result.OrderId, result.Reason, Order = order });
    });

    var devOrders = app.MapGroup("/dev/orders").RequireAuthorization("Staff");

    devOrders.MapPost("/demo", async (
        DateOnly? date,
        DemoOrderService demo,
        CancellationToken ct) =>
    {
        var day = date ?? DateOnly.FromDateTime(DateTime.Today).AddDays(1);

        try
        {
            var created = await demo.CreateForDateAsync(DevSeed.TenantId, day, 4, ct);
            return Results.Ok(new { date = day, created });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = "invalid_date", message = ex.Message });
        }
    });
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization();

static IResult LoginError(string email, string? returnUrl) =>
    Results.Redirect(
        $"/login?error=1&email={Uri.EscapeDataString(email)}&returnUrl={Uri.EscapeDataString(returnUrl ?? string.Empty)}");

static bool IsLocalUrl(string? url) =>
    !string.IsNullOrWhiteSpace(url)
    && url.StartsWith('/')
    && !url.StartsWith("//")
    && !url.StartsWith("/\\");

static double? ParseConfigDouble(string? value) =>
    double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : null;

static string HomeForRole(string? role) =>
    role switch
    {
        nameof(UserRole.Admin) or nameof(UserRole.Dispatcher) or nameof(UserRole.Head) => "/orders",
        nameof(UserRole.Courier) => "/my-routes",
        _ => "/welcome"
    };

static string BuildLoginHtml(
    string token,
    string? returnUrl,
    string? email,
    bool hasError,
    bool denied,
    bool isDevelopment)
{
    var errorHtml = hasError
        ? """<div class="alert error">Неверный email или пароль.</div>"""
        : denied
            ? """<div class="alert error">Недостаточно прав для этой страницы.</div>"""
            : string.Empty;

    var demoHtml = isDevelopment
        ? """
          <div class="login-demo">
              <p class="field-label">Демо-доступы (Development)</p>
              <table class="table compact">
                  <tbody>
                      <tr><td>admin@demo.local</td><td>Администратор</td></tr>
                      <tr><td>dispatcher@demo.local</td><td>Диспетчер</td></tr>
                      <tr><td>head@demo.local</td><td>Руководитель</td></tr>
                      <tr><td>courier@demo.local</td><td>Курьер</td></tr>
                      <tr><td>courier2@demo.local</td><td>Курьер 2</td></tr>
                      <tr><td>courier3@demo.local</td><td>Курьер 3</td></tr>
                      <tr><td>point@demo.local</td><td>Менеджер точки</td></tr>
                      <tr><td>sales@demo.local</td><td>Менеджер продаж</td></tr>
                  </tbody>
              </table>
              <p class="hint">Пароль у всех демо-учёток: <code>demo123</code></p>
          </div>
          """
        : string.Empty;

    var tokenValue = WebUtility.HtmlEncode(token);
    var returnValue = WebUtility.HtmlEncode(returnUrl ?? string.Empty);
    var emailValue = WebUtility.HtmlEncode(email ?? string.Empty);

    return $"""
<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Вход — DeliverySystem</title>
    <link rel="stylesheet" href="/app.css" />
</head>
<body class="login-body">
    <main class="login-card">
        <div class="brand-mark">DS</div>
        <h1>Вход в DeliverySystem</h1>
        <p class="muted">Диспетчерская система доставки</p>
        {errorHtml}
        <form class="login-form" method="post" action="/auth/login">
            <input type="hidden" name="__RequestVerificationToken" value="{tokenValue}" />
            <input type="hidden" name="returnUrl" value="{returnValue}" />
            <label class="field-label" for="email">Email</label>
            <input class="input" id="email" name="email" type="email" value="{emailValue}" autocomplete="username" required autofocus />
            <label class="field-label" for="password">Пароль</label>
            <input class="input" id="password" name="password" type="password" autocomplete="current-password" required />
            <button class="btn primary" type="submit">Войти</button>
        </form>
        {demoHtml}
    </main>
</body>
</html>
""";
}

app.Run();

public sealed record SimulateRequest(string Scenario, string? Now, long? LeadId);
