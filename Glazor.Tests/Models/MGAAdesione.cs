using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Glazor.Tests.Models;

[Table("MGA_Adesione")] 
[Index(nameof(NumeroPolizza), nameof(NumeroAdesione), IsUnique = true)]
public class MGAAdesione : Entity
{
    public int Id { get; set; }

    [MaxLength(256)]
    public string NumeroPolizza { get; set; } = null!;

    public uint NumeroAdesione { get; set; }

    [MaxLength(256)]
    public string Cognome { get; set; } = null!;

    [MaxLength(256)]
    public string Nome { get; set; } = null!;

    [MaxLength(64)]
    public string? CodiceFiscaleSocio { get; set; } = null!;

    public DateOnly DataNascita { get; set; }

    public DateOnly? DataInizio { get; set; }

    public int? TitolareId { get; set; }
    /// <summary>
    /// Null se è titolare, altrimenti punta al titolare
    /// </summary>
    [DeleteBehavior(DeleteBehavior.NoAction)]
    public MGAAdesione? Titolare { get; set; } = null!;

    public TipoLegame Legame { get; set; }

    public TipoTariffa? Tariffa { get; set; }

    public bool Carenze { get; set; }

    public DateTime CreationTime { get; private set; }
    public DateTime LastUpdateTime { get; private set; }

    public MGAAdesione()
    {
        CreationTime = DateTime.Now;
    }

    private void Check()
    {
        switch (Legame)
        {
            case TipoLegame.Titolare:
                if (TitolareId != null || Titolare != null)
                    throw new Exception($"Un titolare non può referenziare un altro titolare: {Id} -> {TitolareId}");
                break;
            default:
                if (TitolareId == null && Titolare == null)
                    throw new Exception($"Un non titolare ({Legame}): {Id} deve referenziare un titolare");
                break;
        }
    }

    protected internal override void OnBeforeInsert()
    {
        Check();
        CreationTime = DateTime.UtcNow;
        LastUpdateTime = CreationTime;
    }

    protected internal override void OnBeforeUpdate()
    {
        Check();
        LastUpdateTime = DateTime.UtcNow;
    }

    public enum TipoTariffa
    {
        GruppoOmogeneo = 1,
        Azienda = 2,
    }

    public enum TipoLegame
    {
        Titolare = 1,
        Coniuge = 2,
        Genitore = 3,
        Figlia = 4,
        Figlio = 5,
    }
}
