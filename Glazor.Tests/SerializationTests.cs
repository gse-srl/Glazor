using Glazor.Misc;
using Glazor.Tests.Models;
using MessagePack;
using static Glazor.Tests.Models.MGAAdesione;

namespace Glazor.Tests;

public class SerializationTests
{
    [SetUp]
    public void Setup()
    {
    }

    [TearDown]
    public void TearDown()
    {
    }

    [Test]
    public void SerdeTest()
    {
        MGAAdesione titolare = new()
        {
            NumeroPolizza = "0001",
            NumeroAdesione = 1,
            Cognome = "Pro",
            Nome = "Vallers",
            DataNascita = new DateOnly(1948, 6, 16),
            DataInizio = new DateOnly(1996, 1, 31),
            Legame = TipoLegame.Titolare,
            Carenze = true,
            CodiceFiscaleSocio = "PRTVNT",
            Tariffa = TipoTariffa.Azienda,
        };
        MGAAdesione adesione = new MGAAdesione()
        {
            NumeroPolizza = "0001",
            NumeroAdesione = 2,
            Cognome = "Pro",
            Nome = "Erlumo",
            DataNascita = new DateOnly(1996, 6, 24),
            Titolare = titolare,
            Legame = TipoLegame.Figlio,
            Carenze = false,
        };
        {
            byte[] bin = MessagePackSerializer.Serialize(adesione, MessagePackConf.Options);
            Console.WriteLine($"{MessagePackSerializer.ConvertToJson(bin, MessagePackConf.Options)}");
            MGAAdesione des = MessagePackSerializer.Deserialize<MGAAdesione>(bin, MessagePackConf.Options);
            Assert.That(des.NumeroPolizza, Is.EqualTo(adesione.NumeroPolizza));
            Assert.That(des.NumeroAdesione, Is.EqualTo(adesione.NumeroAdesione));
            Assert.That(des.DataNascita, Is.EqualTo(adesione.DataNascita));
            Assert.That(des.Legame, Is.EqualTo(adesione.Legame));
            Assert.That(des.Carenze, Is.EqualTo(adesione.Carenze));
            Assert.That(des.Titolare, Is.Not.SameAs(adesione.Titolare));
            Assert.That(des.Titolare.NumeroPolizza, Is.EqualTo(titolare.NumeroPolizza));
            Assert.That(des.Titolare.NumeroAdesione, Is.EqualTo(titolare.NumeroAdesione));
            Assert.That(des.Titolare.DataNascita, Is.EqualTo(titolare.DataNascita));
            Assert.That(des.Titolare.Tariffa, Is.EqualTo(titolare.Tariffa));
        }
        //
        MGASinistro sinistro = new()
        {
            NumeroSinistro = "S0001",
            LastProgressivoOperazione = 1,
            DataContabile = new DateOnly(2025, 10, 30),
            OraAccadimento = new TimeOnly(10, 13),
            NumeroDenuncia = "D0001",
            DataDenuncia = new DateOnly(2025, 10, 29),
            Adesione = titolare,
        };
        MGAAttore attore = new MGAAttore()
        {
            Sinistro = sinistro,
            ChiaveEsterna = "ATT0001",
            Cognome = "Oropallo",
            Nome = "Pinco",
        };
        {
            byte[] bin = MessagePackSerializer.Serialize(attore, MessagePackConf.Options);
            Console.WriteLine($"{MessagePackSerializer.ConvertToJson(bin, MessagePackConf.Options)}");
            MGAAttore des = MessagePackSerializer.Deserialize<MGAAttore>(bin, MessagePackConf.Options);
            Assert.That(des.ChiaveEsterna, Is.EqualTo(attore.ChiaveEsterna));
            Assert.That(des.CreationTime, Is.EqualTo(attore.CreationTime));
            Assert.That(des.Sinistro.NumeroSinistro, Is.EqualTo(attore.Sinistro.NumeroSinistro));
            Assert.That(des.Sinistro.LastProgressivoOperazione, Is.EqualTo(attore.Sinistro.LastProgressivoOperazione));
            Assert.That(des.Sinistro.OraAccadimento, Is.EqualTo(attore.Sinistro.OraAccadimento));
            Assert.That(des.Sinistro.Adesione, Is.Not.SameAs(adesione.Titolare));
            Assert.That(des.Sinistro.Adesione.NumeroPolizza, Is.EqualTo(titolare.NumeroPolizza));
            Assert.That(des.Sinistro.Adesione.NumeroAdesione, Is.EqualTo(titolare.NumeroAdesione));
            Assert.That(des.Sinistro.Adesione.DataNascita, Is.EqualTo(titolare.DataNascita));
            Assert.That(des.Sinistro.Adesione.Tariffa, Is.EqualTo(titolare.Tariffa));
        }
    }
}

