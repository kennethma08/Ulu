using System.Linq;
using Whatsapp_API.Data;
using Whatsapp_API.Models.Entities.System;

namespace Whatsapp_API.Data.Seeders
{
    public static class CompanySeeder
    {
        public static void Seed(MyDbContext db)
        {
            var empresa = db.Companies.FirstOrDefault(e => e.Name == "CNET");
            if (empresa == null)
            {
                empresa = new Company { Name = "CNET" }; 
                db.Companies.Add(empresa);
                db.SaveChanges();
            }
        }
    }
}
