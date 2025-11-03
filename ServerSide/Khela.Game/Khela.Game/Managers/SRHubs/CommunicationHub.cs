using Microsoft.AspNetCore.SignalR;

namespace Khela.Game.Managers.SRHubs
{
    public class CommunicationHub : Hub
    {
        public async Task SendMessage(string user, string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", user, message);
        }
    }
}
