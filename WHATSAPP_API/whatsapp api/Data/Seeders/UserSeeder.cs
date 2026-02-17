using System;
using System.Linq;
using Whatsapp_API.Data;
using Whatsapp_API.Models.Entities.Security;

namespace Whatsapp_API.Data.Seeders
{
    public static class UserSeeder
    {
        public static void Seed(MyDbContext db, int companyId, string name, string email, string pass)
        {
            if (db.Users.Any(u => u.CompanyId == companyId && u.Email == email)) return;

            var perfilAdmin =
                db.Profiles.FirstOrDefault(p => p.CompanyId == companyId && p.Name == "Administrador")
                ?? db.Profiles.First(p => p.CompanyId == companyId);

            db.Users.Add(new User
            {
                Name = name,
                Email = email,
                Pass = pass,
                Phone = "+50670000000",
                Status = true,
                IdProfile = perfilAdmin.Id,
                CompanyId = companyId,
                LastActivity = DateTime.UtcNow,
                IsOnline = false,
                ConversationCount = 0
            });

            db.SaveChanges();
        }
    }
}
