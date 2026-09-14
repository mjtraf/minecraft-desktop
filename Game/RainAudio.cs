using Godot;
namespace CozyCave;
public partial class Main
{
    private readonly List<AudioStreamPlayer3D> rainVoices=[];
    private AudioStreamOggVorbis[] rainClips=[];
    private float rainSoundDelay=.2f;
    private float rainGain=.8f, rainShelterDelay;
    private bool rainListenerSheltered;
    private int lastRainClip=-1;
    private readonly Random rainSoundRandom=new();
    private void SetupRainAudio()
    {
        rainClips=Enumerable.Range(1,8).Select(i=>GD.Load<AudioStreamOggVorbis>($"res://Assets/Vanilla/weather/rain{i}.ogg")).ToArray();
        foreach(var clip in rainClips)clip.Loop=false;
        for(int i=0;i<4;i++)
        {
            var voice=new AudioStreamPlayer3D {UnitSize=8,MaxDistance=24,AttenuationModel=AudioStreamPlayer3D.AttenuationModelEnum.InverseDistance,AttenuationFilterCutoffHz=20500,AttenuationFilterDb=0};
            AddChild(voice);rainVoices.Add(voice);
        }
        rainAudio=rainVoices[0];
        rainListenerSheltered=RainSheltered();rainGain=rainListenerSheltered?.2f:.8f;
    }
    private bool RainSheltered()
    {
        var eye=camera.GlobalPosition;
        var ray=PhysicsRayQueryParameters3D.Create(eye,eye+Vector3.Up*(ValleyTop-eye.Y+2));
        ray.CollisionMask=1;ray.Exclude=[player.GetRid()];
        return GetWorld3D().DirectSpaceState.IntersectRay(ray).Count>0;
    }
    private int PickRainClip(bool above)
    {
        int count=rainClips.Length;int clip;
        do {clip=rainSoundRandom.Next(count);}while(clip==lastRainClip);
        return lastRainClip=clip;
    }
    private void PlayRainAt(Vector3 at,bool above)
    {
        var voice=rainVoices.FirstOrDefault(v=>!v.Playing);if(voice==null)return;
        voice.Position=at;voice.PitchScale=1;
        voice.VolumeDb=Mathf.LinearToDb(Math.Max(.0001f,state.Settings.EffectsVolume*rainGain));
        voice.Stream=rainClips[PickRainClip(above)];voice.Play();
    }
    private void UpdateRainAudio(float delta)
    {
        if(proof || paused || locked || rainClips.Length==0)return;
        rainShelterDelay-=delta;
        if(rainShelterDelay<=0){rainListenerSheltered=RainSheltered();rainShelterDelay=.15f;}
        rainGain=Mathf.MoveToward(rainGain,rainListenerSheltered?.2f:.8f,delta*.6f);
        rainSoundDelay-=delta;if(rainSoundDelay>0)return;
        rainSoundDelay=.35f+(float)rainSoundRandom.NextDouble()*.4f;
        bool sheltered=rainListenerSheltered;var eye=camera.GlobalPosition;
        // A sound stays at the rain impact point for its lifetime. It never rides
        // directly above the listener as the old looping emitter did.
        for(int attempt=0;attempt<6;attempt++)
        {
            int x=Mathf.RoundToInt(eye.X)+rainSoundRandom.Next(sheltered?-10:-5,sheltered?11:6),z=Mathf.RoundToInt(eye.Z)+rainSoundRandom.Next(sheltered?-10:-5,sheltered?11:6);
            if(Math.Abs(x)>WorldRadius || Math.Abs(z)>WorldRadius)continue;
            var ray=PhysicsRayQueryParameters3D.Create(new Vector3(x,ValleyTop+2,z),new Vector3(x,eye.Y-10,z));ray.CollisionMask=1;ray.Exclude=[player.GetRid()];
            var hit=GetWorld3D().DirectSpaceState.IntersectRay(ray);if(hit.Count==0)continue;
            var at=hit["position"].AsVector3()+Vector3.Up*.05f;
            if(at.DistanceTo(eye)>16)continue;
            PlayRainAt(at,sheltered);return;
        }
        if(!sheltered)PlayRainAt(eye+new Vector3(2,-1,2),false); // Audible even while flying above distant ground.
    }
}
