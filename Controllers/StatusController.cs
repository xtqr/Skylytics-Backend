using System;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Coflnet.Sky.Api.Controllers
{
    /// <summary>
    /// Status and health check endpoints
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class StatusController : ControllerBase
    {
        private readonly HypixelContext _context;

        public StatusController(HypixelContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Health check endpoint
        /// </summary>
        /// <returns>Health status</returns>
        [HttpGet("health")]
        public ActionResult<HealthStatus> GetHealth()
        {
            return Ok(new HealthStatus
            {
                Status = "healthy",
                Version = Program.Version,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Get system statistics
        /// </summary>
        /// <returns>System statistics</returns>
        [HttpGet("stats")]
        [ResponseCache(Duration = 60)]
        public async Task<ActionResult<SystemStats>> GetStats()
        {
            var totalAuctions = await _context.Auctions.CountAsync();
            var totalPlayers = await _context.Players.CountAsync();
            var totalItems = await _context.Items.CountAsync();
            var activeAuctions = await _context.Auctions.CountAsync(a => a.End > DateTime.UtcNow);

            return Ok(new SystemStats
            {
                TotalAuctions = totalAuctions,
                ActiveAuctions = activeAuctions,
                TotalPlayers = totalPlayers,
                TotalItems = totalItems,
                Uptime = DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                Version = Program.Version
            });
        }

        /// <summary>
        /// Check database connection
        /// </summary>
        /// <returns>Database status</returns>
        [HttpGet("db")]
        public async Task<ActionResult<DbStatus>> GetDbStatus()
        {
            try
            {
                var canConnect = await _context.Database.CanConnectAsync();
                return Ok(new DbStatus
                {
                    Connected = canConnect,
                    Provider = _context.Database.ProviderName,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return Ok(new DbStatus
                {
                    Connected = false,
                    Error = ex.Message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }
    }

    /// <summary>
    /// Health status DTO
    /// </summary>
    public class HealthStatus
    {
        public string Status { get; set; }
        public string Version { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// System statistics DTO
    /// </summary>
    public class SystemStats
    {
        public long TotalAuctions { get; set; }
        public int ActiveAuctions { get; set; }
        public int TotalPlayers { get; set; }
        public int TotalItems { get; set; }
        public TimeSpan Uptime { get; set; }
        public string Version { get; set; }
    }

    /// <summary>
    /// Database status DTO
    /// </summary>
    public class DbStatus
    {
        public bool Connected { get; set; }
        public string Provider { get; set; }
        public string Error { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
