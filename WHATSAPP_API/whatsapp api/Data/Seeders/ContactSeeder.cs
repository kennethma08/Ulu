using System;
using System.Linq;
using Whatsapp_API.Data;
using Whatsapp_API.Models.Entities.Messaging;

namespace Whatsapp_API.Data.Seeders
{
    public static class ContactSeeder
    {
        public static void Seed(MyDbContext db, int companyId)
        {
            if (db.Contacts.Any(c => c.CompanyId == companyId)) return;

            db.Contacts.AddRange(
                new Contact
                {
                    Name = "Juan Pérez",
                    PhoneNumber = "+50670000010",
                    Country = "CR",
                    CreatedAt = DateTime.UtcNow,
                    Status = "new",
                    WelcomeSent = false,
                    CompanyId = companyId
                },
                new Contact
                {
                    Name = "Ana Gómez",
                    PhoneNumber = "+50670000011",
                    Country = "CR",
                    CreatedAt = DateTime.UtcNow,
                    Status = "new",
                    WelcomeSent = false,
                    CompanyId = companyId
                }
            );

            db.SaveChanges();
        }
    }
}
