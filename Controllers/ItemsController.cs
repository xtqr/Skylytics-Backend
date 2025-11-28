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
    /// Item related endpoints for item details and search
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ItemsController : ControllerBase
    {
        private readonly HypixelContext _context;
        private readonly ItemDetails _itemDetails;

        public ItemsController(HypixelContext context, ItemDetails itemDetails)
        {
            _context = context;
            _itemDetails = itemDetails;
        }

        /// <summary>
        /// Get all items
        /// </summary>
        /// <param name="page">Page number</param>
        /// <param name="pageSize">Items per page (max 100)</param>
        /// <returns>List of items</returns>
        [HttpGet]
        [ResponseCache(Duration = 300)]
        public async Task<ActionResult<IEnumerable<ItemResponse>>> GetItems(
            [FromQuery] int page = 0,
            [FromQuery] int pageSize = 50)
        {
            pageSize = Math.Min(pageSize, 100);

            var items = await _context.Items
                .OrderBy(i => i.Tag)
                .Skip(page * pageSize)
                .Take(pageSize)
                .Include(i => i.Names)
                .ToListAsync();

            return Ok(items.Select(ToItemResponse));
        }

        /// <summary>
        /// Get item by tag
        /// </summary>
        /// <param name="itemTag">The item tag (e.g., HYPERION)</param>
        /// <returns>Item details</returns>
        [HttpGet("{itemTag}")]
        [ResponseCache(Duration = 300)]
        public async Task<ActionResult<ItemResponse>> GetItem(string itemTag)
        {
            var item = await _context.Items
                .Where(i => i.Tag == itemTag)
                .Include(i => i.Names)
                .FirstOrDefaultAsync();

            if (item == null)
                return NotFound(new { message = "Item not found" });

            return Ok(ToItemResponse(item));
        }

        /// <summary>
        /// Search items by name or tag
        /// </summary>
        /// <param name="query">Search query</param>
        /// <param name="limit">Max results (default 20, max 100)</param>
        /// <returns>Matching items</returns>
        [HttpGet("search")]
        [ResponseCache(Duration = 120)]
        public async Task<ActionResult<IEnumerable<ItemResponse>>> SearchItems(
            [FromQuery] string query,
            [FromQuery] int limit = 20)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest(new { message = "Query is required" });

            limit = Math.Min(limit, 100);
            var searchTerm = query.ToUpper().Replace(" ", "_");
            var searchTermLower = query.ToLower();

            // Search by tag
            var itemsByTag = await _context.Items
                .Where(i => i.Tag.Contains(searchTerm))
                .Take(limit)
                .Include(i => i.Names)
                .ToListAsync();

            // Search by name if not enough results
            if (itemsByTag.Count < limit)
            {
                var remaining = limit - itemsByTag.Count;
                var existingIds = itemsByTag.Select(i => i.Id).ToList();

                var itemsByName = await _context.AltItemNames
                    .Where(n => n.Name.ToLower().Contains(searchTermLower))
                    .Where(n => !existingIds.Contains(n.DBItemId))
                    .Select(n => n.DBItemId)
                    .Distinct()
                    .Take(remaining)
                    .ToListAsync();

                var additionalItems = await _context.Items
                    .Where(i => itemsByName.Contains(i.Id))
                    .Include(i => i.Names)
                    .ToListAsync();

                itemsByTag.AddRange(additionalItems);
            }

            return Ok(itemsByTag.Select(ToItemResponse));
        }

        /// <summary>
        /// Get popular items by search hits
        /// </summary>
        /// <param name="limit">Max results (default 20, max 100)</param>
        /// <returns>Popular items</returns>
        [HttpGet("popular")]
        [ResponseCache(Duration = 300)]
        public async Task<ActionResult<IEnumerable<ItemResponse>>> GetPopularItems(
            [FromQuery] int limit = 20)
        {
            limit = Math.Min(limit, 100);

            var items = await _context.Items
                .OrderByDescending(i => i.HitCount)
                .Take(limit)
                .Include(i => i.Names)
                .ToListAsync();

            return Ok(items.Select(ToItemResponse));
        }

        /// <summary>
        /// Get item ID mappings
        /// </summary>
        /// <returns>Dictionary of tag to ID mappings</returns>
        [HttpGet("ids")]
        [ResponseCache(Duration = 600)]
        public async Task<ActionResult<Dictionary<string, int>>> GetItemIds()
        {
            var items = await _context.Items
                .Where(i => i.Tag != null)
                .Select(i => new { i.Tag, i.Id })
                .ToDictionaryAsync(i => i.Tag, i => i.Id);

            return Ok(items);
        }

        /// <summary>
        /// Get item categories
        /// </summary>
        /// <returns>List of item categories</returns>
        [HttpGet("categories")]
        [ResponseCache(Duration = 600)]
        public ActionResult<IEnumerable<string>> GetCategories()
        {
            return Ok(Enum.GetNames(typeof(Category)));
        }

        /// <summary>
        /// Get item tiers
        /// </summary>
        /// <returns>List of item tiers</returns>
        [HttpGet("tiers")]
        [ResponseCache(Duration = 600)]
        public ActionResult<IEnumerable<string>> GetTiers()
        {
            return Ok(Enum.GetNames(typeof(Tier)));
        }

        private static ItemResponse ToItemResponse(DBItem item)
        {
            return new ItemResponse
            {
                Id = item.Id,
                Tag = item.Tag,
                Name = item.Name,
                Tier = item.Tier.ToString(),
                Category = item.Category.ToString(),
                IconUrl = item.IconUrl,
                Description = item.Description,
                AlternativeNames = item.Names?.Select(n => n.Name).ToList() ?? new List<string>(),
                HitCount = item.HitCount
            };
        }
    }

    /// <summary>
    /// Item response DTO
    /// </summary>
    public class ItemResponse
    {
        public int Id { get; set; }
        public string Tag { get; set; }
        public string Name { get; set; }
        public string Tier { get; set; }
        public string Category { get; set; }
        public string IconUrl { get; set; }
        public string Description { get; set; }
        public List<string> AlternativeNames { get; set; }
        public int HitCount { get; set; }
    }
}
