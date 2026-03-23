using AutoMapper;
using AuditoriaExtend.Application.DTOs;
using AuditoriaExtend.Domain.Entities;

namespace AuditoriaExtend.Application.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Lote, LoteDto>();
        CreateMap<CriarLoteDto, Lote>();

        CreateMap<Documento, DocumentoDto>();

        CreateMap<DivergenciaAuditoria, DivergenciaAuditoriaDto>()
            .ForMember(d => d.NomeArquivoDocumento, o => o.MapFrom(s => s.Documento != null ? s.Documento.NomeArquivo : string.Empty));

        CreateMap<RevisaoHumana, RevisaoHumanaDto>()
            .ForMember(d => d.Divergencia, o => o.MapFrom(s => s.Divergencia));
    }
}
