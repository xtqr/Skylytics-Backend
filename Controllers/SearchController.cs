using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using dev;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Api.Controllers
{
    /// <summary>
    /// Unified search endpoint for all entities
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class SearchController : ControllerBase
    {
        private readonly HypixelContext _context;

        public SearchController(HypixelContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Universal search across items, players, and auctions
        /// </summary>
        /// <param name="query">Search query</param>
        /// <param name="type">Type filter: item, player, auction, or all (default: all)</param>
        /// <param name="limit">Max results per type (default 10, max 50)</param>
        /// <returns>Search results grouped by type</returns>
        [HttpGet]
        [ResponseCache(Duration = 60)]
        public async Task<ActionResult<SearchResult>> Search(
            [FromQuery] string query,
            [FromQuery] string type = "all",
            [FromQuery] int limit = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest(new { message = "Query is required" });

            limit = Math.Min(limit, 50);
            type = type?.ToLower() ?? "all";

            var result = new SearchResult();

            // Search items
            if (type == "all" || type == "item")
            {
                var searchTerm = query.ToUpper().Replace(" ", "_");
                var items = await _context.Items
                    .Where(i => i.Tag.Contains(searchTerm) || 
                               (i.Name != null && i.Name.ToLower().Contains(query.ToLower())))
                    .Take(limit)
                    .Select(i => new SearchItemResult
                    {
                        Type = "item",
                        Tag = i.Tag,
                        Name = i.Name,
                        Tier = i.Tier.ToString(),
                        Category = i.Category.ToString()
                    })
                    .ToListAsync();

                result.Items = items;
            }

            // Search players
            if (type == "all" || type == "player")
            {
                var players = await _context.Players
                    .Where(p => p.Name != null && p.Name.ToLower().Contains(query.ToLower()))
                    .Take(limit)
                    .Select(p => new SearchPlayerResult
                    {
                        Type = "player",
                        Uuid = p.UuId,
                        Name = p.Name
                    })
                    .ToListAsync();

                result.Players = players;
            }

            // Search auctions (by UUID or item name)
            if (type == "all" || type == "auction")
            {
                var auctions = new List<SearchAuctionResult>();
                
                // Try to find by UUID first
                if (query.Length >= 32)
                {
                    var uuid = query.Replace("-", "");
                    var auction = await _context.Auctions
                        .Where(a => a.Uuid == uuid)
                        .Select(a => new SearchAuctionResult
                        {
                            Type = "auction",
                            Uuid = a.Uuid,
                            ItemName = a.ItemName,
                            Price = a.Bin ? a.StartingBid : a.HighestBidAmount,
                            Seller = a.AuctioneerId
                        })
                        .FirstOrDefaultAsync();

                    if (auction != null)
                        auctions.Add(auction);
                }

                // Also search by item name if we have room
                if (auctions.Count < limit)
                {
                    var remaining = limit - auctions.Count;
                    var byName = await _context.Auctions
                        .Where(a => a.ItemName != null && a.ItemName.ToLower().Contains(query.ToLower()))
                        .OrderByDescending(a => a.End)
                        .Take(remaining)
                        .Select(a => new SearchAuctionResult
                        {
                            Type = "auction",
                            Uuid = a.Uuid,
                            ItemName = a.ItemName,
                            Price = a.Bin ? a.StartingBid : a.HighestBidAmount,
                            Seller = a.AuctioneerId
                        })
                        .ToListAsync();

                    auctions.AddRange(byName);
                }

                result.Auctions = auctions;
            }

            return Ok(result);
        }

        /// <summary>
        /// Auto-complete suggestions for search
        /// </summary>
        /// <param name="query">Partial search query</param>
        /// <param name="limit">Max suggestions (default 10, max 20)</param>
        /// <returns>List of suggestions</returns>
        [HttpGet("suggest")]
        [ResponseCache(Duration = 120)]
        public async Task<ActionResult<IEnumerable<SearchSuggestion>>> GetSuggestions(
            [FromQuery] string query,
            [FromQuery] int limit = 10)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                return BadRequest(new { message = "Query must be at least 2 characters" });

            limit = Math.Min(limit, 20);
            var suggestions = new List<SearchSuggestion>();

            // Item suggestions (prioritize popular items)
            var searchTerm = query.ToUpper().Replace(" ", "_");
            var items = await _context.Items
                .Where(i => i.Tag.Contains(searchTerm))
                .OrderByDescending(i => i.HitCount)
                .Take(limit / 2)
                .Select(i => new SearchSuggestion
                {
                    Text = i.Name ?? i.Tag,
                    Type = "item",
                    Value = i.Tag
                })
                .ToListAsync();

            suggestions.AddRange(items);

            // Player suggestions
            var players = await _context.Players
                .Where(p => p.Name != null && p.Name.ToLower().StartsWith(query.ToLower()))
                .Take(limit / 2)
                .Select(p => new SearchSuggestion
                {
                    Text = p.Name,
                    Type = "player",
                    Value = p.UuId
                })
                .ToListAsync();

            suggestions.AddRange(players);

            return Ok(suggestions.Take(limit));
        }
    }

    /// <summary>
    /// Search result container
    /// </summary>
    public class SearchResult
    {
        public List<SearchItemResult> Items { get; set; } = new();
        public List<SearchPlayerResult> Players { get; set; } = new();
        public List<SearchAuctionResult> Auctions { get; set; } = new();
    }

    /// <summary>
    /// Search item result
    /// </summary>
    public class SearchItemResult
    {
        public string Type { get; set; }
        public string Tag { get; set; }
        public string Name { get; set; }
        public string Tier { get; set; }
        public string Category { get; set; }
    }

    /// <summary>
    /// Search player result
    /// </summary>
    public class SearchPlayerResult
    {
        public string Type { get; set; }
        public string Uuid { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Search auction result
    /// </summary>
    public class SearchAuctionResult
    {
        public string Type { get; set; }
        public string Uuid { get; set; }
        public string ItemName { get; set; }
        public long Price { get; set; }
        public string Seller { get; set; }
    }

    /// <summary>
    /// Search suggestion
    /// </summary>
    public class SearchSuggestion
    {
        public string Text { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
    }
}
