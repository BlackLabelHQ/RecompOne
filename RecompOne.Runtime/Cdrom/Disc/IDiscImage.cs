namespace RecompOne.Runtime.Cdrom;

public enum DiscTrackKind
{
    Data,
    Audio
}

public readonly record struct DiscTrack(int Number, DiscTrackKind Kind, int StartLba, int SectorSize, int PregapLba);

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

    void ReadSectorData(int lba, Span<byte> destination)
    {
        ReadSectorData(lba, destination.Length).AsSpan().CopyTo(destination);
    }

    byte[] ReadRawSector(int lba);

    byte[] ReadSector(int lba)
    {
        return ReadSectorData(lba, 2048);
    }
}
