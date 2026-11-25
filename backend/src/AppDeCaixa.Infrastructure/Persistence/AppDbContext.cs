using Microsoft.EntityFrameworkCore;
using AppDeCaixa.Infrastructure.Persistence.Entities;

namespace AppDeCaixa.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<CashSessionRecord> CashSessions => Set<CashSessionRecord>();
    public DbSet<CashMovementRecord> CashMovements => Set<CashMovementRecord>();
    public DbSet<ServiceOrderRecord> ServiceOrders => Set<ServiceOrderRecord>();
    public DbSet<ServiceCatalogRecord> Services => Set<ServiceCatalogRecord>();

    internal IQueryable<ServiceOrderRecord> OpenOrders(int companyId) =>
        ServiceOrders.Where(order => order.CompanyId == companyId && order.ClosedAt == null &&
            order.Status != null && order.Status.ToLower() == "open");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserRecord>(entity =>
        {
            entity.ToTable("usuario", "public");
            entity.HasKey(user => user.Id).HasName("usuario_pkey");
            entity.Property(user => user.Id).HasColumnName("usuario_id").UseSerialColumn();
            entity.Property(user => user.CompanyId).HasColumnName("empresa_id");
            entity.Property(user => user.EmployeeId).HasColumnName("colaborador_id");
            entity.Property(user => user.Name).HasColumnName("nome").HasColumnType("character varying").IsRequired();
            entity.Property(user => user.Cpf).HasColumnName("cpf").HasColumnType("character varying");
            entity.Property(user => user.Email).HasColumnName("email").HasColumnType("character varying");
            entity.Property(user => user.PasswordHash).HasColumnName("senha_hash").HasColumnType("character varying").IsRequired();
            entity.Property(user => user.LastLogin).HasColumnName("ultimo_login").HasColumnType("timestamp without time zone");
            entity.Property(user => user.LastLogout).HasColumnName("ultimo_logout").HasColumnType("timestamp without time zone");
            entity.Property(user => user.IsActive).HasColumnName("ativo");
            entity.Property(user => user.Role).HasColumnName("role").HasMaxLength(16);
            entity.ToTable(table => table.HasCheckConstraint(
                "usuario_role_check",
                "role IS NULL OR role IN ('Manager', 'Cashier', 'Employee')"));

            entity.HasIndex(user => user.Cpf).IsUnique().HasDatabaseName("usuario_cpf_key");
            entity.HasIndex(user => user.Email).IsUnique().HasDatabaseName("usuario_email_key");
            entity.HasOne<CollaboratorRecord>()
                .WithMany()
                .HasForeignKey(user => user.EmployeeId)
                .HasConstraintName("usuario_colaborador_id_fkey");
        });

        modelBuilder.Entity<CompanyRecord>(entity =>
        {
            entity.ToTable("empresa", "public");
            entity.HasKey(company => company.Id).HasName("empresa_pkey");
            entity.Property(company => company.Id).HasColumnName("empresa_id").UseSerialColumn();
            entity.Property(company => company.Name).HasColumnName("nome").HasColumnType("text").IsRequired();
            entity.Property(company => company.Cnpj).HasColumnName("cnpj").HasColumnType("text").IsRequired();
            entity.Property(company => company.Contact).HasColumnName("ponto_focal").HasColumnType("text").IsRequired();
            entity.HasIndex(company => company.Cnpj).IsUnique().HasDatabaseName("empresa_cnpj_key");
        });

        modelBuilder.Entity<CollaboratorRecord>(entity =>
        {
            entity.ToTable("colaborador", "public");
            entity.HasKey(collaborator => collaborator.Id).HasName("colaborador_pkey");
            entity.Property(collaborator => collaborator.Id).HasColumnName("colaborador_id").UseSerialColumn();
            entity.Property(collaborator => collaborator.CompanyId).HasColumnName("empresa_id");
            entity.Property(collaborator => collaborator.Name).HasColumnName("nome").HasColumnType("character varying").IsRequired();
            entity.Property(collaborator => collaborator.Cpf).HasColumnName("cpf").HasColumnType("character varying");
            entity.Property(collaborator => collaborator.Email).HasColumnName("email").HasColumnType("character varying");
            entity.Property(collaborator => collaborator.IsActive).HasColumnName("ativo");
            entity.HasIndex(collaborator => collaborator.Cpf).IsUnique().HasDatabaseName("colaborador_cpf_key");
        });

        modelBuilder.Entity<CashSessionRecord>(entity =>
        {
            entity.ToTable("caixa", "public");
            entity.HasKey(session => session.Id).HasName("caixa_pkey");
            entity.Property(session => session.Id).HasColumnName("caixa_id").UseSerialColumn();
            entity.Property(session => session.CompanyId).HasColumnName("empresa_id");
            entity.Property(session => session.OpenedOn).HasColumnName("data_abertura");
            entity.Property(session => session.ClosedOn).HasColumnName("data_fechamento");
            entity.Property(session => session.OpenedByUserId).HasColumnName("usuario_id_abertura");
            entity.Property(session => session.ClosedByUserId).HasColumnName("usuario_id_fechamento");
            entity.Property(session => session.OpeningCash).HasColumnName("valor_abertura");
            entity.Property(session => session.ClosingCash).HasColumnName("valor_fechamento");
            entity.Property(session => session.TotalEntries).HasColumnName("valor_entradas");
            entity.Property(session => session.TotalOutflows).HasColumnName("valor_saidas");
            entity.Property(session => session.Status).HasColumnName("status").HasColumnType("character varying");
            entity.HasIndex(session => session.CompanyId)
                .IsUnique()
                .HasFilter("data_fechamento IS NULL")
                .HasDatabaseName("caixa_one_open_per_company_idx");
        });

        modelBuilder.Entity<CashMovementRecord>(entity =>
        {
            entity.ToTable("caixa_movimento", "public", table =>
            {
                table.HasCheckConstraint("caixa_movimento_tipo_check", "tipo IN ('CashIn', 'CashOut')");
                table.HasCheckConstraint("caixa_movimento_valor_check", "valor > 0");
                table.HasCheckConstraint("caixa_movimento_observacao_check", "length(btrim(observacao)) BETWEEN 1 AND 500");
            });
            entity.HasKey(movement => movement.Id).HasName("caixa_movimento_pkey");
            entity.Property(movement => movement.Id).HasColumnName("caixa_movimento_id").UseSerialColumn();
            entity.Property(movement => movement.CashSessionId).HasColumnName("caixa_id").IsRequired();
            entity.Property(movement => movement.Direction).HasColumnName("tipo").HasColumnType("character varying(16)").IsRequired();
            entity.Property(movement => movement.Amount).HasColumnName("valor").IsRequired();
            entity.Property(movement => movement.Note).HasColumnName("observacao").HasColumnType("text").IsRequired();
            entity.Property(movement => movement.PerformedByUserId).HasColumnName("usuario_id").IsRequired();
            entity.Property(movement => movement.RecordedAt).HasColumnName("data_hora").HasColumnType("timestamp without time zone").IsRequired();
            entity.HasOne<CashSessionRecord>().WithMany().HasForeignKey(movement => movement.CashSessionId)
                .HasConstraintName("caixa_movimento_caixa_id_fkey")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<UserRecord>().WithMany().HasForeignKey(movement => movement.PerformedByUserId)
                .HasConstraintName("caixa_movimento_usuario_id_fkey")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(movement => new { movement.CashSessionId, movement.RecordedAt })
                .HasDatabaseName("caixa_movimento_caixa_id_data_hora_idx");
        });

        modelBuilder.Entity<ServiceOrderRecord>(entity =>
        {
            entity.ToTable("comanda", "public");
            entity.HasKey(order => order.Id).HasName("comanda_pkey");
            entity.Property(order => order.Id).HasColumnName("comanda_id").UseSerialColumn();
            entity.Property(order => order.CompanyId).HasColumnName("empresa_id");
            entity.Property(order => order.ClientName).HasColumnName("cliente_nome").HasColumnType("text");
            entity.Property(order => order.OpenedAt).HasColumnName("data_hora_abertura").HasColumnType("timestamp without time zone");
            entity.Property(order => order.ClosedAt).HasColumnName("data_hora_fechamento").HasColumnType("timestamp without time zone");
            entity.Property(order => order.Status).HasColumnName("status").HasColumnType("character varying");
            entity.Property(order => order.TotalValue).HasColumnName("valor_total");
            entity.Property(order => order.IsDebt).HasColumnName("fiado");
            entity.Property(order => order.OpenedByUserId).HasColumnName("usuario_id_abertura");
            entity.Property(order => order.ClosedByUserId).HasColumnName("usuario_id_fechamento");
            entity.Property(order => order.CashSessionId).HasColumnName("caixa_id");
            entity.HasOne<CashSessionRecord>().WithMany().HasForeignKey(order => order.CashSessionId)
                .HasConstraintName("comanda_caixa_id_fkey");
        });

        modelBuilder.Entity<ServiceCatalogRecord>(entity =>
        {
            entity.ToTable("servicos", "public");
            entity.HasKey(service => service.Id).HasName("servicos_pkey");
            entity.Property(service => service.Id).HasColumnName("servicos_id").UseSerialColumn();
            entity.Property(service => service.CompanyId).HasColumnName("empresa_id");
            entity.Property(service => service.Name).HasColumnName("nome").HasColumnType("text").IsRequired();
            entity.Property(service => service.Description).HasColumnName("descricao").HasColumnType("text").IsRequired();
            entity.Property(service => service.CommissionPercentage).HasColumnName("porcentagem");
            entity.Property(service => service.Price).HasColumnName("valor");
            entity.Property(service => service.IsActive).HasColumnName("ativo");
        });
    }
}
