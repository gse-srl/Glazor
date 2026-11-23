using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Glazor.Tests.Models;

[Table("MGAXml_Sinistro")]
[Index(nameof(NumeroSinistro), IsUnique = true)]
public class MGASinistro : Entity
{
    public int Id { get; set; }

    [MaxLength(256)]
    public string NumeroSinistro { get; set; } = null!;

    public int LastProgressivoOperazione { get; set; } 

    public DateOnly DataContabile { get; set; }

    public TimeOnly? OraAccadimento { get; set; }

    [MaxLength(256)]
    public string NumeroDenuncia { get; set; } = null!;

    public DateOnly DataDenuncia { get; set; }

    public int AdesioneId { get; set; }
    [DeleteBehavior(DeleteBehavior.NoAction)]
    public MGAAdesione Adesione { get; set; } = null!;

    public ICollection<MGAAttore> Attori { get; set; } = [];

    public DateTime CreationTime { get; private set; }
    public DateTime LastUpdateTime { get; private set; }

    public MGASinistro()
    {
        CreationTime = DateTime.Now;
    }

    protected internal override void OnBeforeInsert()
    {
        CreationTime = DateTime.UtcNow;
        LastUpdateTime = CreationTime;
    }

    protected internal override void OnBeforeUpdate()
    {
        LastUpdateTime = DateTime.UtcNow;
    }
}
