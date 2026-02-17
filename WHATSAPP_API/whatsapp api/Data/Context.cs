using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Whatsapp_API.Models.Entities.Messaging;
using Whatsapp_API.Infrastructure.MultiTenancy;
using Whatsapp_API.Models.Entities.Security;
using Whatsapp_API.Models.Entities.System;

namespace Whatsapp_API.Data
{
    public class MyDbContext : DbContext
    {
        private TenantContext? _tenant;
        private TenantContext Tenant =>
            _tenant ??= (this.GetService<TenantContext>() ?? new TenantContext { CompanyId = 0 });

        public int CurrentEmpresaId => Tenant.CompanyId;

        public MyDbContext(DbContextOptions<MyDbContext> options) : base(options) { }
        public MyDbContext() : base() { }

        public DbSet<User> Users { get; set; }
        public DbSet<Profile> Profiles { get; set; }
        public DbSet<Contact> Contacts { get; set; }
        public DbSet<Conversation> Conversations { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<Attachment> Attachments { get; set; }
        public DbSet<Integration> Integrations { get; set; }
        public DbSet<Company> Companies { get; set; }
        public DbSet<WhatsappTemplate> WhatsappTemplates { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Conversation>()
                .HasOne(c => c.Contact)
                .WithMany()
                .HasForeignKey(c => c.ContactId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Message>()
                .HasOne(m => m.Conversation)
                .WithMany()
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Message>()
                .HasOne(m => m.Contact)
                .WithMany()
                .HasForeignKey(m => m.ContactId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Attachment>()
                .HasOne(a => a.Message)
                .WithMany(m => m.Attachments)
                .HasForeignKey(a => a.MessageId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Conversation>()
                .HasOne(c => c.ClosedByUser)
                .WithMany()
                .HasForeignKey(c => c.ClosedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Message>().HasIndex(m => new { m.ConversationId, m.SentAt });
            modelBuilder.Entity<Conversation>().HasIndex(c => new { c.ContactId, c.StartedAt });
            modelBuilder.Entity<Conversation>().HasIndex(c => new { c.ClosedByUserId, c.EndedAt });

            // << NUEVO: rápido de consultar por empresa/flag de agente >>
            modelBuilder.Entity<Conversation>().HasIndex(c => new { c.CompanyId, c.AgentRequestedAt });

            modelBuilder.Entity<WhatsappTemplate>()
                .HasIndex(t => new { t.CompanyId, t.Name, t.Language })
                .IsUnique();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                var cfg = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.Development.json", optional: true)
                    .AddJsonFile("appsettings.json", optional: true)
                    .AddEnvironmentVariables()
                    .Build();

                var cs = cfg.GetConnectionString("DefaultConnection")
                         ?? "Server=.;Database=chatbotsistema;Trusted_Connection=True;TrustServerCertificate=True;";
                optionsBuilder.UseSqlServer(cs);
            }
            base.OnConfiguring(optionsBuilder);
        }

        public override int SaveChanges()
        {
            ApplyEmpresaId();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyEmpresaId();
            return base.SaveChangesAsync(cancellationToken);
        }

        private void ApplyEmpresaId()
        {
            var eid = CurrentEmpresaId;
            foreach (var entry in ChangeTracker.Entries().Where(e =>
                         e.State == EntityState.Added || e.State == EntityState.Modified))
            {
                SetEmpresaIdIfPresent(entry, eid);
            }
        }

        private static void SetEmpresaIdIfPresent(EntityEntry entry, int eid)
        {
            var prop = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "EmpresaId");
            if (prop == null) return;

            if (entry.State == EntityState.Added)
            {
                var current = 0;
                if (prop.CurrentValue is int cv) current = cv;
                if (current == 0 && eid > 0)
                    prop.CurrentValue = eid;
            }
            else if (entry.State == EntityState.Modified)
            {
                // no-op
            }
        }
    }
}
