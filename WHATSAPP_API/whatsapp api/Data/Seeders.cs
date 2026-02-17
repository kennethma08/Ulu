using System.Linq;
using Whatsapp_API.Data.Seeders;

namespace Whatsapp_API.Data
{
    // carga datos base para empezar a usar el sistema
    public static class Seeder
    {
        public static void Seed(MyDbContext dbContext)
        {
            CompanySeeder.Seed(dbContext); // crea empresas demo

            var empresa = dbContext.Companies.First(e => e.Name == "CNET"); // toma cnet

            ProfileSeeder.SeedForEmpresa(dbContext, empresa.Id); // perfiles/roles

            // usuario admin inicial
            UserSeeder.Seed(dbContext, empresa.Id, "Kenneth Martinez", "web2@cnet.co.cr", "123456");

            ContactSeeder.Seed(dbContext, empresa.Id); // contactos de prueba
        }
    }
}
