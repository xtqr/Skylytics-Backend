using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using dev;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Controllers
{
    /// <summary>
    /// Bazaar related endpoints for prices and orders
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class BazaarController : ControllerBase
    {
        private readonly HypixelContext _context;

        public BazaarController(HypixelContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Get current bazaar prices for all products
        /// </summary>
        /// <returns>List of all bazaar products with current prices</returns>
        [HttpGet]
        [ResponseCache(Duration = 60)]
        public async Task<ActionResult<IEnumerable<BazaarProductResponse>>> GetBazaarPrices()
        {
            var latestPull = await _context.BazaarPull
                .OrderByDescending(p => p.Timestamp)
                .Include(p => p.Products)
                .ThenInclude(p => p.QuickStatus)
                .FirstOrDefaultAsync();

            if (latestPull == null)
                return NotFound(new { message = "No bazaar data available" });

            return Ok(latestPull.Products.Select(ToBazaarProductResponse));
        }

        /// <summary>
        /// Get bazaar price for a specific product
        /// </summary>
        /// <param name="productId">The product ID (e.g., ENCHANTED_DIAMOND)</param>
        /// <returns>Product price details</returns>
        [HttpGet("{productId}")]
        [ResponseCache(Duration = 30)]
        public async Task<ActionResult<BazaarProductResponse>> GetProductPrice(string productId)
        {
            var product = await _context.BazaarPrices
                .Where(p => p.ProductId == productId)
                .OrderByDescending(p => p.Timestamp)
                .Include(p => p.QuickStatus)
                .Include(p => p.BuySummery)
                .Include(p => p.SellSummary)
                .FirstOrDefaultAsync();

            if (product == null)
                return NotFound(new { message = "Product not found" });

            return Ok(ToBazaarProductResponse(product));
        }

        /// <summary>
        /// Get bazaar price history for a product
        /// </summary>
        /// <param name="productId">The product ID</param>
        /// <param name="hours">Hours of history (default 24, max 168)</param>
        /// <returns>Price history</returns>
        [HttpGet("{productId}/history")]
        [ResponseCache(Duration = 300)]
        public async Task<ActionResult<IEnumerable<BazaarPriceHistory>>> GetPriceHistory(
            string productId,
            [FromQuery] int hours = 24)
        {
            hours = Math.Min(hours, 168); // Max 1 week
            var since = DateTime.UtcNow.AddHours(-hours);

            var prices = await _context.BazaarPrices
                .Where(p => p.ProductId == productId && p.Timestamp > since)
                .OrderBy(p => p.Timestamp)
                .Include(p => p.QuickStatus)
                .ToListAsync();

            return Ok(prices.Select(p => new BazaarPriceHistory
            {
                Timestamp = p.Timestamp,
                BuyPrice = p.QuickStatus?.BuyPrice ?? 0,
                SellPrice = p.QuickStatus?.SellPrice ?? 0,
                BuyVolume = p.QuickStatus?.BuyVolume ?? 0,
                SellVolume = p.QuickStatus?.SellVolume ?? 0
            }));
        }

        /// <summary>
        /// Get top bazaar products by volume
        /// </summary>
        /// <param name="limit">Number of products (default 20, max 100)</param>
        /// <returns>Top products by trading volume</returns>
        [HttpGet("top/volume")]
        [ResponseCache(Duration = 120)]
        public async Task<ActionResult<IEnumerable<BazaarProductResponse>>> GetTopByVolume(
            [FromQuery] int limit = 20)
        {
            limit = Math.Min(limit, 100);

            var latestPull = await _context.BazaarPull
                .OrderByDescending(p => p.Timestamp)
                .Include(p => p.Products)
                .ThenInclude(p => p.QuickStatus)
                .FirstOrDefaultAsync();

            if (latestPull == null)
                return NotFound(new { message = "No bazaar data available" });

            var topProducts = latestPull.Products
                .Where(p => p.QuickStatus != null)
                .OrderByDescending(p => (p.QuickStatus.BuyVolume + p.QuickStatus.SellVolume) * p.QuickStatus.BuyPrice)
                .Take(limit);

            return Ok(topProducts.Select(ToBazaarProductResponse));
        }

        /// <summary>
        /// Get bazaar products with best margins (flip opportunities)
        /// </summary>
        /// <param name="limit">Number of products (default 20, max 100)</param>
        /// <returns>Products with highest profit margins</returns>
        [HttpGet("top/margin")]
        [ResponseCache(Duration = 120)]
        public async Task<ActionResult<IEnumerable<BazaarMarginResponse>>> GetTopByMargin(
            [FromQuery] int limit = 20)
        {
            limit = Math.Min(limit, 100);

            var latestPull = await _context.BazaarPull
                .OrderByDescending(p => p.Timestamp)
                .Include(p => p.Products)
                .ThenInclude(p => p.QuickStatus)
                .FirstOrDefaultAsync();

            if (latestPull == null)
                return NotFound(new { message = "No bazaar data available" });

            var products = latestPull.Products
                .Where(p => p.QuickStatus != null && p.QuickStatus.BuyPrice > 0 && p.QuickStatus.SellPrice > 0)
                .Select(p => new BazaarMarginResponse
                {
                    ProductId = p.ProductId,
                    BuyPrice = p.QuickStatus.BuyPrice,
                    SellPrice = p.QuickStatus.SellPrice,
                    Margin = p.QuickStatus.BuyPrice - p.QuickStatus.SellPrice,
                    MarginPercent = (p.QuickStatus.BuyPrice - p.QuickStatus.SellPrice) / p.QuickStatus.SellPrice * 100,
                    BuyVolume = p.QuickStatus.BuyVolume,
                    SellVolume = p.QuickStatus.SellVolume
                })
                .OrderByDescending(p => p.MarginPercent)
                .Take(limit);

            return Ok(products);
        }

        /// <summary>
        /// Search bazaar products
        /// </summary>
        /// <param name="query">Search query</param>
        /// <returns>Matching products</returns>
        [HttpGet("search")]
        [ResponseCache(Duration = 60)]
        public async Task<ActionResult<IEnumerable<BazaarProductResponse>>> SearchProducts(
            [FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return BadRequest(new { message = "Query is required" });

            var searchTerm = query.ToUpper().Replace(" ", "_");

            var latestPull = await _context.BazaarPull
                .OrderByDescending(p => p.Timestamp)
                .Include(p => p.Products)
                .ThenInclude(p => p.QuickStatus)
                .FirstOrDefaultAsync();

            if (latestPull == null)
                return NotFound(new { message = "No bazaar data available" });

            var products = latestPull.Products
                .Where(p => p.ProductId.Contains(searchTerm))
                .Take(50);

            return Ok(products.Select(ToBazaarProductResponse));
        }

        private static BazaarProductResponse ToBazaarProductResponse(ProductInfo product)
        {
            return new BazaarProductResponse
            {
                ProductId = product.ProductId,
                BuyPrice = product.QuickStatus?.BuyPrice ?? 0,
                SellPrice = product.QuickStatus?.SellPrice ?? 0,
                BuyVolume = product.QuickStatus?.BuyVolume ?? 0,
                SellVolume = product.QuickStatus?.SellVolume ?? 0,
                BuyMovingWeek = product.QuickStatus?.BuyMovingWeek ?? 0,
                SellMovingWeek = product.QuickStatus?.SellMovingWeek ?? 0,
                BuyOrders = product.QuickStatus?.BuyOrders ?? 0,
                SellOrders = product.QuickStatus?.SellOrders ?? 0,
                Timestamp = product.Timestamp
            };
        }
    }

    /// <summary>
    /// Bazaar product response DTO
    /// </summary>
    public class BazaarProductResponse
    {
        public string ProductId { get; set; }
        public double BuyPrice { get; set; }
        public double SellPrice { get; set; }
        public long BuyVolume { get; set; }
        public long SellVolume { get; set; }
        public long BuyMovingWeek { get; set; }
        public long SellMovingWeek { get; set; }
        public int BuyOrders { get; set; }
        public int SellOrders { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Bazaar price history DTO
    /// </summary>
    public class BazaarPriceHistory
    {
        public DateTime Timestamp { get; set; }
        public double BuyPrice { get; set; }
        public double SellPrice { get; set; }
        public long BuyVolume { get; set; }
        public long SellVolume { get; set; }
    }

    /// <summary>
    /// Bazaar margin response DTO
    /// </summary>
    public class BazaarMarginResponse
    {
        public string ProductId { get; set; }
        public double BuyPrice { get; set; }
        public double SellPrice { get; set; }
        public double Margin { get; set; }
        public double MarginPercent { get; set; }
        public long BuyVolume { get; set; }
        public long SellVolume { get; set; }
    }
}
