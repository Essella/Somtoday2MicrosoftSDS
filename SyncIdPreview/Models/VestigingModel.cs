namespace SyncIdPreview.Models
{
    internal sealed record VestigingModel
    {
        public Vestiging Vestiging { get; init; }
        public List<Lesgroep> Lesgroepen { get; init; } = [];
        public List<Medewerker> Medewerkers { get; init; } = [];
        public List<Leerling> Leerlingen { get; init; } = [];
        public List<OuderVerzorger> OuderVerzorgers { get; init; } = [];
    }
}
