using System.Numerics;

namespace RecompOne.UI;

public sealed partial class AssetPackEditor
{
    private int _sndView;
    private string _sndSearch = "";
    private bool _sndHideDone;
    private ulong _sndHash;
    private string? _playing;

    private int _musView;
    private string _musKey = "";
    private string _musSearch = "";
    private bool _musPlayed;
    private bool _musHideDone;
    private readonly Dictionary<string, AudioClip?> _xaClips = [];
    private readonly HashSet<string> _xaDecoding = [];

    private static string Key(SeenStream s)
    {
        return $"{s.File}:{s.Channel}:{s.StartLba}";
    }

    private static string Key(int file, int channel, int lba)
    {
        return $"{file}:{channel}:{lba}";
    }

    private void Play(string id, short[] pcm, int channels, int rate)
    {
        Audio.Preview(pcm, channels, rate);
        _playing = id;
    }

    private void Stop()
    {
        Audio.StopPreview();
        _playing = null;
    }

    private bool PlayButton(string id, Func<(short[] Pcm, int Channels, int Rate)?> source, string tip)
    {
        var playing = _playing == id && Audio.PreviewPlaying;
        var label = playing ? FontSet.Or(FontSet.Pause, "Stop") : FontSet.Or(FontSet.Play, "Play");
        ImGui.PushID(id);
        var clicked = ImGui.SmallButton(label);
        ImGui.PopID();
        ImGuiUtils.Tooltip(playing ? "Stop" : tip);
        if (!clicked) return false;

        if (playing)
        {
            Stop();
            return true;
        }

        var audio = source();
        if (audio is { } a && a.Pcm.Length > 0) Play(id, a.Pcm, a.Channels, a.Rate);
        return true;
    }

    private static (short[] Pcm, int Channels, int Rate)? Original(SeenSample s)
    {
        return (SpuAdpcm.Decode(s.Adpcm), 1, s.NaturalRate);
    }

    private (short[] Pcm, int Channels, int Rate)? FromPack(AssetPack pack, string file)
    {
        var data = pack.ReadAsset(file);
        if (data == null) return null;
        try
        {
            var clip = AudioClip.Decode(file, data);
            return (clip.Pcm, clip.Channels, clip.Rate);
        }
        catch (Exception e)
        {
            Say($"Couldn't play {file}: {e.Message}", true);
            return null;
        }
    }

    private void DrawSoundsTab(AssetPack pack)
    {
        var scale = Theme.Scale;
        var contents = Contents(pack);
        var mine = contents.Sounds.ToDictionary(s => s.Hash);

        ImGuiUtils.Segmented("sndview", ref _sndView, "Heard in game", $"In this pack ({contents.Sounds.Count})");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(170f * scale);
        ImGui.InputTextWithHint("##sndsearch", "Search by id", ref _sndSearch, 32);
        if (_sndView == 0)
        {
            ImGui.SameLine();
            ImGui.Checkbox("Hide replaced##snd", ref _sndHideDone);
            ImGui.SameLine();
            if (ImGui.Button("Clear list##snd")) SampleRegistry.Clear();
        }


        var detailsHeight = 170f * scale;
        ImGui.BeginChild("##sndlist", new Vector2(0f, -detailsHeight), ImGuiChildFlags.Borders);

        var rows = new List<(ulong Hash, SeenSample? Seen, PackContents.Sound? Mine)>();
        if (_sndView == 0)
        {
            foreach (var s in SampleRegistry.Snapshot().OrderByDescending(s => s.FirstSeen))
            {
                var replaced = Manager.ResolveSfx(s.Hash) != null || mine.ContainsKey(s.Hash);
                if (_sndHideDone && replaced) continue;
                rows.Add((s.Hash, s, mine.GetValueOrDefault(s.Hash)));
            }
        }
        else
        {
            foreach (var s in contents.Sounds) rows.Add((s.Hash, SampleRegistry.Get(s.Hash), s));
        }

        rows.RemoveAll(r => _sndSearch.Length > 0 && !$"{r.Hash:x16}".Contains(_sndSearch, StringComparison.OrdinalIgnoreCase));

        if (rows.Count == 0)
            ImGuiUtils.Hint(_sndView == 0 ? "Play the game and sounds will show up here." : "No sounds in this pack yet.");
        else if (ImGui.BeginTable("##sounds", 5,
                     ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Sound");
            ImGui.TableSetupColumn("Length", ImGuiTableColumnFlags.WidthFixed, 70f * scale);
            ImGui.TableSetupColumn("Played", ImGuiTableColumnFlags.WidthFixed, 60f * scale);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 150f * scale);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 28f * scale);
            ImGui.TableHeadersRow();

            foreach (var (hash, seen, file) in rows)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Selectable($"Sound {hash:x16}", _sndHash == hash,
                        ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap))
                    _sndHash = hash;

