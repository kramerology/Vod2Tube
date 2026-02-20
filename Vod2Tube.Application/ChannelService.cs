using Vod2Tube.Domain;
using Vod2Tube.Infrastructure;

namespace Vod2Tube.Application.Services
{
    public class ChannelService
    {
        private readonly AppDbContext _dbContext;

        public ChannelService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<Channel> AddNewChannelAsync(Channel channel)
        {
            channel.AddedAtUTC = DateTime.UtcNow;
            _dbContext.Channels.Add(channel);
            await _dbContext.SaveChangesAsync();
            return channel;
        }

        public async Task<Channel?> GetChannelByIdAsync(int id)
        {
            return await _dbContext.Channels.FindAsync(id);
        }

        public async Task<bool> UpdateChannelAsync(Channel channel)
        {
            var existing = await _dbContext.Channels.FindAsync(channel.Id);
            if (existing == null)
                return false;

            existing.ChannelName = channel.ChannelName;
            existing.Active = channel.Active;

            await _dbContext.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteChannelAsync(int id)
        {
            var channel = await _dbContext.Channels.FindAsync(id);
            if (channel == null)
                return false;

            _dbContext.Channels.Remove(channel);
            await _dbContext.SaveChangesAsync();
            return true;
        }
    }
}