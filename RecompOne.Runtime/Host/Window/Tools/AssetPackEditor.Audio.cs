using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Assets;
using RecompOne.Runtime.Assets.Sfx;
using RecompOne.Runtime.Assets.Xa;

namespace RecompOne.Runtime.Host.Window;

internal sealed partial class AssetPackEditor
{
    private int _sndView;
    private string _sndSearch = "";
    private bool _sndHideDone;
    private ulong _sndHash;
    private string? _playing;

    private int _musView;
    private string _musKey = "";
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
                if (ToolUi.PickFile("Audio", "wav,ogg") is { } file)
                    Try("Replacing", () =>
                    {
                        PackAuthoring.SetSound(pack, _sndHash, file);
                        Say("Replaced.");
                    });
            ImGuiUtils.Tooltip("WAV or OGG. Stretched to the original length.");
        }

        ImGui.SameLine();
        using (ImGuiUtils.Disabled(seen == null))
        {
            if (ImGui.Button("Save original as...", new Vector2(buttonW, 0f)) && seen != null)
                if (ToolUi.SaveFile("WAV audio", "wav", $"{_sndHash:x16}.wav") is { } target)
                    Try("Saving", () =>
                    {
                        PackAuthoring.ExportOriginal(seen, target);
                        Say($"Saved to {target}");
                    });
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

    private void DrawMusicTab(AssetPack pack)
    {
        var scale = Theme.Scale;
        var contents = Contents(pack);

        ImGuiUtils.Segmented("musview", ref _musView, "Heard in game", $"In this pack ({contents.Music.Count})");
        ImGui.SameLine();
        if (_musView == 0 && ImGui.Button("Clear list##mus")) XaRegistry.Clear();

        var detailsHeight = 170f * scale;
        ImGui.BeginChild("##muslist", new Vector2(0f, -detailsHeight), ImGuiChildFlags.Borders);

        var rows = new List<(int File, int Channel, int Lba, SeenStream? Seen, XaEntryDto? Mine)>();
        if (_musView == 0)
            foreach (var s in XaRegistry.Snapshot().OrderByDescending(s => s.LastSeen))
                rows.Add((s.File, s.Channel, s.StartLba, s, MineFor(contents, s.File, s.Channel, s.StartLba)));
        else
            foreach (var e in contents.Music)
                rows.Add((e.FileNumber ?? 0, e.Channel ?? 0, e.StartLba ?? 0, null, e));

        if (rows.Count == 0)
        {
            ImGuiUtils.Hint(_musView == 0
                ? "Play the game and streamed tracks will show up here."
                : "No tracks in this pack yet.");
        }
        else if (ImGui.BeginTable("##music", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Track");
            ImGui.TableSetupColumn("Heard", ImGuiTableColumnFlags.WidthFixed, 70f * scale);
            ImGui.TableSetupColumn("Played", ImGuiTableColumnFlags.WidthFixed, 60f * scale);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 150f * scale);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 28f * scale);
            ImGui.TableHeadersRow();

            foreach (var (file, channel, lba, seen, mine) in rows)
            {
                var key = Key(file, channel, lba);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Selectable($"Track {file}, channel {channel}  (sector {lba})", _musKey == key,
                        ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap))
                    _musKey = key;

                ImGui.TableNextColumn();
                ImGui.TextDisabled(seen != null ? $"{seen.Seconds:0} s" : "-");
                ImGui.TableNextColumn();
                ImGui.TextDisabled(seen != null ? $"{seen.Hits}x" : "-");
                ImGui.TableNextColumn();
                var owner = Manager.ResolveXa((byte)file, (byte)channel, lba);
                if (mine != null) ImGuiUtils.Tag("this pack", ImGuiUtils.Green);
                else if (owner != null) ImGuiUtils.Tag(PackName(owner.PackId), ImGuiUtils.Amber);
                ImGui.TableNextColumn();
                PlayButton($"x{key}", () => XaOriginal(file, channel, lba), "play the original");
            }

            ImGui.EndTable();
        }

        ImGui.EndChild();

        ImGui.BeginChild("##musdetails", Vector2.Zero, ImGuiChildFlags.Borders);
        var selected = rows.FirstOrDefault(r => Key(r.File, r.Channel, r.Lba) == _musKey);
        if (_musKey.Length == 0 || (selected.Seen == null && selected.Mine == null))
            ImGuiUtils.Hint("Select a track.");
        else
            DrawMusicDetails(pack, selected.File, selected.Channel, selected.Lba, selected.Mine);
        ImGui.EndChild();
    }

    private static XaEntryDto? MineFor(PackContents contents, int file, int channel, int lba)
    {
        return contents.Music.FirstOrDefault(e => e.FileNumber == file && e.Channel == channel && e.StartLba == lba);
    }

    private (short[] Pcm, int Channels, int Rate)? XaOriginal(int file, int channel, int lba)
    {
        var key = Key(file, channel, lba);
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
                var decoded = XaExport.Decode(lba, (byte)file, (byte)channel);
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

    private void DrawMusicDetails(AssetPack pack, int file, int channel, int lba, XaEntryDto? mine)
    {
        var editable = pack.Editable;
        ImGui.TextUnformatted($"Track {file}, channel {channel}");
        ImGui.SameLine();
        ImGui.TextDisabled($"starts at sector {lba}");

        PlayButton($"dx{Key(file, channel, lba)}", () => XaOriginal(file, channel, lba), "Play the original");
        ImGui.SameLine();
        ImGui.TextDisabled("Original");
        if (mine?.Audio is { } audio)
        {
            ImGui.SameLine();
            PlayButton($"dy{Key(file, channel, lba)}", () => FromPack(pack, audio), "Play your version");
            ImGui.SameLine();
            ImGui.TextDisabled("Yours");
        }

        var buttonW = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2f) / 3f;
        using (ImGuiUtils.Disabled(!editable))
        {
            if (ImGui.Button("Choose audio file...", new Vector2(buttonW, 0f)))
                if (ToolUi.PickFile("Audio", "wav,ogg") is { } source)
                    Try("Replacing", () =>
                    {
                        var stream = new SeenStream { File = (byte)file, Channel = (byte)channel, StartLba = lba };
                        PackAuthoring.SetMusic(pack, stream, source);
                        Say("Replaced.");
                    });
            ImGuiUtils.Tooltip("WAV or OGG.");
        }

        ImGui.SameLine();
        if (ImGui.Button("Save original as...", new Vector2(buttonW, 0f)))
            if (ToolUi.SaveFile("WAV audio", "wav", $"track_f{file}_c{channel}_{lba}.wav") is { } target)
            {
                Say("Reading the track from the disc...");
                Task.Run(() =>
                {
                    var clip = XaExport.Decode(lba, (byte)file, (byte)channel);
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