                ImGui.TableNextColumn();
                ImGui.TextDisabled(seen != null ? $"{seen.Seconds:0.00} s" : "-");
                ImGui.TableNextColumn();
                ImGui.TextDisabled(seen != null ? $"{seen.Hits}x" : "-");
                ImGui.TableNextColumn();
                var owner = Manager.ResolveSfx(hash);
                if (file != null) ImGuiUtils.Tag("this pack", ImGuiUtils.Green);
                else if (owner != null) ImGuiUtils.Tag(PackName(owner.PackId), ImGuiUtils.Amber);
                ImGui.TableNextColumn();
                if (seen != null) PlayButton($"o{hash:x16}", () => Original(seen), "Play the original");
            }

            ImGui.EndTable();
        }

        ImGui.EndChild();

        ImGui.BeginChild("##snddetails", Vector2.Zero, ImGuiChildFlags.Borders);
        DrawSoundDetails(pack, contents);
        ImGui.EndChild();
    }

    private void DrawSoundDetails(AssetPack pack, PackContents contents)
    {
        if (_sndHash == 0)
        {
            ImGuiUtils.Hint("Select a sound.");
            return;
        }

        var seen = SampleRegistry.Get(_sndHash);
        var mine = contents.Sounds.FirstOrDefault(s => s.Hash == _sndHash);
        var editable = pack.Editable;

        ImGui.TextUnformatted($"Sound {_sndHash:x16}");
        if (seen != null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"{seen.Seconds:0.00} s  -  about {seen.NaturalRate} Hz" + (seen.Loops ? "  -  loops" : ""));
        }

        if (seen != null) PlayButton($"do{_sndHash:x16}", () => Original(seen), "Play the original");
        if (seen != null) ImGui.SameLine();
        ImGui.TextDisabled("Original");
        if (mine != null)
        {
            ImGui.SameLine();
            PlayButton($"dm{_sndHash:x16}", () => FromPack(pack, mine.File), "Play your version");
            ImGui.SameLine();
            ImGui.TextDisabled("Yours");
        }

        var buttonW = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2f) / 3f;
        using (ImGuiUtils.Disabled(!editable))
        {
            if (ImGui.Button("Choose sound file...", new Vector2(buttonW, 0f)))
            {
                var hash = _sndHash;
                ToolUi.PickFile("Audio", "wav,ogg", file => Try("Replacing", () =>
                {
                    PackAuthoring.SetSound(pack, hash, file);
                    Say("Replaced.");
                }));
            }
            ImGuiUtils.Tooltip("WAV or OGG. Stretched to the original length.");
        }

        ImGui.SameLine();
        using (ImGuiUtils.Disabled(seen == null))
        {
            if (ImGui.Button("Save original as...", new Vector2(buttonW, 0f)) && seen != null)
                ToolUi.SaveFile("WAV audio", "wav", $"{_sndHash:x16}.wav", target => Try("Saving", () =>
                {
                    PackAuthoring.ExportOriginal(seen, target);
                    Say($"Saved to {target}");
                }));
        }

        ImGui.SameLine();
        using (ImGuiUtils.Disabled(mine == null || !editable))
        {
            if (ImGuiUtils.Danger("Remove", size: new Vector2(buttonW, 0f)) && mine != null)
                Try("Removing", () =>
                {
                    PackAuthoring.RemoveSound(pack, mine);
                    Say("Removed.");
                });
        }

        if (mine == null) return;

        using (ImGuiUtils.Disabled(!editable))
        {
            var options = mine.Options;
            var volume = (options?.Gain ?? 1f) * 100f;
            ImGui.SetNextItemWidth(200f * Theme.Scale);
            ImGui.SliderFloat("Volume", ref volume, 0f, 200f, "%.0f%%");
            if (ImGui.IsItemDeactivatedAfterEdit())
                Try("Saving", () =>
                {
                    PackAuthoring.SoundOptions(pack, _sndHash).Gain = MathF.Abs(volume - 100f) < 0.5f ? null : volume / 100f;
                    PackAuthoring.Save(pack);
                });

            ImGui.SameLine();
            var follow = options?.PitchFollow ?? true;
            if (ImGui.Checkbox("Follow the game's pitch", ref follow))
                Try("Saving", () =>
                {
                    PackAuthoring.SoundOptions(pack, _sndHash).PitchFollow = follow ? null : false;
                    PackAuthoring.Save(pack);
                });
            ImGuiUtils.Help("Off: always plays at its own speed. this is better for voices and menu sounds.");
        }
    }

    private sealed class MusicRow
    {
        public string Key = "";
        public string Group = "";
        public string Label = "";
        public string Where = "";
        public DiscAudioTrack? Disc;
        public SeenStream? Seen;
        public XaEntryDto? Mine;
        public int File;
        public int Channel;
        public int Lba;
        public int Track;
        public bool Cdda;
        public double Seconds;
        public int Hits;
    }

    private const string HeardGroup = "Heard in game";

    private static string Clock(double seconds)
    {
        var total = (int)Math.Round(seconds);
        return $"{total / 60}:{total % 60:00}";
    }

    private static MusicRow DiscRow(AssetPack pack, DiscAudioTrack track)
    {
        var cdda = track.Kind == DiscAudioKind.Cdda;
        return new MusicRow
        {
            Key = cdda ? $"t:{track.Number}" : Key(track.File, track.Channel, track.StartLba),
            Group = track.Source,
            Label = cdda
                ? $"Track {track.Number:00}"
                : $"Channel {track.Channel}, stream {track.Number}" + (track.Video ? "  (video)" : ""),
            Where = cdda
                ? $"sectors {track.StartLba} to {track.LastLba}"
                : $"file {track.File}, channel {track.Channel}, sectors {track.StartLba} to {track.LastLba}",
            Disc = track,
            Mine = PackAuthoring.MusicFor(pack, track),
            File = track.File,
            Channel = track.Channel,
            Lba = track.StartLba,
            Track = track.Number,
            Cdda = cdda,
            Seconds = track.Seconds,
            Hits = track.Hits
        };
    }

    private static MusicRow HeardRow(PackContents contents, SeenStream seen)
    {
        return new MusicRow
        {
            Key = Key(seen),
            Group = HeardGroup,
            Label = $"Track {seen.File}, channel {seen.Channel}  (sector {seen.StartLba})",
            Where = $"file {seen.File}, channel {seen.Channel}, starts at sector {seen.StartLba}",
            Seen = seen,
            Mine = MineFor(contents, seen.File, seen.Channel, seen.StartLba),
            File = seen.File,
            Channel = seen.Channel,
            Lba = seen.StartLba,
            Seconds = seen.Seconds,
            Hits = seen.Hits
        };
    }

    private static MusicRow PackRow(AssetPack pack, XaEntryDto entry)
    {
        var track = entry.Track is { } number
            ? DiscAudio.CddaTrack(number)
            : DiscAudio.Find(entry.StartLba ?? 0, (byte)(entry.FileNumber ?? 0), (byte)(entry.Channel ?? 0));
        if (track != null)
        {
            var row = DiscRow(pack, track);
            row.Label = $"{track.Source}  -  {row.Label}";
            row.Group = "";
            row.Mine = entry;
            return row;
        }

        var cdda = entry.Track.HasValue;
        return new MusicRow
        {
            Key = cdda ? $"t:{entry.Track}" : Key(entry.FileNumber ?? 0, entry.Channel ?? 0, entry.StartLba ?? 0),
            Label = cdda
                ? $"CD audio track {entry.Track:00}"
                : $"Track {entry.FileNumber ?? 0}, channel {entry.Channel ?? 0}  (sector {entry.StartLba ?? 0})",
            Where = cdda ? "not on this disc" : $"starts at sector {entry.StartLba ?? 0}",
            Mine = entry,
            File = entry.FileNumber ?? 0,
            Channel = entry.Channel ?? 0,
            Lba = entry.StartLba ?? 0,
            Track = entry.Track ?? 0,
            Cdda = cdda
        };
    }

    private List<MusicRow> MusicRows(AssetPack pack, PackContents contents, bool onDisc)
    {
        var rows = new List<MusicRow>();
        if (_musView != 0)
        {
            foreach (var entry in contents.Music) rows.Add(PackRow(pack, entry));
            return rows;
        }

        foreach (var track in DiscAudio.Cdda) rows.Add(DiscRow(pack, track));
        foreach (var source in DiscAudio.Xa.GroupBy(t => t.Source))
        foreach (var track in source.OrderBy(t => t.Channel).ThenBy(t => t.StartLba))
            rows.Add(DiscRow(pack, track));

        if (!DiscAudio.HasXa)
            foreach (var seen in XaRegistry.Snapshot().OrderByDescending(s => s.LastSeen))
                rows.Add(HeardRow(contents, seen));

        rows.RemoveAll(r => (_musPlayed && onDisc && r.Hits == 0) || (_musHideDone && r.Mine != null) ||
                            (_musSearch.Length > 0 &&
                             !r.Label.Contains(_musSearch, StringComparison.OrdinalIgnoreCase) &&
                             !r.Group.Contains(_musSearch, StringComparison.OrdinalIgnoreCase)));
        return rows;
    }

    private XaEntry? MusicOwner(MusicRow row)
    {
        if (row.Cdda) return Manager.ResolveCdda(row.Track);
        var inTrack = row.Disc == null
            ? null
            : Manager.ResolveXaIn((byte)row.File, (byte)row.Channel, row.Disc.StartLba, row.Disc.LastLba);
        return inTrack ?? Manager.ResolveXa((byte)row.File, (byte)row.Channel, row.Lba);
    }

    private void DrawMusicTab(AssetPack pack)
    {
        var scale = Theme.Scale;
        var contents = Contents(pack);
        var discCount = DiscAudio.Cdda.Count + DiscAudio.Xa.Count;
        var onDisc = discCount > 0;

        ImGuiUtils.Segmented("musview", ref _musView, onDisc ? $"On disc ({discCount})" : HeardGroup,
            $"In this pack ({contents.Music.Count})");
        if (_musView == 0)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(160f * scale);
            ImGui.InputTextWithHint("##mussearch", "Search", ref _musSearch, 48);
            if (onDisc)
            {
                ImGui.SameLine();
                ImGui.Checkbox("Only played##mus", ref _musPlayed);
            }

            ImGui.SameLine();
            ImGui.Checkbox("Hide replaced##mus", ref _musHideDone);
            if (!DiscAudio.HasXa)
            {
                ImGui.SameLine();
                if (ImGui.Button("Clear list##mus")) XaRegistry.Clear();
            }

            if (DiscAudio.Scanning)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("Reading the disc...");
            }
        }

        var detailsHeight = 170f * scale;
        ImGui.BeginChild("##muslist", new Vector2(0f, -detailsHeight), ImGuiChildFlags.Borders);

        var rows = MusicRows(pack, contents, onDisc);
        if (rows.Count == 0)
        {
            ImGuiUtils.Hint(_musView != 0 ? "No tracks in this pack yet."
                : onDisc ? "No matches."
                : "Play the game and streamed tracks will show up here.");
        }
        else if (ImGui.BeginTable("##music", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Track");
            ImGui.TableSetupColumn("Length", ImGuiTableColumnFlags.WidthFixed, 70f * scale);
            ImGui.TableSetupColumn("Played", ImGuiTableColumnFlags.WidthFixed, 60f * scale);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 150f * scale);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 28f * scale);
            ImGui.TableHeadersRow();

            string? group = null;
            foreach (var row in rows)
            {
                if (row.Group.Length > 0 && row.Group != group)
                {
                    group = row.Group;
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGuiUtils.TextColored(Theme.AccentText, group);
                }

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Selectable($"{row.Label}##{row.Key}", _musKey == row.Key,
                        ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap))
                    _musKey = row.Key;
                ImGuiUtils.Tooltip(row.Where);

                ImGui.TableNextColumn();
                ImGui.TextDisabled(row.Seconds > 0 ? Clock(row.Seconds) : "-");
                ImGui.TableNextColumn();
                ImGui.TextDisabled(row.Hits > 0 ? $"{row.Hits}x" : "-");
                ImGui.TableNextColumn();
                if (row.Mine != null)
                {
                    ImGuiUtils.Tag("this pack", ImGuiUtils.Green);
                }
                else if (MusicOwner(row) is { } owner)
                {
                    ImGuiUtils.Tag(PackName(owner.PackId), ImGuiUtils.Amber);
                }

                ImGui.TableNextColumn();
                if (row.Disc != null || !row.Cdda)
                    PlayButton($"x{row.Key}", () => MusicOriginal(row), "play the original");
            }

            ImGui.EndTable();
        }

        ImGui.EndChild();

        ImGui.BeginChild("##musdetails", Vector2.Zero, ImGuiChildFlags.Borders);
        var selected = rows.FirstOrDefault(r => r.Key == _musKey);
        if (selected == null) ImGuiUtils.Hint("Select a track.");
        else DrawMusicDetails(pack, selected);
        ImGui.EndChild();
    }

    private static XaEntryDto? MineFor(PackContents contents, int file, int channel, int lba)
    {
        return contents.Music.FirstOrDefault(e =>
            e.Track == null && e.FileNumber == file && e.Channel == channel && e.StartLba == lba);
    }

    private static AudioClip? DecodeOriginal(MusicRow row)
    {
        if (row.Disc != null) return XaExport.Decode(row.Disc);
        return row.Cdda ? null : XaExport.Decode(row.Lba, (byte)row.File, (byte)row.Channel);
    }

    private (short[] Pcm, int Channels, int Rate)? MusicOriginal(MusicRow row)
    {
        var key = row.Key;
        bool start;
        lock (_xaClips)
        {
            if (_xaClips.TryGetValue(key, out var clip))
                return clip == null ? null : (clip.Pcm, clip.Channels, clip.Rate);
            start = _xaDecoding.Add(key);
        }

        if (start)
        {
            Say("Reading the track from the disc...");
            Task.Run(() =>
            {
                var decoded = DecodeOriginal(row);
                lock (_xaClips)
                {
                    _xaClips[key] = decoded;
                    _xaDecoding.Remove(key);
                }

                if (decoded != null && _playing == null)
                {
                    Audio.Preview(decoded.Pcm, decoded.Channels, decoded.Rate);
                    _playing = $"x{key}";
                }
            });
        }

        return null;
    }

    private void DrawMusicDetails(AssetPack pack, MusicRow row)
    {
        var editable = pack.Editable;
        var mine = row.Mine;
        var known = row.Disc != null || !row.Cdda;
        ImGui.TextUnformatted(row.Group.Length > 0 && row.Group != HeardGroup ? $"{row.Group}  -  {row.Label}" : row.Label);
        ImGui.SameLine();
        ImGui.TextDisabled(row.Disc != null ? $"{Clock(row.Seconds)}  -  {row.Where}" : row.Where);

        if (known)
        {
            PlayButton($"dx{row.Key}", () => MusicOriginal(row), "Play the original");
            ImGui.SameLine();
            ImGui.TextDisabled("Original");
        }

        if (mine?.Audio is { } audio)
        {
            if (known) ImGui.SameLine();
            PlayButton($"dy{row.Key}", () => FromPack(pack, audio), "Play your version");
            ImGui.SameLine();
            ImGui.TextDisabled("Yours");
        }

        var buttonW = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2f) / 3f;
        using (ImGuiUtils.Disabled(!editable || !known))
        {
            if (ImGui.Button("Choose audio file...", new Vector2(buttonW, 0f)))
                ToolUi.PickFile("Audio", "wav,ogg", source => Try("Replacing", () =>
                {
                    if (row.Disc != null)
                        PackAuthoring.SetMusic(pack, row.Disc, source);
                    else
                        PackAuthoring.SetMusic(pack,
                            new SeenStream { File = (byte)row.File, Channel = (byte)row.Channel, StartLba = row.Lba }, source);
                    Say("Replaced.");
                }));
            ImGuiUtils.Tooltip("WAV or OGG.");
        }

        ImGui.SameLine();
        using (ImGuiUtils.Disabled(!known))
        {
            var name = row.Cdda ? $"cd_track{row.Track:00}.wav" : $"track_f{row.File}_c{row.Channel}_{row.Lba}.wav";
            if (ImGui.Button("Save original as...", new Vector2(buttonW, 0f)))
                ToolUi.SaveFile("WAV audio", "wav", name, target =>
                {
                    Say("Reading the track from the disc...");
                    Task.Run(() =>
                    {
                        var clip = DecodeOriginal(row);
                        if (clip == null)
                        {
                            Say("Couldn't read that track from the disc.", true);
                            return;
                        }

                        try
                        {
                            WavWriter.Write(target, clip);
                            Say($"Saved to {target}");
                        }
                        catch (Exception e)
                        {
                            Say($"Saving failed: {e.Message}", true);
                        }
                    });
                });
        }

        ImGui.SameLine();
        using (ImGuiUtils.Disabled(mine == null || !editable))
        {
            if (ImGuiUtils.Danger("Remove", size: new Vector2(buttonW, 0f)) && mine != null)
                Try("Removing", () =>
                {
                    PackAuthoring.RemoveMusic(pack, mine);
                    Say("Removed.");
                });
        }

        if (mine == null) return;

        using (ImGuiUtils.Disabled(!editable))
        {
            var volume = (mine.Gain ?? 1f) * 100f;
            ImGui.SetNextItemWidth(200f * Theme.Scale);
            ImGui.SliderFloat("Volume##mus", ref volume, 0f, 200f, "%.0f%%");
            if (ImGui.IsItemDeactivatedAfterEdit())
                Try("Saving", () =>
                {
                    mine.Gain = MathF.Abs(volume - 100f) < 0.5f ? null : volume / 100f;
                    PackAuthoring.Save(pack);
                });

            ImGui.SameLine();
            var loop = mine.Loop ?? false;
            if (ImGui.Checkbox("Loop", ref loop))
                Try("Saving", () =>
                {
                    mine.Loop = loop ? true : null;
                    PackAuthoring.Save(pack);
                });
            ImGuiUtils.Help("Restarts when your file ends.");

            if (row.Cdda) return;

            ImGui.SameLine();
            var longer = mine.Extend ?? false;
            if (ImGui.Checkbox("Play past the original", ref longer))
                Try("Saving", () =>
                {
                    mine.Extend = longer ? true : null;
                    PackAuthoring.Save(pack);
                });
            ImGuiUtils.Help("Keeps playing after the original track ends.");
        }
    }
}
