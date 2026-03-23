using Microsoft.EntityFrameworkCore;
using AuditoriaExtend.Domain.Entities;

namespace AuditoriaExtend.Infrastructure.Data;

public class AuditoriaDbContext : DbContext
{
    public AuditoriaDbContext(DbContextOptions<AuditoriaDbContext> options) : base(options) { }

    public DbSet<Lote> Lotes => Set<Lote>();
    public DbSet<Documento> Documentos => Set<Documento>();
    public DbSet<AtendimentoAgrupado> AtendimentosAgrupados => Set<AtendimentoAgrupado>();
    public DbSet<DivergenciaAuditoria> DivergenciasAuditoria => Set<DivergenciaAuditoria>();
    public DbSet<RevisaoHumana> RevisoesHumanas => Set<RevisaoHumana>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Lote>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.NomeArquivo).HasMaxLength(500).IsRequired();
            e.Property(l => l.CaminhoArquivo).HasMaxLength(1000);
            e.Property(l => l.MensagemErro).HasMaxLength(2000);
            e.HasMany(l => l.Documentos).WithOne(d => d.Lote).HasForeignKey(d => d.LoteId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Documento>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.NomeArquivo).HasMaxLength(500).IsRequired();
            e.Property(d => d.CaminhoArquivo).HasMaxLength(1000);
            e.Property(d => d.MensagemErro).HasMaxLength(2000);
            e.HasMany(d => d.Divergencias).WithOne(div => div.Documento).HasForeignKey(div => div.DocumentoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AtendimentoAgrupado>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.NomePaciente).HasMaxLength(200);
            e.Property(a => a.NomeMedico).HasMaxLength(200);
            e.Property(a => a.NumeroGuia).HasMaxLength(50);
            e.Property(a => a.NumeroPedido).HasMaxLength(50);
        });

        modelBuilder.Entity<DivergenciaAuditoria>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.Descricao).HasMaxLength(500).IsRequired();
            e.Property(d => d.DetalhesTecnicos).HasMaxLength(2000);
            e.Property(d => d.CampoAfetado).HasMaxLength(100);
            e.Property(d => d.ValorEncontrado).HasMaxLength(500);
            e.Property(d => d.ValorEsperado).HasMaxLength(500);
            e.HasOne(d => d.Revisao).WithOne(r => r.Divergencia)
                .HasForeignKey<RevisaoHumana>(r => r.DivergenciaId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RevisaoHumana>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.NomeAuditor).HasMaxLength(200).IsRequired();
            e.Property(r => r.Justificativa).HasMaxLength(1000);
            e.Property(r => r.ObservacaoCorrecao).HasMaxLength(1000);
        });
    }
}
