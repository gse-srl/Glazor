using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Glazor.Tests.Models;

[Table("MGAXml_Attore")]
[Index(nameof(SinistroId), nameof(ChiaveEsterna), IsUnique = true)]
public class MGAAttore
{
    public int Id { get; set; }

    public int SinistroId { get; set; }
    [DeleteBehavior(DeleteBehavior.NoAction)]
    public MGASinistro Sinistro { get; set; } = null!;

    [MaxLength(256)]
    public string ChiaveEsterna { get; set; } = null!;

    [MaxLength(256)]
    public string? Cognome { get; set; }

    [MaxLength(256)]
    public string? Nome { get; set; }

    public DateTime CreationTime { get; init; }

    public MGAAttore()
    {
        CreationTime = DateTime.Now;
    }
}
