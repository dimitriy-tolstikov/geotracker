using Microsoft.AspNetCore.SignalR;

namespace Geo.Api.Hubs;

// STUB: реализацию пишет модуль realtime (см. SPEC.md).
public class TrackingHub : Hub
{
    public Task JoinObjectGroup(string objectId) => Task.CompletedTask;

    public Task LeaveObjectGroup(string objectId) => Task.CompletedTask;
}
