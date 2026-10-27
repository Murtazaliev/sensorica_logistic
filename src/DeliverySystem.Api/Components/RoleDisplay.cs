using System.Security.Claims;
using DeliverySystem.Core.Domain;

namespace DeliverySystem.Api.Components;

public static class RoleDisplay
{
    public static string Label(string? role) => role switch
    {
        nameof(UserRole.Admin) => "Администратор",
        nameof(UserRole.Dispatcher) => "Диспетчер",
        nameof(UserRole.Courier) => "Курьер",
        nameof(UserRole.PointManager) => "Менеджер точки",
        nameof(UserRole.SalesManager) => "Менеджер продаж",
        nameof(UserRole.Head) => "Руководитель",
        _ => role ?? "—"
    };

    public static string Label(ClaimsPrincipal user) =>
        Label(user.FindFirst(ClaimTypes.Role)?.Value);
}
