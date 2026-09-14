using Godot;

namespace CozyCave;
public partial class Main
{
    private AudioStreamPlayer effect = null!, ambience = null!, music = null!;
    private AudioStreamPlayer3D? rainAudio, streamAudio;
    private readonly Dictionary<string, AudioStream> effects = [];
    private int trackIndex;
    private string? playingPath;
    private void SetupAudio()
    {
        effect = new AudioStreamPlayer(); ambience = new AudioStreamPlayer(); music = new AudioStreamPlayer();
        AddChild(effect); AddChild(ambience); AddChild(music);
        foreach (var name in new[] { "step", "open", "close", "place", "pickup", "trapdoor_open", "trapdoor_close", "ambience" })
        {
            var path = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "CozyCave", "packs", "Audio", name + ".wav");
            effects[name] = System.IO.File.Exists(path) ? AudioStreamWav.LoadFromFile(path) : GD.Load<AudioStream>(name == "ambience" ? "res://Assets/Audio/ambience.wav" : "res://Assets/Vanilla/" + name + ".ogg");
        }
        // No synthetic noise bed: weather and the stream provide the ambience.
        ambience.Stop();
        SetupRainAudio();
        var streamSound=GD.Load<AudioStreamOggVorbis>("res://Assets/Vanilla/water.ogg");streamSound.Loop=true;
        streamAudio=new AudioStreamPlayer3D {Stream=streamSound,Position=new Vector3(6,0,0),UnitSize=3,MaxDistance=16};AddChild(streamAudio);streamAudio.Play();
        music.Finished += () => { trackIndex++; PlayTrack(); };
        if (state.Settings.MusicEnabled) PlayTrack();
    }
    private void PlayEffect(string name)
    {
        if (effect == null || !effects.TryGetValue(name, out var sound) || locked || paused) return;
        effect.Stream = sound; effect.PitchScale = name == "step" ? (float)(.9 + random.NextDouble() * .2) : 1;
        effect.VolumeDb = Mathf.LinearToDb(Math.Max(.0001f, state.Settings.EffectsVolume)); effect.Play();
    }
    private void UpdateAudio()
    {
        if (ambience == null) return;
        if(catVoice!=null) catVoice.StreamPaused=locked||paused||hidden;
        ambience.VolumeDb = Mathf.LinearToDb(Math.Max(.0001f, state.Settings.EffectsVolume * .4f));
        music.VolumeDb = Mathf.LinearToDb(Math.Max(.0001f, state.Settings.MusicVolume));
        ambience.StreamPaused = locked || paused; effect.StreamPaused = locked || paused;
        music.StreamPaused = locked || paused || !state.Settings.MusicEnabled;
        if(streamAudio!=null) {streamAudio.StreamPaused=locked||paused;streamAudio.VolumeDb=Mathf.LinearToDb(Math.Max(.0001f,state.Settings.EffectsVolume*.3f));}
        foreach(var voice in rainVoices) {voice.StreamPaused=locked||paused;voice.VolumeDb=Mathf.LinearToDb(Math.Max(.0001f,state.Settings.EffectsVolume*rainGain));}
    }
    private void PlayTrack()
    {
        music.Stop(); playingPath = null;
        var list = state.Settings.Playlist;
        if (list.Count == 0 || !state.Settings.MusicEnabled) return;
        for (int n = 0; n < list.Count; n++, trackIndex++)
        {
            var path = list[trackIndex % list.Count];
            if (!System.IO.File.Exists(path)) continue;
            try
            {
                music.Stream = System.IO.Path.GetExtension(path).ToLowerInvariant() switch
                {
                    ".mp3" => AudioStreamMP3.LoadFromFile(path), ".ogg" => AudioStreamOggVorbis.LoadFromFile(path),
                    ".wav" => AudioStreamWav.LoadFromFile(path), _ => null
                };
                if (music.Stream == null) continue;
                playingPath = path; music.Play(); return;
            }
            catch (Exception e) { Toast("Could not play track: " + e.Message); }
        }
        Toast("No playable music files. Add MP3, OGG or WAV files to the jukebox.");
    }
    private bool minecraftMusicLoading, musicPanelOpen;
    private async Task LoadMinecraftMusic()
    {
        minecraftMusicLoading=true;if(musicPanelOpen) ShowMusic();Toast("Downloading Minecraft, Clark and Sweden...");
        try
        {
            using var manifest=System.Text.Json.JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://Assets/Vanilla/music-sources.json"));
            var folder=System.IO.Path.Combine(store.DirectoryPath,"music","minecraft");System.IO.Directory.CreateDirectory(folder);
            using var client=new System.Net.Http.HttpClient {Timeout=TimeSpan.FromSeconds(90)};
            var paths=new List<string>();
            foreach(var item in manifest.RootElement.EnumerateArray())
            {
                string name=item.GetProperty("Name").GetString()!,hash=item.GetProperty("Hash").GetString()!,url=item.GetProperty("Url").GetString()!;
                var path=System.IO.Path.Combine(folder,name+".ogg");
                bool Valid(byte[] data)=>Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(data)).Equals(hash,StringComparison.OrdinalIgnoreCase);
                if(!System.IO.File.Exists(path) || !Valid(await System.IO.File.ReadAllBytesAsync(path)))
                {
                    var bytes=await client.GetByteArrayAsync(url);if(!Valid(bytes)) throw new System.IO.InvalidDataException("Music download failed verification.");
                    await System.IO.File.WriteAllBytesAsync(path+".tmp",bytes);System.IO.File.Move(path+".tmp",path,true);
                }
                paths.Add(path);
            }
            foreach(var path in paths) if(!state.Settings.Playlist.Contains(path)) state.Settings.Playlist.Add(path);
            trackIndex=state.Settings.Playlist.IndexOf(paths[0]);state.Settings.MusicEnabled=true;Changed();PlayTrack();Toast("Playing original Minecraft music.");
        }
        catch(Exception e) {Toast("Music download failed: "+e.Message+". You can still use Add music.");}
        finally {minecraftMusicLoading=false;if(musicPanelOpen) ShowMusic();}
    }
    private void ShowMusic()
    {
        var box = OpenPanel("The jukebox", "Original Minecraft music or your own MP3, OGG and WAV files.");musicPanelOpen=true;
        box.AddChild(new Label { Text = playingPath == null ? "Nothing playing" : "Now playing · " + System.IO.Path.GetFileName(playingPath) });
        var row = new HBoxContainer(); box.AddChild(row);
        row.AddChild(Button(state.Settings.MusicEnabled ? "Pause music" : "Play music", () => { state.Settings.MusicEnabled = !state.Settings.MusicEnabled; if (state.Settings.MusicEnabled && !music.Playing) PlayTrack(); Changed(); ShowMusic(); }));
        row.AddChild(Button(minecraftMusicLoading ? "Downloading Minecraft music..." : "Play Minecraft classics", () => {if(!minecraftMusicLoading) _=LoadMinecraftMusic();}));
        row.AddChild(Button("Next", () => { trackIndex++; PlayTrack(); ShowMusic(); }));
        row.AddChild(Button("Add music…", () => PickFiles(paths =>
        {
            foreach (var p in paths.Where(p => new[] { ".mp3", ".ogg", ".wav" }.Contains(System.IO.Path.GetExtension(p).ToLowerInvariant())))
                if (!state.Settings.Playlist.Contains(p)) state.Settings.Playlist.Add(p);
            Changed(); ShowMusic();
        })));
        row.AddChild(Button("Clear playlist", () => { state.Settings.Playlist.Clear(); music.Stop(); playingPath = null; Changed(); ShowMusic(); }));
        Slider(box, "Music volume", 0, 1, .05, state.Settings.MusicVolume, v => state.Settings.MusicVolume = (float)v);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 220) }; box.AddChild(scroll);
        var tracks = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; scroll.AddChild(tracks);
        for (int i = 0; i < state.Settings.Playlist.Count; i++)
        {
            int index = i;
            tracks.AddChild(Button(System.IO.Path.GetFileName(state.Settings.Playlist[i]), () => { trackIndex = index; state.Settings.MusicEnabled = true; Changed(); PlayTrack(); ShowMusic(); }));
        }
    }
}
