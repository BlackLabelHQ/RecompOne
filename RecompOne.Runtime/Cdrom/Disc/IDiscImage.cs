namespace RecompOne.Runtime.Cdrom;

public enum DiscTrackKind
{
    Data,
    Audio,
}

public readonly record struct DiscTrack(int Number, DiscTrackKind Kind, int StartLba, int SectorSize);

public interface IDiscImage : IDisposable
{
    string Format { get; }

    int FirstTrack { get; }

    int LastTrack { get; }

    bool HasTracks { get; }

    int LeadoutLba { get; }

    int DataSectors { get; }

    IReadOnlyList<DiscTrack> Tracks { get; }

    bool TrackStartLba(int track, out int lba);

    byte[] ReadSectorData(int lba, int size);

    byte[] ReadSector(int lba) => ReadSectorData(lba, 2048);
}
