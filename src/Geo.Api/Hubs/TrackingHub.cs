using Microsoft.AspNetCore.SignalR;

namespace Geo.Api.Hubs;

/// <summary>
/// Хаб живого трекинга. Сервер пушит события через IHubContext&lt;TrackingHub&gt;
/// (см. TrackSimulatorService), клиент может подписаться на конкретный объект.
/// </summary>
public class TrackingHub : Hub
{
    /// <summary>Broadcast-группа всех подключённых клиентов.</summary>
    public const string AllGroup = "all";

    /// <summary>Имя клиентского события обновления позиции.</summary>
    public const string PositionUpdated = "positionUpdated";

    /// <summary>Имя клиентского события входа/выхода в геозону.</summary>
    public const string ZoneEvent = "zoneEvent";

    /// <summary>Имя группы конкретного объекта.</summary>
    public static string ObjectGroup(Guid id) => $"object-{id}";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, AllGroup);
        await base.OnConnectedAsync();
    }

    /// <summary>Подписка на обновления конкретного объекта.</summary>
    public Task JoinObjectGroup(string objectId)
        => Groups.AddToGroupAsync(Context.ConnectionId, $"object-{objectId}");

    /// <summary>Отписка от обновлений конкретного объекта.</summary>
    public Task LeaveObjectGroup(string objectId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"object-{objectId}");
}
