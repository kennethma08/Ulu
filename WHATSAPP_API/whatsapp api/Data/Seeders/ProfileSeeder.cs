using System.Linq;
using Whatsapp_API.Data;
using Whatsapp_API.Models.Entities.Security;

namespace Whatsapp_API.Data.Seeders
{
    public static class ProfileSeeder
    {
        public static void Seed(MyDbContext db)
        {
            var empresaIds = db.Companies.Select(e => e.Id).ToList();
            foreach (var eid in empresaIds)
                SeedForEmpresa(db, eid);
        }

        public static void SeedForEmpresa(MyDbContext db, int companyId)
        {
            var nombres = new[] { "Super Administrador", "Administrador", "Agente" };
            var cambios = false;

            foreach (var n in nombres)
            {
                if (!db.Profiles.Any(p => p.CompanyId == companyId && p.Name == n))
                {
                    db.Profiles.Add(new Profile { Name = n, CompanyId = companyId });
                    cambios = true;
                }
            }

            if (cambios) db.SaveChanges();
        }
    }
}
