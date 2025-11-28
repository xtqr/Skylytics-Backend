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
    /// Auction related endpoints for browsing and searching auctions
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AuctionsController : ControllerBase
    {
        private readonly HypixelContext _context;
        private readonly AuctionService _auctionService;

        public AuctionsController(HypixelContext context, AuctionService auctionService)
        {
            _context = context;
            _auctionService = auctionService;
        }

        /// <summary>
        /// Get auction by UUID
        /// </summary>
        /// <param name="auctionUuid">The auction UUID</param>
        /// <returns>Auction details</returns>
        [HttpGet("{auctionUuid}")]
        [ResponseCache(Duration = 60)]
        public async Task<ActionResult<AuctionDetailsResponse>> GetAuction(string auctionUuid)
        {
            try
            {
                var auction = await _auctionService.GetAuctionAsync(auctionUuid, q => q
                    .Include(a => a.Enchantments)
                    .Include(a => a.Bids)
                    .Include(a => a.NbtData));

                if (auction == null)
                    return NotFound(new { message = "Auction not found" });

                return Ok(ToAuctionDetailsResponse(auction));
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Get recent auctions
        /// </summary>
        /// <param name="page">Page number (default 0)</param>
        /// <param name="pageSize">Items per page (default 20, max 100)</param>
        /// <returns>List of recent auctions</returns>
        [HttpGet("recent")]
        [ResponseCache(Duration = 30)]
        public async Task<ActionResult<IEnumerable<AuctionDetailsResponse>>> GetRecentAuctions(
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 20)
        {
            pageSize = Math.Min(pageSize, 100);

            var auctions = await _context.Auctions
                .OrderByDescending(a => a.Start)
                .Skip(page * pageSize)
                .Take(pageSize)
                .Include(a => a.Enchantments)
                .ToListAsync();

            return Ok(auctions.Select(ToAuctionDetailsResponse));
        }

        /// <summary>
        /// Get active auctions
        /// </summary>
        /// <param name="page">Page number (default 0)</param>
        /// <param name="pageSize">Items per page (default 20, max 100)</param>
        /// <returns>List of active auctions</returns>
        [HttpGet("active")]
        [ResponseCache(Duration = 30)]
        public async Task<ActionResult<IEnumerable<AuctionDetailsResponse>>> GetActiveAuctions(
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 20)
        {
            pageSize = Math.Min(pageSize, 100);
            var now = DateTime.UtcNow;

            var auctions = await _context.Auctions
                .Where(a => a.End > now)
                .OrderBy(a => a.End)
                .Skip(page * pageSize)
                .Take(pageSize)
                .Include(a => a.Enchantments)
                .ToListAsync();

            return Ok(auctions.Select(ToAuctionDetailsResponse));
        }

        /// <summary>
        /// Search auctions by item tag
        /// </summary>
        /// <param name="itemTag">The item tag to search for</param>
        /// <param name="page">Page number (default 0)</param>
        /// <param name="pageSize">Items per page (default 20, max 100)</param>
        /// <returns>List of matching auctions</returns>
        [HttpGet("tag/{itemTag}")]
        [ResponseCache(Duration = 60)]
        public async Task<ActionResult<IEnumerable<AuctionDetailsResponse>>> GetAuctionsByTag(
            string itemTag,
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 20)
        {
            pageSize = Math.Min(pageSize, 100);

            var auctions = await _context.Auctions
                .Where(a => a.Tag == itemTag)
                .OrderByDescending(a => a.End)
                .Skip(page * pageSize)
                .Take(pageSize)
                .Include(a => a.Enchantments)
                .ToListAsync();

            return Ok(auctions.Select(ToAuctionDetailsResponse));
        }

        /// <summary>
        /// Get ended auctions for an item
        /// </summary>
        /// <param name="itemTag">The item tag</param>
        /// <param name="days">Number of days to look back (default 7, max 30)</param>
        /// <param name="page">Page number</param>
        /// <param name="pageSize">Items per page</param>
        /// <returns>List of ended auctions</returns>
        [HttpGet("ended/{itemTag}")]
        [ResponseCache(Duration = 120)]
        public async Task<ActionResult<IEnumerable<AuctionDetailsResponse>>> GetEndedAuctionsByTag(
            string itemTag,
            [FromQuery] int days = 7,
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 20)
        {
            days = Math.Min(days, 30);
            pageSize = Math.Min(pageSize, 100);
            var since = DateTime.UtcNow.AddDays(-days);

            var auctions = await _context.Auctions
                .Where(a => a.Tag == itemTag && a.End > since && a.End < DateTime.UtcNow)
                .OrderByDescending(a => a.End)
                .Skip(page * pageSize)
                .Take(pageSize)
                .Include(a => a.Enchantments)
                .Include(a => a.Bids)
                .ToListAsync();

            return Ok(auctions.Select(ToAuctionDetailsResponse));
        }

        /// <summary>
        /// Get BIN auctions for an item
        /// </summary>
        /// <param name="itemTag">The item tag</param>
        /// <param name="page">Page number</param>
        /// <param name="pageSize">Items per page</param>
        /// <returns>List of BIN auctions</returns>
        [HttpGet("bin/{itemTag}")]
        [ResponseCache(Duration = 30)]
        public async Task<ActionResult<IEnumerable<AuctionDetailsResponse>>> GetBinAuctions(
            string itemTag,
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 20)
        {
            pageSize = Math.Min(pageSize, 100);
            var now = DateTime.UtcNow;

            var auctions = await _context.Auctions
                .Where(a => a.Tag == itemTag && a.Bin && a.End > now)
                .OrderBy(a => a.StartingBid)
                .Skip(page * pageSize)
                .Take(pageSize)
                .Include(a => a.Enchantments)
                .ToListAsync();

            return Ok(auctions.Select(ToAuctionDetailsResponse));
        }

        /// <summary>
        /// Get player's auctions
        /// </summary>
        /// <param name="playerUuid">Player UUID</param>
        /// <param name="page">Page number</param>
        /// <param name="pageSize">Items per page</param>
        /// <returns>List of player's auctions</returns>
        [HttpGet("player/{playerUuid}")]
        [ResponseCache(Duration = 60)]
        public async Task<ActionResult<IEnumerable<AuctionDetailsResponse>>> GetPlayerAuctions(
            string playerUuid,
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 20)
        {
            pageSize = Math.Min(pageSize, 100);

            var auctions = await _context.Auctions
                .Where(a => a.AuctioneerId == playerUuid)
                .OrderByDescending(a => a.End)
                .Skip(page * pageSize)
                .Take(pageSize)
                .Include(a => a.Enchantments)
                .Include(a => a.Bids)
                .ToListAsync();

            return Ok(auctions.Select(ToAuctionDetailsResponse));
        }

        /// <summary>
        /// Get player's bids
        /// </summary>
        /// <param name="playerUuid">Player UUID</param>
        /// <param name="page">Page number</param>
        /// <param name="pageSize">Items per page</param>
        /// <returns>List of auctions the player has bid on</returns>
        [HttpGet("player/{playerUuid}/bids")]
        [ResponseCache(Duration = 60)]
        public async Task<ActionResult<IEnumerable<AuctionDetailsResponse>>> GetPlayerBids(
            string playerUuid,
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 20)
        {
            pageSize = Math.Min(pageSize, 100);

            var player = await _context.Players.FirstOrDefaultAsync(p => p.UuId == playerUuid);
            if (player == null)
                return NotFound(new { message = "Player not found" });

            // Get auction UUIDs from bids
            var auctionUuids = await _context.Bids
                .Where(b => b.BidderId == player.Id)
                .Select(b => b.AuctionId)
                .Distinct()
                .Skip(page * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Find auctions by UUID
            var auctions = await _context.Auctions
                .Where(a => auctionUuids.Contains(a.Uuid))
                .Include(a => a.Enchantments)
                .Include(a => a.Bids)
                .ToListAsync();

            return Ok(auctions.Select(ToAuctionDetailsResponse));
        }

        private static AuctionDetailsResponse ToAuctionDetailsResponse(SaveAuction auction)
        {
            return new AuctionDetailsResponse
            {
                Uuid = auction.Uuid,
                AuctioneerId = auction.AuctioneerId,
                ItemName = auction.ItemName,
                Tag = auction.Tag,
                Tier = auction.Tier,
                Category = auction.Category,
                StartingBid = auction.StartingBid,
                HighestBid = auction.HighestBidAmount,
                BidCount = auction.Bids?.Count ?? 0,
                Start = auction.Start,
                End = auction.End,
                Bin = auction.Bin,
                Count = auction.Count,
                Enchantments = auction.Enchantments?.Select(e => new EnchantmentResponse
                {
                    Type = e.Type.ToString(),
                    Level = e.Level
                }).ToList(),
                Reforge = auction.Reforge.ToString(),
                FlatNbt = auction.FlatenedNBT
            };
        }
    }

    /// <summary>
    /// Auction details response DTO
    /// </summary>
    public class AuctionDetailsResponse
    {
        public string Uuid { get; set; }
        public string AuctioneerId { get; set; }
        public string ItemName { get; set; }
        public string Tag { get; set; }
        public Tier Tier { get; set; }
        public Category Category { get; set; }
        public long StartingBid { get; set; }
        public long HighestBid { get; set; }
        public int BidCount { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public bool Bin { get; set; }
        public int Count { get; set; }
        public List<EnchantmentResponse> Enchantments { get; set; }
        public string Reforge { get; set; }
        public Dictionary<string, string> FlatNbt { get; set; }
    }

    /// <summary>
    /// Enchantment response DTO
    /// </summary>
    public class EnchantmentResponse
    {
        public string Type { get; set; }
        public byte Level { get; set; }
    }
}
