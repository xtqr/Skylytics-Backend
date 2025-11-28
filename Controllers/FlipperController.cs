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
    /// Flip finding endpoints for profit opportunities
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class FlipperController : ControllerBase
    {
        private readonly HypixelContext _context;

        public FlipperController(HypixelContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Get potential flip opportunities based on price difference
        /// </summary>
        /// <param name="minProfit">Minimum profit amount (default 100000)</param>
        /// <param name="minProfitPercent">Minimum profit percentage (default 10)</param>
        /// <param name="limit">Max results (default 20, max 100)</param>
        /// <returns>List of flip opportunities</returns>
        [HttpGet("opportunities")]
        [ResponseCache(Duration = 30)]
        public async Task<ActionResult<IEnumerable<FlipOpportunity>>> GetFlipOpportunities(
            [FromQuery] long minProfit = 100000,
            [FromQuery] int minProfitPercent = 10,
            [FromQuery] int limit = 20)
        {
            limit = Math.Min(limit, 100);
            var now = DateTime.UtcNow;

            // Get active BIN auctions
            var activeBins = await _context.Auctions
                .Where(a => a.Bin && a.End > now && a.StartingBid > 0)
                .OrderBy(a => a.StartingBid)
                .Take(5000)
                .Select(a => new
                {
                    a.Uuid,
                    a.Tag,
                    a.ItemName,
                    a.StartingBid,
                    a.AuctioneerId,
                    a.End,
                    a.Tier
                })
                .ToListAsync();

            // Group by tag and find potential flips
            var flipOpportunities = new List<FlipOpportunity>();

            var groupedByTag = activeBins.GroupBy(a => a.Tag);

            foreach (var group in groupedByTag)
            {
                var auctions = group.OrderBy(a => a.StartingBid).ToList();
                if (auctions.Count < 2) continue;

                var lowest = auctions.First();
                var median = auctions[auctions.Count / 2];

                // Check if there's a significant price difference
                var profit = median.StartingBid - lowest.StartingBid;
                var profitPercent = lowest.StartingBid > 0
                    ? (profit * 100.0 / lowest.StartingBid)
                    : 0;

                if (profit >= minProfit && profitPercent >= minProfitPercent)
                {
                    flipOpportunities.Add(new FlipOpportunity
                    {
                        AuctionUuid = lowest.Uuid,
                        ItemTag = lowest.Tag,
                        ItemName = lowest.ItemName,
                        BuyPrice = lowest.StartingBid,
                        MedianPrice = median.StartingBid,
                        Profit = profit,
                        ProfitPercent = profitPercent,
                        Seller = lowest.AuctioneerId,
                        EndsAt = lowest.End,
                        Tier = lowest.Tier.ToString()
                    });
                }
            }

            return Ok(flipOpportunities
                .OrderByDescending(f => f.ProfitPercent)
                .Take(limit));
        }

        /// <summary>
        /// Get underpriced BIN auctions for a specific item
        /// </summary>
        /// <param name="itemTag">The item tag</param>
        /// <param name="percentBelow">Percentage below average (default 20)</param>
        /// <returns>Underpriced auctions</returns>
        [HttpGet("underpriced/{itemTag}")]
        [ResponseCache(Duration = 60)]
        public async Task<ActionResult<IEnumerable<UnderpricedAuction>>> GetUnderpricedAuctions(
            string itemTag,
            [FromQuery] int percentBelow = 20)
        {
            var now = DateTime.UtcNow;

            // Get active BIN auctions for the item
            var auctions = await _context.Auctions
                .Where(a => a.Tag == itemTag && a.Bin && a.End > now)
                .OrderBy(a => a.StartingBid)
                .Take(100)
                .ToListAsync();

            if (!auctions.Any())
                return NotFound(new { message = "No active BIN auctions found" });

            // Calculate average
            var average = auctions.Average(a => a.StartingBid);
            var threshold = average * (100 - percentBelow) / 100;

            var underpriced = auctions
                .Where(a => a.StartingBid < threshold)
                .Select(a => new UnderpricedAuction
                {
                    AuctionUuid = a.Uuid,
                    ItemTag = a.Tag,
                    ItemName = a.ItemName,
                    Price = a.StartingBid,
                    AveragePrice = (long)average,
                    Discount = (average - a.StartingBid) / average * 100,
                    Seller = a.AuctioneerId,
                    EndsAt = a.End
                });

            return Ok(underpriced);
        }

        /// <summary>
        /// Get snipe opportunities (very recently listed low-price auctions)
        /// </summary>
        /// <param name="maxAge">Maximum age in minutes (default 5, max 30)</param>
        /// <param name="limit">Max results (default 20, max 50)</param>
        /// <returns>Recent snipe opportunities</returns>
        [HttpGet("snipes")]
        [ResponseCache(Duration = 10)]
        public async Task<ActionResult<IEnumerable<SnipeOpportunity>>> GetSnipeOpportunities(
            [FromQuery] int maxAge = 5,
            [FromQuery] int limit = 20)
        {
            maxAge = Math.Min(maxAge, 30);
            limit = Math.Min(limit, 50);

            var since = DateTime.UtcNow.AddMinutes(-maxAge);
            var now = DateTime.UtcNow;

            // Get recently listed BIN auctions
            var recentAuctions = await _context.Auctions
                .Where(a => a.Bin && a.Start > since && a.End > now)
                .OrderByDescending(a => a.Start)
                .Take(500)
                .Select(a => new
                {
                    a.Uuid,
                    a.Tag,
                    a.ItemName,
                    a.StartingBid,
                    a.AuctioneerId,
                    a.Start,
                    a.End
                })
                .ToListAsync();

            // Get average prices for these items
            var tags = recentAuctions.Select(a => a.Tag).Distinct().ToList();

            var items = await _context.Items
                .Where(i => tags.Contains(i.Tag))
                .Select(i => new { i.Tag, i.Id })
                .ToDictionaryAsync(i => i.Tag, i => i.Id);

            var recentPrices = await _context.Prices
                .Where(p => items.Values.Contains(p.ItemId))
                .GroupBy(p => p.ItemId)
                .Select(g => new
                {
                    ItemId = g.Key,
                    Avg = g.OrderByDescending(p => p.Date).First().Avg
                })
                .ToDictionaryAsync(p => p.ItemId, p => p.Avg);

            var snipes = new List<SnipeOpportunity>();

            foreach (var auction in recentAuctions)
            {
                if (!items.TryGetValue(auction.Tag, out var itemId)) continue;
                if (!recentPrices.TryGetValue(itemId, out var avgPrice)) continue;
                if (avgPrice <= 0) continue;

                var discount = (avgPrice - auction.StartingBid) / avgPrice * 100;
                if (discount >= 15) // At least 15% below average
                {
                    snipes.Add(new SnipeOpportunity
                    {
                        AuctionUuid = auction.Uuid,
                        ItemTag = auction.Tag,
                        ItemName = auction.ItemName,
                        Price = auction.StartingBid,
                        AveragePrice = (long)avgPrice,
                        Discount = discount,
                        ListedAt = auction.Start,
                        EndsAt = auction.End,
                        Seller = auction.AuctioneerId
                    });
                }
            }

            return Ok(snipes
                .OrderByDescending(s => s.Discount)
                .Take(limit));
        }

        /// <summary>
        /// Get flip statistics
        /// </summary>
        /// <returns>Overall flip market statistics</returns>
        [HttpGet("stats")]
        [ResponseCache(Duration = 300)]
        public async Task<ActionResult<FlipStats>> GetFlipStats()
        {
            var now = DateTime.UtcNow;
            var oneDayAgo = now.AddDays(-1);

            var activeBins = await _context.Auctions
                .CountAsync(a => a.Bin && a.End > now);

            var activeAuctions = await _context.Auctions
                .CountAsync(a => !a.Bin && a.End > now);

            var soldLast24h = await _context.Auctions
                .CountAsync(a => a.End > oneDayAgo && a.End <= now && a.HighestBidAmount > 0);

            var totalVolume24h = await _context.Auctions
                .Where(a => a.End > oneDayAgo && a.End <= now && a.HighestBidAmount > 0)
                .SumAsync(a => (long?)a.HighestBidAmount) ?? 0;

            return Ok(new FlipStats
            {
                ActiveBinAuctions = activeBins,
                ActiveAuctions = activeAuctions,
                SoldLast24Hours = soldLast24h,
                TotalVolume24Hours = totalVolume24h,
                LastUpdated = now
            });
        }
    }

    /// <summary>
    /// Flip opportunity DTO
    /// </summary>
    public class FlipOpportunity
    {
        public string AuctionUuid { get; set; }
        public string ItemTag { get; set; }
        public string ItemName { get; set; }
        public long BuyPrice { get; set; }
        public long MedianPrice { get; set; }
        public long Profit { get; set; }
        public double ProfitPercent { get; set; }
        public string Seller { get; set; }
        public DateTime EndsAt { get; set; }
        public string Tier { get; set; }
    }

    /// <summary>
    /// Underpriced auction DTO
    /// </summary>
    public class UnderpricedAuction
    {
        public string AuctionUuid { get; set; }
        public string ItemTag { get; set; }
        public string ItemName { get; set; }
        public long Price { get; set; }
        public long AveragePrice { get; set; }
        public double Discount { get; set; }
        public string Seller { get; set; }
        public DateTime EndsAt { get; set; }
    }

    /// <summary>
    /// Snipe opportunity DTO
    /// </summary>
    public class SnipeOpportunity
    {
        public string AuctionUuid { get; set; }
        public string ItemTag { get; set; }
        public string ItemName { get; set; }
        public long Price { get; set; }
        public long AveragePrice { get; set; }
        public double Discount { get; set; }
        public DateTime ListedAt { get; set; }
        public DateTime EndsAt { get; set; }
        public string Seller { get; set; }
    }

    /// <summary>
    /// Flip statistics DTO
    /// </summary>
    public class FlipStats
    {
        public int ActiveBinAuctions { get; set; }
        public int ActiveAuctions { get; set; }
        public int SoldLast24Hours { get; set; }
        public long TotalVolume24Hours { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
