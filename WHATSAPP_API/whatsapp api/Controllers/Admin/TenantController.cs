using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Whatsapp_API.Data;

namespace Whatsapp_API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/tenants")]
    public class TenantController : ControllerBase
    {
        private readonly MyDbContext _db;
        private readonly IConfiguration _cfg;

        public TenantController(MyDbContext db, IConfiguration cfg)
        {
            _db = db;
            _cfg = cfg;
        }

        [HttpPost("{tenant}/migrate")]
        public IActionResult MigrateTenant(string tenant)
        {
            _db.Database.Migrate();
            Seeder.Seed(_db);
            return Ok(new { tenant, migrated = true, mode = "single-db" });
        }
    }
}
