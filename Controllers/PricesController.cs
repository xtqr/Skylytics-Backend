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
    /// Price history and analytics endpoints
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PricesController : ControllerBase
    {
        private readonly HypixelContext _context;

        public PricesController(HypixelContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Get price history for an item
        /// </summary>
        /// <param name="itemTag">The item tag</param>
        /// <param name="days">Number of days (default 7, max 30)</param>
        /// <returns>Price history</returns>
        [HttpGet("{itemTag}/history")]
        [ResponseCache(Duration = 300)]
        public async Task<ActionResult<IEnumerable<PriceHistoryResponse>>> GetPriceHistory(
            string itemTag,
            [FromQuery] int days = 7)
        {
            days = Math.Min(days, 30);
            var since = DateTime.UtcNow.AddDays(-days);

            // Get item ID
            var item = await _context.Items
                .Where(i => i.Tag == itemTag)
                .FirstOrDefaultAsync();

            if (item == null)
                return NotFound(new { message = "Item not found" });

            var prices = await _context.Prices
                .Where(p => p.ItemId == item.Id && p.Date > since)
                .OrderBy(p => p.Date)
                .ToListAsync();

            return Ok(prices.Select(p => new PriceHistoryResponse
            {
                Date = p.Date,
                Average = p.Avg,
                Min = p.Min,
                Max = p.Max,
                Volume = p.Volume
            }));
        }

        /// <summary>
        /// Get current average price for an item
        /// </summary>
        /// <param name="itemTag">The item tag</param>
        /// <returns>Current price info</returns>
        [HttpGet("{itemTag}/current")]
        [ResponseCache(Duration = 60)]
        public async Task<ActionResult<CurrentPriceResponse>> GetCurrentPrice(string itemTag)
        {
            var item = await _context.Items
                .Where(i => i.Tag == itemTag)
                .FirstOrDefaultAsync();

            if (item == null)
                return NotFound(new { message = "Item not found" });

            // Get latest price data
            var latestPrice = await _context.Prices
                .Where(p => p.ItemId == item.Id)
                .OrderByDescending(p => p.Date)
                .FirstOrDefaultAsync();

            // Get recent auctions for BIN prices
            var recentBins = await _context.Auctions
                .Where(a => a.Tag == itemTag && a.Bin && a.End > DateTime.UtcNow.AddDays(-1))
                .OrderBy(a => a.StartingBid)
                .Take(10)
                .Select(a => a.StartingBid)
                .ToListAsync();

            var lowestBin = recentBins.Any() ? recentBins.Min() : 0;
            var avgBin = recentBins.Any() ? (long)recentBins.Average() : 0;

            return Ok(new CurrentPriceResponse
            {
                ItemTag = itemTag,
                Average = latestPrice?.Avg ?? 0,
                Min = latestPrice?.Min ?? 0,
                Max = latestPrice?.Max ?? 0,
                Volume = latestPrice?.Volume ?? 0,
                LowestBin = lowestBin,
                AverageBin = avgBin,
                LastUpdated = latestPrice?.Date ?? DateTime.MinValue
            });
        }

        /// <summary>
        /// Get lowest BIN price for an item
        /// </summary>
        /// <param name="itemTag">The item tag</param>
        /// <returns>Lowest BIN info</returns>
        [HttpGet("{itemTag}/lowestbin")]
        [ResponseCache(Duration = 30)]
        public async Task<ActionResult<LowestBinResponse>> GetLowestBin(string itemTag)
        {
            var now = DateTime.UtcNow;

            var lowestBin = await _context.Auctions
                .Where(a => a.Tag == itemTag && a.Bin && a.End > now)
                .OrderBy(a => a.StartingBid)
                .FirstOrDefaultAsync();

            if (lowestBin == null)
                return NotFound(new { message = "No active BIN auctions found" });

            return Ok(new LowestBinResponse
            {
                ItemTag = itemTag,
                Price = lowestBin.StartingBid,
                AuctionUuid = lowestBin.Uuid,
                Seller = lowestBin.AuctioneerId,
                EndsAt = lowestBin.End
            });
        }

        /// <summary>
        /// Get price comparison for multiple items
        /// </summary>
        /// <param name="tags">Comma-separated item tags</param>
        /// <returns>Price comparison data</returns>
        [HttpGet("compare")]
        [ResponseCache(Duration = 120)]
        public async Task<ActionResult<IEnumerable<CurrentPriceResponse>>> ComparePrices(
            [FromQuery] string tags)
        {
            if (string.IsNullOrWhiteSpace(tags))
                return BadRequest(new { message = "Tags parameter is required" });

            var tagList = tags.Split(',').Select(t => t.Trim()).Take(10).ToList();
            var result = new List<CurrentPriceResponse>();

            foreach (var tag in tagList)
            {
                var item = await _context.Items
                    .Where(i => i.Tag == tag)
                    .FirstOrDefaultAsync();

                if (item == null) continue;

                var latestPrice = await _context.Prices
                    .Where(p => p.ItemId == item.Id)
                    .OrderByDescending(p => p.Date)
                    .FirstOrDefaultAsync();

                var recentBins = await _context.Auctions
                    .Where(a => a.Tag == tag && a.Bin && a.End > DateTime.UtcNow.AddDays(-1))
                    .OrderBy(a => a.StartingBid)
                    .Take(10)
                    .Select(a => a.StartingBid)
                    .ToListAsync();

                result.Add(new CurrentPriceResponse
                {
                    ItemTag = tag,
                    Average = latestPrice?.Avg ?? 0,
                    Min = latestPrice?.Min ?? 0,
                    Max = latestPrice?.Max ?? 0,
                    Volume = latestPrice?.Volume ?? 0,
                    LowestBin = recentBins.Any() ? recentBins.Min() : 0,
                    AverageBin = recentBins.Any() ? (long)recentBins.Average() : 0,
                    LastUpdated = latestPrice?.Date ?? DateTime.MinValue
                });
            }

            return Ok(result);
        }

        /// <summary>
        /// Get top items by price change
        /// </summary>
        /// <param name="direction">up or down</param>
        /// <param name="limit">Max results (default 20, max 100)</param>
        /// <returns>Items with biggest price changes</returns>
        [HttpGet("trending/{direction}")]
        [ResponseCache(Duration = 300)]
        public async Task<ActionResult<IEnumerable<PriceTrendResponse>>> GetTrendingPrices(
            string direction,
            [FromQuery] int limit = 20)
        {
            limit = Math.Min(limit, 100);
            var today = DateTime.UtcNow.Date;
            var yesterday = today.AddDays(-1);

            var todayPrices = await _context.Prices
                .Where(p => p.Date >= today)
                .GroupBy(p => p.ItemId)
                .Select(g => new { ItemId = g.Key, Avg = g.Average(p => p.Avg) })
                .ToDictionaryAsync(p => p.ItemId, p => p.Avg);

            var yesterdayPrices = await _context.Prices
                .Where(p => p.Date >= yesterday && p.Date < today)
                .GroupBy(p => p.ItemId)
                .Select(g => new { ItemId = g.Key, Avg = g.Average(p => p.Avg) })
                .ToDictionaryAsync(p => p.ItemId, p => p.Avg);

            var changes = todayPrices
                .Where(t => yesterdayPrices.ContainsKey(t.Key) && yesterdayPrices[t.Key] > 0)
                .Select(t => new
                {
                    ItemId = t.Key,
                    TodayAvg = t.Value,
                    YesterdayAvg = yesterdayPrices[t.Key],
                    Change = t.Value - yesterdayPrices[t.Key],
                    ChangePercent = (t.Value - yesterdayPrices[t.Key]) / yesterdayPrices[t.Key] * 100
                });

            var sorted = direction?.ToLower() == "down"
                ? changes.OrderBy(c => c.ChangePercent)
                : changes.OrderByDescending(c => c.ChangePercent);

            var topChanges = sorted.Take(limit).ToList();
            var itemIds = topChanges.Select(c => c.ItemId).ToList();

            var items = await _context.Items
                .Where(i => itemIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i);

            return Ok(topChanges.Select(c => new PriceTrendResponse
            {
                ItemTag = items.ContainsKey(c.ItemId) ? items[c.ItemId].Tag : "Unknown",
                ItemName = items.ContainsKey(c.ItemId) ? items[c.ItemId].Name : "Unknown",
                TodayAvg = c.TodayAvg,
                YesterdayAvg = c.YesterdayAvg,
                Change = c.Change,
                ChangePercent = c.ChangePercent
            }));
        }
    }

    /// <summary>
    /// Price history response DTO
    /// </summary>
    public class PriceHistoryResponse
    {
        public DateTime Date { get; set; }
        public double Average { get; set; }
        public float Min { get; set; }
        public float Max { get; set; }
        public int Volume { get; set; }
    }

    /// <summary>
    /// Current price response DTO
    /// </summary>
    public class CurrentPriceResponse
    {
        public string ItemTag { get; set; }
        public double Average { get; set; }
        public float Min { get; set; }
        public float Max { get; set; }
        public int Volume { get; set; }
        public long LowestBin { get; set; }
        public long AverageBin { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// Lowest BIN response DTO
    /// </summary>
    public class LowestBinResponse
    {
        public string ItemTag { get; set; }
        public long Price { get; set; }
        public string AuctionUuid { get; set; }
        public string Seller { get; set; }
        public DateTime EndsAt { get; set; }
    }

    /// <summary>
    /// Price trend response DTO
    /// </summary>
    public class PriceTrendResponse
    {
        public string ItemTag { get; set; }
        public string ItemName { get; set; }
        public double TodayAvg { get; set; }
        public double YesterdayAvg { get; set; }
        public double Change { get; set; }
        public double ChangePercent { get; set; }
    }
}
