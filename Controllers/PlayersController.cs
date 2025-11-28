using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Api.Controllers
{
    /// <summary>
    /// Player related endpoints
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PlayersController : ControllerBase
    {
        private readonly HypixelContext _context;

        public PlayersController(HypixelContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Get player by UUID
        /// </summary>
        /// <param name="playerUuid">The player UUID</param>
        /// <returns>Player details</returns>
        [HttpGet("{playerUuid}")]
        [ResponseCache(Duration = 300)]
        public async Task<ActionResult<PlayerResponse>> GetPlayer(string playerUuid)
        {
            var player = await _context.Players
                .Where(p => p.UuId == playerUuid)
                .FirstOrDefaultAsync();

            if (player == null)
                return NotFound(new { message = "Player not found" });

            return Ok(ToPlayerResponse(player));
        }

        /// <summary>
        /// Search players by name
        /// </summary>
        /// <param name="name">Player name to search</param>
        /// <param name="limit">Max results (default 20, max 100)</param>
        /// <returns>Matching players</returns>
        [HttpGet("search")]
        [ResponseCache(Duration = 120)]
        public async Task<ActionResult<IEnumerable<PlayerResponse>>> SearchPlayers(
            [FromQuery] string name,
            [FromQuery] int limit = 20)
        {
            if (string.IsNullOrWhiteSpace(name))
                return BadRequest(new { message = "Name is required" });

            limit = Math.Min(limit, 100);

            var players = await _context.Players
                .Where(p => p.Name != null && p.Name.ToLower().Contains(name.ToLower()))
                .Take(limit)
                .ToListAsync();

            return Ok(players.Select(ToPlayerResponse));
        }

        /// <summary>
        /// Get player by name
        /// </summary>
        /// <param name="playerName">The player name</param>
        /// <returns>Player details</returns>
        [HttpGet("name/{playerName}")]
        [ResponseCache(Duration = 300)]
        public async Task<ActionResult<PlayerResponse>> GetPlayerByName(string playerName)
        {
            var player = await _context.Players
                .Where(p => p.Name != null && p.Name.ToLower() == playerName.ToLower())
                .FirstOrDefaultAsync();

            if (player == null)
                return NotFound(new { message = "Player not found" });

            return Ok(ToPlayerResponse(player));
        }

        /// <summary>
        /// Get player's auction statistics
        /// </summary>
        /// <param name="playerUuid">Player UUID</param>
        /// <returns>Player's auction statistics</returns>
        [HttpGet("{playerUuid}/stats")]
        [ResponseCache(Duration = 300)]
        public async Task<ActionResult<PlayerStatsResponse>> GetPlayerStats(string playerUuid)
        {
            var player = await _context.Players
                .Where(p => p.UuId == playerUuid)
                .FirstOrDefaultAsync();

            if (player == null)
                return NotFound(new { message = "Player not found" });

            var totalAuctions = await _context.Auctions
                .CountAsync(a => a.AuctioneerId == playerUuid);

            var soldAuctions = await _context.Auctions
                .Where(a => a.AuctioneerId == playerUuid && a.HighestBidAmount > 0)
                .CountAsync();

            var totalBids = await _context.Bids
                .CountAsync(b => b.BidderId == player.Id);

            var totalSpent = await _context.Bids
                .Where(b => b.BidderId == player.Id)
                .SumAsync(b => (long?)b.Amount) ?? 0;

            var totalEarned = await _context.Auctions
                .Where(a => a.AuctioneerId == playerUuid && a.HighestBidAmount > 0)
                .SumAsync(a => (long?)a.HighestBidAmount) ?? 0;

            return Ok(new PlayerStatsResponse
            {
                PlayerUuid = playerUuid,
                PlayerName = player.Name,
                TotalAuctions = totalAuctions,
                SoldAuctions = soldAuctions,
                TotalBids = totalBids,
                TotalSpent = totalSpent,
                TotalEarned = totalEarned
            });
        }

        /// <summary>
        /// Get recently active players
        /// </summary>
        /// <param name="limit">Max results (default 20, max 100)</param>
        /// <returns>Recently active players</returns>
        [HttpGet("recent")]
        [ResponseCache(Duration = 60)]
        public async Task<ActionResult<IEnumerable<PlayerResponse>>> GetRecentPlayers(
            [FromQuery] int limit = 20)
        {
            limit = Math.Min(limit, 100);

            // Get players who recently created auctions
            var recentAuctioneers = await _context.Auctions
                .OrderByDescending(a => a.Start)
                .Select(a => a.AuctioneerId)
                .Distinct()
                .Take(limit)
                .ToListAsync();

            var players = await _context.Players
                .Where(p => recentAuctioneers.Contains(p.UuId))
                .ToListAsync();

            return Ok(players.Select(ToPlayerResponse));
        }

        private static PlayerResponse ToPlayerResponse(Player player)
        {
            return new PlayerResponse
            {
                Uuid = player.UuId,
                Name = player.Name,
                Id = player.Id
            };
        }
    }

    /// <summary>
    /// Player response DTO
    /// </summary>
    public class PlayerResponse
    {
        public string Uuid { get; set; }
        public string Name { get; set; }
        public int Id { get; set; }
    }

    /// <summary>
    /// Player statistics response DTO
    /// </summary>
    public class PlayerStatsResponse
    {
        public string PlayerUuid { get; set; }
        public string PlayerName { get; set; }
        public int TotalAuctions { get; set; }
        public int SoldAuctions { get; set; }
        public int TotalBids { get; set; }
        public long TotalSpent { get; set; }
        public long TotalEarned { get; set; }
    }
}
